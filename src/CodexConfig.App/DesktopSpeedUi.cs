using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private DesktopSpeedSession? speedSession;
    private bool updatingSpeed;
    private string? speedFeedback, speedPreferencesHome;
    private Func<CodexDesktopTarget, ConfigSnapshot, Task<(DesktopSpeedPlan? Plan, int? Port)>>? smokeSpeedPrepare;
    private Func<Task>? smokeSpeedAttach;
    private DesktopSpeedSettings SpeedPreferences() => new(home, smokeOutput is null ? null : Path.Combine(smokeOutput, "speed-preferences"));

    private void RefreshSpeedSettings()
    {
        if (SpeedCompatibilityBox is null || initializing) return;
        updatingSpeed = true;
        try
        {
            if (speedPreferencesHome != home) { speedPreferencesHome = home; speedFeedback = null; }
            var enabled = SpeedPreferences().Load(); SpeedCompatibilityBox.IsChecked = enabled;
            SpeedCompatibilityBox.IsEnabled = !closing && !desktopRestartBusy;
            var eligible = snapshot is not null && DesktopSpeedSettings.Eligible(store, snapshot);
            ApplySpeedCompatibilityButton.IsEnabled = enabled && eligible && !Dirty && !closing && !desktopRestartBusy;
            ApplySpeedCompatibilityButton.Content = desktopRestartBusy ? "正在应用…" : DesktopApplicationAction;
            SpeedCompatibilityStatus.Text = desktopRestartBusy && speedFeedback is not null ? speedFeedback : speedSession is not null ? speedSession.Status : speedFeedback ??
                (!enabled ? "已关闭。" : !eligible ? "已开启，当前保存的连接不适用。请先保存 API Key 直连，并移除活动 Profile。" :
                    Dirty ? "已开启，请先确认保存当前草稿，再应用到 Codex。" : "已开启，尚未应用。点击下方按钮，统一打开或重启 Codex 并加载兼容。");
        }
        catch (Exception error)
        {
            SpeedCompatibilityBox.IsEnabled = ApplySpeedCompatibilityButton.IsEnabled = false;
            SpeedCompatibilityStatus.Text = "速度兼容设置无法读取：" + error.Message;
        }
        finally { updatingSpeed = false; }
    }

    private async void SpeedCompatibilityChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || updatingSpeed || closing) return;
        try
        {
            SpeedPreferences().Save(SpeedCompatibilityBox.IsChecked == true); speedFeedback = null;
            if (SpeedCompatibilityBox.IsChecked != true) { pendingSpeedRetry = null; await StopSpeedSessionAsync(); }
        }
        catch (Exception error) { speedFeedback = "速度兼容设置未能保存：" + error.Message; }
        RefreshSpeedSettings();
    }

    private void SpeedSettingsClicked(object sender, RoutedEventArgs e)
    { ShowPage("preferences"); SpeedCompatibilityCard.BringIntoView(); }
    private async void ApplySpeedCompatibilityClicked(object sender, RoutedEventArgs e) => await RestartCodexFromReviewAsync(true);

    private async Task<(DesktopSpeedPlan? Plan, int? Port)> PrepareSpeedCompatibilityAsync(CodexDesktopTarget target, ConfigSnapshot saved)
    {
        if (!SpeedPreferences().Load()) return (null, null);
        if (!DesktopSpeedSettings.Eligible(store, saved)) return (null, null);
        if (smokeOutput is not null && smokeSpeedPrepare is not null) return await smokeSpeedPrepare(target, saved);
        if (smokeOutput is not null) throw new InvalidOperationException("隔离界面检查不能读取或调试真实 Codex。");
        var plan = await Task.Run(() => DesktopSpeedPatch.Inspect(target.Executable), lifetime.Token);
        using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start();
        return (plan, ((IPEndPoint)reserve.LocalEndpoint).Port);
    }

    private async Task AttachSpeedCompatibilityAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port)
    {
        if (smokeOutput is not null)
        {
            if (smokeSpeedAttach is null) throw new InvalidOperationException("隔离界面检查不能连接真实 Codex。");
            await smokeSpeedAttach(); return;
        }
        var expectedHome = saved.Home; var fingerprint = store.ReadSettings(saved).Fingerprint();
        bool Eligible()
        {
            try
            {
                if (!string.Equals(expectedHome, home, StringComparison.OrdinalIgnoreCase)) return false;
                if (!new DesktopSpeedSettings(expectedHome).Load()) return false;
                var current = store.Load(expectedHome);
                return DesktopSpeedSettings.Eligible(store, current) && store.ReadSettings(current).Fingerprint() == fingerprint;
            }
            catch { return false; }
        }
        var waiting = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            try { speedSession = await DesktopSpeedSession.StartAsync(port, target.Executable, plan, Eligible, lifetime.Token); break; }
            catch (Exception error) when (waiting.Elapsed < TimeSpan.FromSeconds(30) && error is DesktopSpeedNotReadyException or System.Net.Http.HttpRequestException)
            { speedFeedback = error.Message; RefreshSpeedSettings(); await Task.Delay(200, lifetime.Token); }
        }
        RefreshSpeedSettings();
        await speedSession!.WaitUntilAppliedAsync(lifetime.Token);
    }

    private async Task StopSpeedSessionAsync()
    {
        var session = speedSession; speedSession = null;
        if (session is not null) await session.DisposeAsync();
    }
}
