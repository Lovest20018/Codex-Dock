using CodexConfig.Application;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    // Routes retain the old destinations, while the visible navigation follows tasks.
    private sealed record DockPage(string Tag, string Section, string Tab, string Title, string Description, bool EditsConfiguration = false);
    private static readonly DockPage WorkspaceNav = new("workspace", "workspace", "workspace", "工作台", "确认当前状态，继续需要完成的操作。");
    private static readonly DockPage ConnectionNav = new("connection", "configuration", "connection", "连接", "设置服务和认证，再选择模型。", true);
    private static readonly DockPage ModelsNav = new("models", "configuration", "models", "模型与行为", "选择模型与默认行为，核对差异后保存。", true);
    private static readonly DockPage CatalogNav = new("catalog", "configuration", "catalog", "高级配置", "搜索全部字段，修改保留在同一份主配置草稿中。", true);
    private static readonly DockPage TemplatesNav = new("templates", "configuration", "resources", "方案与 Profile", "配置方案加入主配置草稿；Profile 单独编辑与保存。", true);
    private static readonly DockPage ProfilesNav = new("profiles", "configuration", "resources", "方案与 Profile", "Profile 单独保存，不切换主配置与现有聊天。");
    private static readonly DockPage EnhancementsNav = new("enhancements", "runtime", "enhancements", "Codex 与增强", "管理增强偏好，并核对实例后应用。");
    private static readonly DockPage GatewaysNav = new("gateways", "runtime", "gateways", "模型网关", "管理已保存连接使用的模型网关与运行统计。");
    private static readonly DockPage MobileNav = new("mobile", "phone", "mobile", "连接与配对", "检测已保存入口，在本机生成安卓配对码。");
    private static readonly DockPage MobileDevicesNav = new("mobile-devices", "phone", "mobile-devices", "授权设备", "查看登录设备，按需撤销授权或解除登录限制。");
    private static readonly DockPage MobileSettingsNav = new("mobile-settings", "phone", "mobile-settings", "服务设置", "设置手机访问入口，保存并启动；管理服务启停。");
    private static readonly DockPage FilePreviewNav = new("preview", "diagnosis", "preview", "已保存文件", "磁盘上保存了什么？读取当前目录的脱敏快照。");
    private static readonly DockPage SourcesNav = new("sources", "diagnosis", "sources", "配置来源", "配置从哪里来？明确选择执行端和项目后读取。");
    private static readonly DockPage DiagnosticsNav = new("diagnostics", "diagnosis", "diagnostics", "聊天参数", "聊天记录了什么？只读取明确选中的参数元数据。");
    private static readonly DockPage RecoveryNav = new("recovery", "diagnosis", "recovery", "备份恢复", "恢复会影响什么？先核对来源与差异，再确认恢复。");
    private static readonly DockPage SettingsNav = new("settings", "settings", "settings", "启动与窗口", "管理当前用户的开机启动与窗口行为。");
    private static readonly DockPage UpdatesNav = new("updates", "settings", "updates", "软件更新", "检查正式发布，校验安装包后再决定是否安装。");
    private static readonly DockPage AboutNav = new("about", "settings", "about", "关于", "了解当前版本与使用范围。");
    private static readonly DockPage HelpNav = new("help", "help", "help", "帮助", "按任务查找说明，并前往对应页面。");
    private static readonly DockPage[] Pages = [WorkspaceNav, ConnectionNav, ModelsNav, CatalogNav, TemplatesNav, ProfilesNav,
        EnhancementsNav, GatewaysNav, MobileNav, MobileDevicesNav, MobileSettingsNav, FilePreviewNav, SourcesNav, DiagnosticsNav, RecoveryNav, SettingsNav, UpdatesNav, AboutNav, HelpNav];
    private DockPage currentPage = WorkspaceNav;
    private readonly Dictionary<string, DockPage> sectionHistory = [];
    private bool synchronizingNavigation;
    private string? renderedSection, renderedPendingChanges;
    private readonly Dictionary<string, double> pageScrollOffsets = [];
    private int pageNavigationRevision;
    private static string SectionTitle(string section) => section switch
    { "configuration" => "Codex 配置", "runtime" => "运行与增强", "phone" => "手机访问", "diagnosis" => "诊断与恢复", "settings" => "设置", "help" => "帮助", _ => "工作台" };
    private bool IsPhonePage => currentPage.Section == "phone";

    private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (synchronizingNavigation || viewModel is null || (args.SelectedItem as NavigationViewItem)?.Tag is not string section) return;
        var destination = sectionHistory.GetValueOrDefault(section) ?? Pages.First(page => page.Section == section);
        if (section == "phone" && !sectionHistory.ContainsKey(section) && mobileDraft.Settings?.Configured != true) destination = MobileSettingsNav;
        NavigateTo(destination);
    }

    private void NavigateTo(DockPage page, bool revealStart = false)
    {
        var changed = currentPage != page;
        if (changed && currentPage == WorkspaceNav) quickModelRead?.Cancel();
        if (changed) pageScrollOffsets[currentPage.Tag] = PageScroll.VerticalOffset;
        currentPage = page; sectionHistory[page.Section] = page;
        synchronizingNavigation = true;
        try
        {
            Navigation.SelectedItem = Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>().Single(item => item.Tag?.ToString() == page.Section);
            if (renderedSection != page.Section)
            {
                var tabs = Pages.Where(route => route.Section == page.Section).GroupBy(route => route.Tab).Select(group => group.First()).ToArray();
                SectionTabs.Children.Clear();
                foreach (var route in tabs)
                {
                    var button = new ToggleButton { Content = route.Title, Tag = route, IsChecked = route.Tab == page.Tab };
                    button.Click += (_, _) => NavigateTo(route.Tab == "resources" && sectionHistory.GetValueOrDefault("resources") is { } resource ? resource : route);
                    SectionTabs.Children.Add(button);
                }
                CompactSectionInput.ItemsSource = tabs; renderedSection = page.Section;
            }
            foreach (var button in SectionTabs.Children.OfType<ToggleButton>()) button.IsChecked = ((DockPage)button.Tag).Tab == page.Tab;
            CompactSectionInput.SelectedItem = ((IEnumerable<DockPage>)CompactSectionInput.ItemsSource).First(route => route.Tab == page.Tab);
            ResourceTypeInput.Visibility = page.Tab == "resources" ? Visibility.Visible : Visibility.Collapsed;
            ResourceTypeInput.SelectedIndex = page == ProfilesNav ? 1 : 0;
            if (page.Tab == "resources") sectionHistory["resources"] = page;
            SectionNavigation.Visibility = page.Section is "workspace" or "help" ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { synchronizingNavigation = false; }

        PageTitle.Text = SectionTitle(page.Section); PageDescription.Text = page.Description;
        foreach (var (panel, route) in new (FrameworkElement, DockPage)[] { (WorkspacePanel, WorkspaceNav), (ConnectionPanel, ConnectionNav), (ModelsPanel, ModelsNav),
            (CatalogPanel, CatalogNav), (TemplatesPanel, TemplatesNav), (ProfilesPanel, ProfilesNav), (EnhancementsPanel, EnhancementsNav), (GatewaysPanel, GatewaysNav),
            (FilePreviewPanel, FilePreviewNav), (SourcesPanel, SourcesNav), (DiagnosticsPanel, DiagnosticsNav), (RecoveryPanel, RecoveryNav), (SettingsPanel, SettingsNav),
            (UpdatesPanel, UpdatesNav), (AboutPanel, AboutNav), (HelpPanel, HelpNav) }) panel.Visibility = page == route ? Visibility.Visible : Visibility.Collapsed;
        MobilePanel.Visibility = IsPhonePage ? Visibility.Visible : Visibility.Collapsed;
        MobileSettingsCard.Visibility = MobileServiceCard.Visibility = page == MobileSettingsNav ? Visibility.Visible : Visibility.Collapsed;
        MobileStatusCard.Visibility = MobilePairCard.Visibility = page == MobileNav ? Visibility.Visible : Visibility.Collapsed;
        MobileDevicesCard.Visibility = page == MobileDevicesNav ? Visibility.Visible : Visibility.Collapsed;
        MobileNotificationHint.Visibility = page == MobileNav ? Visibility.Visible : Visibility.Collapsed;
        var scrollOffset = revealStart ? 0 : changed ? pageScrollOffsets.GetValueOrDefault(page.Tag) : PageScroll.VerticalOffset;
        var navigationRevision = ++pageNavigationRevision;
        if (changed || revealStart) DispatcherQueue.TryEnqueue(() =>
        {
            if (closing || navigationRevision != pageNavigationRevision) return;
            PageScroll.UpdateLayout(); PageScroll.ChangeView(null, scrollOffset, null, true);
        });
        if (viewModel is null) return;
        RefreshView();
        if (initialized && changed) ActivatePage(page);
    }

    private async void ActivatePage(DockPage page)
    {
        try
        {
            if ((page == GatewaysNav || page.Section == "phone") && options.SmokeOutput is null) await RefreshGatewaysAsync();
            if (page.Section == "phone") await RefreshMobileAsync();
            if (page == CatalogNav) RenderConfigurationCatalog();
            if (page == TemplatesNav) EnsureTemplateLibrary();
            if (page == RecoveryNav) ReadRecoveryStatus();
            if (page == ProfilesNav) ReadProfileNames();
            if (page == SettingsNav) ReadStartupSettings();
            if (page == FilePreviewNav) await ReadSavedConfigurationPreviewAsync();
            if (!closing) RefreshView();
        }
        catch (Exception error) { if (!closing) SetShellError("页面读取未完成", error.Message); }
    }

    private void SectionSelectionChanged(object sender, SelectionChangedEventArgs args)
    { if (!synchronizingNavigation && CompactSectionInput.SelectedItem is DockPage page) NavigateTo(page.Tab == "resources" ? sectionHistory.GetValueOrDefault("resources") ?? page : page); }
    private void ResourceSelectionChanged(object sender, SelectionChangedEventArgs args)
    { if (!synchronizingNavigation && initialized) NavigateTo(ResourceTypeInput.SelectedIndex == 1 ? ProfilesNav : TemplatesNav); }
    private void OpenModelsClicked(object sender, RoutedEventArgs args)
    { NavigateTo(ModelsNav); FocusSearch(BehaviorModelInput); }
    private void OpenMobileSettingsClicked(object sender, RoutedEventArgs args) => NavigateTo(MobileSettingsNav);

    private void RefreshConsolidatedShell()
    {
        var editable = !ApplicationBusy && !dialogOpen && !closing;
        WorkspaceDirectoryButton.IsEnabled = PendingChangesButton.IsEnabled = editable;
        Navigation.IsEnabled = !dialogOpen && !closing;
        // Switch to a compact selector before tabs need to compete with the form.
        var compact = Root.ActualWidth < 900;
        PageTitle.FontSize = compact ? 24 : 28;
        PageDescription.Visibility = Root.ActualHeight < 600 ? Visibility.Collapsed : Visibility.Visible;
        SectionTabs.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactSectionInput.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        var pending = new List<(string Label, DockPage Page)>();
        if (connectionDraft.HasDraft) pending.Add(("主配置 · 未保存", CatalogNav));
        if (profileDraft.HasDraft) pending.Add(("Profile：" + profileDraft.Snapshot?.Name + " · 未保存", ProfilesNav));
        if (HasTemplateDraft) pending.Add(("配置方案 · 未保存到方案库", TemplatesNav));
        if (mobileDraft.HasDraft) pending.Add(("手机访问设置 · 未保存", MobileSettingsNav));
        PendingChangesButton.Visibility = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingChangesButton.Content = "待处理修改 " + pending.Count;
        var key = string.Join("|", pending.Select(item => item.Label));
        if (renderedPendingChanges != key)
        {
            PendingChangesList.Children.Clear();
            foreach (var (label, page) in pending)
            {
                var button = new Button { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch };
                button.Click += (_, _) => { PendingChangesButton.Flyout.Hide(); NavigateTo(page); }; PendingChangesList.Children.Add(button);
            }
            renderedPendingChanges = key;
        }
        ConnectionModelSummary.Text = "默认模型：" + (string.IsNullOrWhiteSpace(connectionDraft.Model) ? "尚未选择" : connectionDraft.Model);
        var next = WorkspaceNextStep();
        WorkspaceNextTitle.Text = next.Title; WorkspaceNextButton.Content = next.Action;
        WorkspaceNextButton.IsEnabled = editable;
        RefreshQuickModelEntry(editable);
        var overview = gatewayManagement.Overview?.Home == viewModel.Home ? gatewayManagement.Overview : null;
        WorkspaceRuntimeSummary.Text = "客户端增强：" + (applicationRuntime.IsEnhancementRunning ? "已接入" : "当前未接入") +
            "\n模型网关：" + (overview?.Model.Status ?? "尚未确认") + "　手机服务：" + (overview?.Mobile.Status ?? "尚未确认") +
            "\n配置文件、应用结果和聊天实际参数分别核对；聊天参数可在诊断与恢复查看。";
    }

    private sealed record WorkspaceStep(string Title, string Action, DockPage Page, bool Apply = false);
    private WorkspaceStep WorkspaceNextStep()
    {
        if (HasPendingRecovery) return new("配置需要恢复", "查看恢复记录", RecoveryNav);
        if (viewModel.Error.Length > 0) return new("配置需要处理", "诊断与恢复", RecoveryNav);
        if (connectionDraft.HasDraft) return new("主配置有未保存修改", "查看修改", CatalogNav);
        if (!viewModel.CanApplyConfiguration) return new("先完成连接设置", "设置连接", ConnectionNav);
        if (lastApplication?.Outcome == DesktopApplyOutcome.Conflict) return new("配置已变化，需要核对", "核对配置来源", SourcesNav);
        if (desktopApplication.PendingRetry is not null) return new("客户端增强可重试", "重试增强", EnhancementsNav, true);
        if (quickModelAwaitingApply)
        {
            if (lastApplication?.Outcome == DesktopApplyOutcome.ExitBlocked)
                return new("模型已保存，等待 Codex 退出", "重试应用", WorkspaceNav, true);
            if (lastApplication?.Outcome == DesktopApplyOutcome.Failed)
                return new("模型已保存，Codex 尚待应用", "重新核对并应用", WorkspaceNav, true);
            return new("模型已保存，等待应用", "在 Codex 使用新模型", WorkspaceNav, true);
        }
        if (lastApplication?.Outcome == DesktopApplyOutcome.ExitBlocked) return new("所选 Codex 尚未退出", "查看应用结果", EnhancementsNav);
        if (lastApplication?.Outcome == DesktopApplyOutcome.Failed) return new("本次应用未完成", "查看应用结果", EnhancementsNav);
        if (configurationResult?.Outcome == ConfigurationSaveOutcome.Saved && lastApplication?.Outcome is null or DesktopApplyOutcome.Cancelled)
            return new("配置已保存，等待应用", "应用到 Codex", EnhancementsNav, true);
        if (profileDraft.HasDraft) return new("还有未保存修改", "继续编辑 Profile", ProfilesNav);
        if (HasTemplateDraft) return new("还有未保存修改", "继续编辑方案", TemplatesNav);
        if (mobileDraft.HasDraft) return new("还有未保存修改", "继续编辑手机设置", MobileSettingsNav);
        return lastApplication?.Outcome == DesktopApplyOutcome.Completed
            ? new("本次应用已完成", "运行与增强", EnhancementsNav) : new("当前配置已读取", "运行与增强", EnhancementsNav);
    }
    private async void WorkspaceNextClicked(object sender, RoutedEventArgs args)
        => await RunWorkspaceNextAsync();
    private async Task RunWorkspaceNextAsync()
    {
        if (ApplicationBusy || dialogOpen || closing) return;
        var next = WorkspaceNextStep(); NavigateTo(next.Page, revealStart: true);
        if (next.Apply) await ApplyDesktopAsync();
    }
}
