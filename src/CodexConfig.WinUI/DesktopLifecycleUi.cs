using CodexConfig.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private NativeTrayIcon? tray;
    private bool exitRequested, enhancedLaunchBusy;
    private string? pendingEnhancedHome;
    private string automaticStatus = "已开启。成功应用一次后，Dock 会记住本次实例并自动接回。";
    private string backgroundStatus = "窗口关闭后收起到托盘，增强继续运行。通过托盘菜单退出 Codex Dock。";

    private void InitializeTray()
    {
        if (tray is not null) return;
        try
        {
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "codex-dock.ico");
            AppWindow.SetIcon(icon);
            using var taskbarTheme = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var lightTaskbar = taskbarTheme?.GetValue("SystemUsesLightTheme") is int value && value != 0;
            var trayIcon = Path.Combine(AppContext.BaseDirectory, "Assets", lightTaskbar ? "tray-on-light.ico" : "tray-on-dark.ico");
            tray = new(WinRT.Interop.WindowNative.GetWindowHandle(this), trayIcon,
                () => DispatcherQueue.TryEnqueue(RestoreWindow),
                () => DispatcherQueue.TryEnqueue(() => { RestoreWindow(); NavigateTo(EnhancementsNav); }),
                () => DispatcherQueue.TryEnqueue(() => { RestoreWindow(); NavigateTo(GatewaysNav); }),
                () => DispatcherQueue.TryEnqueue(async () => { RestoreWindow(); await StopGatewaysAndExitAsync(); }),
                () => DispatcherQueue.TryEnqueue(async () => { RestoreWindow(); exitRequested = true; await ExitWindowAsync(); }),
                () => DispatcherQueue.TryEnqueue(() => { RestoreWindow(); backgroundStatus = "托盘暂不可用，请保持窗口运行；关闭窗口将退出程序。"; RefreshView(); }),
                () => DispatcherQueue.TryEnqueue(async () => await RequestInstallerExitAsync()));
        }
        catch (Exception error)
        { backgroundStatus = "托盘未能创建：" + error.Message + " 关闭窗口将退出程序。"; }
        RefreshView();
    }

    internal void RestoreWindow()
    {
        if (closing) return;
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized) presenter.Restore();
        Activate(); NativeTrayIcon.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    private bool TryHideToTray()
    {
        if (tray?.IsAvailable != true) return false;
        AppWindow.Hide(); return true;
    }

    private async Task ExitWindowAsync()
    {
        if (await StopBeforeCloseAsync())
        { runtimeTimer.Stop(); lifetime.Cancel(); tray?.Dispose(); tray = null; allowClose = true; Close(); }
        else exitRequested = false;
    }

    private async Task RequestInstallerExitAsync()
    {
        if (closing) return;
        if (ApplicationBusy || dialogOpen || closePrompt || enhancedLaunchBusy)
        {
            RestoreWindow();
            SetShellError("安装正在等待 Dock 退出", "请先完成或取消当前操作，再从托盘退出 Codex Dock。");
            return;
        }
        if (connectionDraft.HasDraft || profileDraft.HasDraft || HasTemplateDraft || mobileDraft.HasDraft || applicationRuntime.IsEnhancementRunning)
            RestoreWindow();
        await RequestWindowExitAsync();
    }

    private async Task TryAutomaticEnhancementsAsync()
    {
        var expectedHome = viewModel.Home;
        try
        {
            await automaticEnhancements.TryResumeAsync(viewModel.Snapshot, () => !closing && !ManualApplicationBusy && !dialogOpen &&
                !closePrompt && !HasPendingRecovery && !connectionDraft.HasDraft && viewModel.PreferencesAvailable && viewModel.CanApplyConfiguration && viewModel.Home == expectedHome, lifetime.Token);
        }
        catch (Exception error) { if (!closing) { automaticStatus = error.Message; RefreshView(); } }
    }

    private async void CreateEnhancedShortcutClicked(object sender, RoutedEventArgs e)
    {
        if (ApplicationBusy || dialogOpen || closing || !viewModel.AutomaticReconnectEnabled) return;
        if (options.SmokeOutput is not null) { automaticStatus = "隔离检查只允许在测试目录创建快捷方式。"; RefreshView(); return; }
        dialogOpen = true; RefreshView();
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Desktop, SuggestedFileName = "Codex（增强）" };
            picker.FileTypeChoices.Add("Windows 快捷方式", new List<string> { ".lnk" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is not null) SaveEnhancedShortcut(file.Path);
        }
        catch (Exception error) { automaticStatus = "快捷方式未创建：" + error.Message; }
        finally { dialogOpen = false; RefreshView(); }
    }

    private void SaveEnhancedShortcut(string path)
    {
        if (options.SmokeOutput is { } output && !Path.GetFullPath(path).StartsWith(Path.GetFullPath(output) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("隔离检查不能写入真实桌面快捷方式。");
        EnhancedLaunchShortcut.Create(path, Environment.ProcessPath!, viewModel.Home);
        automaticStatus = "快捷方式已创建。双击它即可增强启动，Dock 在后台提供增强。";
        RefreshView();
    }

    internal async Task RequestEnhancedLaunchAsync(string home)
    {
        if (!initialized) { pendingEnhancedHome = home; return; }
        if (closing) return;
        if (ApplicationBusy || dialogOpen || enhancedLaunchBusy)
        { RestoreWindow(); automaticStatus = "正在处理操作，请完成后再次增强启动。"; RefreshView(); return; }
        enhancedLaunchBusy = true;
        try
        {
            if (!await ResolveProfileDraftAsync() || !await ResolveTemplateEditAsync() || !await ResolveConnectionDraftAsync() || !await ResolveMobileDraftAsync()) return;
            if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(viewModel.Home)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)), StringComparison.OrdinalIgnoreCase) && !await SwitchHomeAsync(home)) return;
            NavigateTo(EnhancementsNav);
            if (!viewModel.PreferencesAvailable || !viewModel.EnhancementPreferences.AutomaticSettings.Load().Enabled)
            { RestoreWindow(); automaticStatus = "此配置目录的增强启动已关闭，可在这里选择是否开启。"; return; }
            automaticEnhancements.ResetRetry(); await TryAutomaticEnhancementsAsync();
            if (viewModel.Snapshot is not { } saved || !viewModel.ConnectionRecoveryEnabled &&
                !(viewModel.SpeedCompatibilityEnabled && DesktopSpeedSettings.Eligible(store, saved)))
            { RestoreWindow(); automaticStatus = "请先选择一项适用于当前连接的增强。"; return; }
            if (desktopEnhancements.Session is { IsRunning: true, IsApplied: true } && desktopEnhancements.AppliedTarget is { } existing &&
                desktopEnhancements.AppliedSnapshotHash == DesktopEnhancementSettings.SnapshotHash(store.Load(saved.Home)) &&
                new CodexConfig.Application.NativeAutomaticEnhancementRuntime(desktopEnhancements).HasFeatures(
                    viewModel.SpeedCompatibilityEnabled && DesktopSpeedSettings.Eligible(store, saved), viewModel.ConnectionRecoveryEnabled))
            {
                if (options.SmokeOutput is null) new CodexDesktop().Activate(existing);
                TryHideToTray(); return;
            }
            RestoreWindow(); await ApplyDesktopAsync(enhancedLaunch: true);
            if (lastApplication is { DesktopOpened: true, Outcome: CodexConfig.Application.DesktopApplyOutcome.Completed })
                TryHideToTray();
        }
        catch (Exception error) { RestoreWindow(); automaticStatus = "增强启动未完成：" + error.Message; }
        finally { enhancedLaunchBusy = false; if (!closing) RefreshView(); }
    }
}
