using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace CodexConfig.Core;

public sealed record BridgeStatus(long Requests, long FastRequests, long AutoReviewRequests, int ProtocolVersion = 0,
    string? LastError = null, int? LastUpstreamStatus = null, string? LastRequestId = null, string? LastRequestKind = null, string? LastUpstreamCode = null);

// A narrow inference bridge. Account, plugin and login endpoints are intentionally absent.
public sealed class ModelBridge : IAsyncDisposable
{
    public const int ProtocolVersion = 3;
    private readonly Func<MixedConnection> readConnection;
    private readonly Func<byte[]?> readAuth;
    private readonly HttpClient client;
    private readonly HttpClient proxyClient;
    private readonly IWebProxy proxy;
    private readonly SemaphoreSlim slots = new(16);
    private WebApplication? app;
    private long requests, fastRequests, autoReviewRequests;
    private int stopRequested;
    private BridgeStatus diagnostics = new(0, 0, 0);
    public bool StopRequested => Volatile.Read(ref stopRequested) != 0;
    private const int MaxBody = 64 * 1024 * 1024;
    private static readonly string[] RequestHeaders = ["Accept", "User-Agent", "originator", "version", "session-id", "thread-id", "session_id", "conversation_id", "OpenAI-Beta", "x-codex-beta-features", "X-Client-Request-Id", "x-openai-subagent", "x-codex-turn-metadata"];
    private static readonly HashSet<string> ExcludedHeaders = new(StringComparer.OrdinalIgnoreCase)
    { "Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Trailer", "Upgrade", "Proxy-Authenticate", "Proxy-Authorization", "Set-Cookie", "Content-Length" };

    public ModelBridge(Func<MixedConnection> readConnection, Func<byte[]?> readAuth, HttpMessageHandler? handler = null, IWebProxy? proxy = null)
    {
        this.readConnection = readConnection; this.readAuth = readAuth;
        this.proxy = proxy ?? new SystemHttpProxy();
        client = handler is null ? new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) : new(handler);
        proxyClient = handler is null ? new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = true, Proxy = this.proxy }) : client;
        client.Timeout = Timeout.InfiniteTimeSpan;
        proxyClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task StartAsync(CancellationToken cancellation = default)
    {
        var settings = readConnection();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production", ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.PreferHostingUrlsKey, "false");
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, settings.Port);
            options.Limits.MaxRequestBodySize = MaxBody;
            options.Limits.MaxRequestHeadersTotalSize = 64 * 1024;
        });
        app = builder.Build();
        app.UseWebSockets();
        app.Run(HandleAsync);
        await app.StartAsync(cancellation);
    }

    private async Task HandleAsync(HttpContext context)
    {
        try
        {
            var settings = readConnection();
            if (context.Request.Headers.ContainsKey("Origin")) { context.Response.StatusCode = 403; return; }
            var supplied = context.Request.Headers.Authorization.ToString();
            var token = supplied.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? supplied[7..] : "";
            if (token.Length == 0 || !(SameToken(token, settings.LocalToken) || OfficialCredentials.MatchesAccessToken(readAuth(), token)))
            { context.Response.StatusCode = 401; return; }
            if (context.Request.Path == "/codex-config/health" && HttpMethods.IsGet(context.Request.Method))
            {
                if (!SameToken(token, settings.LocalToken)) { context.Response.StatusCode = 403; return; }
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, diagnostics with { Requests = Interlocked.Read(ref requests), FastRequests = Interlocked.Read(ref fastRequests), AutoReviewRequests = Interlocked.Read(ref autoReviewRequests), ProtocolVersion = ProtocolVersion }, cancellationToken: context.RequestAborted);
                return;
            }
            if (context.Request.Path == "/codex-config/stop" && HttpMethods.IsPost(context.Request.Method))
            {
                if (!SameToken(token, settings.LocalToken)) { context.Response.StatusCode = 403; return; }
                context.Response.StatusCode = 202;
                Volatile.Write(ref stopRequested, 1);
                return;
            }
            var rawPath = context.Request.Path.Value ?? "";
            var path = rawPath.StartsWith("/backend-api/codex/", StringComparison.Ordinal) ? rawPath[18..] : rawPath.StartsWith("/v1/", StringComparison.Ordinal) ? rawPath[3..] : "";
            var allowed = path == "/models" && HttpMethods.IsGet(context.Request.Method) ||
                path is "/responses" or "/responses/compact" && HttpMethods.IsPost(context.Request.Method) ||
                path == "/responses" && context.WebSockets.IsWebSocketRequest;
            if (!allowed) { context.Response.StatusCode = 404; return; }
            await slots.WaitAsync(context.RequestAborted);
            try
            {
                diagnostics = diagnostics with { LastRequestKind = path == "/responses/compact" ? "compact" : SafeValue(context.Request.Headers["x-openai-subagent"].ToString(), settings) ?? (path == "/models" ? "models" : "turn") };
                var target = settings.BaseUrl.TrimEnd('/') + path + context.Request.QueryString;
                if (context.WebSockets.IsWebSocketRequest) await ForwardWebSocketAsync(context, target, settings);
                else await ForwardHttpAsync(context, target, settings);
            }
            finally { slots.Release(); }
        }
        catch (OperationCanceledException)
        {
            if (context.RequestAborted.IsCancellationRequested || context.Response.HasStarted) context.Abort();
            else await WriteErrorAsync(context, 504, "model_connection_timeout", "模型入口连接或等待数据超时，请检查网络。", null, null);
        }
        catch (Exception)
        {
            // Neither upstream exception messages nor response bodies enter diagnostics.
            if (context.Response.HasStarted) context.Abort();
            else await WriteErrorAsync(context, 502, "model_connection_unavailable", "无法连接模型入口，请检查地址、证书或代理。", null, null);
        }
    }

    private async Task ForwardHttpAsync(HttpContext context, string target, MixedConnection settings)
    {
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        CopyRequestHeaders(context, (key, value) => request.Headers.TryAddWithoutValidation(key, value));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        if (HttpMethods.IsPost(context.Request.Method))
        {
            await using var body = new MemoryStream();
            var buffer = new byte[16384];
            int length;
            while ((length = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                if (body.Length + length > MaxBody) { context.Response.StatusCode = 413; return; }
                await body.WriteAsync(buffer.AsMemory(0, length), context.RequestAborted);
            }
            var bytes = body.ToArray();
            CountRequest(bytes);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            if (context.Request.Headers.TryGetValue("Content-Encoding", out var encoding))
                request.Content.Headers.TryAddWithoutValidation("Content-Encoding", encoding.ToArray());
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await (settings.UseSystemProxy ? proxyClient : client).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        var requestId = response.Headers.TryGetValues("x-request-id", out var ids) ? SafeValue(ids.FirstOrDefault(), settings) : null;
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var authFailure = status is 401 or 403;
            string? upstreamCode = null;
            try
            {
                var errorBytes = await ReadBoundedAsync(response.Content, 32 * 1024, timeout.Token);
                using var errorJson = JsonDocument.Parse(errorBytes);
                if (errorJson.RootElement.TryGetProperty("error", out var detail) && detail.ValueKind == JsonValueKind.Object &&
                    detail.TryGetProperty("code", out var codeValue) && codeValue.ValueKind == JsonValueKind.String)
                    upstreamCode = SafeValue(codeValue.GetString(), settings);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException) { }
            // Keep upstream auth failures separate from the native account's refresh path.
            await WriteErrorAsync(context, authFailure || status is >= 300 and < 400 ? 502 : status,
                authFailure ? "model_authentication_failed" : "model_upstream_error",
                authFailure ? $"模型入口鉴权失败（HTTP {status}），请检查 PAT / API Key。" : $"模型入口返回 HTTP {status}。", status, requestId, upstreamCode);
            return;
        }
        diagnostics = diagnostics with { LastError = null, LastUpstreamStatus = (int)response.StatusCode, LastRequestId = requestId, LastUpstreamCode = null };
        if (HttpMethods.IsGet(context.Request.Method))
        {
            var bytes = await ReadBoundedAsync(response.Content, 2 * 1024 * 1024, timeout.Token);
            byte[] catalog;
            try { catalog = NativeModelCatalog.Convert(bytes); }
            catch (Exception error) when (error is InvalidDataException or JsonException)
            {
                await WriteErrorAsync(context, 502, "model_catalog_invalid", "模型入口的目录格式无法转换为 Codex 模型目录，请检查服务类型。", (int)response.StatusCode, requestId);
                return;
            }
            context.Response.ContentType = "application/json";
            if (requestId is not null) context.Response.Headers["x-request-id"] = requestId;
            await context.Response.Body.WriteAsync(catalog, context.RequestAborted);
            return;
        }
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers.Concat(response.Content.Headers))
            if (!ExcludedHeaders.Contains(header.Key)) context.Response.Headers[header.Key] = header.Value.ToArray();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var chunk = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            await context.Response.Body.WriteAsync(chunk.AsMemory(0, count), timeout.Token);
            await context.Response.Body.FlushAsync(timeout.Token);
        }
    }

    private async Task ForwardWebSocketAsync(HttpContext context, string target, MixedConnection settings)
    {
        var uri = new UriBuilder(target) { Scheme = new Uri(target).Scheme == "https" ? "wss" : "ws" };
        using var upstream = new ClientWebSocket();
        upstream.Options.Proxy = settings.UseSystemProxy ? proxy : null;
        upstream.Options.SetRequestHeader("Authorization", "Bearer " + settings.ApiKey);
        CopyRequestHeaders(context, (key, value) => { if (key != "Accept") upstream.Options.SetRequestHeader(key, value); });
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        upstream.Options.CollectHttpResponseDetails = true;
        try { await upstream.ConnectAsync(uri.Uri, lifetime.Token); }
        catch (WebSocketException)
        {
            var status = (int)upstream.HttpStatusCode;
            // Preserve unsupported-handshake responses so the native client can choose HTTP before any output.
            await WriteErrorAsync(context, status is 404 or 405 or 426 or 501 ? status : 502,
                status is 401 or 403 ? "model_authentication_failed" : "model_websocket_unavailable", "模型入口拒绝 WebSocket 连接，请检查入口支持和凭据。", status == 0 ? null : status, null);
            return;
        }
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        using var downstream = await context.WebSockets.AcceptWebSocketAsync();
        var send = PumpAsync(downstream, upstream, lifetime, inspect: true);
        var receive = PumpAsync(upstream, downstream, lifetime, inspect: false);
        await Task.WhenAny(send, receive);
        lifetime.Cancel(); downstream.Abort(); upstream.Abort();
        try { await Task.WhenAll(send, receive); } catch (Exception error) when (error is OperationCanceledException or WebSocketException) { }
    }

    private async Task PumpAsync(WebSocket source, WebSocket destination, CancellationTokenSource lifetime, bool inspect)
    {
        var cancellation = lifetime.Token;
        var buffer = new byte[16384];
        using var message = new MemoryStream();
        while (!cancellation.IsCancellationRequested)
        {
            var result = await source.ReceiveAsync(buffer.AsMemory(), cancellation);
            lifetime.CancelAfter(TimeSpan.FromMinutes(5));
            if (result.MessageType == WebSocketMessageType.Close) return;
            if (inspect)
            {
                if (message.Length + result.Count > MaxBody) throw new InvalidDataException("Model message too large.");
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) { CountRequest(message.ToArray()); message.SetLength(0); }
            }
            await destination.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, cancellation);
        }
    }

    private void CountRequest(byte[] bytes)
    {
        Interlocked.Increment(ref requests);
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.TryGetProperty("model", out var model) && model.GetString() == "codex-auto-review") Interlocked.Increment(ref autoReviewRequests);
            if (doc.RootElement.TryGetProperty("service_tier", out var tier) && tier.GetString() is "fast" or "priority" or "ultrafast") Interlocked.Increment(ref fastRequests);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { }
    }

    private static void CopyRequestHeaders(HttpContext context, Action<string, string> copy)
    {
        foreach (var key in RequestHeaders)
            if (context.Request.Headers.TryGetValue(key, out var value)) copy(key, value.ToString());
    }

    private static bool SameToken(string first, string second) => CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(first), System.Text.Encoding.UTF8.GetBytes(second));

    private async Task WriteErrorAsync(HttpContext context, int status, string code, string message, int? upstreamStatus, string? requestId, string? upstreamCode = null)
    {
        diagnostics = diagnostics with { LastError = message, LastUpstreamStatus = upstreamStatus, LastRequestId = requestId, LastUpstreamCode = upstreamCode };
        context.Response.StatusCode = status; context.Response.ContentType = "application/json";
        if (requestId is not null) context.Response.Headers["x-request-id"] = requestId;
        await JsonSerializer.SerializeAsync(context.Response.Body, new { error = new { type = "upstream_error", code, message, upstream_status = upstreamStatus, upstream_code = upstreamCode, request_id = requestId } }, cancellationToken: context.RequestAborted);
    }

    private static string? SafeValue(string? value, MixedConnection settings) => string.IsNullOrEmpty(value) || value.Length > 150 ||
        value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':')) || value.Contains(settings.ApiKey, StringComparison.Ordinal) || value.Contains(settings.LocalToken, StringComparison.Ordinal) ? null : value;

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken cancellation)
    {
        await using var input = await content.ReadAsStreamAsync(cancellation); using var output = new MemoryStream();
        var bytes = new byte[8192]; int count;
        while ((count = await input.ReadAsync(bytes, cancellation)) > 0) { if (output.Length + count > limit) throw new InvalidDataException("模型目录过大。"); await output.WriteAsync(bytes.AsMemory(0, count), cancellation); }
        return output.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
        if (!ReferenceEquals(proxyClient, client)) proxyClient.Dispose();
        client.Dispose(); slots.Dispose();
    }
}
