using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace CodexConfig.Core;

public sealed class DesktopSpeedNotReadyException(string message, Exception? inner = null) : IOException(message, inner);
public sealed class DesktopEnhancementResumeException(string message) : IOException(message);

// A session only touches modules before their native registration callback executes.
// CDP evaluation uses function literals; no CSP bypass, import maps, file patches or eval hooks.
public sealed class DesktopSpeedSession : IAsyncDisposable
{
    private readonly SpeedCdp client;
    private readonly DesktopSpeedPlan plan;
    private readonly Func<bool> eligible;
    private readonly Process owner;
    private readonly Uri moduleBase;
    private readonly DesktopConnectionBackend? connections;
    private readonly string scope;
    private readonly string bindingName = "__dockConnections_" + Guid.NewGuid().ToString("N");
    private readonly List<Task> bindingTasks = [];
    private readonly SemaphoreSlim bindingSlots = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private readonly TaskCompletionSource applied = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<string> observed = [];
    private readonly Dictionary<string, SpeedRuntimeLocation> breakpoints = [];
    private Task? loop;
    private string? leaseScript;
    private string? connectionScript;
    private bool resumeOnly;
    private bool ownsPage;
    private const string State = "globalThis.__codexDockEnhancement";
    public static string PlanHash(DesktopSpeedPlan plan) => ManagedModelCatalogStore.Hash(System.Text.Encoding.UTF8.GetBytes(
        "Dock runtime lifecycle 2\n" + JsonSerializer.Serialize(new { plan.SpeedEnabled, plan.ConnectionsEnabled, plan.Runtimes }) + DesktopConnectionPatch.Controller("plan")));
    public string Status { get; private set; } = "正在核对速度兼容模块…";
    public bool IsApplied { get; private set; }
    public bool HasSpeed => plan.SpeedEnabled;
    public bool HasConnections => plan.ConnectionsEnabled;
    public bool IsRunning => loop is { IsCompleted: false };
    public string ConnectionStatus => connections?.Status ?? "旧聊天接续未启用。";
    private int RequiredCount => (plan.SpeedEnabled ? DesktopSpeedPatch.RequiredGateCount : 0) + (plan.ConnectionsEnabled ? 1 : 0);
    private DesktopSpeedSession(SpeedCdp client, DesktopSpeedPlan plan, Func<bool> eligible, Process owner, Uri moduleBase, DesktopConnectionBackend? connections, string scope)
    { this.client = client; this.plan = plan; this.eligible = eligible; this.owner = owner; this.moduleBase = moduleBase; this.connections = connections; this.scope = scope; }

    public static async Task<DesktopSpeedSession> StartAsync(int port, string expectedExecutable, DesktopSpeedPlan plan,
        Func<bool> eligible, CancellationToken cancellation = default, DesktopConnectionBackend? connections = null,
        CodexDesktopProcess? expectedOwner = null, string scope = "", bool resumeOnly = false)
    {
        if (!eligible()) throw new InvalidOperationException("请先保存适用的连接，再应用客户端兼容。");
        if (plan.ConnectionsEnabled && connections is null) throw new ArgumentException("旧聊天接续缺少当前配置目录。");
        Process owner;
        try { owner = GatewayWorkerProcess.Capture(port) ?? throw new IOException("调试端口没有独立的桌面进程。"); }
        catch (IOException error) { throw new DesktopSpeedNotReadyException("正在等待 Codex 调试端口。", error); }
        SpeedCdp? client = null;
        try
        {
            if (!CodexDesktop.SamePath(owner.MainModule!.FileName, expectedExecutable)) throw new IOException("调试端口不属于本次 Codex 桌面进程。");
            if (expectedOwner is not null && (owner.Id != expectedOwner.Id || owner.StartTime.ToFileTimeUtc() != expectedOwner.Started))
                throw new IOException("调试端口不属于此前确认的 Codex 实例，已取消接入。");
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(3) };
            var bytes = await http.GetByteArrayAsync($"http://127.0.0.1:{port}/json/list", cancellation).ConfigureAwait(false);
            if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Codex 调试目标列表过大。");
            using var targets = JsonDocument.Parse(bytes);
            var pages = targets.RootElement.EnumerateArray().Where(page => page.GetProperty("type").GetString() == "page" &&
                Uri.TryCreate(page.GetProperty("url").GetString(), UriKind.Absolute, out var uri) && uri.Scheme is "app" or "file").ToArray();
            if (pages.Length == 0) throw new DesktopSpeedNotReadyException("正在等待 Codex 主页面，启动或安装窗口不参与兼容加载。");
            if (pages.Length != 1) throw new IOException("无法唯一核对 Codex 桌面页面，请关闭额外桌面窗口后重试。");
            var socket = new Uri(pages[0].GetProperty("webSocketDebuggerUrl").GetString()!);
            if (socket.Scheme != "ws" || socket.Host != "127.0.0.1" || socket.Port != port || socket.UserInfo.Length != 0)
                throw new IOException("Codex 调试地址不是本次本机端口。");
            client = await SpeedCdp.ConnectAsync(socket, cancellation).ConfigureAwait(false);
            var document = new Uri(pages[0].GetProperty("url").GetString()!);
            var moduleBase = document.Scheme == "app" ? new Uri(document.GetLeftPart(UriPartial.Authority) + "/") : new Uri(document, ".");
            var session = new DesktopSpeedSession(client, plan, eligible, owner, moduleBase, connections, scope) { resumeOnly = resumeOnly };
            try { await session.InitializeAsync(cancellation).ConfigureAwait(false); return session; }
            catch { await session.DisposeAsync().ConfigureAwait(false); client = null; throw; }
        }
        catch { if (client is not null) await client.DisposeAsync().ConfigureAwait(false); owner.Dispose(); throw; }
    }

    private async Task InitializeAsync(CancellationToken cancellation)
    {
        await client.CallAsync("Page.enable", new { }, cancellation);
        await client.CallAsync("Runtime.enable", new { }, cancellation);
        Status = "Codex 已打开，正在等待主页面完成启动…";
        await WaitForDocumentAsync(cancellation);
        if (resumeOnly)
        {
            var marker = await client.CallAsync("Runtime.evaluate", new { expression = "(()=>{const s=" + State + ";return !!s&&s.plan===" + JsonSerializer.Serialize(PlanHash(plan)) +
                "&&s.scope===" + JsonSerializer.Serialize(scope) + "&&s.until<=Date.now()})()", returnByValue = true }, cancellation);
            if (!marker.GetProperty("result").TryGetProperty("value", out var valid) || valid.ValueKind != JsonValueKind.True)
                throw new DesktopEnhancementResumeException("此页面尚不能自动接回，请使用增强启动；已有页面保持不变。");
        }
        while (client.Events.TryRead(out _)) { } // Discard startup load events before our first reload.
        if (plan.ConnectionsEnabled)
        {
            await client.CallAsync("Runtime.addBinding", new { name = bindingName }, cancellation);
            if (resumeOnly)
            {
                var resumed = await client.CallAsync("Runtime.evaluate", new { expression = DesktopConnectionPatch.Controller(bindingName, resumeOnly: true), returnByValue = true }, cancellation);
                if (!resumed.GetProperty("result").TryGetProperty("value", out var connected) || connected.ValueKind != JsonValueKind.True)
                {
                    await client.CallAsync("Runtime.removeBinding", new { name = bindingName }, cancellation);
                    throw new DesktopEnhancementResumeException("聊天接续仍在处理操作，稍后自动重试；本次未刷新页面。");
                }
                ownsPage = true;
            }
            connectionScript = (await client.CallAsync("Page.addScriptToEvaluateOnNewDocument", new { source = DesktopConnectionPatch.Controller(bindingName) }, cancellation)).GetProperty("identifier").GetString();
        }
        ownsPage = true;
        await client.CallAsync("Debugger.enable", new { maxScriptsCacheSize = 0 }, cancellation);
        foreach (var runtime in plan.Runtimes)
        {
            var table = runtime.Chunk + ".__webpack_modules__";
            var anchors = (plan.SpeedEnabled ? "serviceTierAccess:|readServiceTier|Failed to read service tier for request" : "") +
                (plan.ConnectionsEnabled ? (plan.SpeedEnabled ? "|" : "") + "this\\.requestClient\\.sendRequest" : "");
            var condition = "Object.values(" + table + ").some(f=>typeof f===\"function\"&&/" + anchors + "/.test(Function.prototype.toString.call(f)))";
            var result = await client.CallAsync("Debugger.setBreakpointByUrl", new { lineNumber = runtime.Line, columnNumber = runtime.Column,
                urlRegex = "^" + Regex.Escape(new Uri(moduleBase, runtime.Path).AbsoluteUri) + "$", condition }, cancellation);
            breakpoints.Add(result.GetProperty("breakpointId").GetString()!, runtime);
        }
        loop = RunAsync();
        if (!resumeOnly) await client.CallAsync("Page.reload", new { ignoreCache = false }, cancellation);
    }

    private async Task WaitForDocumentAsync(CancellationToken cancellation)
    {
        var timer = Stopwatch.StartNew(); var stable = 0;
        while (timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            var state = await client.CallAsync("Runtime.evaluate", new
            {
                expression = "document.readyState===\"complete\"&&(location.protocol===\"app:\"||location.protocol===\"file:\")",
                returnByValue = true
            }, cancellation);
            if (state.TryGetProperty("result", out var result) && result.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.True)
            { if (++stable == 3) return; }
            else stable = 0;
            await Task.Delay(200, cancellation);
        }
        throw new IOException("Codex 主页面尚未完成启动，已停止兼容加载；请先确认桌面端能正常打开。");
    }

    public async Task WaitUntilAppliedAsync(CancellationToken cancellation = default)
    {
        try { await applied.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellation).ConfigureAwait(false); }
        catch (TimeoutException error) { throw new IOException("速度兼容加载超时：" + Status, error); }
    }

    private async Task RunAsync()
    {
        var token = stopping.Token; var refreshed = DateTimeOffset.MinValue;
        var activated = resumeOnly; var loaded = resumeOnly; var reloadIssued = resumeOnly;
        var checkedPolicy = DateTimeOffset.MinValue; long ownedPause = 0;
        try
        {
            if (resumeOnly)
            {
                IsApplied = true; Status = "已自动接回客户端增强，Codex 页面保持不变。";
                await RenewLeaseAsync(token).ConfigureAwait(false); applied.TrySetResult();
            }
            while (!token.IsCancellationRequested && !owner.HasExited)
            {
                if (DateTimeOffset.UtcNow - checkedPolicy > TimeSpan.FromSeconds(1))
                {
                    if (!eligible()) { Status = "客户端兼容已停用：配置目录或开关发生变化。"; break; }
                    checkedPolicy = DateTimeOffset.UtcNow;
                }
                while (client.Events.TryRead(out var notification))
                {
                    var message = notification.Message;
                    if (message.GetProperty("method").GetString() == "Runtime.executionContextsCleared")
                    { observed.Clear(); IsApplied = false; loaded = false; continue; }
                    if (message.GetProperty("method").GetString() == "Runtime.bindingCalled")
                    {
                        if (plan.ConnectionsEnabled && bindingTasks.Count(task => !task.IsCompleted) < 32)
                            bindingTasks.Add(HandleBindingAsync(message.GetProperty("params").Clone(), token));
                        bindingTasks.RemoveAll(task => task.IsCompleted);
                        continue;
                    }
                    if (message.GetProperty("method").GetString() == "Page.loadEventFired") { loaded = true; continue; }
                    if (message.GetProperty("method").GetString() != "Debugger.paused") continue;
                    // Navigation can resume a pause before the queued notification is consumed.
                    // Never evaluate its stale frames or resume a later, unrelated pause.
                    if (!client.IsCurrentPause(notification.Pause)) continue;
                    var parameters = message.GetProperty("params");
                    if (!parameters.GetProperty("hitBreakpoints").EnumerateArray().Any(value => breakpoints.ContainsKey(value.GetString()!)))
                        throw new IOException("检测到其他调试暂停，速度兼容已停止，请先完成调试后重新应用。");
                    ownedPause = notification.Pause;
                    try { await PatchPausedAsync(parameters, token).ConfigureAwait(false); }
                    catch when (!client.IsCurrentPause(ownedPause)) { ownedPause = 0; continue; }
                    catch
                    {
                        // Preserve the registration failure if navigation already invalidated the pause.
                        try { await ResumeOwnedPauseAsync(ownedPause, token).ConfigureAwait(false); ownedPause = 0; }
                        catch { }
                        throw;
                    }
                    await ResumeOwnedPauseAsync(ownedPause, token).ConfigureAwait(false); ownedPause = 0;
                    if (!activated && observed.Count == RequiredCount)
                    {
                        activated = true;
                        await RenewLeaseAsync(token).ConfigureAwait(false);
                    }
                }
                if (activated && loaded && !reloadIssued)
                {
                    // Wait for the current document to finish registering its modules before reload.
                    // Reloading immediately from a registration pause invalidates later call frames.
                    reloadIssued = true; loaded = false; observed.Clear();
                    await client.CallAsync("Page.reload", new { ignoreCache = false }, token).ConfigureAwait(false);
                }
                else if (activated && loaded && reloadIssued && observed.Count == RequiredCount && !IsApplied)
                {
                    IsApplied = true;
                    Status = plan.SpeedEnabled ? "已应用：本机 API 速度门控及当前模型档位读取已核对。档位仍由模型目录与服务决定。" : "已应用：旧聊天接续入口已核对。";
                    await RenewLeaseAsync(token).ConfigureAwait(false);
                    applied.TrySetResult();
                }
                if (activated && DateTimeOffset.UtcNow - refreshed > TimeSpan.FromSeconds(3))
                { await RenewLeaseAsync(token).ConfigureAwait(false); refreshed = DateTimeOffset.UtcNow; }
                if (client.Events.Completion.IsCompleted) await client.Events.Completion.ConfigureAwait(false);
                await Task.Delay(100, token).ConfigureAwait(false);
            }
            if (owner.HasExited) Status = "Codex 已退出；下次打开时请重新应用速度兼容。";
        }
        catch (OperationCanceledException) { Status = "速度兼容已停用。"; }
        catch (Exception error) { Status = "速度兼容未完成：" + error.Message; applied.TrySetException(error); }
        finally
        {
            IsApplied = false; applied.TrySetException(new IOException(Status));
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await ResumeOwnedPauseAsync(ownedPause, cleanup.Token).ConfigureAwait(false);
            }
            catch { }
            try
            {
                await client.CallAsync("Runtime.evaluate", new { expression = DesktopSpeedPatch.Lease + "=0" }, cleanup.Token).ConfigureAwait(false);
                await client.CallAsync("Runtime.evaluate", new { expression = "if(" + State + "){" + State + ".until=0}" }, cleanup.Token).ConfigureAwait(false);
                if (plan.ConnectionsEnabled) await client.CallAsync("Runtime.evaluate", new { expression = "globalThis.__codexDockConnection?.stop?.()" }, cleanup.Token).ConfigureAwait(false);
                if (leaseScript is not null) await client.CallAsync("Page.removeScriptToEvaluateOnNewDocument", new { identifier = leaseScript }, cleanup.Token).ConfigureAwait(false);
                if (connectionScript is not null) await client.CallAsync("Page.removeScriptToEvaluateOnNewDocument", new { identifier = connectionScript }, cleanup.Token).ConfigureAwait(false);
                if (plan.ConnectionsEnabled) await client.CallAsync("Runtime.removeBinding", new { name = bindingName }, cleanup.Token).ConfigureAwait(false);
                await client.CallAsync("Debugger.disable", new { }, cleanup.Token).ConfigureAwait(false);
            }
            catch { } // A disconnected page also loses its heartbeat within 15 seconds.
        }
    }

    private async Task ResumeOwnedPauseAsync(long pause, CancellationToken token)
    {
        if (!client.IsCurrentPause(pause)) return;
        try { await client.CallAsync("Debugger.resume", new { }, token).ConfigureAwait(false); }
        catch when (!client.IsCurrentPause(pause)) { } // Navigation or another client already resumed it.
    }

    private async Task PatchPausedAsync(JsonElement parameters, CancellationToken token)
    {
        var ids = parameters.GetProperty("hitBreakpoints").EnumerateArray().Select(value => value.GetString()!).Where(breakpoints.ContainsKey).ToArray();
        if (ids.Length != 1) throw new InvalidDataException("速度兼容暂停位置无法唯一核对。");
        var frame = parameters.GetProperty("callFrames")[0].GetProperty("callFrameId").GetString()!;
        var table = breakpoints[ids[0]].Chunk + ".__webpack_modules__";
        var anchors = (plan.SpeedEnabled ? "serviceTierAccess:|readServiceTier|Failed to read service tier for request" : "") +
            (plan.ConnectionsEnabled ? (plan.SpeedEnabled ? "|" : "") + "this\\.requestClient\\.sendRequest" : "");
        var result = await EvaluateFrameAsync(frame, "Object.entries(" + table + ").filter(([k,f])=>typeof f===\"function\"&&/" + anchors + "/.test(Function.prototype.toString.call(f))).map(([key,f])=>({key,source:Function.prototype.toString.call(f)}))", token);
        var assignments = new List<string>(); var gates = new HashSet<string>();
        foreach (var factory in result.EnumerateArray())
        {
            var key = factory.GetProperty("key").GetString()!; var source = factory.GetProperty("source").GetString()!;
            if (!Regex.IsMatch(key, @"^[A-Za-z_$][\w$]*$") || !source.StartsWith(key + "(", StringComparison.Ordinal))
                throw new InvalidDataException("Codex 模块工厂结构无法核对。");
            if (source.Contains("__codexDockSpeedUntil", StringComparison.Ordinal) || source.Contains(DesktopConnectionPatch.Marker, StringComparison.Ordinal)) continue;
            var rewrite = plan.SpeedEnabled ? DesktopSpeedPatch.Rewrite(source) : new SpeedRewrite(source, []);
            var rewritten = rewrite.Source; var found = rewrite.Gates.ToList();
            if (plan.ConnectionsEnabled && DesktopConnectionPatch.Count(rewritten) > 0)
            { rewritten = DesktopConnectionPatch.Rewrite(rewritten); found.Add("connections"); }
            if (found.Count == 0) continue;
            if (Regex.IsMatch(source, @"\bimport\s*[.(]")) throw new InvalidDataException("此速度模块包含不支持的模块引用。");
            var quoted = JsonSerializer.Serialize(key);
            assignments.Add("p[" + quoted + "]=({" + rewritten + "})[" + quoted + "];");
            gates.UnionWith(found);
        }
        if (assignments.Count == 0) return;
        // All literals parse before any assignment; a malformed replacement leaves the table unchanged.
        await EvaluateFrameAsync(frame, "(()=>{const p={};" + string.Join("", assignments) + "Object.assign(" + table + ",p);return true})()", token);
        observed.UnionWith(gates);
        if (!IsApplied) Status = "正在核对速度兼容模块：" + string.Join(", ", observed);
    }

    private async Task<JsonElement> EvaluateFrameAsync(string frame, string expression, CancellationToken token)
    {
        var result = await client.CallAsync("Debugger.evaluateOnCallFrame", new { callFrameId = frame, expression, returnByValue = true, silent = true }, token);
        if (result.TryGetProperty("exceptionDetails", out _)) throw new InvalidDataException("Codex 速度兼容模块未能编译或读取。");
        return result.GetProperty("result").GetProperty("value");
    }

    private async Task RenewLeaseAsync(CancellationToken token)
    {
        var until = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 15000;
        var expression = DesktopSpeedPatch.Lease + "=" + (plan.SpeedEnabled && (connections is null || connections.SpeedEligible) ? until : 0);
        if (plan.ConnectionsEnabled)
            expression += ";if(globalThis.__codexDockConnection){globalThis.__codexDockConnection.until=" + until + ";globalThis.__codexDockConnection.enabled=" + (connections!.Enabled ? "true" : "false") + "}";
        var currentExpression = expression + (IsApplied ? ";" + State + "=" + JsonSerializer.Serialize(new { plan = PlanHash(plan), scope, until }) : "");
        await client.CallAsync("Runtime.evaluate", new { expression = currentExpression }, token);
        if (leaseScript is not null) await client.CallAsync("Page.removeScriptToEvaluateOnNewDocument", new { identifier = leaseScript }, token);
        leaseScript = (await client.CallAsync("Page.addScriptToEvaluateOnNewDocument", new { source = "if(window.top===window){" + expression + "}" }, token)).GetProperty("identifier").GetString();
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        if (loop is not null) await loop.ConfigureAwait(false);
        else if (ownsPage)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await client.CallAsync("Runtime.evaluate", new { expression = DesktopSpeedPatch.Lease + "=0;if(" + State + "){" + State + ".until=0}" }, cleanup.Token);
                if (plan.ConnectionsEnabled) await client.CallAsync("Runtime.evaluate", new { expression = "globalThis.__codexDockConnection?.stop?.()" }, cleanup.Token);
                if (connectionScript is not null) await client.CallAsync("Page.removeScriptToEvaluateOnNewDocument", new { identifier = connectionScript }, cleanup.Token);
                if (plan.ConnectionsEnabled) await client.CallAsync("Runtime.removeBinding", new { name = bindingName }, cleanup.Token);
                await client.CallAsync("Debugger.disable", new { }, cleanup.Token);
            }
            catch { }
        }
        await Task.WhenAll(bindingTasks).ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false); owner.Dispose();
    }

    private async Task HandleBindingAsync(JsonElement value, CancellationToken token)
    {
        if (value.GetProperty("name").GetString() != bindingName || value.GetProperty("payload").GetString() is not { Length: <= 8192 } payload) return;
        var context = value.GetProperty("executionContextId").GetInt32();
        JsonElement request;
        try { using var document = JsonDocument.Parse(payload); request = document.RootElement.Clone(); }
        catch (JsonException) { return; }
        if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("id", out var id) || !id.TryGetInt32(out var number) || number <= 0) return;
        try
        {
            await bindingSlots.WaitAsync(token);
            object response;
            try
            {
                var main = await client.CallAsync("Runtime.evaluate", new { contextId = context, expression = "window.top===window&&(location.protocol==='app:'||location.protocol==='file:')", returnByValue = true }, token);
                if (!main.TryGetProperty("result", out var result) || !result.TryGetProperty("value", out var valid) || valid.ValueKind != JsonValueKind.True) return;
                response = new { ok = true, value = await Task.Run(() => connections!.Handle(request), token) };
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { response = new { ok = false, error = error is IOException or InvalidOperationException ? error.Message : "聊天接续请求无法核对，历史保持不变。" }; }
            finally { bindingSlots.Release(); }
            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await client.CallAsync("Runtime.evaluate", new { contextId = context, expression = "globalThis.__codexDockConnection?.settle(" + number + "," + json + ")" }, token);
        }
        catch (Exception) { } // A navigated or closed document has no request left to settle.
    }
}

internal sealed class SpeedCdp : IAsyncDisposable
{
    internal sealed record Notification(JsonElement Message, long Pause);
    private readonly ClientWebSocket socket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly Channel<Notification> events = Channel.CreateUnbounded<Notification>();
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private Task? receiving; private int next; private long pauseSequence; private long activePause;
    internal ChannelReader<Notification> Events => events.Reader;
    internal bool IsCurrentPause(long pause) => pause != 0 && Volatile.Read(ref activePause) == pause;
    internal static async Task<SpeedCdp> ConnectAsync(Uri uri, CancellationToken token)
    {
        var client = new SpeedCdp(); client.socket.Options.Proxy = null;
        try { await client.socket.ConnectAsync(uri, token); client.receiving = client.ReceiveAsync(); return client; }
        catch { await client.DisposeAsync(); throw; }
    }
    internal async Task<JsonElement> CallAsync(string method, object parameters, CancellationToken token)
    {
        var id = Interlocked.Increment(ref next); var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
            await sending.WaitAsync(token);
            try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token); } finally { sending.Release(); }
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        finally { pending.TryRemove(id, out _); }
    }
    private async Task ReceiveAsync()
    {
        Exception failure = new IOException("Codex 调试连接已关闭。");
        try
        {
            var buffer = new byte[65536];
            while (!stopping.IsCancellationRequested)
            {
                using var stream = new MemoryStream(); WebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(buffer, stopping.Token);
                    if (received.MessageType == WebSocketMessageType.Close) return;
                    if (stream.Length + received.Count > 24 * 1024 * 1024) throw new InvalidDataException("Codex 调试消息过大。");
                    stream.Write(buffer, 0, received.Count);
                } while (!received.EndOfMessage);
                using var document = JsonDocument.Parse(stream.ToArray()); var message = document.RootElement;
                if (message.TryGetProperty("id", out var id) && pending.TryGetValue(id.GetInt32(), out var completion))
                {
                    if (message.TryGetProperty("error", out var error)) completion.TrySetException(new IOException("Codex 调试命令未被客户端接受：" + error.GetProperty("message").GetString()));
                    else completion.TrySetResult(message.GetProperty("result").Clone());
                }
                else if (message.TryGetProperty("method", out var method))
                {
                    switch (method.GetString())
                    {
                        case "Debugger.paused":
                            var pause = Interlocked.Increment(ref pauseSequence); Volatile.Write(ref activePause, pause);
                            events.Writer.TryWrite(new(message.Clone(), pause)); break;
                        case "Debugger.resumed": Volatile.Write(ref activePause, 0); break;
                        case "Page.loadEventFired": events.Writer.TryWrite(new(message.Clone(), 0)); break;
                        case "Runtime.executionContextsCleared": events.Writer.TryWrite(new(message.Clone(), 0)); break;
                        case "Runtime.bindingCalled": events.Writer.TryWrite(new(message.Clone(), 0)); break;
                    }
                }
            }
        }
        catch (Exception error) { failure = error; }
        finally { Volatile.Write(ref activePause, 0); foreach (var completion in pending.Values) completion.TrySetException(failure); events.Writer.TryComplete(failure); }
    }
    public async ValueTask DisposeAsync()
    {
        stopping.Cancel(); socket.Abort(); if (receiving is not null) await receiving.ConfigureAwait(false); socket.Dispose();
    }
}
