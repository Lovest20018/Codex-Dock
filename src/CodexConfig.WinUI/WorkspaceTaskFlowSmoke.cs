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
                "Opening advanced model editing focuses the model input after any remembered page position");
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
            Check(WorkspaceNextTitle.Text == "配置已保存，等待应用" && WorkspaceNextButton.Content.ToString()!.Contains("使用新设置"), "Cancelling application retains the saved configuration's next step without claiming success");
            lastApplication = null; configurationResult = null;
            await LoadProfileAsync("flow-profile"); ProfileDocumentInput.Text = "model='flow-profile-edit'\n"; RefreshView();
            Check(WorkspaceNextStep().Open && PendingChangesList.Children.Count == 1 && profileDraft.HasDraft && original.SameContents(store.Load(home)),
                "Pending changes shows the independent Profile while Home keeps the normal Codex action");
            profileDraft.Load(null); NavigateTo(ConnectionNav); await Settle();
            var gap = ConnectionFields.TransformToVisual(PageScroll).TransformPoint(new()).Y;
            Check(gap >= 0 && gap < 35, "Closed global warnings and login guidance do not reserve empty rows above the connection fields");
            await CaptureAsync(output, "task-flow-connection-light.png");
            var mobile = (SmokeMobileRuntime)mobileRuntime; mobile.Checked = false; mobile.PairImage = Convert.FromBase64String(FixturePairPng);
            NavigateTo(MobileNav); await Settle(); var calls = mobile.Trace.Count;
            Check(MobileSettingsCard.Visibility == Visibility.Visible && MobileFooter.Visibility == Visibility.Visible &&
                MobileNextStepText.Text.Contains("第一步") && !CreateMobilePairButton.IsEnabled && MobileAdvancedEntry.IsExpanded == false,
                "First-time pairing has its setup fields and save action on the same page with the port collapsed");
            MobileUrlInput.Text = "https://flow-phone.invalid"; MobileUsernameInput.Text = "flow-user"; MobilePasswordInput.Password = "fake-flow-phone-password";
            smokeDialog = _ => Task.FromResult(ContentDialogResult.None);
            await PrepareMobilePairAsync();
            Check(mobileDraft.HasDraft && mobile.Trace.Count == calls && mobilePair is null,
                "Cancelling phone setup keeps its draft and does not offer pairing against an unsaved entry");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            mobile.EntryFailure = true;
            await PrepareMobilePairAsync();
            Check(currentPage == MobileNav && mobilePair is null && !mobileDraft.HasDraft && mobile.Trace.Skip(calls).Where(action => action != "devices").SequenceEqual(new[] { "save", "start", "pair" }) &&
                CheckMobileEntryButton.Content.ToString() == "重新检测" && MobileEntryHelpButton.Visibility == Visibility.Visible,
                "Preparing a pair saves and starts once, keeps the same page on entry failure, and offers detection and forwarding help");
            var afterFailure = mobile.Trace.Count;
            mobile.EntryFailure = false;
            await GenerateMobilePairAsync();
            Check(mobilePair is not null && MobilePairImage.Visibility == Visibility.Visible && MobileNextStepText.Text.Contains("检测通过") &&
                mobile.Trace.Skip(afterFailure).Where(action => action != "devices").SequenceEqual(new[] { "pair" }) && original.SameContents(store.Load(home)),
                "Pair retry reuses the saved running service and renders a QR without modifying Codex routing");
            mobile.PairState = "used"; mobile.CodexReadiness = "configure"; gatewayTicks = 3;
            await MobilePairTickAsync();
            Check(mobilePair is null && MobilePairHint.Text.Contains("手机已配对") && MobileCodexReadinessText.Text.Contains("尚无") && OpenMobileManagerButton.IsEnabled,
                "Successful scanning is distinct from enabling a Codex source that the phone can access");
            Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(720, 540)); await Settle();
            var primaryPosition = CreateMobilePairButton.TransformToVisual(Root).TransformPoint(new());
            Check(primaryPosition.Y >= 0 && primaryPosition.Y + CreateMobilePairButton.ActualHeight <= Root.ActualHeight,
                "Generating the next phone pairing code is visible at the top of the compact layout");
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
