using System.IO;
using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private DesktopSpeedSession? speedSession => desktopEnhancements.Session;
    private bool updatingSpeed;
    private bool updatingConnections;
    private DesktopConnectionBackend? connectionBackend => desktopEnhancements.ConnectionBackend;
    private string? speedFeedback, speedPreferencesHome;
    private Func<CodexDesktopTarget, ConfigSnapshot, Task<(DesktopSpeedPlan? Plan, int? Port)>>? smokeSpeedPrepare;
    private Func<Task>? smokeSpeedAttach;
    private DesktopSpeedSettings SpeedPreferences() => enhancements.Storage.SpeedSettings;
    private DesktopConnectionSettings ConnectionPreferences() => enhancements.Storage.ConnectionSettings;

    private void RefreshSpeedSettings()
    {
        if (SpeedCompatibilityBox is null || initializing) return;
        updatingSpeed = true;
        try
        {
            if (speedPreferencesHome != home) { speedPreferencesHome = home; speedFeedback = null; }
            var preferences = enhancements.Reload(); var enabled = preferences.SpeedCompatibilityEnabled; SpeedCompatibilityBox.IsChecked = enabled;
            SpeedCompatibilityBox.IsEnabled = !closing && !desktopRestartBusy;
            var eligible = snapshot is not null && DesktopSpeedSettings.Eligible(store, snapshot);
            SpeedCompatibilityStatus.Text = !enabled ? "已关闭。" : desktopRestartBusy && speedFeedback is not null ? speedFeedback : !eligible ?
                "已开启，当前保存的连接不适用。请先保存 API Key 直连，并移除活动 Profile。" : speedSession is { HasSpeed: true } ? speedSession.Status : speedFeedback ??
                (
                    Dirty ? "已开启，请先确认保存当前草稿，再应用到 Codex。" : "已开启，尚未应用。点击下方按钮，统一打开或重启 Codex 并加载兼容。");
        }
        catch (Exception error)
        {
            SpeedCompatibilityBox.IsEnabled = false;
            SpeedCompatibilityStatus.Text = "速度兼容设置无法读取：" + error.Message;
        }
        finally { updatingSpeed = false; }
        try { var preferences = enhancements.Reload(); workspaceCoordinator.Enhancements(preferences.ConnectionRecoveryEnabled, preferences.SpeedCompatibilityEnabled, preferences.AutomaticReconnectEnabled); } catch { }
        RefreshConnectionRecovery();
        RefreshAutomaticEnhancements();
    }

    private void RefreshConnectionRecovery()
    {
        if (ConnectionRecoveryBox is null || initializing || updatingConnections) return;
        updatingConnections = true;
        try
        {
            var preferences = enhancements.Reload(); var enabled = preferences.ConnectionRecoveryEnabled; ConnectionRecoveryBox.IsChecked = enabled;
            ConnectionRecoveryBox.IsEnabled = !closing && !desktopRestartBusy;
            if (enabled && connectionBackend is not null && speedSession is { IsApplied: true }) _ = connectionBackend.Inspect();
            ConnectionRecoveryStatus.Text = !enabled ? "已关闭。" : Dirty ? "已开启，请先确认保存草稿。" :
                speedSession is { IsApplied: true } && connectionBackend is not null ? connectionBackend.Status : "已开启，尚未应用。通过下方按钮重新打开 Codex 后接入。";
        }
        catch (Exception error)
        {
            ConnectionRecoveryStatus.Text = "旧聊天接续暂不可用：" + error.Message;
            ConnectionRecoveryBox.IsEnabled = false;
        }
        finally { updatingConnections = false; RefreshDesktopEnhancementsAction(); }
    }
    private void RefreshDesktopEnhancementsAction()
    {
        if (ApplyDesktopEnhancementsButton is null) return;
        var connections = ConnectionRecoveryBox.IsChecked == true && ConnectionRecoveryBox.IsEnabled;
        var speed = false;
        try { speed = SpeedCompatibilityBox.IsChecked == true && SpeedCompatibilityBox.IsEnabled && snapshot is not null && DesktopSpeedSettings.Eligible(store, snapshot); }
        catch { /* The speed status above retains the readable configuration error. */ }
        ApplyDesktopEnhancementsButton.IsEnabled = (connections || speed) && snapshot is not null && !Dirty && !closing && !desktopRestartBusy && !autoEnhancementBusy;
        ApplyDesktopEnhancementsButton.Content = autoEnhancementBusy ? "正在自动接回…" : desktopRestartBusy ? "正在应用…" : AutomaticEnhancementsBox.IsChecked == true ? "增强启动 Codex" : DesktopApplicationAction;
    }
    private void ConnectionRecoveryChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || updatingConnections || closing) return;
        if (autoEnhancementBusy) automaticEnhancements.Cancel();
        try { enhancements.SetConnectionRecovery(ConnectionRecoveryBox.IsChecked == true); }
        catch (Exception error) { ShowError(error); }
        RefreshConnectionRecovery();
        try { var preferences = enhancements.Reload(); workspaceCoordinator.Enhancements(preferences.ConnectionRecoveryEnabled, preferences.SpeedCompatibilityEnabled, preferences.AutomaticReconnectEnabled); } catch { }
    }
    private async void ApplyDesktopEnhancementsClicked(object sender, RoutedEventArgs e)
    {
        if (Dirty) { OpenConfigurationReview(); return; }
        if (AutomaticEnhancementsBox.IsChecked == true) { await RequestEnhancedLaunchAsync(home); return; }
        OpenDesktopApplicationPanel(); await RestartCodexFromReviewAsync();
    }

    private async void SpeedCompatibilityChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || updatingSpeed || closing) return;
        if (autoEnhancementBusy) automaticEnhancements.Cancel();
        try
        {
            enhancements.SetSpeedCompatibility(SpeedCompatibilityBox.IsChecked == true); speedFeedback = null;
            if (SpeedCompatibilityBox.IsChecked != true)
            {
                desktopApplication.ClearEnhancementRetry();
                if (connectionBackend is null || !ConnectionPreferences().Load()) await StopSpeedSessionAsync();
            }
        }
        catch (Exception error) { speedFeedback = "速度兼容设置未能保存：" + error.Message; }
        RefreshSpeedSettings();
    }

    private void SpeedSettingsClicked(object sender, RoutedEventArgs e)
    { ShowPage("enhancements"); EnhancementsScroll.ScrollToTop(); }
    private void EnhancementStartupClicked(object sender, RoutedEventArgs e)
    { ShowPage("app"); AutoStartBox.BringIntoView(); }

    private async Task<(DesktopSpeedPlan? Plan, int? Port)> PrepareSpeedCompatibilityAsync(CodexDesktopTarget target, ConfigSnapshot saved)
    {
        if (smokeOutput is not null)
        {
            var speed = SpeedPreferences().Load() && DesktopSpeedSettings.Eligible(store, saved);
            if (!speed && !ConnectionPreferences().Load()) return (null, null);
            if (smokeSpeedPrepare is null) throw new InvalidOperationException("隔离界面检查不能读取或调试真实 Codex。");
            return await smokeSpeedPrepare(target, saved);
        }
        var prepared = await desktopEnhancements.PrepareAsync(target, saved, lifetime.Token);
        return (prepared.Plan, prepared.Port);
    }

    private async Task AttachSpeedCompatibilityAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port,
        bool resumeOnly = false, CancellationToken? cancellation = null)
    {
        if (smokeOutput is not null)
        {
            if (smokeSpeedAttach is null) throw new InvalidOperationException("隔离界面检查不能连接真实 Codex。");
            await smokeSpeedAttach(); return;
        }
        await desktopEnhancements.AttachAsync(target, saved, plan, port, cancellation ?? lifetime.Token, resumeOnly);
        sessionAppliedHash = desktopEnhancements.AppliedSnapshotHash;
        lastAppliedDesktopTarget = target;
        if (desktopEnhancements.AutomaticStatus is { } status) automaticEnhancementStatus = status;
        RefreshSpeedSettings();
    }

    private async Task StopSpeedSessionAsync()
    {
        sessionAppliedHash = null;
        await desktopEnhancements.DisposeAsync();
    }
}
