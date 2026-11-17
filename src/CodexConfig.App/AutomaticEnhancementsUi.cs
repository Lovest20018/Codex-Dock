using System.Diagnostics;
using System.IO;
using System.Windows;
using CodexConfig.Core;
using CodexConfig.Application;
using Microsoft.Win32;

namespace CodexConfig.App;

public partial class MainWindow
{
    private bool updatingAutomaticEnhancements, desktopInitializationReady;
    private bool autoEnhancementBusy => automaticEnhancements.IsBusy;
    private string? pendingEnhancedHome;
    private string automaticEnhancementStatus = "已关闭。";
    private string? automaticHome, sessionAppliedHash;
    private Task? automaticEnhancementTask => automaticEnhancements.Pending;
    internal bool EnhancedLaunchRequested { get; init; }
    private DesktopEnhancementSettings EnhancementPreferences() => enhancements.Storage.AutomaticSettings;
    private Func<Task<bool>>? smokeAutomaticAttach;
    private Action<string, string, string>? smokeCreateEnhancedShortcut;

    private void RefreshAutomaticEnhancements()
    {
        if (initializing || AutomaticEnhancementsBox is null || updatingAutomaticEnhancements) return;
        updatingAutomaticEnhancements = true;
        try
        {
            if (automaticHome != home) { automaticHome = home; automaticEnhancements.ResetRetry(); automaticEnhancementStatus = "已开启。等待核对上次接入的实例。"; }
            var preferences = enhancements.Reload(); AutomaticEnhancementsBox.IsChecked = preferences.AutomaticReconnectEnabled;
            AutomaticEnhancementsBox.IsEnabled = !closing && !desktopRestartBusy;
            var features = ConnectionRecoveryBox.IsChecked == true || SpeedCompatibilityBox.IsChecked == true;
            CreateEnhancedShortcutButton.IsEnabled = preferences.AutomaticReconnectEnabled && features && !closing && !desktopRestartBusy && !autoEnhancementBusy;
            AutomaticEnhancementsStatus.Text = !preferences.AutomaticReconnectEnabled ? "已关闭。手动应用已开启的增强仍可使用。" : !features ? "已开启，请选择下方至少一项增强。" : automaticEnhancementStatus;
        }
        catch (Exception error)
        {
            AutomaticEnhancementsBox.IsEnabled = CreateEnhancedShortcutButton.IsEnabled = false;
            AutomaticEnhancementsStatus.Text = error.Message;
        }
        finally { updatingAutomaticEnhancements = false; }
    }

    private async void AutomaticEnhancementsChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || updatingAutomaticEnhancements || closing) return;
        try
        {
            enhancements.SetAutomaticReconnect(AutomaticEnhancementsBox.IsChecked == true);
            try { var preferences = enhancements.Reload(); workspaceCoordinator.Enhancements(preferences.ConnectionRecoveryEnabled, preferences.SpeedCompatibilityEnabled, preferences.AutomaticReconnectEnabled); } catch { }
            automaticEnhancements.ResetRetry();
            if (AutomaticEnhancementsBox.IsChecked != true) automaticEnhancements.Cancel();
            automaticEnhancementStatus = "已开启。成功应用一次后，Dock 会记住本次实例并自动接回。";
            RefreshAutomaticEnhancements();
            await TryAutomaticEnhancementsAsync();
        }
        catch (Exception error) { automaticEnhancementStatus = error.Message; RefreshAutomaticEnhancements(); }
    }

    private Task TryAutomaticEnhancementsAsync()
    {
        var expectedHome = home;
        return automaticEnhancements.TryResumeAsync(snapshot, () => !closing && home == expectedHome && !desktopRestartBusy && !Dirty &&
            !busy && !officialLoginBusy && !gatewayPolling && !gatewayLaunchInProgress, lifetime.Token);
    }

    private sealed class WpfAutomaticRuntime(MainWindow window) : IAutomaticEnhancementRuntime
    {
        private readonly IAutomaticEnhancementRuntime native = new NativeAutomaticEnhancementRuntime(window.desktopEnhancements);
        public bool IsRunning => native.IsRunning;
        public bool IsApplied => native.IsApplied;
        public bool HasFeatures(bool speed, bool connections) => native.HasFeatures(speed, connections);
        public Task<IReadOnlyList<CodexDesktopTarget>> FindTargetsAsync(CancellationToken cancellation)
        {
            if (window.smokeOutput is null) return native.FindTargetsAsync(cancellation);
            var binding = window.EnhancementPreferences().Load().Binding!;
            return Task.FromResult<IReadOnlyList<CodexDesktopTarget>>([new(binding.Executable,
                [new(binding.ProcessId, 1, binding.Started, binding.Executable)]) { UserDataDirectory = binding.UserDataDirectory }]);
        }
        public Task<DesktopSpeedPlan> InspectAsync(CodexDesktopTarget target, bool speed, bool connections, CancellationToken cancellation)
            => window.smokeOutput is null ? native.InspectAsync(target, speed, connections, cancellation) :
                Task.FromResult(new DesktopSpeedPlan([], new Dictionary<string, int>()) { SpeedEnabled = speed, ConnectionsEnabled = connections });
        public Task StopAsync() => window.StopSpeedSessionAsync();
        public async Task ResumeAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port, CancellationToken cancellation)
        {
            if (window.smokeOutput is not null)
            {
                if (window.smokeAutomaticAttach is null) throw new InvalidOperationException("隔离检查未配置自动接回，未读取真实桌面。");
                if (!await window.smokeAutomaticAttach().WaitAsync(cancellation)) throw new DesktopEnhancementResumeException("等待 Codex；本次未启动、退出或刷新页面。");
                return;
            }
            await window.AttachSpeedCompatibilityAsync(target, saved, plan, port, resumeOnly: true, cancellation: cancellation);
        }
    }
    private void CreateEnhancedShortcutClicked(object sender, RoutedEventArgs e)
    {
        if (closing || desktopRestartBusy || autoEnhancementBusy) return;
        try
        {
            if (!EnhancementPreferences().Load().Enabled) return;
            if (smokeOutput is not null) throw new InvalidOperationException("隔离检查通过专用入口创建测试快捷方式。");
            var dialog = new SaveFileDialog { Title = "保存 Codex 增强启动快捷方式", Filter = "Windows 快捷方式|*.lnk", FileName = "Codex（增强）.lnk",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AddExtension = true, DefaultExt = ".lnk", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return;
            SaveEnhancedShortcut(dialog.FileName);
        }
        catch (Exception error) { automaticEnhancementStatus = "快捷方式未创建：" + error.Message; RefreshAutomaticEnhancements(); }
    }
    private void SaveEnhancedShortcut(string path)
    {
        if (smokeOutput is not null)
        {
            if (smokeCreateEnhancedShortcut is null) throw new InvalidOperationException("隔离检查不能写入真实桌面快捷方式。");
            smokeCreateEnhancedShortcut(path, Environment.ProcessPath!, home);
        }
        else EnhancedLaunchShortcut.Create(path, Environment.ProcessPath!, home);
        automaticEnhancementStatus = "快捷方式已创建。以后双击它即可增强启动；Dock 在后台提供增强。"; RefreshAutomaticEnhancements();
    }

    internal async Task RequestEnhancedLaunchAsync(string directory)
    {
        try { await RequestEnhancedLaunchCoreAsync(directory); }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (closing) return;
            RestoreWindow(); ShowPage("enhancements");
            automaticEnhancementStatus = "增强启动未完成：" + error.Message; RefreshAutomaticEnhancements();
            SetStatus(automaticEnhancementStatus, true);
        }
    }
    private async Task RequestEnhancedLaunchCoreAsync(string directory)
    {
        if (!desktopInitializationReady) { pendingEnhancedHome = directory; return; }
        if (closing || desktopRestartBusy || autoEnhancementBusy || mobileBusy || gatewayPolling || gatewayLaunchInProgress)
        { RestoreWindow(); SetStatus("正在处理操作，请完成后再次增强启动。", true); return; }
        if (!string.Equals(home, directory, StringComparison.OrdinalIgnoreCase))
        {
            RestoreWindow();
            if (!ResolveUnsaved()) return;
            await StopSpeedSessionAsync(); SwitchConfigurationHome(directory);
        }
        if (!EnhancementPreferences().Load().Enabled)
        { RestoreWindow(); ShowPage("enhancements"); automaticEnhancementStatus = "此配置目录的增强启动已关闭，可在这里选择是否开启。"; RefreshAutomaticEnhancements(); return; }
        automaticEnhancements.ResetRetry();
        await TryAutomaticEnhancementsAsync();
        if (snapshot is null || ConnectionRecoveryBox.IsChecked != true && !(SpeedCompatibilityBox.IsChecked == true && DesktopSpeedSettings.Eligible(store, snapshot)))
        { RestoreWindow(); ShowPage("enhancements"); automaticEnhancementStatus = "请先选择一项适用于当前连接的增强。"; RefreshAutomaticEnhancements(); return; }
        if (speedSession is { IsApplied: true } && SessionHasCurrentFeatures() && sessionAppliedHash == DesktopEnhancementSettings.SnapshotHash(snapshot) && lastAppliedDesktopTarget is { } existing)
        { if (smokeOutput is null) new CodexDesktop().Activate(existing); return; }
        RestoreWindow(); ShowPage("enhancements"); OpenDesktopApplicationPanel();
        await RestartCodexFromReviewAsync(enhancedLaunch: true);
        if (desktopRestarted && lastAppliedDesktopTarget is { } opened)
        {
            CloseOverlay(); if (smokeOutput is null) new CodexDesktop().Activate(opened);
            if (tray is not null) Hide();
        }
    }
    private bool SessionHasCurrentFeatures()
    {
        try { return speedSession is { } session && session.HasConnections == (ConnectionRecoveryBox.IsChecked == true) &&
            session.HasSpeed == (SpeedCompatibilityBox.IsChecked == true && snapshot is not null && DesktopSpeedSettings.Eligible(store, snapshot)); }
        catch { return false; }
    }
}
