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

public sealed record BridgeStatus(long Requests, long FastRequests, long AutoReviewRequests, int ProtocolVersion = 0);

// A narrow inference bridge. Account, plugin and login endpoints are intentionally absent.
public sealed class ModelBridge : IAsyncDisposable
{
    public const int ProtocolVersion = 2;
    private readonly Func<MixedConnection> readConnection;
    private readonly Func<byte[]?> readAuth;
    private readonly HttpClient client;
    private readonly HttpClient proxyClient;
    private readonly IWebProxy proxy;
    private readonly SemaphoreSlim slots = new(16);
    private WebApplication? app;
    private long requests, fastRequests, autoReviewRequests;
    private int stopRequested;
    public bool StopRequested => Volatile.Read(ref stopRequested) != 0;
    private const int MaxBody = 64 * 1024 * 1024;
    private static readonly string[] RequestHeaders = ["Accept", "User-Agent", "originator", "version", "session_id", "conversation_id", "OpenAI-Beta", "x-codex-turn-state", "x-codex-beta-features", "X-Client-Request-Id"];
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
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, new BridgeStatus(Interlocked.Read(ref requests), Interlocked.Read(ref fastRequests), Interlocked.Read(ref autoReviewRequests), ProtocolVersion), cancellationToken: context.RequestAborted);
                return;
            }
            if (context.Request.Path == "/codex-config/stop" && HttpMethods.IsPost(context.Request.Method))
            {
                if (!SameToken(token, settings.LocalToken)) { context.Response.StatusCode = 403; return; }
                context.Response.StatusCode = 202;
                Volatile.Write(ref stopRequested, 1);
                return;
            }
            var path = context.Request.Path.Value;
            var allowed = path == "/v1/models" && HttpMethods.IsGet(context.Request.Method) ||
                path is "/v1/responses" or "/v1/responses/compact" &&
                (HttpMethods.IsPost(context.Request.Method) || context.WebSockets.IsWebSocketRequest);
            if (!allowed) { context.Response.StatusCode = 404; return; }
            await slots.WaitAsync(context.RequestAborted);
            try
            {
                var target = settings.BaseUrl.TrimEnd('/') + path![3..] + context.Request.QueryString;
                if (context.WebSockets.IsWebSocketRequest) await ForwardWebSocketAsync(context, target, settings);
                else await ForwardHttpAsync(context, target, settings);
            }
            finally { slots.Release(); }
        }
        catch (OperationCanceledException) { context.Abort(); }
        catch (Exception)
        {
            // Neither upstream exception messages nor response bodies enter diagnostics.
            if (context.Response.HasStarted) context.Abort();
            else { context.Response.StatusCode = 502; await context.Response.WriteAsync("Model connection unavailable. Open Codex Config to check the connection."); }
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
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        using var response = await (settings.UseSystemProxy ? proxyClient : client).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if ((int)response.StatusCode is >= 300 and < 400 || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // A Krill authentication failure must not trigger an official-account token refresh.
            context.Response.StatusCode = 502;
            await context.Response.WriteAsync("Upstream rejected the model connection. Check its address and credential in Codex Config.");
            return;
        }
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers.Concat(response.Content.Headers))
            if (!ExcludedHeaders.Contains(header.Key)) context.Response.Headers[header.Key] = header.Value.ToArray();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var chunk = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
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
        lifetime.CancelAfter(TimeSpan.FromMinutes(15));
        await upstream.ConnectAsync(uri.Uri, lifetime.Token);
        using var downstream = await context.WebSockets.AcceptWebSocketAsync();
        var send = PumpAsync(downstream, upstream, lifetime.Token, inspect: true);
        var receive = PumpAsync(upstream, downstream, lifetime.Token, inspect: false);
        await Task.WhenAny(send, receive);
        lifetime.Cancel(); downstream.Abort(); upstream.Abort();
        try { await Task.WhenAll(send, receive); } catch (Exception error) when (error is OperationCanceledException or WebSocketException) { }
    }

    private async Task PumpAsync(WebSocket source, WebSocket destination, CancellationToken cancellation, bool inspect)
    {
        var buffer = new byte[16384];
        using var message = new MemoryStream();
        while (!cancellation.IsCancellationRequested)
        {
            var result = await source.ReceiveAsync(buffer.AsMemory(), cancellation);
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

    public async ValueTask DisposeAsync()
    {
        if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
        if (!ReferenceEquals(proxyClient, client)) proxyClient.Dispose();
        client.Dispose(); slots.Dispose();
    }
}
