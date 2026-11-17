using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private sealed record CatalogScope(string Label, string Id) { public override string ToString() => Label; }
    private readonly ConfigurationCatalogService configurationCatalog = new();
    private readonly Dictionary<string, Control> catalogInputs = new(StringComparer.Ordinal);
    private readonly List<Action<bool>> catalogRefreshers = [];
    private readonly DispatcherTimer catalogSearchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private ConfigurationCatalogPage? catalogPage;
    private ConfigSnapshot? catalogSnapshot;
    private int catalogPageIndex;
    private bool buildingCatalog;
    private string? editingCatalogPath;

    private void InitializeConfigurationCatalog()
    {
        CatalogScopeInput.ItemsSource = new[] { new CatalogScope("已配置", "configured"), new("全部", "all"), new("已修改", "changed") };
        CatalogScopeInput.SelectedIndex = 0;
        CatalogCategoryInput.ItemsSource = new[] { new CatalogCategory("all", "所有分类"), new("unlisted", "未收录配置") }.Concat(ConfigSchema.Instance.Categories).ToArray();
        CatalogCategoryInput.DisplayMemberPath = "Name"; CatalogCategoryInput.SelectedIndex = 0;
        catalogSearchTimer.Tick += (_, _) => { catalogSearchTimer.Stop(); catalogPageIndex = 0; RenderConfigurationCatalog(); };
    }
    private void CatalogSearchChanged(TextBox sender, TextBoxTextChangingEventArgs args)
    { if (initialized && !updatingControls) { catalogSearchTimer.Stop(); catalogSearchTimer.Start(); } }
    private void OpenCatalogClicked(object sender, RoutedEventArgs args) => NavigateTo(CatalogNav);
    private void CatalogFilterChanged(object sender, RoutedEventArgs args)
    { if (initialized && !updatingControls && !buildingCatalog) { catalogPageIndex = 0; RenderConfigurationCatalog(); } }
    private void CatalogSelectionChanged(object sender, SelectionChangedEventArgs args) => CatalogFilterChanged(sender, args);
    private void PreviousCatalogPageClicked(object sender, RoutedEventArgs args) { catalogPageIndex--; RenderConfigurationCatalog(); PageScroll.ChangeView(null, 0, null, true); }
    private void NextCatalogPageClicked(object sender, RoutedEventArgs args) { catalogPageIndex++; RenderConfigurationCatalog(); PageScroll.ChangeView(null, 0, null, true); }
    private void RefreshConfigurationCatalogControls()
    {
        if (CatalogRows is null) return;
        if (!ReferenceEquals(catalogSnapshot, connectionDraft.Snapshot)) { catalogPageIndex = 0; RenderConfigurationCatalog(); }
        var editable = connectionDraft.Available && !HasPendingRecovery && !ManualApplicationBusy && !dialogOpen && !closing;
        NewSettingPathInput.IsEnabled = NewSettingValueInput.IsEnabled = AddSettingButton.IsEnabled = editable;
        foreach (var refresh in catalogRefreshers) refresh(editable);
        CatalogErrors.Text = connectionDraft.FieldErrors.Count > 0 ? "有 " + connectionDraft.FieldErrors.Count + " 项输入尚未通过校验；保存前请修正，或撤销对应项。" : "";
        CatalogErrors.Visibility = connectionDraft.FieldErrors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RenderConfigurationCatalog()
    {
        if (!initialized || buildingCatalog || CatalogScopeInput.SelectedItem is not CatalogScope scope) return;
        buildingCatalog = true;
        try
        {
            catalogSnapshot = connectionDraft.Snapshot;
            catalogPage = configurationCatalog.Query(connectionDraft, new(scope.Id, CatalogSearchInput.Text,
                (CatalogCategoryInput.SelectedItem as CatalogCategory)?.Id ?? "all", CatalogAdvancedInput.IsChecked == true, catalogPageIndex));
            catalogPageIndex = catalogPage.Page; CatalogRows.Children.Clear(); catalogInputs.Clear(); catalogRefreshers.Clear();
            foreach (var entry in catalogPage.Entries) CatalogRows.Children.Add(BuildConfigurationField(entry));
            CatalogCount.Text = catalogPage.Count == 0 ? "没有匹配的配置项" : catalogPage.Count + " 项 · 第 " + (catalogPage.Page + 1) + " / " + catalogPage.Pages + " 页";
            PreviousCatalogPageButton.IsEnabled = catalogPage.Page > 0; NextCatalogPageButton.IsEnabled = catalogPage.Page + 1 < catalogPage.Pages;
        }
        catch (Exception error) { CatalogRows.Children.Clear(); SetShellError("配置列表无法显示", error.Message); }
        finally { buildingCatalog = false; }
    }
    private UIElement BuildConfigurationField(CatalogEntry entry)
    {
        var path = entry.Path; var original = new TomlDocument(connectionDraft.Snapshot?.Config).Get(path);
        var sensitive = ConfigurationCatalogService.Sensitive(path, original);
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = ConfigHelp.Name(entry.HelpPath), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = path + " · " + entry.Lifecycle, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        body.Children.Add(new TextBlock { Text = ConfigHelp.Summary(entry), Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
        var state = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.7 }; body.Children.Add(state);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 55, 55)) };
        Control? input = null; Action? read = null; var rowLoading = true;
        void Edit(string value)
        {
            if (rowLoading || !CanEditConnection) return;
            editingCatalogPath = path;
            try { connectionDraft.EditField(path, value); }
            catch (Exception failure) { SetShellError("字段未修改", failure.Message); }
            finally { editingCatalogPath = null; }
        }
        if (ConfigurationCatalogService.CanEdit(entry))
        {
            if (entry.Kind is "boolean" or "enum" && original is not Tomlyn.Model.TomlTable && !sensitive)
            {
                var choices = new List<BehaviorChoice> { new("不设本地覆盖 · 跟随 Codex", null) };
                choices.AddRange(entry.Kind == "boolean" ? new[] { new BehaviorChoice("开启", "true"), new("关闭", "false") } : entry.EnumValues.Select(value => new BehaviorChoice(value, TomlDocument.Quote(value))));
                var current = connectionDraft.FieldInput(path);
                if (current is not null && choices.All(choice => choice.Value != current)) choices.Add(new("当前值（保留）", current));
                var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = choices };
                combo.SelectionChanged += (_, _) => { if (!rowLoading && CanEditConnection && combo.SelectedItem is BehaviorChoice choice) { if (choice.Value is null) RemoveCatalogOverride(path); else Edit(choice.Value); } };
                read = () => combo.SelectedItem = choices.FirstOrDefault(choice => choice.Value == connectionDraft.FieldInput(path)); input = combo;
            }
            else if (sensitive)
            {
                var password = new PasswordBox { PasswordRevealMode = PasswordRevealMode.Peek, MaxLength = 65536, PlaceholderText = "TOML 值（内容隐藏）" };
                password.PasswordChanged += (_, _) => Edit(password.Password); read = () => { var value = connectionDraft.FieldInput(path) ?? ""; if (password.Password != value) password.Password = value; }; input = password;
            }
            else
            {
                var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 38, MaxHeight = 200, MaxLength = 65536, PlaceholderText = "TOML 值，例如 true、123、\"文本\" 或 [\"a\"]" };
                text.TextChanging += (sender, _) => Edit(sender.Text); read = () => { var value = connectionDraft.FieldInput(path) ?? ""; if (text.Text != value) text.Text = value; }; input = text;
            }
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, ConfigHelp.Name(entry.HelpPath)); body.Children.Add(input); catalogInputs[path] = input;
        }
        else
        {
            body.Children.Add(new TextBlock { Text = entry.Dynamic ? "可在“添加设置”中将 <name> 替换为实际名称。" : entry.ManagedConnection ? "在连接页编辑服务与模型；这里保留现有连接设置。" : "此项显示为只读，请编辑对应子项。", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
            if (entry.ManagedConnection) { var link = new HyperlinkButton { Content = "前往连接配置", Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left }; link.Click += OpenConnectionClicked; body.Children.Add(link); }
        }
        body.Children.Add(error);
        var actions = new ActionWrapPanel();
        var inherit = new Button { Content = "移除本地覆盖" }; var undo = new Button { Content = "撤销此项修改" };
        inherit.Click += (_, _) => RemoveCatalogOverride(path);
        undo.Click += (_, _) => { if (CanEditConnection) { connectionDraft.UndoField(path); RenderConfigurationCatalog(); } };
        if (input is not null) { actions.Children.Add(inherit); actions.Children.Add(undo); body.Children.Add(actions); }
        var help = ConfigHelp.Find(entry.HelpPath);
        if (help is not null)
        {
            var helpBody = new StackPanel { Spacing = 8 };
            foreach (var text in new[] { help.Effect, help.ValueHelp, help.Limitations, help.DefaultBehavior }.Where(value => !string.IsNullOrWhiteSpace(value)))
                helpBody.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
            body.Children.Add(new Expander { Header = "了解更多", HorizontalAlignment = HorizontalAlignment.Stretch, Content = helpBody });
        }
        void Refresh(bool editable)
        {
            rowLoading = true;
            try
            {
                if (editingCatalogPath != path && input?.FocusState is not (FocusState.Keyboard or FocusState.Pointer)) read?.Invoke();
                if (input is not null) input.IsEnabled = editable;
                inherit.IsEnabled = editable; undo.IsEnabled = editable && connectionDraft.PendingFields.ContainsKey(path);
                state.Text = "本文件：" + ConfigurationCatalogService.Display(path, original) + (connectionDraft.PendingFields.ContainsKey(path) ? "\n此项有未保存修改" : "");
                error.Text = connectionDraft.FieldErrors.GetValueOrDefault(path) ?? ""; error.Visibility = error.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { rowLoading = false; }
        }
        catalogRefreshers.Add(Refresh); Refresh(connectionDraft.Available && !HasPendingRecovery && !ManualApplicationBusy && !dialogOpen && !closing);
        return new Border { Child = body, Padding = new(20), CornerRadius = new(8), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(12, 128, 128, 128)) };
    }
    private void AddSettingClicked(object sender, RoutedEventArgs args)
    {
        if (!CanEditConnection) return;
        try
        {
            var path = TomlDocument.FormatPath(TomlDocument.ParsePath(NewSettingPathInput.Text.Trim()));
            if (TomlDocument.ParsePath(path).Any(part => part.Contains("<name>", StringComparison.Ordinal))) throw new ArgumentException("请将 <name> 替换为实际名称。");
            if (ConfigSchema.Instance.Find(path) is { } entry && !ConfigurationCatalogService.CanEdit(entry with { Path = path })) throw new ArgumentException("此字段由专用页面管理或属于父表，请选择对应子项。");
            connectionDraft.EditField(path, NewSettingValueInput.Password);
            NewSettingPathInput.Text = ""; NewSettingValueInput.Password = "";
            CatalogScopeInput.SelectedIndex = 2; CatalogSearchInput.Text = path; catalogSearchTimer.Stop(); catalogPageIndex = 0; RenderConfigurationCatalog();
        }
        catch (Exception error) { SetShellError("设置尚未添加", error.Message); }
    }
    private void RemoveCatalogOverride(string path)
    {
        if (!CanEditConnection) return;
        try { connectionDraft.SetOverride(path, null); RenderConfigurationCatalog(); }
        catch (Exception error) { SetShellError("字段未修改", error.Message); }
        RefreshView();
    }
}
