using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

public sealed class TemplateBrowser : UserControl
{
    private readonly StackPanel body = new();
    private readonly ScrollViewer scroll = new();
    private readonly TextBlock selectedCount = HelpContent.Note("");
    private readonly StackPanel footerActions = new() { Orientation = Orientation.Horizontal };
    private readonly List<(ComboBox Path, TextBox Value, CheckBox Default)> editRows = [];
    private TemplateRepository? repository;
    private bool testMode;
    private Tomlyn.Model.TomlTable original = new();
    private IReadOnlyDictionary<string, string?> draft = new Dictionary<string, string?>();
    private ConfigTemplate? current;
    private ConfigTemplate? editing;
    private bool creating;
    private string editBaseline = "";
    private readonly TextBlock error = HelpContent.Note("");
    internal readonly TextBox NameInput = new() { MaxLength = 60 };
    internal readonly TextBox DescriptionInput = new() { MaxLength = 240 };
    internal readonly Button SaveTemplateButton = new() { Content = "保存模板" };
    internal readonly Button CancelEditButton = new() { Content = "取消" };
    internal readonly Button NewTemplateButton = new() { Content = "新增模板" };
    internal readonly Button CaptureTemplateButton = new() { Content = "保存当前设置为模板" };
    internal readonly Button AddRowButton = new() { Content = "添加设置" };
    internal readonly Dictionary<string, CheckBox> Choices = new(StringComparer.Ordinal);
    internal readonly Button StageButton = new() { Content = "应用所选项" };
    public event Action<ConfigTemplate, string[]>? StageRequested;
    public event Action<string, bool>? StatusChanged;
    internal ConfigTemplate? Current => current;
    internal bool IsEditing => editing is not null;
    internal MessageBoxResult PendingTestChoice { get; set; } = MessageBoxResult.No;
    internal string ValidationMessage => error.Text;
    public event Action? ViewChanged;

    public TemplateBrowser()
    {
        var layout = new Grid(); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        scroll.SetResourceReference(StyleProperty, "PageScroll"); scroll.Padding = new(0, 0, 10, 18); scroll.Content = body; layout.Children.Add(scroll);
        var footer = new Grid { Margin = new(0, 10, 10, 12) }; footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        selectedCount.VerticalAlignment = VerticalAlignment.Center; selectedCount.Margin = new(0, 0, 18, 0);
        footer.Children.Add(selectedCount); Grid.SetColumn(footerActions, 1); footer.Children.Add(footerActions);
        Grid.SetRow(footer, 1); layout.Children.Add(footer); Content = layout;
        StageButton.SetResourceReference(StyleProperty, "PrimaryButton"); SaveTemplateButton.SetResourceReference(StyleProperty, "PrimaryButton");
        CancelEditButton.Margin = new(0, 0, 8, 0); CancelEditButton.SetResourceReference(StyleProperty, "QuietButton");
        error.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        AutomationProperties.SetName(NameInput, "模板名称"); AutomationProperties.SetName(DescriptionInput, "模板简介");
        StageButton.Click += (_, _) => { if (current is not null) StageRequested?.Invoke(current, Choices.Where(pair => pair.Value.IsChecked == true && pair.Value.IsEnabled).Select(pair => pair.Key).ToArray()); };
        NewTemplateButton.Click += (_, _) => BeginEdit(TemplateRepository.New("", []), true);
        CaptureTemplateButton.Click += (_, _) =>
        {
            try { BeginEdit(TemplateRepository.New("我的设置", TemplateRepository.Capture(original, draft)), true); }
            catch (Exception ex) { StatusChanged?.Invoke("当前设置无法生成模板，请先修正输入。" + ex.Message, true); }
        };
        SaveTemplateButton.Click += (_, _) => SaveEdit();
        CancelEditButton.Click += (_, _) => { if (ResolvePending()) ShowList(); };
        AddRowButton.Click += (_, _) => { AddEditRow(new("", "")); scroll.ScrollToBottom(); };
    }

    internal void Initialize(TemplateRepository value, bool isolatedTest = false) { repository = value; testMode = isolatedTest; }
    internal void Configure(byte[]? config, IReadOnlyDictionary<string, string?> edits)
    {
        original = new TomlDocument(config).Model; draft = edits;
        if (!IsEditing) ShowList();
    }

    internal bool ResolvePending()
    {
        if (editing is null) return true;
        if (Fingerprint() == editBaseline) { ShowList(); return true; }
        var choice = testMode ? PendingTestChoice : MessageBox.Show(Window.GetWindow(this), "模板尚未保存。是否先保存？", "未保存的模板", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Yes) return SaveEdit();
        if (choice == MessageBoxResult.Cancel) return false;
        ShowList(); return true;
    }

    private Button Action(string title, Action action, bool quiet = false)
    {
        var button = new Button { Content = title, Margin = new(0, 0, 8, 0) };
        if (quiet) button.SetResourceReference(StyleProperty, "QuietButton");
        button.Click += (_, _) => action(); return button;
    }
    private void ResetView()
    {
        body.Children.Clear(); footerActions.Children.Clear(); Choices.Clear(); error.Text = ""; scroll.ScrollToTop();
    }

    internal void ShowList()
    {
        editing = null; current = null; ResetView();
        selectedCount.Text = "应用模板后，可继续编辑并保存配置。";
        if (NewTemplateButton.Parent is Panel oldNew) oldNew.Children.Remove(NewTemplateButton);
        if (CaptureTemplateButton.Parent is Panel oldCapture) oldCapture.Children.Remove(CaptureTemplateButton);
        var actions = new WrapPanel { Margin = new(0, 0, 0, 18) };
        NewTemplateButton.Margin = new(0, 0, 8, 8); CaptureTemplateButton.Margin = new(0, 0, 8, 8);
        actions.Children.Add(NewTemplateButton); actions.Children.Add(CaptureTemplateButton); body.Children.Add(actions);
        try
        {
            var templates = repository?.Load() ?? BuiltInTemplates.All;
            if (templates.Count == 0) body.Children.Add(HelpContent.Note("还没有模板，新增一个或恢复内置模板。", new(0, 20, 0, 20)));
            foreach (var template in templates)
            {
                var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var title = new StackPanel { Margin = new(0, 0, 18, 0) };
                title.Children.Add(new TextBlock { Text = template.Name, FontSize = 15, FontWeight = FontWeights.Medium });
                title.Children.Add(HelpContent.Note($"{template.Settings.Count} 项设置 · " + (TemplateRepository.IsBuiltIn(template.Id) ? "内置模板" : "我的模板")));
                if (!string.IsNullOrWhiteSpace(template.Description)) title.Children.Add(HelpContent.Note(template.Description));
                row.Children.Add(title);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                buttons.Children.Add(Action("查看", () => ShowTemplate(template))); buttons.Children.Add(Action("编辑", () => BeginEdit(template), true));
                var more = Action("更多", () => { }, true); var menu = new ContextMenu();
                var copy = new MenuItem { Header = "复制模板" }; copy.Click += (_, _) => CopyTemplate(template);
                var delete = new MenuItem { Header = "删除模板" }; delete.Click += (_, _) => DeleteTemplate(template);
                menu.Items.Add(copy); menu.Items.Add(delete); more.ContextMenu = menu;
                more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; }; buttons.Children.Add(more);
                Grid.SetColumn(buttons, 1); row.Children.Add(buttons);
                var border = new Border { Child = row, Padding = new(0, 15, 0, 15), BorderThickness = new(0, 0, 0, 1) };
                border.SetResourceReference(Border.BorderBrushProperty, "Line"); body.Children.Add(border);
            }
            var restore = Action("恢复内置模板", RestoreBuiltIns, true); restore.Margin = new(0, 20, 0, 0); restore.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(restore);
        }
        catch (Exception ex) { body.Children.Add(HelpContent.Note(ex.Message)); }
        ViewChanged?.Invoke();
    }

    internal void ShowTemplate(ConfigTemplate template)
    {
        current = template; editing = null; ResetView(); footerActions.Children.Add(StageButton);
        var top = new DockPanel { Margin = new(0, 0, 0, 12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right);
        buttons.Children.Add(Action("编辑", () => BeginEdit(template), true)); buttons.Children.Add(Action("复制", () => CopyTemplate(template), true));
        var back = Action("‹ 模板列表", ShowList, true); back.HorizontalAlignment = HorizontalAlignment.Left;
        top.Children.Add(buttons); top.Children.Add(back); body.Children.Add(top);
        body.Children.Add(new TextBlock { Text = template.Name, FontSize = 18, FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(template.Description)) body.Children.Add(HelpContent.Note(template.Description));
        if (Uri.TryCreate(template.SourceUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            body.Children.Add(new Expander { Header = "来源", Content = HelpContent.SourceLink(template.SourceName, template.SourceUrl), Margin = new(0, 8, 0, 0) });
        body.Children.Add(HelpContent.Note("勾选要应用的设置。已有修改默认保留。", new(0, 14, 0, 12)));
        foreach (var diff in template.BuildDiff(original, draft))
        {
            var shell = new StackPanel();
            var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new(1.3, GridUnitType.Star) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var label = new StackPanel { Margin = new(0, 0, 16, 0) };
            var box = new CheckBox { Content = ConfigHelp.Name(diff.Setting.Path), IsChecked = diff.SelectedByDefault, IsEnabled = !diff.Same, Margin = new(0, 0, 0, 3) };
            Choices[diff.Setting.Path] = box; label.Children.Add(box);
            var help = ConfigHelp.Find(diff.Setting.Path);
            if (help is not null) label.Children.Add(HelpContent.Note(help.Summary));
            row.Children.Add(label);
            var values = HelpContent.Note(DisplayValue(diff.CurrentValue) + "  →  " + DisplayValue(diff.Setting.Value), new(0)); values.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(values, 1); row.Children.Add(values);
            var details = new Expander { Header = "了解更多", Content = HelpContent.Details(diff.Setting.Path, ConfigSchema.Instance.Find(diff.Setting.Path)), Visibility = Visibility.Collapsed, Margin = new(22, 12, 0, 0) };
            var info = Action("了解更多", () => { details.Visibility = details.IsVisible ? Visibility.Collapsed : Visibility.Visible; details.IsExpanded = details.Visibility == Visibility.Visible; }, true);
            info.Margin = new(8, 0, 0, 0); Grid.SetColumn(info, 2); row.Children.Add(info); shell.Children.Add(row);
            if (diff.Same || diff.HasDraft || help?.VerificationStatus == "historical") shell.Children.Add(HelpContent.Note(diff.Same ? "与当前值相同" : diff.HasDraft ? "已有修改，勾选将替换" : "旧版设置，当前版本可能不支持。", new(22, 5, 0, 0)));
            shell.Children.Add(details);
            var border = new Border { Child = shell, Padding = new(0, 12, 0, 12), BorderThickness = new(0, 0, 0, 1) };
            border.SetResourceReference(Border.BorderBrushProperty, "Line"); body.Children.Add(border);
            box.Checked += (_, _) => UpdateCount(); box.Unchecked += (_, _) => UpdateCount();
        }
        UpdateCount();
        ViewChanged?.Invoke();
    }

    private static string DisplayValue(string? value)
    {
        var text = value ?? "默认"; if (text.Length > 80) text = text[..80] + "…"; return text;
    }
    private void UpdateCount()
    {
        var count = Choices.Values.Count(box => box.IsEnabled && box.IsChecked == true);
        StageButton.Content = $"应用所选 {count} 项"; StageButton.IsEnabled = count > 0; selectedCount.Text = $"已选 {count} 项 · 应用后还需保存配置";
    }

    internal void BeginEdit(ConfigTemplate template, bool isNew = false)
    {
        editing = template; creating = isNew; current = null; ResetView(); editRows.Clear();
        selectedCount.Text = "模板保存在本机。"; footerActions.Children.Add(CancelEditButton); footerActions.Children.Add(SaveTemplateButton);
        body.Children.Add(new TextBlock { Text = isNew ? "新增模板" : "编辑模板", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 18) });
        body.Children.Add(HelpContent.Note("名称", new(0, 0, 0, 6))); NameInput.Text = template.Name; body.Children.Add(NameInput);
        body.Children.Add(HelpContent.Note("简介（选填）", new(0, 12, 0, 6))); DescriptionInput.Text = template.Description; body.Children.Add(DescriptionInput);
        body.Children.Add(HelpContent.Note("设置", new(0, 20, 0, 6)));
        body.Children.Add(HelpContent.Note("选择或输入字段；取值示例：true、50000、\"live\"。勾选的项目应用时默认选中。", new(0, 0, 0, 8)));
        var headings = new Grid { Margin = new(27, 8, 70, 0) };
        headings.ColumnDefinitions.Add(new() { Width = new(1.2, GridUnitType.Star) }); headings.ColumnDefinitions.Add(new());
        headings.Children.Add(HelpContent.Note("设置字段")); var valueHeading = HelpContent.Note("取值"); Grid.SetColumn(valueHeading, 1); headings.Children.Add(valueHeading); body.Children.Add(headings);
        body.Children.Add(error);
        AddRowButton.HorizontalAlignment = HorizontalAlignment.Left; AddRowButton.Margin = new(0, 14, 0, 0); body.Children.Add(AddRowButton);
        foreach (var setting in template.Settings) AddEditRow(setting);
        if (editRows.Count == 0) AddEditRow(new("", ""));
        editBaseline = Fingerprint(); NameInput.Focus();
        ViewChanged?.Invoke();
    }

    internal void AddEditRow(TemplateSetting setting)
    {
        var row = new Grid { Margin = new(0, 8, 0, 0) }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1.2, GridUnitType.Star) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var box = new CheckBox { IsChecked = setting.SelectByDefault, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0), ToolTip = "应用时默认选中" };
        AutomationProperties.SetName(box, "应用时默认选中"); row.Children.Add(box);
        var path = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, ItemsSource = ConfigHelp.Entries.Keys.Where(key => TemplateRepository.CanInclude(key)).Select(FieldLabel).ToArray(), Text = setting.Path, Margin = new(0, 0, 10, 0) };
        AutomationProperties.SetName(path, "模板配置字段"); Grid.SetColumn(path, 1); row.Children.Add(path);
        var value = new TextBox { Text = setting.Value, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(value, "模板配置取值"); Grid.SetColumn(value, 2); row.Children.Add(value);
        var remove = Action("移除", () => { editRows.RemoveAll(item => ReferenceEquals(item.Path, path)); body.Children.Remove(row); }, true);
        remove.Margin = new(8, 0, 0, 0); Grid.SetColumn(remove, 3); row.Children.Add(remove);
        editRows.Add((path, value, box)); body.Children.Insert(body.Children.IndexOf(AddRowButton), row);
    }
    private static string FieldLabel(string path) => ConfigHelp.Name(path) + " · " + path;
    private static string FieldPath(string label) => ConfigHelp.Entries.Keys.FirstOrDefault(path => FieldLabel(path) == label) ?? label.Trim();
    private string Fingerprint() => System.Text.Json.JsonSerializer.Serialize(new { NameInput.Text, Description = DescriptionInput.Text, Rows = editRows.Select(row => new { Path = row.Path.Text, row.Value.Text, Default = row.Default.IsChecked }) });
    internal bool SaveEdit()
    {
        if (editing is null || repository is null) return false;
        try
        {
            var saved = repository.Save(editing with { Name = NameInput.Text, Description = DescriptionInput.Text,
                Settings = editRows.Select(row => new TemplateSetting(FieldPath(row.Path.Text), row.Value.Text.Trim(), row.Default.IsChecked == true)).ToArray() }, creating);
            editing = null; ShowTemplate(saved); StatusChanged?.Invoke("模板已保存。", false); return true;
        }
        catch (Exception ex) { error.Text = ex is InvalidDataException ? "取值格式不正确，请检查 TOML 写法。" : ex.Message; return false; }
    }
    internal void CopyTemplate(ConfigTemplate template) => BeginEdit(TemplateRepository.New(template.Name + " 副本", template.Settings) with { Description = template.Description }, true);
    internal void DeleteTemplate(ConfigTemplate template)
    {
        if (repository is null || !testMode && MessageBox.Show(Window.GetWindow(this), $"删除模板“{template.Name}”？", "删除模板", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { repository.Delete(template.Id); ShowList(); StatusChanged?.Invoke("模板已删除。", false); }
        catch (Exception ex) { StatusChanged?.Invoke(ex.Message, true); }
    }
    internal void RestoreBuiltIns()
    {
        if (repository is null || !testMode && MessageBox.Show(Window.GetWindow(this), "恢复内置模板？对内置模板的编辑也会还原。", "恢复内置模板", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { repository.RestoreBuiltIns(); ShowList(); StatusChanged?.Invoke("内置模板已恢复。", false); }
        catch (Exception ex) { StatusChanged?.Invoke(ex.Message, true); }
    }
}

