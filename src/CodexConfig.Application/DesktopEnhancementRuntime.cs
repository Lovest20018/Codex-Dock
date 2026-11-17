using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using CodexConfig.Core;

namespace CodexConfig.Application;

public sealed record DesktopEnhancementPreparation(DesktopSpeedPlan? Plan = null, int? Port = null);

// Owns the same runtime connection regardless of which XAML shell initiated it.
// It never closes or starts a desktop process; the apply service owns that order.
public sealed class DesktopEnhancementRuntime(ConfigStore store, ClientEnhancementPreferencesService preferences,
    Func<string> activeHome, Func<bool> closing) : IAsyncDisposable
{
    public DesktopSpeedSession? Session { get; private set; }
    public DesktopConnectionBackend? ConnectionBackend { get; private set; }
    public string? AppliedSnapshotHash { get; private set; }
    public CodexDesktopTarget? AppliedTarget { get; private set; }
    public string Status { get; private set; } = "增强尚未接入。";
    public string? AutomaticStatus { get; private set; }
    public event EventHandler? StatusChanged;

    public async Task<DesktopEnhancementPreparation> PrepareAsync(CodexDesktopTarget target, ConfigSnapshot saved, CancellationToken cancellation)
    {
        var speed = preferences.SpeedSettings.Load() && DesktopSpeedSettings.Eligible(store, saved);
        var connections = preferences.ConnectionSettings.Load();
        if (!speed && !connections) return new();
        if (connections && new TomlDocument(saved.Config).String("profile") is not null)
            throw new InvalidOperationException("旧聊天接续暂不接管活动 Profile，请先核对其连接。");
        var plan = await Task.Run(() => DesktopSpeedPatch.Inspect(target.Executable, speed, connections), cancellation);
        using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start();
        return new(plan, ((IPEndPoint)reserve.LocalEndpoint).Port);
    }

    public async Task AttachAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port,
        CancellationToken cancellation, bool resumeOnly = false)
    {
        var expectedHome = saved.Home; var fingerprint = plan.ConnectionsEnabled ? null : store.ReadSettings(saved).Fingerprint();
        var scope = expectedHome.ToUpperInvariant() + "\n" + DesktopEnhancementSettings.SnapshotHash(saved);
        var attaching = 1;
        bool HomeMatches() => !closing() && string.Equals(expectedHome, activeHome(), StringComparison.OrdinalIgnoreCase);
        bool ConnectionsEnabled() => HomeMatches() && preferences.ConnectionSettings.Load();
        ConnectionBackend = plan.ConnectionsEnabled ? new DesktopConnectionBackend(store, saved, ConnectionsEnabled, () => preferences.SpeedSettings.Load()) : null;
        bool Eligible()
        {
            try
            {
                if (!HomeMatches()) return false;
                if (resumeOnly && Volatile.Read(ref attaching) != 0 && (cancellation.IsCancellationRequested || !preferences.AutomaticSettings.Load().Enabled)) return false;
                if (plan.ConnectionsEnabled) return ConnectionsEnabled() || plan.SpeedEnabled && preferences.SpeedSettings.Load();
                if (!preferences.SpeedSettings.Load()) return false;
                var current = store.Load(expectedHome);
                return DesktopSpeedSettings.Eligible(store, current) && store.ReadSettings(current).Fingerprint() == fingerprint;
            }
            catch { return false; }
        }
        var waiting = Stopwatch.StartNew();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try { Session = await DesktopSpeedSession.StartAsync(port, target.Executable, plan, Eligible, cancellation, ConnectionBackend, target.MainProcess, scope, resumeOnly); break; }
            catch (Exception error) when (!resumeOnly && waiting.Elapsed < TimeSpan.FromSeconds(30) && error is DesktopSpeedNotReadyException or HttpRequestException)
            { SetStatus(error.Message); await Task.Delay(200, cancellation); }
        }
        await Session.WaitUntilAppliedAsync(cancellation);
        Interlocked.Exchange(ref attaching, 0);
        AppliedSnapshotHash = DesktopEnhancementSettings.SnapshotHash(saved); AppliedTarget = target;
        SetStatus(Session.Status);
        AutomaticStatus = null;
        if (target.MainProcess is { } owner && target.UserDataDirectory is { } profile)
        {
            try
            {
                preferences.AutomaticSettings.Remember(new(target.Executable, profile, owner.Id, owner.Started, port,
                    DesktopEnhancementSettings.SnapshotHash(saved), DesktopSpeedSession.PlanHash(plan)));
                AutomaticStatus = resumeOnly ? "已自动接回，Codex 页面保持不变。" : "已记住本次实例。重开 Dock 后可自动接回，Codex 关闭时不会自动重开。";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            { AutomaticStatus = "增强已应用，但未能保存自动接回记录：" + error.Message; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        var session = Session; Session = null; ConnectionBackend = null; AppliedSnapshotHash = null; AppliedTarget = null;
        if (session is not null) await session.DisposeAsync();
    }

    private void SetStatus(string status) { Status = status; StatusChanged?.Invoke(this, EventArgs.Empty); }
}
