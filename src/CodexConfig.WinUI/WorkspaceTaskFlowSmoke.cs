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
        { for (var i = 0; i < 250 && (readingSavedPreview || mobileRefreshing); i++) await Task.Delay(20); await Task.Delay(100); Root.UpdateLayout(); }
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
            NavigateTo(ConnectionNav); await Settle(); OpenModelsClicked(WorkspaceNextButton, new()); await Settle();
            var position = BehaviorModelInput.TransformToVisual(PageScroll).TransformPoint(new());
            Check(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), BehaviorModelInput) && position.Y >= -1 && position.Y + BehaviorModelInput.ActualHeight <= PageScroll.ActualHeight + 1,
                "The choose model link reveals and focuses the only model input after any remembered page position");
            var original = store.Load(home); var runtime = (SmokeDesktopApplyRuntime)applicationRuntime;
            lastApplication = new(DesktopApplyOutcome.ExitBlocked, original, null, false, false, new("退出受阻", "模拟实例仍未退出", true)); RefreshView(); NavigateTo(WorkspaceNav); await Settle();
            Check(WorkspaceNextTitle.Text.Contains("未退出") && WorkspaceNextButton.Content.ToString() == "查看应用结果", "Workbench distinguishes an exit failure from a merely loaded configuration");
            var restarts = runtime.Restarts; WorkspaceNextClicked(WorkspaceNextButton, new()); await Settle();
            Check(currentPage == EnhancementsNav && runtime.Restarts == restarts, "Inspecting an unsuccessful application opens its result without forcing or restarting a desktop");
            lastApplication = new(DesktopApplyOutcome.Conflict, original, null, false, false, new("配置冲突", "模拟配置已变化", true)); RefreshView(); NavigateTo(WorkspaceNav);
            Check(WorkspaceNextTitle.Text.Contains("变化") && WorkspaceNextButton.Content.ToString() == "核对配置来源", "Workbench identifies an external configuration conflict and offers read only diagnosis");
            WorkspaceNextClicked(WorkspaceNextButton, new()); await Settle();
            Check(currentPage == SourcesNav && sourceResult is null && runtime.Restarts == restarts, "Conflict diagnosis opens the source page without launching a native read or reapplying stale files");
            configurationResult = new(ConfigurationSaveOutcome.Saved, "fixture saved", original);
            lastApplication = new(DesktopApplyOutcome.Cancelled, original, null, false, false, new("已取消", "fixture cancelled")); RefreshView(); NavigateTo(WorkspaceNav);
            Check(WorkspaceNextTitle.Text == "配置已保存，等待应用" && WorkspaceNextButton.Content.ToString() == "应用到 Codex", "Cancelling application retains the saved configuration's next step without claiming success");
            lastApplication = null; configurationResult = null;
            await LoadProfileAsync("flow-profile"); ProfileDocumentInput.Text = "model='flow-profile-edit'\n"; RefreshView();
            Check(WorkspaceNextTitle.Text == "还有未保存修改" && WorkspaceNextButton.Content.ToString() == "继续编辑 Profile", "Workbench gives an independent Profile draft a direct next step when the main configuration is clean");
            WorkspaceNextClicked(WorkspaceNextButton, new()); await Settle();
            Check(currentPage == ProfilesNav && profileDraft.HasDraft && original.SameContents(store.Load(home)), "Continuing an independent draft navigates without saving or replacing the main configuration");
            profileDraft.Load(null); NavigateTo(ConnectionNav); await Settle();
            var gap = ConnectionFields.TransformToVisual(PageScroll).TransformPoint(new()).Y;
            Check(gap >= 0 && gap < 35, "Closed global warnings and login guidance do not reserve empty rows above the connection fields");
            await CaptureAsync(output, "task-flow-connection-light.png");
            var mobile = (SmokeMobileRuntime)mobileRuntime; mobile.Checked = false;
            NavigateTo(MobileNav); await Settle(); var calls = mobile.Trace.Count;
            Check(MobileNextStepText.Text.Contains("第一步") && !CreateMobilePairButton.IsEnabled && ContinueMobilePairButton.Visibility == Visibility.Collapsed,
                "Unconfigured pairing explains its setup prerequisite without enabling a pair or starting a service");
            OpenMobileSettingsClicked(MobileSetupLinkButton, new()); await Settle();
            MobileUrlInput.Text = "https://flow-phone.invalid"; MobileUsernameInput.Text = "flow-user"; MobilePasswordInput.Password = "fake-flow-phone-password";
            smokeDialog = _ => Task.FromResult(ContentDialogResult.None);
            Check(!await SaveMobileAsync() && mobileDraft.HasDraft && mobile.Trace.Count == calls && ContinueMobilePairButton.Visibility == Visibility.Collapsed,
                "Cancelling phone setup keeps its draft and does not offer pairing against an unsaved entry");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(await SaveMobileAsync() && ContinueMobilePairButton.Visibility == Visibility.Visible && !mobileDraft.HasDraft,
                "Successful phone save and confirmed startup offer the next pairing step in the same footer");
            var afterSave = mobile.Trace.Count; OpenPairingPageClicked(ContinueMobilePairButton, new()); await Settle();
            Check(currentPage == MobileNav && MobileNextStepText.Text.Contains("检测 HTTPS") && CheckMobileEntryButton.IsEnabled &&
                mobile.Trace.Skip(afterSave).All(action => action == "devices") && original.SameContents(store.Load(home)),
                "Continuing to pairing only navigates and reads status without automatic HTTPS detection or configuration writes");
            await RunMobileControlAsync(async (saved, eligible) => AcceptMobileResult(await mobileConnection.CheckEntryAsync(saved, eligible, lifetime.Token)));
            Check(MobileNextStepText.Text.Contains("检测通过") && CreateMobilePairButton.IsEnabled, "Explicit entry detection advances the guidance to pairing without generating a code automatically");
            Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(720, 540)); NavigateTo(MobileSettingsNav); await Settle();
            var nextPosition = ContinueMobilePairButton.TransformToVisual(Root).TransformPoint(new());
            Check(nextPosition.Y >= 0 && nextPosition.Y + ContinueMobilePairButton.ActualHeight <= Root.ActualHeight,
                "The save to pairing continuation remains reachable in the compact phone footer");
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
