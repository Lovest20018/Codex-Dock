using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunWorkspaceTaskFlowSmokeAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        async Task Settle()
        { for (var i = 0; i < 250 && (FilePreviewPanel.IsReading || mobileRefreshing); i++) await Task.Delay(20); await Task.Delay(100); Root.UpdateLayout(); }
        var previous = viewModel.Home; var size = AppWindow.Size;
        var home = Path.Combine(output, "flow-home"); Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='flow-model'\nopenai_base_url='https://flow.invalid/v1'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-flow-key\"}");
        try
        {
            await SwitchHomeAsync(home); NavigateTo(ModelsNav); AppWindow.Resize(new(1120, 820)); await Settle();
            PageTitle.Focus(FocusState.Programmatic); PageScroll.ChangeView(null, 380, null, true); await Settle(); var offset = PageScroll.VerticalOffset;
            Check(offset > 250, "Task flow fixture positions the actual model page below its initial viewport");
            NavigateTo(ModelsNav); await Settle();
            Check(Math.Abs(PageScroll.VerticalOffset - offset) < 2, "Selecting the already active page preserves the current scroll position");
            NavigateTo(ConnectionNav); await Settle(); OpenModelsClicked(WorkspacePanel.WorkspaceNextButton, new()); await Settle();
            var position = ModelsPanel.BehaviorModelInput.TransformToVisual(PageScroll).TransformPoint(new());
            Check(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), ModelsPanel.BehaviorModelInput) && position.Y >= -1 && position.Y + ModelsPanel.BehaviorModelInput.ActualHeight <= PageScroll.ActualHeight + 1,
                "Opening advanced model editing focuses the model input after any remembered page position");
            var original = store.Load(home); var runtime = (SmokeDesktopApplyRuntime)applicationRuntime;
            lastApplication = new(DesktopApplyOutcome.ExitBlocked, original, null, false, false, new("退出受阻", "模拟实例仍未退出", true)); RefreshView(); NavigateTo(WorkspaceNav); await Settle();
            Check(WorkspacePanel.WorkspaceNextTitle.Text.Contains("未退出") && WorkspacePanel.WorkspaceNextButton.Content.ToString() == "查看应用结果", "Workbench distinguishes an exit failure from a merely loaded configuration");
            var restarts = runtime.Restarts; WorkspaceNextClicked(WorkspacePanel.WorkspaceNextButton, new()); await Settle();
            Check(currentPage == EnhancementsNav && runtime.Restarts == restarts, "Inspecting an unsuccessful application opens its result without forcing or restarting a desktop");
            lastApplication = new(DesktopApplyOutcome.Conflict, original, null, false, false, new("配置冲突", "模拟配置已变化", true)); RefreshView(); NavigateTo(WorkspaceNav);
            Check(WorkspacePanel.WorkspaceNextTitle.Text.Contains("变化") && WorkspacePanel.WorkspaceNextButton.Content.ToString() == "核对配置来源", "Workbench identifies an external configuration conflict and offers read only diagnosis");
            WorkspaceNextClicked(WorkspacePanel.WorkspaceNextButton, new()); await Settle();
            Check(currentPage == SourcesNav && sourceResult is null && runtime.Restarts == restarts, "Conflict diagnosis opens the source page without launching a native read or reapplying stale files");
            configurationResult = new(ConfigurationSaveOutcome.Saved, "fixture saved", original);
            lastApplication = new(DesktopApplyOutcome.Cancelled, original, null, false, false, new("已取消", "fixture cancelled")); RefreshView(); NavigateTo(WorkspaceNav);
            Check(WorkspacePanel.WorkspaceNextTitle.Text == "配置已保存，等待应用" && WorkspacePanel.WorkspaceNextButton.Content.ToString()!.Contains("使用新设置"), "Cancelling application retains the saved configuration's next step without claiming success");
            lastApplication = null; configurationResult = null;
            await LoadProfileAsync("flow-profile"); ProfilesPanel.ProfileDocumentInput.Text = "model='flow-profile-edit'\n"; RefreshView();
            Check(WorkspaceNextStep().Open && PendingChangesList.Children.Count == 1 && profileDraft.HasDraft && original.SameContents(store.Load(home)),
                "Pending changes shows the independent Profile while Home keeps the normal Codex action");
            profileDraft.Load(null); NavigateTo(ConnectionNav); await Settle();
            var gap = ConnectionPanel.ConnectionFields.TransformToVisual(PageScroll).TransformPoint(new()).Y;
            Check(gap >= 0 && gap < 35, "Closed global warnings and login guidance do not reserve empty rows above the connection fields");
            await CaptureAsync(output, "task-flow-connection-light.png");
            var mobile = (SmokeMobileRuntime)mobileRuntime; mobile.Checked = false; mobile.PairImage = Convert.FromBase64String(FixturePairPng);
            NavigateTo(MobileNav); await Settle(); var calls = mobile.Trace.Count;
            Check(MobilePanel.MobileSettingsCard.Visibility == Visibility.Visible && MobileFooter.Visibility == Visibility.Visible &&
                MobilePanel.MobileEntryStepText.Text.Contains("当前") && !MobilePanel.CreateMobilePairButton.IsEnabled && MobilePanel.MobileAdvancedEntry.IsExpanded == false,
                "First-time pairing has its setup fields and save action on the same page with the port collapsed");
            MobilePanel.MobileUrlInput.Text = "https://flow-phone.invalid"; MobilePanel.MobileUsernameInput.Text = "flow-user"; MobilePanel.MobilePasswordInput.Password = "fake-flow-phone-password";
            smokeDialog = _ => Task.FromResult(ContentDialogResult.None);
            await SaveMobileAsync(start: false);
            Check(mobileDraft.HasDraft && mobile.Trace.Count == calls && mobilePair is null,
                "Cancelling phone setup keeps its draft and does not offer pairing against an unsaved entry");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            await SaveMobileAsync(start: false);
            Check(currentPage == MobileNav && GetMobileSetupStage() == MobileSetupStage.Start && MobilePanel.StartMobilePairingButton.IsEnabled &&
                mobile.Trace.Skip(calls).Where(action => action != "devices").SequenceEqual(new[] { "save" }) && !mobileDraft.HasDraft,
                "Saving the entry advances to the start step with the gateway still stopped");
            await StartMobilePairingAsync();
            Check(GetMobileSetupStage() == MobileSetupStage.Check && MobilePanel.CheckMobileEntryButton.IsEnabled &&
                mobile.Trace.Skip(calls).Where(action => action != "devices").SequenceEqual(new[] { "save", "start" }),
                "Starting the service exposes HTTPS detection and does not generate a pairing code");
            mobile.EntryFailure = true;
            await CheckMobileEntryAsync();
            Check(currentPage == MobileNav && mobilePair is null && !mobileDraft.HasDraft && mobile.Trace.Skip(calls).Where(action => action != "devices").SequenceEqual(new[] { "save", "start", "check" }) &&
                MobilePanel.CheckMobileEntryButton.Content.ToString() == "重新检测" && MobilePanel.MobileEntryHelpButton.Visibility == Visibility.Visible,
                "HTTPS detection failure keeps the current step and offers retry and forwarding help");
            var afterFailure = mobile.Trace.Count;
            mobile.EntryFailure = false;
            await CheckMobileEntryAsync();
            await GenerateMobilePairAsync();
            Check(mobilePair is not null && MobilePanel.MobilePairImage.Visibility == Visibility.Visible && MobilePanel.MobileNextStepText.Text.Contains("检测通过") &&
                mobile.Trace.Skip(afterFailure).Where(action => action != "devices").SequenceEqual(new[] { "check", "pair" }) && original.SameContents(store.Load(home)),
                "Pair retry reuses the saved running service and renders a QR without modifying Codex routing");
            mobile.PairState = "used"; mobile.CodexReadiness = "configure"; gatewayTicks = 3;
            await MobilePairTickAsync();
            Check(mobilePair is null && MobilePanel.MobileStatusCard.Visibility == Visibility.Visible && MobilePanel.MobileStepsPanel.Visibility == Visibility.Collapsed &&
                MobilePanel.MobileAccessStatus.Text.Contains("尚无") && MobilePanel.OpenMobileManagerButton.IsEnabled,
                "Successful scanning collapses the four steps into a summary and leaves Codex access discoverable");
            Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(720, 540)); await Settle();
            await CaptureAsync(output, "phone-paired-summary-compact-dark.png");
            PairAnotherDeviceClicked(MobilePanel.PairAnotherDeviceButton, new()); await Settle();
            var primaryPosition = MobilePanel.CreateMobilePairButton.TransformToVisual(Root).TransformPoint(new());
            Check(GetMobileSetupStage() == MobileSetupStage.Pair && primaryPosition.Y >= 0 && primaryPosition.Y + MobilePanel.CreateMobilePairButton.ActualHeight <= Root.ActualHeight,
                "Pairing another device goes back to the QR step within the compact viewport");
            await CaptureAsync(output, "task-flow-phone-compact-dark.png");
            NavigateTo(WorkspaceNav); AppWindow.Resize(new(1120, 820)); Root.RequestedTheme = ElementTheme.Light;
            lastApplication = new(DesktopApplyOutcome.ExitBlocked, original, null, false, false, new("退出受阻", "模拟实例仍未退出", true)); RefreshView(); await Settle();
            await CaptureAsync(output, "task-flow-workspace-result.png");
        }
        finally
        {
            lastApplication = null; configurationResult = null; profileDraft.Load(null); mobileDraft.Load(mobileDraft.Settings); LoadConnectionDraft();
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); await SwitchHomeAsync(previous); smokeDialog = null;
            AppWindow.Resize(size); Root.RequestedTheme = ElementTheme.Light; NavigateTo(WorkspaceNav);
        }
    }
}
