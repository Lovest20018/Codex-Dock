using CodexConfig.Application;
using CodexConfig.Core;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private Func<CancellationToken, Task>? smokeAutomaticResume;
    private async Task RunDesktopLifecycleSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); }
        var preferences = viewModel.EnhancementPreferences;
        LoadWorkspace(options.Home); NavigateTo(EnhancementsNav);
        var original = store.Load(options.Home);
        SetShellError("未能选择配置目录", "fixture picker error"); RefreshView(); RefreshView();
        Check(ErrorBar.IsOpen && ErrorBar.Title == "未能选择配置目录" && ErrorBar.Message == "fixture picker error",
            "Background refresh preserves actionable picker reload and exit errors until another user operation resolves them");
        shellError = ""; RefreshView();
        preferences.SetConnectionRecovery(true); preferences.SetSpeedCompatibility(false); preferences.SetAutomaticReconnect(true);
        var plan = new DesktopSpeedPlan([], new Dictionary<string, int>()) { SpeedEnabled = false, ConnectionsEnabled = true };
        preferences.AutomaticSettings.Remember(new(Path.Combine(options.Home, "fixture.exe"), options.Home, 42, 100, 12345,
            DesktopEnhancementSettings.SnapshotHash(original), DesktopSpeedSession.PlanHash(plan)));
        LoadWorkspace(options.Home); automaticEnhancements.ResetRetry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var resumes = 0;
        smokeAutomaticResume = async cancellation => { resumes++; entered.TrySetResult(); await release.Task.WaitAsync(cancellation); };
        var first = TryAutomaticEnhancementsAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = TryAutomaticEnhancementsAsync();
        await WaitForQueuedRefreshAsync();
        Check(automaticEnhancements.IsBusy && !ApplyButton.IsEnabled && !EnhancementsPanel.CreateEnhancedShortcutButton.IsEnabled && resumes == 1,
            "WinUI automatic reconnect shares one operation and disables conflicting launch actions");
        release.TrySetResult(); await Task.WhenAll(first, second);
        await WaitForQueuedRefreshAsync();
        Check(resumes == 1 && EnhancementsPanel.AutomaticStatus.Text.Contains("已自动接回") && original.SameContents(store.Load(options.Home)),
            "WinUI resumes a remembered fake desktop without restarting reloading or writing its configuration");
        automaticEnhancements.Cancel();
        Check(!automaticEnhancements.IsBusy && ApplyButton.IsEnabled, "A completed automatic attempt has no disposed cancellation source and releases the UI");
        var parsed = WinUiLaunchOptions.Parse(["--home", options.Home, "--enhanced-launch", "--start-in-tray"]);
        Check(parsed.Home == options.Home && parsed.EnhancedLaunch && parsed.StartInTray && parsed.SmokeOutput is null,
            "Enhanced and tray launch arguments retain the explicitly selected configuration directory");
        try { WinUiLaunchOptions.Parse(["--smoke-test", Path.Combine(output, "forbidden"), "--enhanced-launch"]); throw new InvalidOperationException("Unsafe smoke arguments accepted."); }
        catch (ArgumentException) { Check(!Directory.Exists(Path.Combine(output, "forbidden")), "Smoke mode rejects launch actions before creating any workspace"); }
        var shortcut = Path.Combine(output, "Codex enhanced fixture.lnk"); SaveEnhancedShortcut(shortcut);
        Check(File.Exists(shortcut) && EnhancementsPanel.AutomaticStatus.Text.Contains("已创建"), "WinUI creates an actual Windows enhanced shortcut only inside the fixture directory");
        preferences.SetAutomaticReconnect(false); LoadWorkspace(options.Home);
        var restarts = ((SmokeDesktopApplyRuntime)applicationRuntime).Restarts;
        await RequestEnhancedLaunchAsync(options.Home);
        Check(EnhancementsPanel.AutomaticStatus.Text.Contains("已关闭") && ((SmokeDesktopApplyRuntime)applicationRuntime).Restarts == restarts,
            "An enhanced launch respects the disabled per-directory setting without opening a desktop");

        var runtime = (SmokeDesktopApplyRuntime)applicationRuntime;
        dialogOpen = true; RefreshView();
        await RequestEnhancedLaunchAsync(Path.Combine(output, "blocked-home"));
        await SwitchHomeAsync(Path.Combine(output, "blocked-home"));
        Check(viewModel.Home == options.Home && runtime.Restarts == restarts && !ApplyButton.IsEnabled && !ChooseHomeButton.IsEnabled,
            "A native picker or confirmation blocks forwarded launches directory changes and apply actions");
        dialogOpen = false; RefreshView();
        preferences.SetAutomaticReconnect(true); LoadWorkspace(options.Home);
        await runtime.StopEnhancementsAsync();
        smokeAutomaticResume = _ => throw new DesktopEnhancementResumeException("fixture desktop absent");
        runtime.Targets = [new(Path.Combine(options.Home, "fixture.exe"), []) { UserDataDirectory = options.Home }];
        smokeDialog = _ => throw new InvalidOperationException("An absent desktop must not require a redundant restart confirmation.");
        await RequestEnhancedLaunchAsync(options.Home);
        Check(runtime.Restarts == restarts + 1 && runtime.IsEnhancementRunning && lastApplication is { DesktopOpened: true, Outcome: DesktopApplyOutcome.Completed },
            "An explicit enhanced launch opens an absent fake desktop once through the shared application service");
        preferences.SetSpeedCompatibility(true); LoadWorkspace(options.Home); var confirmations = 0;
        smokeDialog = _ => { confirmations++; return Task.FromResult(Microsoft.UI.Xaml.Controls.ContentDialogResult.None); };
        await RequestEnhancedLaunchAsync(options.Home);
        Check(confirmations == 1 && runtime.Restarts == restarts + 1 && runtime.IsEnhancementRunning,
            "Changed enhancement choices require confirmation before restarting a running desktop and cancellation retains its session");
        preferences.SetSpeedCompatibility(false); LoadWorkspace(options.Home);
        var other = Path.Combine(output, "shortcut-other-home");
        await RequestEnhancedLaunchAsync(other);
        Check(viewModel.Home == other && !viewModel.AutomaticReconnectEnabled && preferences.AutomaticSettings.Load().Binding is null && runtime.Restarts == restarts + 1,
            "A forwarded directory uses its own opt-in and never inherits another workspace's desktop binding");
        await SwitchHomeAsync(options.Home);
        smokeDialog = null;

        InitializeTray();
        Check(tray?.IsAvailable == true, "WinUI registers its own native notification-area icon");
        NativeTrayIcon.RequestWindowClose(WinRT.Interop.WindowNative.GetWindowHandle(this)); await Task.Delay(100);
        Check(!AppWindow.IsVisible && !closing && tray?.IsAvailable == true,
            "Closing a WinUI window hides it to the tray without ending its enhancement lifetime");
        NativeTrayIcon.RequestTrayOpen(WinRT.Interop.WindowNative.GetWindowHandle(this)); await Task.Delay(100);
        Check(AppWindow.IsVisible && !closing, "A tray restore makes the same WinUI window accessible again");
        await runtime.AttachEnhancementsAsync(new(Path.Combine(options.Home, "fixture.exe"), []), original, plan, 12345, lifetime.Token);
        smokeDialog = _ => Task.FromResult(Microsoft.UI.Xaml.Controls.ContentDialogResult.None);
        await RequestWindowExitAsync();
        Check(!closing && runtime.IsEnhancementRunning && tray?.IsAvailable == true && AppWindow.IsVisible,
            "Cancelling a tray exit preserves both the live enhancement and the accessible notification icon");
        smokeDialog = null; await runtime.StopEnhancementsAsync();
        tray!.Dispose(); tray = null;
        Check(!TryHideToTray() && AppWindow.IsVisible, "An unavailable tray keeps the window accessible for safe exit");

        var scope = "winui-smoke-" + Guid.NewGuid().ToString("N");
        var request = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var primary = new DesktopInstance(() => shown.TrySetResult(), true, scope, value => request.TrySetResult(value)))
        using (var quiet = new DesktopInstance(() => { }, true, scope))
        {
            Check(primary.IsPrimary && !quiet.IsPrimary && !shown.Task.IsCompleted, "A second quiet launch leaves the primary preview in the background");
            await quiet.SendEnhancedLaunchAsync(options.Home);
            Check(await request.Task.WaitAsync(TimeSpan.FromSeconds(3)) == options.Home,
                "An enhanced launch forwards its configuration directory through the shared current-user pipe");
            using var manual = new DesktopInstance(() => { }, false, scope);
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(!manual.IsPrimary, "An ordinary second launch restores the original preview instead of creating another window");
        }
        preferences.SetAutomaticReconnect(true); LoadWorkspace(options.Home);
        PageScroll.ChangeView(null, double.MaxValue, null, true);
        await CaptureAsync(output, "lifecycle-enhancements.png");
        Check(EnhancementsPanel.CreateEnhancedShortcutButton.IsEnabled && original.SameContents(store.Load(options.Home)), "Tray shortcut and reconnect verification preserve all original configuration bytes");
        smokeAutomaticResume = null;
    }
    private sealed class SmokeAutomaticRuntime(MainWindow window) : IAutomaticEnhancementRuntime
    {
        private SmokeDesktopApplyRuntime Runtime => (SmokeDesktopApplyRuntime)window.applicationRuntime;
        public bool IsRunning => Runtime.IsEnhancementRunning;
        public bool IsApplied => IsRunning;
        public bool HasFeatures(bool speed, bool connections) => Runtime.AppliedPlan is { } plan && plan.SpeedEnabled == speed && plan.ConnectionsEnabled == connections;
        public Task<IReadOnlyList<CodexDesktopTarget>> FindTargetsAsync(CancellationToken cancellation)
        {
            var binding = window.viewModel.EnhancementPreferences.AutomaticSettings.Load().Binding!;
            return Task.FromResult<IReadOnlyList<CodexDesktopTarget>>([new(binding.Executable,
                [new(binding.ProcessId, 1, binding.Started, binding.Executable)]) { UserDataDirectory = binding.UserDataDirectory }]);
        }
        public Task<DesktopSpeedPlan> InspectAsync(CodexDesktopTarget target, bool speed, bool connections, CancellationToken cancellation)
            => Task.FromResult(new DesktopSpeedPlan([], new Dictionary<string, int>()) { SpeedEnabled = speed, ConnectionsEnabled = connections });
        public Task StopAsync() => Runtime.StopEnhancementsAsync();
        public async Task ResumeAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port, CancellationToken cancellation)
        {
            if (window.smokeAutomaticResume is null) throw new InvalidOperationException("隔离检查未提供自动接回，未访问真实桌面。");
            await window.smokeAutomaticResume(cancellation);
            await Runtime.AttachEnhancementsAsync(target, saved, plan, port, cancellation);
        }
    }
}
