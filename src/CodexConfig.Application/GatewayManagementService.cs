using System.Security.Cryptography;
using CodexConfig.Core;

namespace CodexConfig.Application;

public enum GatewayKind { Model, Mobile }
public enum GatewayCommand { Start, Stop, Restart }
public enum GatewayOperationOutcome { Completed, Busy, Cancelled, Failed }
public sealed record GatewayDefinition(GatewayKind Kind, string Home, bool Configured, bool CanStart, string Description, string Revision)
{ public string? CompactionPreference { get; init; } }
public sealed record GatewayProbe(bool? Running, string Detail = "")
{ public ModelGatewayDiagnostics? Diagnostics { get; init; } }
public sealed record GatewayState(GatewayKind Kind, bool Configured, bool CanStart, bool? Running, string Description, string Detail = "", string? Error = null)
{
    public ModelGatewayDiagnostics? Diagnostics { get; init; }
    public string Label => Kind == GatewayKind.Model ? "本机模型网关" : "手机网关";
    public string Status => Error is not null ? "需要处理" : Running is true ? "运行中" : Running is null ? "暂未确认" : Configured ? "已停止" : Kind == GatewayKind.Model ? "当前连接无需启动" : "尚未配置";
}
public sealed record GatewayOverview(string Home, GatewayState Model, GatewayState Mobile);
public sealed record GatewayOperationResult(GatewayKind Kind, GatewayCommand Command, GatewayOperationOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome == GatewayOperationOutcome.Completed;
}
public sealed record GatewayLaunchResult(string Home, bool Attempted, IReadOnlyList<GatewayOperationResult> Results, string? Error = null);

// Definitions contain only presentation data and an opaque revision. Tokens and
// passwords remain inside the native runtime and Core's existing encrypted stores.
public interface IGatewayManagementRuntime
{
    Task<GatewayDefinition> LoadAsync(GatewayKind kind, string home, CancellationToken cancellation);
    Task<GatewayProbe> ProbeAsync(GatewayDefinition definition, CancellationToken cancellation);
    Task StartAsync(GatewayDefinition definition, CancellationToken cancellation);
    Task StopAsync(GatewayDefinition definition, CancellationToken cancellation);
}

public sealed class NativeGatewayManagementRuntime(ConfigStore store, string? mobileRoot = null,
    string? modelExecutable = null, string? mobileExecutable = null, string? mobileScript = null) : IGatewayManagementRuntime
{
    private MobileGatewayManager Mobile(string home) => new(home, mobileRoot);
    public Task<GatewayDefinition> LoadAsync(GatewayKind kind, string home, CancellationToken cancellation)
        => Task.Run(() => Load(kind, Path.GetFullPath(home)), cancellation);

    private GatewayDefinition Load(GatewayKind kind, string home)
    {
        if (kind == GatewayKind.Mobile)
        {
            var manager = Mobile(home); var config = manager.Load();
            return new(kind, home, config is not null, config is not null,
                config is null ? "先在“手机连接”保存 HTTPS 入口、账号和密码，再在这里启停。" : "HTTPS 入口：" + config.PublicUrl + "\n本机监听端口：" + config.Port,
                config is null ? "missing" : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manager.SettingsPath))));
        }
        var saved = store.Load(home); var settings = store.ReadSettings(saved);
        if (saved.Bridge is not { } bytes) return new(kind, home, false, false, "API Key 直连无需模型网关。使用官方账号 + API 服务时，先在连接页保存。", "missing");
        var connection = MixedConnection.Read(bytes);
        var ready = settings.Mode == ConnectionMode.OfficialWithApi && connection.OwnsRoute(new TomlDocument(saved.Config));
        var authorized = HttpConnectionPolicy.IsAuthorized(connection.BaseUrl, connection.HttpOrigin);
        var pending = store.HasPending(home);
        return new(kind, home, true, ready && authorized && !pending,
            (ready ? "本机地址：" + connection.NativeBaseUrl + "\nHTTP 响应头等待：" + connection.HeaderTimeoutSeconds + " 秒；新请求采用已保存值。" : "已保留旧模型网关记录；当前连接无需启动，可停止仍在运行的旧网关。") +
            (!authorized ? "\nHTTP 站点未确认，请在连接页确认并保存。" : "") + (pending ? "\n请先恢复未完成的保存。" : ""),
            DesktopEnhancementSettings.SnapshotHash(saved)) { CompactionPreference = ModelGatewayDiagnostics.ReadPreference(saved.Config) };
    }

    private void RequireCurrent(GatewayDefinition definition)
    {
        var current = Load(definition.Kind, definition.Home);
        if (current.Revision != definition.Revision || current.Configured != definition.Configured)
            throw new IOException("已保存的网关配置已改变，请刷新并重新操作。");
    }

    public async Task<GatewayProbe> ProbeAsync(GatewayDefinition definition, CancellationToken cancellation)
    {
        if (!definition.Configured) return new(false);
        RequireCurrent(definition);
        if (definition.Kind == GatewayKind.Mobile)
        {
            var manager = Mobile(definition.Home); var config = manager.Load()!;
            var status = await manager.StatusAsync(cancellation);
            cancellation.ThrowIfCancellationRequested(); RequireCurrent(definition);
            if (status is not null) return new(true, status.Checked ? "本机后台运行中 · HTTPS 入口已检测通过" : "本机后台运行中 · HTTPS 入口尚未检测");
            var first = await ClosedPortAsync(config.ControlPort, cancellation); var second = await ClosedPortAsync(config.Port, cancellation);
            return new(first == false && second == false ? false : null);
        }
        var saved = store.Load(definition.Home); var connection = MixedConnection.Read(saved.Bridge!);
        var model = await BridgeProcess.StatusAsync(connection, cancellation);
        cancellation.ThrowIfCancellationRequested(); RequireCurrent(definition);
        if (model is null) return new(await ClosedPortAsync(connection.Port, cancellation));
        var diagnostics = ModelGatewayDiagnostics.FromStatus(model, definition.CompactionPreference);
        return new(true, diagnostics.Requests) { Diagnostics = diagnostics };
    }

    public async Task StartAsync(GatewayDefinition definition, CancellationToken cancellation)
    {
        RequireCurrent(definition);
        var current = Load(definition.Kind, definition.Home);
        if (!current.CanStart) throw new InvalidOperationException(current.Description);
        if (definition.Kind == GatewayKind.Mobile) await Mobile(definition.Home).StartAsync(cancellation, mobileExecutable, mobileScript);
        else
        {
            var saved = store.Load(definition.Home); HttpConnectionPolicy.RequireAuthorization(store.ReadSettings(saved).BaseUrl, store.ReadSettings(saved).Details.HttpOrigin);
            await BridgeProcess.EnsureAsync(definition.Home, saved.Bridge!, modelExecutable, cancellation);
        }
    }
    public async Task StopAsync(GatewayDefinition definition, CancellationToken cancellation)
    {
        RequireCurrent(definition);
        if (!definition.Configured) return;
        if (definition.Kind == GatewayKind.Mobile) await Mobile(definition.Home).StopAsync(cancellation);
        else await BridgeProcess.StopAsync(store.Load(definition.Home).Bridge!, cancellation);
    }

    // Missing authenticated health never means stopped when a listener is open.
    private static Task<bool?> ClosedPortAsync(int port, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // Reuse Core's exclusive port-release check. Windows can delay a refused
        // connect beyond a short health timeout even after the worker has exited.
        return Task.FromResult<bool?>(GatewayPortState.IsReleased(port) ? false : null);
    }
}

public sealed class GatewayManagementService(IGatewayManagementRuntime runtime, WorkspaceApplyCoordinator operations, string? preferenceRoot = null)
{
    private bool launchAttempted;
    private Task<GatewayOverview?>? refresh;
    private string? refreshHome;
    private long overviewGeneration, refreshGeneration;
    private readonly object overviewGate = new();
    public bool IsBusy { get; private set; }
    public GatewayOverview? Overview { get; private set; }
    public event EventHandler? Changed;
    public GatewayStartupStore Preferences(string home) => new(home, preferenceRoot);
    public GatewayStartupSettings LoadPreferences(string home) => Preferences(home).Load();
    public void SetAutomaticStart(string home, GatewayKind kind, bool enabled)
    {
        var store = Preferences(home); var saved = store.Load();
        store.Save(kind == GatewayKind.Model ? saved with { ModelOnDockStart = enabled } : saved with { MobileOnDockStart = enabled });
    }
    public void ClearOverview()
    { lock (overviewGate) { Interlocked.Increment(ref overviewGeneration); Overview = null; } Changed?.Invoke(this, EventArgs.Empty); }

    public Task<GatewayOverview?> RefreshAsync(string home, Func<bool> eligible, CancellationToken cancellation = default)
    {
        if (IsBusy) return Task.FromResult<GatewayOverview?>(null);
        var generation = Volatile.Read(ref overviewGeneration);
        if (refresh is { IsCompleted: false } pending && refreshHome == home && refreshGeneration == generation) return pending;
        refreshHome = home; refreshGeneration = generation; return refresh = RefreshCoreAsync(home, eligible, generation, cancellation);
    }

    private async Task<GatewayOverview?> RefreshCoreAsync(string home, Func<bool> eligible, long generation, CancellationToken cancellation)
    {
        var model = await ReadStateAsync(GatewayKind.Model, home, cancellation);
        var mobile = await ReadStateAsync(GatewayKind.Mobile, home, cancellation);
        if (!eligible()) return null;
        GatewayOverview overview;
        lock (overviewGate)
        {
            if (IsBusy || generation != Volatile.Read(ref overviewGeneration)) return null;
            Overview = overview = new(home, model, mobile);
        }
        Changed?.Invoke(this, EventArgs.Empty); return overview;
    }
    private async Task<GatewayState> ReadStateAsync(GatewayKind kind, string home, CancellationToken cancellation)
    {
        try
        {
            var definition = await runtime.LoadAsync(kind, home, cancellation); var probe = await runtime.ProbeAsync(definition, cancellation);
            return new(kind, definition.Configured, definition.CanStart, probe.Running, definition.Description, probe.Detail)
            { Diagnostics = kind == GatewayKind.Model && probe.Running == true ? probe.Diagnostics : null };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return new(kind, false, false, null, "无法读取已保存的网关设置。", Error: error.Message); }
    }

    public async Task<GatewayOperationResult> ExecuteAsync(GatewayKind kind, GatewayCommand command, string home, Func<bool> eligible, CancellationToken cancellation = default)
    {
        var label = kind == GatewayKind.Model ? "本机模型网关" : "手机网关";
        using var operation = operations.TryBeginGateway(command == GatewayCommand.Stop, label);
        if (operation is null) return new(kind, command, GatewayOperationOutcome.Busy, "正在处理其他操作，请完成后重试。");
        IsBusy = true; ClearOverview();
        try
        {
            void Validate() { cancellation.ThrowIfCancellationRequested(); if (!eligible()) throw new OperationCanceledException("当前目录或操作已改变。"); }
            Validate(); var definition = await runtime.LoadAsync(kind, home, cancellation); Validate();
            if (command != GatewayCommand.Stop && (!definition.Configured || !definition.CanStart)) throw new InvalidOperationException(definition.Description);
            if (command is GatewayCommand.Stop or GatewayCommand.Restart) { await runtime.StopAsync(definition, cancellation); Validate(); }
            if (command == GatewayCommand.Restart)
            {
                var latest = await runtime.LoadAsync(kind, home, cancellation); Validate();
                if (latest.Revision != definition.Revision || !latest.CanStart) throw new IOException("停止后网关设置已改变，本次没有启动新进程，请刷新核对。");
            }
            if (command is GatewayCommand.Start or GatewayCommand.Restart) { Validate(); await runtime.StartAsync(definition, cancellation); Validate(); }
            var probe = await runtime.ProbeAsync(definition, cancellation); Validate();
            if (command == GatewayCommand.Stop ? probe.Running != false : probe.Running != true)
                throw new IOException(command == GatewayCommand.Stop ? "尚未确认网关停止，请刷新核对。" : "尚未确认网关已运行，请刷新核对。");
            var message = label + (command == GatewayCommand.Stop ? "已停止。" : command == GatewayCommand.Restart ? "已重启。" : "已启动。") + "已保存的设置保持不变。";
            operation.WaitForUser(message); return new(kind, command, GatewayOperationOutcome.Completed, message);
        }
        catch (OperationCanceledException)
        { const string message = "操作已取消，请刷新网关状态。"; operation.WaitForUser(message); return new(kind, command, GatewayOperationOutcome.Cancelled, message); }
        catch (Exception error)
        { operation.Fail(error.Message); return new(kind, command, GatewayOperationOutcome.Failed, error.Message); }
        finally { IsBusy = false; ClearOverview(); }
    }

    public async Task<GatewayLaunchResult> StartOnLaunchAsync(string home, Func<bool> eligible, CancellationToken cancellation = default)
    {
        if (launchAttempted || !eligible()) return new(home, false, []);
        launchAttempted = true;
        GatewayStartupSettings preferences;
        try { preferences = LoadPreferences(home); }
        catch (Exception error) { return new(home, true, [], error.Message); }
        var results = new List<GatewayOperationResult>();
        if (preferences.ModelOnDockStart) results.Add(await ExecuteAsync(GatewayKind.Model, GatewayCommand.Start, home, eligible, cancellation));
        if (eligible() && preferences.MobileOnDockStart) results.Add(await ExecuteAsync(GatewayKind.Mobile, GatewayCommand.Start, home, eligible, cancellation));
        return new(home, true, results);
    }
}
