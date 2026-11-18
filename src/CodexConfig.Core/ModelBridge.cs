using System.Net;
using System.Collections.Concurrent;
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
    string? LastError = null, int? LastUpstreamStatus = null, string? LastRequestId = null, string? LastRequestKind = null, string? LastUpstreamCode = null,
    BridgeMetrics? Metrics = null, long UltrafastRequests = 0);

// A narrow inference bridge. Account, plugin and login endpoints are intentionally absent.
public sealed class ModelBridge : IAsyncDisposable
{
    public const int ProtocolVersion = 9;
    private readonly Func<MixedConnection> readConnection;
    private readonly Func<byte[]?> readAuth;
    private readonly HttpClient client;
    private readonly HttpClient proxyClient;
    private readonly IWebProxy proxy;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim slots = new(16);
    private readonly ConcurrentDictionary<HttpContext, byte> activeRequests = new();
    private WebApplication? app;
    private long requests, fastRequests, ultrafastRequests, autoReviewRequests;
    private int stopRequested;
    private BridgeStatus diagnostics = new(0, 0, 0);
    private readonly BridgeTelemetry telemetry = new();
    public bool StopRequested => Volatile.Read(ref stopRequested) != 0;
    private const int MaxBody = 64 * 1024 * 1024;
    private static readonly string[] RequestHeaders = ["Accept", "User-Agent", "originator", "version", "session-id", "thread-id", "session_id", "conversation_id", "OpenAI-Beta", "x-codex-beta-features", "X-Client-Request-Id", "x-openai-subagent", "x-codex-turn-metadata"];
    private static readonly HashSet<string> ExcludedHeaders = new(StringComparer.OrdinalIgnoreCase)
    { "Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Trailer", "Upgrade", "Proxy-Authenticate", "Proxy-Authorization", "Set-Cookie", "Content-Length" };

    public ModelBridge(Func<MixedConnection> readConnection, Func<byte[]?> readAuth, HttpMessageHandler? handler = null, IWebProxy? proxy = null, TimeProvider? timeProvider = null)
    {
        this.readConnection = readConnection; this.readAuth = readAuth;
        time = timeProvider ?? TimeProvider.System;
        this.proxy = proxy ?? new SystemHttpProxy();
        client = handler is null ? new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(30) }) : new(handler);
        proxyClient = handler is null ? new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = true, Proxy = this.proxy, ConnectTimeout = TimeSpan.FromSeconds(30) }) : client;
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
        BridgeTelemetry.Transfer? transfer = null;
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
                await JsonSerializer.SerializeAsync(context.Response.Body, diagnostics with { Requests = Interlocked.Read(ref requests), FastRequests = Interlocked.Read(ref fastRequests), UltrafastRequests = Interlocked.Read(ref ultrafastRequests), AutoReviewRequests = Interlocked.Read(ref autoReviewRequests), ProtocolVersion = ProtocolVersion, Metrics = telemetry.Snapshot() }, cancellationToken: context.RequestAborted);
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
            if (!HttpConnectionPolicy.IsAuthorized(settings.BaseUrl, settings.HttpOrigin))
            {
                await WriteErrorAsync(context, 403, "http_origin_not_authorized", "此 HTTP 站点未确认或确认已撤销，请在 Dock 中重新确认并保存。", null, null);
                return;
            }
            activeRequests.TryAdd(context, 0);
            if (StopRequested) { context.Abort(); return; }
            transfer = telemetry.Begin(path == "/models" ? null : context.WebSockets.IsWebSocketRequest ? "websocket" : "http");
            await slots.WaitAsync(context.RequestAborted);
            transfer.Enter();
            try
            {
                // A queued request must observe a consent change before opening its upstream.
                settings = readConnection();
                if (!HttpConnectionPolicy.IsAuthorized(settings.BaseUrl, settings.HttpOrigin))
                {
                    await WriteErrorAsync(context, 403, "http_origin_not_authorized", "HTTP 站点确认已撤销，请在 Dock 中重新确认并保存。", null, null);
                    return;
                }
                diagnostics = diagnostics with { LastRequestKind = path == "/responses/compact" ? "compact" : SafeValue(context.Request.Headers["x-openai-subagent"].ToString(), settings) ?? (path == "/models" ? "models" : "turn") };
                var target = settings.BaseUrl.TrimEnd('/') + path + context.Request.QueryString;
                if (context.WebSockets.IsWebSocketRequest) await ForwardWebSocketAsync(context, target, settings, transfer);
                else await ForwardHttpAsync(context, target, settings, transfer);
                transfer.End("completed");
            }
            finally { slots.Release(); }
        }
        catch (HttpPhaseTimeoutException error)
        {
            transfer?.End(error.Headers ? "headers_timeout" : "stream_idle_timeout");
            if (context.RequestAborted.IsCancellationRequested || context.Response.HasStarted) context.Abort();
            else await WriteErrorAsync(context, 504, error.Headers ? "model_response_headers_timeout" : "model_stream_idle_timeout",
                error.Headers ? "等待模型入口响应头超过 " + error.Seconds + " 秒，可在连接设置中调整网关等待。" : "模型入口连续 5 分钟未返回数据，连接已结束。", null, null);
        }
        catch (OperationCanceledException)
        {
            transfer?.End(context.RequestAborted.IsCancellationRequested ? "client_disconnected" : "timeout");
            if (context.RequestAborted.IsCancellationRequested || context.Response.HasStarted) context.Abort();
            else await WriteErrorAsync(context, 504, "model_connection_timeout", "模型入口连接或等待数据超时，请检查网络。", null, null);
        }
        catch (Exception)
        {
            transfer?.End("transport_error");
            // Neither upstream exception messages nor response bodies enter diagnostics.
            if (context.Response.HasStarted) context.Abort();
            else await WriteErrorAsync(context, 502, "model_connection_unavailable", "无法连接模型入口，请检查地址、证书或代理。", null, null);
        }
        finally { activeRequests.TryRemove(context, out _); transfer?.Dispose(); }
    }

    private async Task ForwardHttpAsync(HttpContext context, string target, MixedConnection settings, BridgeTelemetry.Transfer transfer)
    {
        using var observer = telemetry.Observe(context.Request.Path.Value?.EndsWith("/responses/compact", StringComparison.Ordinal) == true,
            "http", transfer, id => SafeValue(id, settings));
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
            CountRequest(bytes, context.Request.Headers["x-openai-subagent"] == "guardian");
            observer.Request(bytes);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            if (context.Request.Headers.TryGetValue("Content-Encoding", out var encoding))
                request.Content.Headers.TryAddWithoutValidation("Content-Encoding", encoding.ToArray());
        }
        if (settings.HeaderTimeoutSeconds is < 60 or > 3600) throw new InvalidDataException("模型网关等待设置无效。");
        using var headerDeadline = new HttpDeadline(time, TimeSpan.FromSeconds(settings.HeaderTimeoutSeconds), context.RequestAborted);
        transfer.Upstream();
        HttpResponseMessage received;
        try { received = await (HttpConnectionPolicy.CanUseProxy(settings.BaseUrl, settings.UseSystemProxy) ? proxyClient : client).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerDeadline.Token); }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested && headerDeadline.Expired)
        { throw new HttpPhaseTimeoutException(true, settings.HeaderTimeoutSeconds); }
        using var response = received;
        headerDeadline.Stop();
        using var timeout = new HttpDeadline(time, TimeSpan.FromMinutes(5), context.RequestAborted);
        transfer.Headers();
        var sse = string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
        observer.SetSse(sse);
        var requestId = response.Headers.TryGetValues("x-request-id", out var ids) ? SafeValue(ids.FirstOrDefault(), settings) : null;
        transfer.HeaderMetadata(sse, requestId);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var authFailure = status is 401 or 403;
            string? upstreamCode = null;
            try
            {
                var errorBytes = await ReadBoundedAsync(response.Content, 32 * 1024, timeout);
                using var errorJson = JsonDocument.Parse(errorBytes);
                if (errorJson.RootElement.TryGetProperty("error", out var detail) && detail.ValueKind == JsonValueKind.Object &&
                    detail.TryGetProperty("code", out var codeValue) && codeValue.ValueKind == JsonValueKind.String)
                    upstreamCode = SafeValue(codeValue.GetString(), settings);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException) { }
            // Keep upstream auth failures separate from the native account's refresh path.
            transfer.End("upstream_error");
            await WriteErrorAsync(context, authFailure || status is >= 300 and < 400 ? 502 : status,
                authFailure ? "model_authentication_failed" : "model_upstream_error",
                authFailure ? $"模型入口鉴权失败（HTTP {status}），请检查 API Key。" : $"模型入口返回 HTTP {status}。", status, requestId, upstreamCode);
            return;
        }
        diagnostics = diagnostics with { LastError = null, LastUpstreamStatus = (int)response.StatusCode, LastRequestId = requestId, LastUpstreamCode = null };
        if (HttpMethods.IsGet(context.Request.Method))
        {
            var bytes = await ReadBoundedAsync(response.Content, 2 * 1024 * 1024, timeout);
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
        var chunk = new byte[16384];
        int count;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            while ((count = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                transfer.Data(); observer.Feed(chunk.AsSpan(0, count));
                timeout.Reset(TimeSpan.FromMinutes(5));
                await context.Response.Body.WriteAsync(chunk.AsMemory(0, count), timeout.Token);
                await context.Response.Body.FlushAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested && timeout.Expired)
        { throw new HttpPhaseTimeoutException(false, 300); }
        observer.Finish();
    }

    private async Task ForwardWebSocketAsync(HttpContext context, string target, MixedConnection settings, BridgeTelemetry.Transfer transfer)
    {
        using var observer = telemetry.Observe(false, "websocket", transfer, id => SafeValue(id, settings));
        var uri = new UriBuilder(target) { Scheme = new Uri(target).Scheme == "https" ? "wss" : "ws" };
        using var upstream = new ClientWebSocket();
        upstream.Options.Proxy = HttpConnectionPolicy.CanUseProxy(settings.BaseUrl, settings.UseSystemProxy) ? proxy : null;
        upstream.Options.SetRequestHeader("Authorization", "Bearer " + settings.ApiKey);
        CopyRequestHeaders(context, (key, value) => { if (key != "Accept") upstream.Options.SetRequestHeader(key, value); });
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        upstream.Options.CollectHttpResponseDetails = true;
        transfer.Upstream();
        try { await upstream.ConnectAsync(uri.Uri, lifetime.Token); }
        catch (WebSocketException)
        {
            transfer.HeaderMetadata(false, SocketRequestId());
            transfer.End("handshake_failed");
            var status = (int)upstream.HttpStatusCode;
            // Preserve unsupported-handshake responses so the native client can choose HTTP before any output.
            await WriteErrorAsync(context, status is 404 or 405 or 426 or 501 ? status : 502,
                status is 401 or 403 ? "model_authentication_failed" : "model_websocket_unavailable", "模型入口拒绝 WebSocket 连接，请检查入口支持和凭据。", status == 0 ? null : status, null);
            return;
        }
        transfer.Headers();
        transfer.HeaderMetadata(false, SocketRequestId());
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        using var downstream = await context.WebSockets.AcceptWebSocketAsync();
        var route = new BridgeSocketRoute(BridgeSocketRoute.Fingerprint(settings), () =>
        {
            var current = readConnection();
            if (!HttpConnectionPolicy.IsAuthorized(current.BaseUrl, current.HttpOrigin)) throw new IOException("站点授权已改变。");
            return BridgeSocketRoute.Fingerprint(current);
        });
        var send = PumpAsync(downstream, upstream, lifetime, true, observer, transfer, context.Request.Headers["x-openai-subagent"] == "guardian", route);
        var receive = PumpAsync(upstream, downstream, lifetime, false, observer, transfer, route: route);
        var retire = RetireChangedSocketAsync(route, lifetime.Token);
        var ended = await Task.WhenAny(send, receive, retire);
        var endReason = context.RequestAborted.IsCancellationRequested ? "client_disconnected" : lifetime.IsCancellationRequested ? "timeout" : ended.IsFaulted ? "transport_error" : "peer_closed";
        if (endReason == "peer_closed" && route.TryRetire())
        {
            // The route is already fenced against new requests and its final
            // response was forwarded before it became idle. Complete a bounded
            // close handshake instead of resetting TCP as soon as the timer ends.
            using var closing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); closing.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await downstream.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Model connection updated", closing.Token);
                if (upstream.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    try { await upstream.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Model connection updated", closing.Token); }
                    catch (Exception error) when (error is WebSocketException or OperationCanceledException) { }
                await send.WaitAsync(closing.Token);
                // A late client message can cause the fenced send pump to end
                // before reading the acknowledgement. Drain only until Close;
                // none of these messages may reach the retired upstream route.
                var closingBuffer = new byte[4096];
                while (downstream.State == WebSocketState.CloseSent)
                    if ((await downstream.ReceiveAsync(closingBuffer.AsMemory(), closing.Token)).MessageType == WebSocketMessageType.Close) break;
                await receive.WaitAsync(closing.Token);
            }
            catch (Exception error) when (error is WebSocketException or OperationCanceledException) { }
        }
        else if (endReason == "peer_closed" && (ReferenceEquals(ended, send) || ReferenceEquals(ended, receive)))
        {
            var source = ReferenceEquals(ended, receive) ? (WebSocket)upstream : downstream;
            var destination = ReferenceEquals(ended, receive) ? downstream : (WebSocket)upstream;
            var other = ReferenceEquals(ended, receive) ? send : receive;
            // The destination may still be receiving already-forwarded frames.
            // Propagate the close and allow its acknowledgement before Abort;
            // an immediate TCP reset can otherwise discard the final message.
            if (source.State == WebSocketState.CloseReceived && destination.State == WebSocketState.Open)
            {
                using var closing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); closing.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await destination.CloseOutputAsync(source.CloseStatus ?? WebSocketCloseStatus.NormalClosure, source.CloseStatusDescription, closing.Token);
                    try { await source.CloseOutputAsync(source.CloseStatus ?? WebSocketCloseStatus.NormalClosure, source.CloseStatusDescription, closing.Token); }
                    catch (Exception error) when (error is WebSocketException or OperationCanceledException) { }
                    await other.WaitAsync(closing.Token);
                }
                catch (Exception error) when (error is WebSocketException or OperationCanceledException) { }
            }
        }
        lifetime.Cancel(); downstream.Abort(); upstream.Abort();
        try { await Task.WhenAll(send, receive, retire); } catch (Exception error) when (error is OperationCanceledException or WebSocketException) { }
        transfer.End(endReason);

        string? SocketRequestId() => upstream.HttpResponseHeaders?.FirstOrDefault(header =>
            header.Key.Equals("x-request-id", StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault() is { } id ? SafeValue(id, settings) : null;
    }

    private async Task PumpAsync(WebSocket source, WebSocket destination, CancellationTokenSource lifetime, bool inspect, BridgeTelemetry.Observer observer, BridgeTelemetry.Transfer transfer, bool guardian = false, BridgeSocketRoute? route = null)
    {
        var cancellation = lifetime.Token;
        var buffer = new byte[16384];
        using var message = new MemoryStream();
        using var serverMessage = new MemoryStream(); var beginning = true;
        while (!cancellation.IsCancellationRequested)
        {
            var result = await source.ReceiveAsync(buffer.AsMemory(), cancellation);
            lifetime.CancelAfter(TimeSpan.FromMinutes(5));
            if (result.MessageType == WebSocketMessageType.Close) return;
            if (inspect)
            {
                if (beginning && route is not null && !route.BeginMessage()) return;
                if (message.Length + result.Count > MaxBody) throw new InvalidDataException("Model message too large.");
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) { var bytes = message.ToArray(); CountRequest(bytes, guardian); observer.Request(bytes); route?.ClientMessage(bytes); message.SetLength(0); }
            }
            else if (result.MessageType == WebSocketMessageType.Text)
            {
                // Completion/control frames are small. Oversized output fragments
                // need no duplicate multi-megabyte buffer for route retirement.
                if (serverMessage.Length + result.Count <= 1024 * 1024) serverMessage.Write(buffer, 0, result.Count);
                transfer.Data(); observer.Feed(buffer.AsSpan(0, result.Count));
                if (result.EndOfMessage) observer.Finish();
            }
            await destination.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, cancellation);
            if (!inspect && result.EndOfMessage) { route?.ServerMessage(serverMessage.ToArray()); serverMessage.SetLength(0); }
            beginning = result.EndOfMessage;
        }
    }

    private static async Task RetireChangedSocketAsync(BridgeSocketRoute route, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            if (route.TryRetire()) return;
            await Task.Delay(200, cancellation);
        }
    }

    private void CountRequest(byte[] bytes, bool guardian = false)
    {
        Interlocked.Increment(ref requests);
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (guardian || doc.RootElement.TryGetProperty("model", out var model) && model.GetString() == "codex-auto-review" ||
                doc.RootElement.TryGetProperty("client_metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("x-openai-subagent", out var agent) && agent.GetString() == "guardian")
                Interlocked.Increment(ref autoReviewRequests);
            if (doc.RootElement.TryGetProperty("service_tier", out var tier) && tier.GetString() is "fast" or "priority" or "ultrafast") Interlocked.Increment(ref fastRequests);
            if (doc.RootElement.TryGetProperty("service_tier", out tier) && tier.GetString() == "ultrafast") Interlocked.Increment(ref ultrafastRequests);
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

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, HttpDeadline timeout)
    {
        try
        {
            await using var input = await content.ReadAsStreamAsync(timeout.Token); using var output = new MemoryStream();
            var bytes = new byte[8192]; int count;
            while ((count = await input.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (output.Length + count > limit) throw new InvalidDataException("模型响应过大。");
                timeout.Reset(TimeSpan.FromMinutes(5));
                await output.WriteAsync(bytes.AsMemory(0, count), timeout.Token);
            }
            return output.ToArray();
        }
        catch (OperationCanceledException) when (timeout.Expired)
        { throw new HttpPhaseTimeoutException(false, 300); }
    }

    private sealed class HttpPhaseTimeoutException(bool headers, int seconds) : TimeoutException
    { internal bool Headers => headers; internal int Seconds => seconds; }
    private sealed class HttpDeadline : IDisposable
    {
        private readonly CancellationTokenSource deadline = new(), linked;
        private readonly ITimer timer;
        internal HttpDeadline(TimeProvider time, TimeSpan duration, CancellationToken cancellation)
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token);
            timer = time.CreateTimer(_ => { try { deadline.Cancel(); } catch (ObjectDisposedException) { } }, null, duration, Timeout.InfiniteTimeSpan);
        }
        internal CancellationToken Token => linked.Token;
        internal bool Expired => deadline.IsCancellationRequested;
        internal void Reset(TimeSpan duration) => timer.Change(duration, Timeout.InfiniteTimeSpan);
        internal void Stop() => timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        public void Dispose() { timer.Dispose(); linked.Dispose(); deadline.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref stopRequested, 1);
        foreach (var context in activeRequests.Keys)
        {
            try { context.Abort(); }
            catch (ObjectDisposedException) { } // A disconnected request can leave this snapshot while Kestrel disposes it.
        }
        if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
        if (!ReferenceEquals(proxyClient, client)) proxyClient.Dispose();
        client.Dispose(); slots.Dispose();
    }
}
