using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private void ResizeConsolidationWindow(double width, double height)
    {
        var scale = Root.XamlRoot.RasterizationScale;
        AppWindow.Resize(new((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale)));
    }

    private async Task RunWorkspaceConsolidationSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        async Task Settle()
        { for (var i = 0; i < 250 && (FilePreviewPanel.IsReading || mobileRefreshing); i++) await Task.Delay(20); await Task.Delay(60); Root.UpdateLayout(); }
        bool InViewport(FrameworkElement control)
        {
            var point = control.TransformToVisual(Root).TransformPoint(new());
            var viewport = PageScroll.TransformToVisual(Root).TransformPoint(new());
            return point.Y >= viewport.Y - 1 && point.Y + control.ActualHeight <= viewport.Y + PageScroll.ActualHeight + 1 && point.X >= 0 && point.X + control.ActualWidth <= Root.ActualWidth + 1;
        }
        var previous = viewModel.Home; var originalSize = AppWindow.Size;
        var originalPaneOpen = Navigation.IsPaneOpen;
        var home = Path.Combine(output, "consolidation-home"); Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='saved-task-model'\nopenai_base_url='https://tasks.invalid/v1'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-task-key\"}");
        var desktop = (SmokeDesktopApplyRuntime)applicationRuntime; var restarts = desktop.Restarts;
        try
        {
            Root.RequestedTheme = ElementTheme.Light;
            ResizeConsolidationWindow(720, 540);
            await CheckLongWorkspaceTextAsync(output, Check);
            Check(await SwitchHomeAsync(home) && viewModel.Home == home, "The navigation fixture loads its saved API connection");
            NavigateTo(WorkspaceNav, revealStart: true); var original = store.Load(home);
            ResizeConsolidationWindow(1120, 820); await Settle();
            Check(Navigation.MenuItems.Count == 3 && Navigation.FooterMenuItems.Count == 3, "The three daily tasks have separate entries above advanced tools, settings and help");
            await CaptureAsync(output, "refined-home-light.png");
            var title = PageTitle.TransformToVisual(Root).TransformPoint(new());
            var directory = WorkspaceDirectoryButton.TransformToVisual(Root).TransformPoint(new());
            var summary = WorkspacePanel.WorkspaceConnectionCard.TransformToVisual(Root).TransformPoint(new());
            Check(Math.Abs(title.Y + PageTitle.ActualHeight / 2 - directory.Y - WorkspaceDirectoryButton.ActualHeight / 2) <= 2 &&
                directory.X > title.X + 300 && Math.Abs(summary.X - title.X) <= 2 && Math.Abs(directory.X + WorkspaceDirectoryButton.ActualWidth - summary.X - WorkspacePanel.WorkspaceConnectionCard.ActualWidth) <= 2,
                $"The 1120 × 820 window places the directory beside the title and aligns the title, actions and summary ({Root.ActualWidth:0} × {Root.ActualHeight:0} DIP client area)");
            var summaryValues = new[] { WorkspacePanel.ModelText, WorkspacePanel.EndpointText, WorkspacePanel.WorkspaceProfileName, WorkspacePanel.WorkspaceDesktopStatus };
            Check(summaryValues.Select(value => value.Text).Take(3).SequenceEqual(new[] { "saved-task-model", "https://tasks.invalid", "consolidation-home" }) &&
                summaryValues.Select(Grid.GetRow).SequenceEqual(Enumerable.Range(0, 4)) &&
                summaryValues.All(value => Grid.GetColumn(value) == 1 && InViewport(value)) && InViewport(WorkspacePanel.WorkspaceEditConnectionButton),
                "The Home summary presents the model, service, configuration name and runtime status in aligned rows");
            NavigateTo(AdvancedNav, revealStart: true);
            await CaptureAsync(output, "advanced-tools-standard.png");
            CheckAdvancedToolGrid(Check);
            foreach (var route in Pages)
            {
                NavigateTo(route); await Settle();
                var advancedChild = NavigationSection(route) == "tools" && route != AdvancedNav;
                var tool = AdvancedTools.FirstOrDefault(item => item.Page == route);
                Check(((NavigationViewItem)Navigation.SelectedItem).Tag?.ToString() == NavigationSection(route) &&
                    (advancedChild
                        ? AdvancedBreadcrumb.Visibility == Visibility.Visible && AdvancedBreadcrumb.ItemsSource is string[] path &&
                            path.SequenceEqual(new[] { AdvancedNav.Title, tool.Group, tool.Label }) && PageTitle.Text == tool.Label
                        : AdvancedBreadcrumb.Visibility == Visibility.Collapsed && PageTitle.Text == SectionTitle(route.Section)),
                    "Route " + route.Tag + " identifies its task and location in navigation");
            }
            Check(original.SameContents(store.Load(home)) && store.LatestBackup(home) is null && desktop.Restarts == restarts,
                "Visiting every destination performs no configuration save backup or desktop restart");
            NavigateTo(ConnectionNav); await Settle(); PageScroll.ChangeView(null, 0, null, true); await Settle();
            await CaptureAsync(output, "consolidated-connection-light.png");
            string Position(FrameworkElement element) => element.Name + " " + element.TransformToVisual(Root).TransformPoint(new()) + " " + element.ActualWidth + "x" + element.ActualHeight;
            Check(InViewport(ConnectionPanel.ConnectionModeInput) && InViewport(ConnectionPanel.BaseUrlInput) && InViewport(ConnectionPanel.ApiKeyInput),
                $"Connection type endpoint and key fit the initial viewport ({Position(PageScroll)}; {Position(ConnectionPanel.ConnectionModeInput)}; {Position(ConnectionPanel.BaseUrlInput)}; {Position(ConnectionPanel.ApiKeyInput)}; root {Root.ActualWidth}x{Root.ActualHeight})");
            Check(ConnectionPanel.SetupApiCard.ActualWidth is >= 680 and <= 720 &&
                Math.Abs(ConnectionPanel.SetupApiCard.TransformToVisual(Root).TransformPoint(new()).X - PageTitle.TransformToVisual(Root).TransformPoint(new()).X) <= 2 &&
                SectionTabs.Visibility == Visibility.Visible &&
                SectionTabs.Children.OfType<ToggleButton>().Where(tab => tab.IsChecked == true).Single().Content.ToString() == "连接",
                "The regular connection form keeps a left-aligned readable width and indicates the selected tab");
            Check(!VisualDescendants<FrameworkElement>(PageScroll).Contains(WorkspaceContextCard), "The complete directory and file commands are outside the main form in the directory flyout");

            // Exercise the compact selector before editing the shared model draft.
            ResizeConsolidationWindow(720, 540); await Settle();
            Check(CompactSectionInput.Visibility == Visibility.Visible, "The compact selector replaces tabs at the minimum window width");
            CompactSectionInput.SelectedItem = ModelsNav; await Settle();
            Check(currentPage == ModelsNav && ModelsPanel.Visibility == Visibility.Visible && VisibleAncestors(ModelsPanel.BehaviorModelInput), "The advanced model editor remains reachable in the connection task");
            ResizeConsolidationWindow(1120, 820); await Settle();
            ModelsPanel.BehaviorModelInput.Text = "task-selected-model"; await Settle(); var revision = connectionDraft.Revision;
            NavigateTo(ConnectionNav); await Settle();
            Check(ConnectionPanel.ConnectionModelSummary.Text.Contains("task-selected-model") && VisibleAncestors(ConnectionPanel.ChooseConnectionModelButton) && !VisibleAncestors(ModelsPanel.DiscoverModelsButton) &&
                connectionDraft.Revision == revision && connectionDraft.HasDraft, "The connection page summarizes the model and links to the only editor");
            var confirmations = 0; smokeDialog = _ => { confirmations++; return Task.FromResult(ContentDialogResult.None); };
            NavigateTo(HelpNav); await ExecuteWorkspaceShortcutAsync(VirtualKey.S, VirtualKeyModifiers.Control);
            Check(ApplicationFooter.Visibility == Visibility.Collapsed && PendingChangesButton.Visibility == Visibility.Visible && confirmations == 0 && connectionDraft.Revision == revision, "Help exposes pending changes but Ctrl S cannot save a different page's main configuration");
            NavigateTo(FilePreviewNav); await Settle(); await ExecuteWorkspaceShortcutAsync(VirtualKey.S, VirtualKeyModifiers.Control);
            Check(ApplicationFooter.Visibility == Visibility.Collapsed && confirmations == 0 && original.SameContents(store.Load(home)), "Read only file diagnosis cannot display or trigger the main configuration save action");
            NavigateTo(MobileSettingsNav); await Settle();
            Check(MobileFooter.Visibility == Visibility.Visible && ApplicationFooter.Visibility == Visibility.Collapsed && VisibleAncestors(MobilePanel.MobileGatewayStartButton), "Phone service settings own their save start controls and lifecycle commands");
            NavigateTo(GatewaysNav); await Settle();
            Check(VisibleAncestors(GatewaysPanel.ModelGatewayStartButton) && !VisibleAncestors(MobilePanel.MobileGatewayStartButton), "The runtime gateway page contains only the model service lifecycle");
            NavigateTo(ModelsNav); await Settle();
            ModelsPanel.BehaviorModelInput.Text = "";
            Check(!await SaveConnectionAsync() && currentPage == ModelsNav && connectionDraft.HasDraft && confirmations == 0 && original.SameContents(store.Load(home)), "A missing default model focuses its only editor and preserves the unsaved connection before review");
            ModelsPanel.BehaviorModelInput.Text = "task-selected-model";
            smokeDialog = _ => { confirmations++; return Task.FromResult(ContentDialogResult.Primary); };
            Check(await SaveConnectionAsync() && !connectionDraft.HasDraft && desktop.Restarts == restarts && new TomlDocument(store.Load(home).Config).String("model") == "task-selected-model",
                "The model task completes the existing encrypted save without restarting Codex");
            Check(currentPage == ModelsNav && DesktopApplicationActions.Visibility == Visibility.Visible && ApplyButton.IsEnabled &&
                ApplyFooterButtons.Children.Count == 2 && ApplicationError.Parent == PageBody,
                "A saved configuration offers application in the same configuration area without returning home");
            PageScroll.ChangeView(null, 0, null, true); await Settle(); await CaptureAsync(output, "consolidated-models-light.png");
            original = store.Load(home); connectionDraft.Model = "main-independent-draft";
            NavigateTo(ProfilesNav); await LoadProfileAsync("independent-task"); ProfilesPanel.ProfileDocumentInput.Text = "model='profile-task-model'\n";
            NavigateTo(MobileSettingsNav); await Settle(); MobilePanel.MobileUrlInput.Text = "https://phone-task.invalid"; MobilePanel.MobileUsernameInput.Text = "task-user"; MobilePanel.MobilePasswordInput.Password = "fake-task-password-123";
            NavigateTo(TemplatesNav); EnsureTemplateLibrary(); BeginTemplateEdit(TemplateRepository.New("Task draft", []), true); TemplatesPanel.TemplateNameInput.Text = "Separate task scheme";
            RefreshView();
            Check(PendingChangesList.Children.Count == 4 && PendingChangesButton.Content.ToString() == "待处理修改 4" && TemplateEditFooter.Visibility == Visibility.Visible && ApplicationFooter.Visibility == Visibility.Collapsed,
                "Four independent edited objects are discoverable together while the scheme editor saves only the library");
            var mainRevision = connectionDraft.Revision; var profileRevision = profileDraft.Revision; var mobileRevision = mobileDraft.Revision;
            NavigateTo(HelpNav); await ExecuteWorkspaceShortcutAsync(VirtualKey.S, VirtualKeyModifiers.Control);
            NavigateTo(ProfilesNav); ResourceTypeInput.SelectedIndex = 0; ResourceTypeInput.SelectedIndex = 1;
            Check(currentPage == ProfilesNav && HasTemplateDraft && connectionDraft.Revision == mainRevision && profileDraft.Revision == profileRevision && mobileDraft.Revision == mobileRevision && original.SameContents(store.Load(home)),
                "Switching resource types and help preserves every draft and all saved files");
            Navigation.SelectedItem = ConfigurationSectionNav; Navigation.SelectedItem = AdvancedSectionNav;
            Check(currentPage == AdvancedNav && profileDraft.Revision == profileRevision && AdvancedPanel.AdvancedToolsList.Children.Count > 0, "Advanced tools are discoverable independently of the three daily tasks");
            await RunAdvancedToolsLayoutSmokeAsync(output, Check);
            Check(profileDraft.Revision == profileRevision && original.SameContents(store.Load(home)), "Searching and opening advanced tools preserve the saved configuration and draft");
            Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(720, 540)); NavigateTo(MobileSettingsNav); await Settle();
            Check(CompactSectionInput.Visibility == Visibility.Visible && SectionTabs.Visibility == Visibility.Collapsed && MobileFooter.ActualHeight > 0 &&
                SaveMobileButton.TransformToVisual(Root).TransformPoint(new()).Y + SaveMobileButton.ActualHeight <= Root.ActualHeight + 1,
                "Compact phone settings retain a reachable secondary selector and their own fixed save action");
            await CaptureAsync(output, "consolidated-phone-compact-dark.png");
            AppWindow.Resize(new(1120, 820)); NavigateTo(ProfilesNav); await Settle(); await CaptureAsync(output, "consolidated-resources-dark.png");
            NavigateTo(WorkspaceNav); Root.RequestedTheme = ElementTheme.Light; await Settle(); await CaptureAsync(output, "consolidated-workspace-pending.png");
            Check(WorkspacePanel.WorkspaceNextButton.Content.ToString() == "继续编辑" && ApplicationFooter.Visibility == Visibility.Collapsed, "Home presents one primary next step for the outstanding main configuration");
        }
        finally
        {
            editingTemplate = null; profileDraft.Load(null); mobileDraft.Load(mobileDraft.Settings); LoadConnectionDraft();
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); await SwitchHomeAsync(previous); smokeDialog = null;
            Root.RequestedTheme = ElementTheme.Light; AppWindow.Resize(originalSize); Navigation.IsPaneOpen = originalPaneOpen;
            NavigateTo(WorkspaceNav);
        }
    }

    private async Task CheckLongWorkspaceTextAsync(string output, Action<bool, string> check)
    {
        var folder = "dock-workspace-" + string.Concat(Enumerable.Repeat("integration", 7));
        var home = Path.Combine(output, folder);
        var model = "provider/" + string.Concat(Enumerable.Repeat("multilingual", 13));
        var endpoint = "https://" + new string('e', 52) + "." + new string('x', 46) + ".invalid";
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), $"model='{model}'\nopenai_base_url='{endpoint}/v1'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-layout-key\"}");
        check(await SwitchHomeAsync(home), "The long-text layout fixture loads through the normal directory switch");
        NavigateTo(WorkspaceNav, revealStart: true);
        var flyout = WorkspaceDirectoryButton.Flyout;
        try
        {
            Root.Width = 460;
            Root.HorizontalAlignment = HorizontalAlignment.Left;
            await Task.Delay(100);
            Root.UpdateLayout();
            check(WorkspaceDirectoryLabel.IsTextTrimmed && WorkspaceDirectoryLabel.Text.EndsWith(folder, StringComparison.Ordinal) &&
                WorkspacePanel.ModelText.Text == model && WorkspacePanel.EndpointText.Text == endpoint && WorkspacePanel.WorkspaceProfileName.Text == folder &&
                new[] { WorkspacePanel.ModelText, WorkspacePanel.EndpointText, WorkspacePanel.WorkspaceProfileName }.All(value => value.ActualHeight > value.FontSize * 2),
                "Long directory names are ellipsized in the header while full saved names, addresses and models wrap in the 460 DIP Home summary");
            flyout.ShowAt(WorkspaceDirectoryButton);
            await Task.Delay(100);
            Root.UpdateLayout();
            check(HomeText.Text == home && HomeText.IsTextSelectionEnabled && HomeText.ActualHeight > HomeText.FontSize * 2,
                "The directory menu exposes the complete selectable path on multiple lines");
        }
        finally
        {
            flyout.Hide();
            Root.Width = double.NaN;
            Root.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    // The monitor can cap the requested window size. Assert the layout for the measured tool area.
    private int ExpectedAdvancedToolColumns() => AdvancedPanel.AdvancedToolsList.ActualWidth switch { < 500 => 1, < 760 => 2, _ => 3 };

    private double ExpectedAdvancedToolColumnWidth()
    {
        var columns = ExpectedAdvancedToolColumns();
        return (AdvancedPanel.AdvancedToolsList.ActualWidth - 12 * (columns - 1)) / columns;
    }

    private void CheckAdvancedToolGrid(Action<bool, string> check)
    {
        var columns = ExpectedAdvancedToolColumns();
        var cards = AdvancedPanel.AdvancedToolsList.Children.OfType<Border>().ToArray();
        var sections = cards.Select(card => (StackPanel)card.Child).ToArray();
        check(cards.Length == 3 && AdvancedPanel.AdvancedToolsList.ColumnDefinitions.Count == columns &&
            sections.Select(section => ((TextBlock)section.Children[0]).Text).SequenceEqual(new[] { "配置", "网关与增强", "诊断与恢复" }) &&
            sections.Select(section => section.Children.OfType<Button>().Count()).SequenceEqual(new[] { 3, 3, 4 }) &&
            cards.Select((card, index) => Grid.GetColumn(card) == index % columns && Grid.GetRow(card) == index / columns).All(placed => placed),
            $"The ten tools occupy three groups in order across {columns} columns at {AdvancedPanel.AdvancedToolsList.ActualWidth:0} DIP available width");
        foreach (var card in cards)
        {
            var cardPosition = card.TransformToVisual(AdvancedPanel.AdvancedToolsList).TransformPoint(new());
            check(cardPosition.X >= -1 && cardPosition.X + card.ActualWidth <= AdvancedPanel.AdvancedToolsList.ActualWidth + 1 &&
                Math.Abs(card.ActualWidth - ExpectedAdvancedToolColumnWidth()) <= 2 &&
                VisualDescendants<Button>(card).All(tool =>
                {
                    var point = tool.TransformToVisual(card).TransformPoint(new());
                    return point.X >= 0 && point.X + tool.ActualWidth <= card.ActualWidth && tool.ActualWidth >= card.ActualWidth - 32 &&
                        tool.ActualHeight >= 38 && point.Y + tool.ActualHeight <= card.ActualHeight;
                }),
                "Each navigation row spans its group and stays inside the tool grid");
        }
    }

    private async Task RunAdvancedToolsLayoutSmokeAsync(string output, Action<bool, string> check)
    {
        double Left(FrameworkElement control) => control.TransformToVisual(Root).TransformPoint(new()).X;
        double Top(FrameworkElement control) => control.TransformToVisual(Root).TransformPoint(new()).Y;
        Button[] Tools() => VisualDescendants<Button>(AdvancedPanel.AdvancedToolsList).ToArray();

        Root.RequestedTheme = ElementTheme.Light;
        ResizeConsolidationWindow(1840, 900);
        await Task.Delay(150); Root.UpdateLayout();
        // Hosted desktops can need the compact rail to give the three-column case enough room.
        if (AdvancedPanel.AdvancedToolsList.ActualWidth < 760 && Navigation.IsPaneOpen) Navigation.IsPaneOpen = false;
        await CaptureAsync(output, "advanced-tools-wide.png");
        var start = Left(PageTitle);
        var width = AdvancedPanel.AdvancedToolsList.ActualWidth;
        var expectedWidth = Math.Min(1000, PageScroll.ViewportWidth) - PageBody.Padding.Left - PageBody.Padding.Right;
        check(Math.Abs(width - expectedWidth) <= 2 && Math.Abs(Left(AdvancedPanel.AdvancedSearchInput) - start) <= 2 &&
            Math.Abs(Left(AdvancedPanel.AdvancedToolsList) - start) <= 2 && Math.Abs(AdvancedPanel.AdvancedSearchInput.ActualWidth - Math.Min(480, width)) <= 2 &&
            Math.Abs(Left(PendingChangesButton) + PendingChangesButton.ActualWidth - start - AdvancedPanel.AdvancedToolsList.ActualWidth) <= 2,
            $"The wide-window request keeps the title, search, tool cards and header actions in the same capped column ({Root.ActualWidth:0} × {Root.ActualHeight:0} DIP client area)");
        CheckAdvancedToolGrid(check);
        await ExecuteWorkspaceShortcutAsync(VirtualKey.F, VirtualKeyModifiers.Control);
        check(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), AdvancedPanel.AdvancedSearchInput), "Ctrl F focuses the advanced tool search");
        AdvancedPanel.AdvancedSearchInput.Text = "回滚"; Root.UpdateLayout();
        var recovery = Tools().SingleOrDefault();
        var resultCard = (Border)AdvancedPanel.AdvancedToolsList.Children[0];
        check(recovery?.Content.ToString() == "备份与恢复" && Math.Abs(Left(resultCard) - start) <= 2 &&
            Math.Abs(resultCard.ActualWidth - ExpectedAdvancedToolColumnWidth()) <= 2,
            "An alias query leaves only an accessible recovery result at the normal column width on the left");

        Root.RequestedTheme = ElementTheme.Dark;
        ResizeConsolidationWindow(720, 540);
        await Task.Delay(100); Root.UpdateLayout();
        check(AdvancedPanel.AdvancedSearchInput.Text == "回滚" && ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), AdvancedPanel.AdvancedSearchInput) &&
            ReferenceEquals(Tools().SingleOrDefault(), recovery) && Math.Abs(resultCard.ActualWidth - ExpectedAdvancedToolColumnWidth()) <= 2,
            "Resizing retains the live query, keyboard focus and the existing result at the compact column width");
        check(ReferenceEquals(FocusManager.FindFirstFocusableElement(AdvancedPanel.AdvancedToolsList), recovery) &&
            recovery!.Focus(FocusState.Keyboard) && AutomationProperties.GetName(recovery) == "备份与恢复",
            "A tool result is keyboard focusable and its accessible name contains only the destination");
        var peer = new ButtonAutomationPeer(recovery!);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        await Task.Delay(100);
        check(currentPage == RecoveryNav, "A search result opens its native tool page directly");

        NavigateTo(AdvancedNav, revealStart: true);
        var previousError = shellError;
        AdvancedPanel.AdvancedSearchInput.Text = "approval_policy";
        check(AdvancedPanel.AdvancedNoResults.Visibility == Visibility.Visible && AdvancedPanel.AdvancedFieldSearchButton.Visibility == Visibility.Visible,
            "A raw configuration field has a visible path to search all settings");
        SearchAdvancedFieldsClicked(AdvancedPanel.AdvancedFieldSearchButton, new());
        check(currentPage == CatalogNav && CatalogPanel.CatalogSearchInput.Text == "approval_policy" && catalogInputs.ContainsKey("approval_policy") && shellError == previousError,
            "Field search opens the matching configuration editor immediately");
        NavigateTo(AdvancedNav, revealStart: true);
        AdvancedPanel.AdvancedSearchInput.Text = "mcp_servers.<name>";
        SearchAdvancedFieldsClicked(AdvancedPanel.AdvancedFieldSearchButton, new());
        check(catalogPage is { Entries.Count: > 0 } && catalogPage.Entries.All(entry => entry.Dynamic) && CatalogPanel.CatalogRows.Children.Count == catalogPage.Entries.Count && shellError == previousError,
            "Schema placeholders can be searched without being treated as saved TOML paths");
        AdvancedPanel.AdvancedSearchInput.Text = "";
        CatalogPanel.CatalogSearchInput.Text = "";
        NavigateTo(AdvancedNav, revealStart: true);
        await CaptureAsync(output, "advanced-tools-compact-dark.png");
        CheckAdvancedToolGrid(check);

        try
        {
            Root.Width = 460;
            Root.HorizontalAlignment = HorizontalAlignment.Left;
            await CaptureAsync(output, "advanced-tools-single-column-dark.png");
            CheckAdvancedToolGrid(check);
            check(Top(PageTitle) >= 46 && Top(WorkspaceDirectoryButton) >= Top(PageTitle) + PageTitle.ActualHeight - 1 &&
                Left(WorkspaceDirectoryButton) + WorkspaceDirectoryButton.ActualWidth <= Left(PendingChangesButton) &&
                Left(PendingChangesButton) + PendingChangesButton.ActualWidth <= Root.ActualWidth,
                "The reduced logical viewport keeps the full directory command and pending changes reachable below the title");
        }
        finally
        {
            Root.Width = double.NaN;
            Root.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        NavigateTo(ConnectionNav, revealStart: true);
        await Task.Delay(100); Root.UpdateLayout();
        ConnectionPanel.BaseUrlInput.Focus(FocusState.Keyboard);
        await CaptureAsync(output, "refined-connection-compact-dark.png");
        check(Top(WorkspaceDirectoryButton) >= Top(PageTitle) + PageTitle.ActualHeight - 1 &&
            Math.Abs(Left(PageTitle) - Left(ConnectionPanel.SetupApiCard)) <= 2 && PendingChangesButton.Visibility == Visibility.Visible &&
            Left(PendingChangesButton) + PendingChangesButton.ActualWidth <= Root.ActualWidth &&
            Left(ConnectionPanel.ApiKeyInput) + ConnectionPanel.ApiKeyInput.ActualWidth <= Root.ActualWidth &&
            Math.Abs(Left(PageTitle) - Left(SaveConnectionButton)) <= 2 &&
            CompactSectionInput.Visibility == Visibility.Visible && ApplicationFooter.Visibility == Visibility.Visible &&
            SaveConnectionButton.Content.ToString() == "核对连接" && Top(SaveConnectionButton) + SaveConnectionButton.ActualHeight <= Root.ActualHeight + 1,
            $"The 720 × 540 dark connection keeps the page selector, pending changes and review action reachable ({Root.ActualWidth:0} × {Root.ActualHeight:0} DIP client area)");
    }
}
