using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private sealed class SmokeDesktopApplyRuntime(ClientEnhancementPreferencesService preferences, ConfigStore store, string home) : IDesktopApplyRuntime
    {
        public IReadOnlyList<CodexDesktopTarget> Targets { get; set; } =
        [
            new(Path.Combine(home, "Codex.exe"), [new(101, 1, 134390198764749156, Path.Combine(home, "Codex.exe"))]) { UserDataDirectory = Path.Combine(home, "profile-one") },
            new(Path.Combine(home, "Codex.exe"), [new(102, 1, 134390198764749157, Path.Combine(home, "Codex.exe"))]) { UserDataDirectory = Path.Combine(home, "profile-two") }
        ];
        public bool IsEnhancementRunning { get; private set; }
        public DesktopSpeedPlan? AppliedPlan { get; private set; }
        public bool RefuseExit, FailAttachment;
        public int Restarts, Attachments, Activations;
        public CodexDesktopTarget? LastTarget;
        public TaskCompletionSource? RestartGate;
        public bool LastForce;
        public Task<IReadOnlyList<CodexDesktopTarget>> FindTargetsAsync(CancellationToken cancellation) => Task.FromResult(Targets);
        public bool Activate(CodexDesktopTarget target)
        {
            if (!Targets.Any(current => DesktopApplicationService.SameRoot(target, current))) return false;
            Activations++;
            return true;
        }
        public Task<DesktopEnhancementPreparation> PrepareEnhancementsAsync(CodexDesktopTarget target, ConfigSnapshot saved, CancellationToken cancellation)
        {
            var selected = preferences.Load();
            var speed = selected.SpeedCompatibilityEnabled && DesktopSpeedSettings.Eligible(store, saved);
            return Task.FromResult(!speed && !selected.ConnectionRecoveryEnabled ? new DesktopEnhancementPreparation() :
                new(new([], new Dictionary<string, int>()) { SpeedEnabled = speed, ConnectionsEnabled = selected.ConnectionRecoveryEnabled }, 12345));
        }
        public Task PrepareGatewayAsync(ConfigSnapshot saved, CancellationToken cancellation) => Task.CompletedTask;
        public async Task<CodexDesktopTarget> RestartAsync(CodexDesktopTarget target, ConfigSnapshot saved, Action validateSaved, int? port,
            Action<CodexDesktopPhase> progress, bool forceConfirmed, CancellationToken cancellation)
        {
            validateSaved(); Restarts++; LastTarget = target; LastForce = forceConfirmed;
            progress(CodexDesktopPhase.Closing);
            if (RefuseExit) throw new CodexDesktopExitTimeoutException("模拟实例仍未全部退出，本次没有启动新实例。");
            if (RestartGate is { } gate) await gate.Task.WaitAsync(cancellation);
            validateSaved(); progress(CodexDesktopPhase.Starting);
            var next = target with { Processes = [new(300 + Restarts, 1, 134390198764749200 + Restarts, target.Executable)], AlsoExit = [] };
            Targets = Targets.Where(candidate => !target.ExitRoots.Any(old => candidate.MainProcess?.Id == old.Id)).Append(next).ToArray();
            progress(CodexDesktopPhase.Verifying); return next;
        }
        public Task StopEnhancementsAsync() { IsEnhancementRunning = false; AppliedPlan = null; return Task.CompletedTask; }
        public Task AttachEnhancementsAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port, CancellationToken cancellation)
        {
            Attachments++;
            if (FailAttachment) throw new IOException("模拟增强接入失败，可在同一实例上重试。");
            IsEnhancementRunning = true; AppliedPlan = plan; return Task.CompletedTask;
        }
    }

    private async Task RunDesktopApplicationSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); }
        var runtime = (SmokeDesktopApplyRuntime)applicationRuntime;
        var original = store.Load(options.Home);
        var captured = runtime.Targets.ToArray();
        LoadWorkspace(options.Home); NavigateTo(EnhancementsNav);
        var picker = CreateTargetPicker(captured);
        Check(picker.Choices.SelectedIndex == -1 && !picker.Dialog.IsPrimaryButtonEnabled && picker.ExitOthers.IsChecked != true,
            "Multiple WinUI instances have no implicit selection or default batch exit");
        var shown = picker.Dialog.ShowAsync();
        try
        {
            await CaptureAsync(output, "application-instance-picker.png", picker.Dialog);
            picker.Choices.SelectedIndex = 1; picker.ExitOthers.IsChecked = true;
            Check(picker.Dialog.IsPrimaryButtonEnabled, "Selecting a captured instance enables the target confirmation");
            await CaptureAsync(output, "application-instance-picker-selected.png", picker.Dialog);
        }
        finally { picker.Dialog.Hide(); await shown; }
        var consentCount = 0;
        smokeDialog = dialog =>
        {
            consentCount++;
            return Task.FromResult(ContentDialogResult.None);
        };
        await ApplyDesktopAsync();
        Check(runtime.Restarts == 0 && consentCount == 1 && lastApplication?.Outcome == DesktopApplyOutcome.Cancelled,
            "Cancelling instance selection leaves all desktops untouched");

        smokeDialog = dialog =>
        {
            if (dialog.Content is StackPanel pickerBody)
            {
                ((ListBox)pickerBody.Children[1]).SelectedIndex = 1;
                ((CheckBox)pickerBody.Children[2]).IsChecked = true;
                return Task.FromResult(ContentDialogResult.Primary);
            }
            var content = ((TextBlock)((ScrollViewer)dialog.Content).Content).Text;
            Check(content.Contains("全部退出") && content.Contains("profile-two") && content.Contains(options.Home),
                "WinUI consent names the selected profile directory and complete batch exit before restart");
            return Task.FromResult(ContentDialogResult.Primary);
        };
        runtime.RefuseExit = true;
        await ApplyDesktopAsync();
        Check(runtime.Restarts == 1 && ForceExitButton.Visibility == Visibility.Visible && desktopApplication.BlockedExit?.Target.ExitRoots.Count == 2,
            "A refused WinUI batch exit exposes a separate force action without claiming startup");
        await CaptureAsync(output, "application-exit-blocked.png");

        runtime.Targets = [captured[0]];
        runtime.RefuseExit = false; runtime.FailAttachment = true;
        smokeDialog = dialog => Task.FromResult(ContentDialogResult.Primary);
        await ApplyDesktopAsync(forceExit: true);
        Check(runtime.Restarts == 2 && runtime.LastForce && runtime.LastTarget?.ExitRoots.Count == 2 && desktopApplication.PendingRetry is not null &&
            ApplyButton.Content.ToString() == "重试客户端增强", "Confirmed force retry preserves captured roots and enhancement failure offers attachment-only retry");
        await CaptureAsync(output, "application-enhancement-pending.png");
        runtime.FailAttachment = false;
        var restartCount = runtime.Restarts;
        await ApplyDesktopAsync();
        Check(lastApplication?.Outcome == DesktopApplyOutcome.Completed && runtime.Restarts == restartCount && runtime.Attachments == 2 && !EnhancementsPanel.PendingBar.IsOpen,
            "WinUI enhancement retry applies once to the same opened instance without restarting or rewriting saved files");
        smokeDialog = _ => Task.FromResult(ContentDialogResult.None);
        Check(!await StopBeforeCloseAsync() && !closing && runtime.IsEnhancementRunning,
            "Cancelling preview exit leaves the enhancement eligibility and live connection intact");
        smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);

        desktopApplication.Reset();
        runtime.RestartGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = ApplyDesktopAsync();
        await ApplyDesktopAsync();
        Check(ApplicationBusy && !ApplyButton.IsEnabled && !ChooseHomeButton.IsEnabled && !EnhancementsPanel.ConnectionRecoveryToggle.IsEnabled,
            "Applying disables duplicate launches directory changes and preference edits");
        Check(!await StopBeforeCloseAsync() && !closing, "An in-progress application cannot be abandoned by closing the preview");
        Check(ApplicationFooter.ActualHeight > 0 && ApplicationFooter.ActualWidth <= Root.ActualWidth, "The shared apply controls remain visible outside the scrollable page");
        runtime.RestartGate.SetResult(); await pending; runtime.RestartGate = null;

        File.AppendAllText(Path.Combine(options.Home, "config.toml"), "service_tier='priority'\n");
        restartCount = runtime.Restarts;
        await ApplyDesktopAsync();
        Check(lastApplication?.Outcome == DesktopApplyOutcome.Conflict && runtime.Restarts == restartCount && ApplicationError.IsOpen,
            "Changed configuration blocks WinUI application before any desktop restart");
        smokeDialog = dialog =>
        {
            var content = ((TextBlock)((ScrollViewer)dialog.Content).Content).Text;
            Check(dialog.Title.ToString() == "核对外部变更" && content.Contains("service_tier") && content.Contains("priority"),
                "Reload presents the actual external configuration difference before adopting it");
            return Task.FromResult(ContentDialogResult.Primary);
        };
        await ReloadWorkspaceAsync();
        Check(!ErrorBar.IsOpen && runtime.Restarts == restartCount, "Reviewing and loading external changes does not apply or launch automatically");
        File.WriteAllBytes(Path.Combine(options.Home, "config.toml"), original.Config!);
        await applicationRuntime.StopEnhancementsAsync(); LoadWorkspace(options.Home); desktopApplication.Reset(); lastApplication = null;
        smokeDialog = null; RefreshView();
        Check(original.SameContents(store.Load(options.Home)), "Shared application UI fixtures restore original saved files and never call native process operations");
        await RunModelGatewayWorkerSmokeAsync(output, Check);
    }

    private async Task RunModelGatewayWorkerSmokeAsync(string output, Action<bool, string> check)
    {
        var home = Path.Combine(output, "isolated-model-gateway"); Directory.CreateDirectory(home);
        var connection = MixedConnection.Create(new("https://model-fixture.invalid/v1", "fake-winui-gateway-key", "fixture-model") { Kind = ConnectionKind.ResponsesApi });
        var bytes = connection.Protect();
        File.WriteAllBytes(Path.Combine(home, MixedConnection.FileName), bytes);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='fixture-model'\nopenai_base_url='" + connection.NativeBaseUrl + "'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-local-token\"}");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var snapshot = store.Load(home);
        try
        {
            await new NativeDesktopApplyRuntime(desktopEnhancements, Dispatch).PrepareGatewayAsync(snapshot, cancellation.Token);
            var status = await BridgeProcess.StatusAsync(connection, cancellation.Token);
            check(status is { ProtocolVersion: ModelBridge.ProtocolVersion, Requests: 0 }, "WinUI starts its own isolated model gateway worker without opening a window or contacting a model service");
        }
        finally { await BridgeProcess.StopAsync(bytes); }
        check(await BridgeProcess.StatusAsync(connection) is null, "The WinUI gateway worker shuts down through its authenticated local control route");
    }
}
