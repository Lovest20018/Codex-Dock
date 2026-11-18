using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunWorkspaceConsolidationSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        async Task Settle()
        { for (var i = 0; i < 250 && (readingSavedPreview || mobileRefreshing); i++) await Task.Delay(20); await Task.Delay(60); Root.UpdateLayout(); }
        bool InViewport(FrameworkElement control)
        {
            var point = control.TransformToVisual(Root).TransformPoint(new());
            var viewport = PageScroll.TransformToVisual(Root).TransformPoint(new());
            return point.Y >= viewport.Y - 1 && point.Y + control.ActualHeight <= viewport.Y + PageScroll.ActualHeight + 1 && point.X >= 0 && point.X + control.ActualWidth <= Root.ActualWidth + 1;
        }
        var previous = viewModel.Home; var originalSize = AppWindow.Size;
        var home = Path.Combine(output, "consolidation-home"); Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='saved-task-model'\nopenai_base_url='https://tasks.invalid/v1'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-task-key\"}");
        var desktop = (SmokeDesktopApplyRuntime)applicationRuntime; var restarts = desktop.Restarts;
        try
        {
            Check(await SwitchHomeAsync(home) && viewModel.Home == home, "The navigation fixture loads its saved API connection");
            NavigateTo(WorkspaceNav); var original = store.Load(home);
            AppWindow.Resize(new(1120, 820)); await Settle();
            Check(Navigation.MenuItems.Count == 3 && Navigation.FooterMenuItems.Count == 3, "The three daily tasks have separate entries above advanced tools, settings and help");
            foreach (var route in Pages)
            {
                NavigateTo(route); await Settle();
                Check(((NavigationViewItem)Navigation.SelectedItem).Tag?.ToString() == NavigationSection(route) && PageTitle.Text == SectionTitle(route.Section), "Route " + route.Tag + " selects its task and retains an accessible destination");
            }
            Check(original.SameContents(store.Load(home)) && store.LatestBackup(home) is null && desktop.Restarts == restarts,
                "Visiting every destination performs no configuration save backup or desktop restart");
            NavigateTo(ConnectionNav); await Settle(); PageScroll.ChangeView(null, 0, null, true); await Settle();
            await CaptureAsync(output, "consolidated-connection-light.png");
            string Position(FrameworkElement element) => element.Name + " " + element.TransformToVisual(Root).TransformPoint(new()) + " " + element.ActualWidth + "x" + element.ActualHeight;
            Check(InViewport(ConnectionModeInput) && InViewport(BaseUrlInput) && InViewport(ApiKeyInput),
                $"Connection type endpoint and key fit the initial viewport ({Position(PageScroll)}; {Position(ConnectionModeInput)}; {Position(BaseUrlInput)}; {Position(ApiKeyInput)}; root {Root.ActualWidth}x{Root.ActualHeight})");
            Check(!VisualDescendants<FrameworkElement>(PageScroll).Contains(WorkspaceContextCard), "The complete directory and file commands are outside the main form in the directory flyout");

            // Use the visible secondary navigation rather than changing model from a hidden connection control.
            CompactSectionInput.SelectedItem = ModelsNav; await Settle();
            Check(currentPage == ModelsNav && ModelsPanel.Visibility == Visibility.Visible && VisibleAncestors(BehaviorModelInput), "The advanced model editor remains reachable in the connection task");
            BehaviorModelInput.Text = "task-selected-model"; await Settle(); var revision = connectionDraft.Revision;
            NavigateTo(ConnectionNav); await Settle();
            Check(ConnectionModelSummary.Text.Contains("task-selected-model") && ConnectionModelEditorHost.Children.Contains(BehaviorModelInput) && VisibleAncestors(DiscoverModelsButton) &&
                connectionDraft.Revision == revision && connectionDraft.HasDraft, "The connection page reads models inline and shares the draft with the advanced model editor");
            var confirmations = 0; smokeDialog = _ => { confirmations++; return Task.FromResult(ContentDialogResult.None); };
            NavigateTo(HelpNav); await ExecuteWorkspaceShortcutAsync(VirtualKey.S, VirtualKeyModifiers.Control);
            Check(ApplicationFooter.Visibility == Visibility.Collapsed && PendingChangesButton.Visibility == Visibility.Visible && confirmations == 0 && connectionDraft.Revision == revision, "Help exposes pending changes but Ctrl S cannot save a different page's main configuration");
            NavigateTo(FilePreviewNav); await Settle(); await ExecuteWorkspaceShortcutAsync(VirtualKey.S, VirtualKeyModifiers.Control);
            Check(ApplicationFooter.Visibility == Visibility.Collapsed && confirmations == 0 && original.SameContents(store.Load(home)), "Read only file diagnosis cannot display or trigger the main configuration save action");
            NavigateTo(MobileSettingsNav); await Settle();
            Check(MobileFooter.Visibility == Visibility.Visible && ApplicationFooter.Visibility == Visibility.Collapsed && VisibleAncestors(MobileGatewayStartButton), "Phone service settings own their save start controls and lifecycle commands");
            NavigateTo(GatewaysNav); await Settle();
            Check(VisibleAncestors(ModelGatewayStartButton) && !VisibleAncestors(MobileGatewayStartButton), "The runtime gateway page contains only the model service lifecycle");
            NavigateTo(ModelsNav); await Settle();
            BehaviorModelInput.Text = "";
            Check(!await SaveConnectionAsync() && currentPage == ModelsNav && connectionDraft.HasDraft && confirmations == 0 && original.SameContents(store.Load(home)), "A missing default model focuses its only editor and preserves the unsaved connection before review");
            BehaviorModelInput.Text = "task-selected-model";
            smokeDialog = _ => { confirmations++; return Task.FromResult(ContentDialogResult.Primary); };
            Check(await SaveConnectionAsync() && !connectionDraft.HasDraft && desktop.Restarts == restarts && new TomlDocument(store.Load(home).Config).String("model") == "task-selected-model",
                "The model task completes the existing encrypted save without restarting Codex");
            Check(currentPage == ModelsNav && DesktopApplicationActions.Visibility == Visibility.Visible && ApplyButton.IsEnabled,
                "A saved configuration offers application in the same configuration area without returning home");
            PageScroll.ChangeView(null, 0, null, true); await Settle(); await CaptureAsync(output, "consolidated-models-light.png");
            original = store.Load(home); connectionDraft.Model = "main-independent-draft";
            NavigateTo(ProfilesNav); await LoadProfileAsync("independent-task"); ProfileDocumentInput.Text = "model='profile-task-model'\n";
            NavigateTo(MobileSettingsNav); await Settle(); MobileUrlInput.Text = "https://phone-task.invalid"; MobileUsernameInput.Text = "task-user"; MobilePasswordInput.Password = "fake-task-password-123";
            NavigateTo(TemplatesNav); EnsureTemplateLibrary(); BeginTemplateEdit(TemplateRepository.New("Task draft", []), true); TemplateNameInput.Text = "Separate task scheme";
            RefreshView();
            Check(PendingChangesList.Children.Count == 4 && PendingChangesButton.Content.ToString() == "待处理修改 4" && TemplateEditFooter.Visibility == Visibility.Visible && ApplicationFooter.Visibility == Visibility.Collapsed,
                "Four independent edited objects are discoverable together while the scheme editor saves only the library");
            var mainRevision = connectionDraft.Revision; var profileRevision = profileDraft.Revision; var mobileRevision = mobileDraft.Revision;
            NavigateTo(HelpNav); await ExecuteWorkspaceShortcutAsync(VirtualKey.S, VirtualKeyModifiers.Control);
            NavigateTo(ProfilesNav); ResourceTypeInput.SelectedIndex = 0; ResourceTypeInput.SelectedIndex = 1;
            Check(currentPage == ProfilesNav && HasTemplateDraft && connectionDraft.Revision == mainRevision && profileDraft.Revision == profileRevision && mobileDraft.Revision == mobileRevision && original.SameContents(store.Load(home)),
                "Switching resource types and help preserves every draft and all saved files");
            Navigation.SelectedItem = ConfigurationSectionNav; Navigation.SelectedItem = AdvancedSectionNav;
            Check(currentPage == AdvancedNav && profileDraft.Revision == profileRevision && AdvancedToolsList.Children.Count > 0, "Advanced tools are discoverable independently of the three daily tasks");
            Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(720, 540)); NavigateTo(MobileSettingsNav); await Settle();
            Check(CompactSectionInput.Visibility == Visibility.Visible && SectionTabs.Visibility == Visibility.Collapsed && MobileFooter.ActualHeight > 0 &&
                SaveMobileButton.TransformToVisual(Root).TransformPoint(new()).Y + SaveMobileButton.ActualHeight <= Root.ActualHeight + 1,
                "Compact phone settings retain a reachable secondary selector and their own fixed save action");
            await CaptureAsync(output, "consolidated-phone-compact-dark.png");
            AppWindow.Resize(new(1120, 820)); NavigateTo(ProfilesNav); await Settle(); await CaptureAsync(output, "consolidated-resources-dark.png");
            NavigateTo(WorkspaceNav); Root.RequestedTheme = ElementTheme.Light; await Settle(); await CaptureAsync(output, "consolidated-workspace-pending.png");
            Check(WorkspaceNextButton.Content.ToString() == "继续编辑" && ApplicationFooter.Visibility == Visibility.Collapsed, "Home presents one primary next step for the outstanding main configuration");
        }
        finally
        {
            editingTemplate = null; profileDraft.Load(null); mobileDraft.Load(mobileDraft.Settings); LoadConnectionDraft();
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); await SwitchHomeAsync(previous); smokeDialog = null;
            Root.RequestedTheme = ElementTheme.Light; AppWindow.Resize(originalSize); NavigateTo(WorkspaceNav);
        }
    }
}
