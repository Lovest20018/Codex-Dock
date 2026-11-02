using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CodexConfig.Core;

namespace CodexConfig.App;

public sealed partial class SettingsEditor : UserControl
{
    private readonly ConfigSchema catalog = ConfigSchema.Instance;
    private readonly Grid toolbar = new();
    private readonly StackPanel rows = new();
    private readonly StackPanel scrollBody = new();
    private readonly Grid searchShell = new();
    private readonly ScrollViewer scroll = new();
    private readonly StackPanel paging = new();
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly TextBlock count = new();
    private readonly TextBlock validationSummary = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12), Visibility = Visibility.Collapsed };
    private const int PageSize = 30;
    private int pageIndex;
    private CatalogEntry[] filteredEntries = [];
    private readonly TextBlock pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 12, 0) };
    internal readonly Button PreviousPage = new() { Content = "上一页" };
    internal readonly Button NextPage = new() { Content = "下一页" };
    internal int RenderedCount => rows.Children.Count;
    internal void ShowAdvanced(bool show) => advanced.IsChecked = show;
    private readonly TextBlock empty = new() { Text = "没有匹配的配置项", Margin = new(0, 45, 0, 45), HorizontalAlignment = HorizontalAlignment.Center };
    internal readonly TextBox Search = new();
    internal readonly TextBox NewPath = new();
    internal readonly TextBox NewValue = new();
    internal readonly Button AddSetting = new();
    private readonly ComboBox category = new() { Width = 160, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    private readonly CheckBox advanced = new() { Content = "显示高级与弃用项", VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, string> dynamicPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> inputs = new(StringComparer.Ordinal);
    private Tomlyn.Model.TomlTable originalModel = new();
    private byte[]? originalBytes;
    private string? configHome, selectedModel;
    private ModelCapabilities? capabilities;
    private byte[]? capabilityBaseline;
    internal bool OrdinaryProfile { get; set; }
    private Dictionary<string, string?> changes = new(StringComparer.Ordinal);
    private bool loading = true;
    private bool preferences = true;
    private string scope = "common";
    private readonly Dictionary<string, Button> scopeButtons = new();
    public Dictionary<string, string> Errors { get; } = new(StringComparer.Ordinal);
    public event Action? Changed;
    public event Action? ConnectionRequested;
    internal int VisibleCount { get; private set; }
    private static readonly string[] CommonPaths = ["model_reasoning_effort", "features.fast_mode", "service_tier", "web_search", "model_context_window", "model_auto_compact_token_limit", "approval_policy", "approvals_reviewer", "sandbox_mode", "analytics.enabled"];

    public SettingsEditor()
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new());
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new()); toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        AutomationProperties.SetName(Search, "搜索配置名称、说明或字段");
        Search.ToolTip = "搜索配置名称、说明或字段；Ctrl+F";
        searchShell.Margin = new(0, 0, 12, 0);
        var searchPlaceholder = HelpContent.Note("搜索设置…", new(12, 0, 12, 0));
        searchPlaceholder.IsHitTestVisible = false; searchPlaceholder.VerticalAlignment = VerticalAlignment.Center;
        searchShell.Children.Add(Search); searchShell.Children.Add(searchPlaceholder);
        category.ItemsSource = new[] { new CatalogCategory("all", "所有分类"), new CatalogCategory("unlisted", "未收录配置") }.Concat(catalog.Categories).ToArray();
        category.SelectedValue = "all"; Grid.SetColumn(category, 1);
        toolbar.Children.Add(searchShell); toolbar.Children.Add(category);
        var scopes = new WrapPanel { Margin = new(0, 10, 0, 8) };
        foreach (var (id, title) in new[] { ("common", "常用"), ("configured", "已配置"), ("all", "全部"), ("changed", "已修改") })
        {
            var button = new Button { Content = title, Margin = new(0, 0, 8, 0), Padding = new(12, 6, 12, 6), MinHeight = 34 };
            button.SetResourceReference(StyleProperty, "QuietButton"); button.Click += (_, _) => SetScope(id);
            scopeButtons[id] = button; scopes.Children.Add(button);
        }
        Grid.SetRow(scopes, 1); toolbar.Children.Add(scopes);
        var options = new Button { Content = "更多选项", HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, 0, 8), Padding = new(10, 5, 10, 5), MinHeight = 34 };
        options.SetResourceReference(StyleProperty, "QuietButton");
        var menu = new ContextMenu();
        var advancedItem = new MenuItem { Header = "显示高级与弃用项", IsCheckable = true };
        advancedItem.Click += (_, _) => advanced.IsChecked = advancedItem.IsChecked;
        advanced.Checked += (_, _) => advancedItem.IsChecked = true; advanced.Unchecked += (_, _) => advancedItem.IsChecked = false;
        menu.Items.Add(advancedItem); options.ContextMenu = menu;
        options.Click += (_, _) => { menu.PlacementTarget = options; menu.IsOpen = true; };
        var add = new Expander { Header = "添加设置", Margin = new(0, 0, 0, 12), Visibility = Visibility.Collapsed };
        var addItem = new MenuItem { Header = "添加设置" }; menu.Items.Add(addItem);
        var instanceItem = new MenuItem { Header = "添加 MCP / 供应商 / 权限方案" }; menu.Items.Insert(0, instanceItem);
        instanceItem.Click += (_, _) => { if (!OrdinaryProfile) AddInstance(); };
        menu.Opened += (_, _) => instanceItem.IsEnabled = !OrdinaryProfile;
        addItem.Click += (_, _) => { add.Visibility = Visibility.Visible; add.IsExpanded = true; NewPath.Focus(); };
        add.Collapsed += (_, _) => add.Visibility = Visibility.Collapsed;
        var addBody = new StackPanel();
        var newPath = NewPath; newPath.ToolTip = "配置路径，例如 features.new_option"; newPath.Margin = new(0, 8, 0, 8);
        AutomationProperties.SetName(newPath, "新配置路径");
        var newValue = NewValue; newValue.ToolTip = "TOML 值，例如 true、123、\"文本\" 或 [\"a\"]"; newValue.AcceptsReturn = true; newValue.MinHeight = 60;
        AutomationProperties.SetName(newValue, "新配置 TOML 值");
        var addButton = AddSetting; addButton.Content = "添加"; addButton.HorizontalAlignment = HorizontalAlignment.Right; addButton.Margin = new(0, 8, 0, 0);
        var addError = Helper("", new(0, 8, 0, 0)); addError.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        addBody.Children.Add(newPath); addBody.Children.Add(newValue); addBody.Children.Add(addButton); addBody.Children.Add(addError); add.Content = addBody;

        addButton.Click += (_, _) =>
        {
            try
            {
                var path = TomlDocument.FormatPath(TomlDocument.ParsePath(newPath.Text.Trim()));
                var repr = newValue.Text.Trim();
                Validate(path, repr);
                changes[path] = repr; Errors.Remove(path); addError.Text = "";
                Revalidate();
                Filter(path); Changed?.Invoke();
            }
            catch (Exception error) { addError.Text = SafeError(newPath.Text, error); }
        };
        Grid.SetRow(options, 1); Grid.SetColumn(options, 1); toolbar.Children.Add(options);
        Grid.SetRow(add, 2); Grid.SetColumnSpan(add, 2); toolbar.Children.Add(add);
        count.SetResourceReference(StyleProperty, "Helper"); count.Margin = new(0, 0, 0, 5); Grid.SetRow(count, 3); Grid.SetColumnSpan(count, 2); count.Visibility = Visibility.Collapsed; toolbar.Children.Add(count);
        scrollBody.Children.Add(toolbar);
        validationSummary.SetResourceReference(TextBlock.ForegroundProperty, "Error"); scrollBody.Children.Add(validationSummary);
        scroll.SetResourceReference(StyleProperty, "PageScroll"); scroll.Padding = new(0, 0, 10, 24);
        var body = new StackPanel(); body.Children.Add(rows); body.Children.Add(empty);
        var surface = new Border { Child = body, Padding = new(20, 0, 20, 4) };
        surface.SetResourceReference(StyleProperty, "SurfacePanel"); scrollBody.Children.Add(surface); scroll.Content = scrollBody;
        layout.Children.Add(scroll); Content = layout;
        paging.Orientation = Orientation.Horizontal; paging.HorizontalAlignment = HorizontalAlignment.Right; paging.Margin = new(0, 8, 10, 8);
        paging.Children.Add(PreviousPage); paging.Children.Add(pageLabel); paging.Children.Add(NextPage);
        Grid.SetRow(paging, 1); layout.Children.Add(paging);
        PreviousPage.Click += (_, _) => { pageIndex--; RenderPage(); };
        NextPage.Click += (_, _) => { pageIndex++; RenderPage(); };
        empty.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Search.TextChanged += (_, _) => { searchPlaceholder.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; searchTimer.Stop(); searchTimer.Start(); };
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); Refresh(); };
        category.SelectionChanged += (_, _) => { if (!loading) { scope = "all"; preferences = false; Refresh(); } };
        advanced.Checked += (_, _) => Refresh(); advanced.Unchecked += (_, _) => Refresh();
        loading = false;
    }

    internal void SetHeader(FrameworkElement header) => scrollBody.Children.Insert(0, header);

    internal void Configure(byte[]? config, Dictionary<string, string?> edits)
    {
        var offset = scroll.VerticalOffset; var page = pageIndex;
        loading = true; originalBytes = config; originalModel = new TomlDocument(config).Model; changes = edits;
        dynamicPaths.Clear();
        loading = false; Revalidate(); Refresh(); pageIndex = page; RenderPage(); scroll.ScrollToVerticalOffset(offset);
    }

    internal void SetMode(bool common)
    {
        SetScope(common ? "common" : "all");
    }

    internal void SetContext(string home, string model, byte[]? inherited = null)
    {
        configHome = home; selectedModel = model; capabilityBaseline = inherited;
        if (!loading) Refresh();
    }
    private void Validate(string path, string? repr)
    {
        if (OrdinaryProfile) ProfileStore.ValidateOrdinary(path, repr);
        else catalog.Validate(path, repr, allowPreferences: true);
    }

    private TomlDocument Projected()
    {
        var document = new TomlDocument(originalBytes);
        foreach (var (path, value) in changes)
            try { if (value is null) document.Remove(path); else document.SetRaw(path, value); }
            catch (Exception error) when (error is ArgumentException or InvalidDataException) { }
        return document;
    }

    private void SetScope(string selected)
    {
        loading = true; scope = selected; preferences = selected == "common"; category.SelectedValue = "all";
        loading = false; searchTimer.Stop(); Refresh(); scroll.ScrollToTop();
    }
    internal void ShowChanged() { Search.Text = ""; SetScope("changed"); }
    internal void ShowConfigured() { Search.Text = ""; SetScope("configured"); }
    internal string Scope => scope;
    internal int CurrentPage => pageIndex;

    internal void FocusSearch() { Search.Focus(); Search.SelectAll(); }
    internal FrameworkElement Input(string path) => inputs[path];
    internal void Filter(string text) { loading = true; scope = "all"; preferences = false; category.SelectedValue = "all"; Search.Text = text; loading = false; searchTimer.Stop(); Refresh(); }

    internal void Revalidate()
    {
        Errors.Clear();
        var projected = Projected();
        ReadCapabilities(projected);
        foreach (var (path, repr) in changes)
            try { Validate(path, repr); }
            catch (Exception error) { Errors[path] = SafeError(path, error); }
        if (Errors.Count == 0)
            foreach (var issue in ConfigConstraints.InspectChanges(new TomlDocument(originalBytes), projected, capabilities).Where(issue => issue.IsError))
                Errors[issue.Path] = issue.Message;
        validationSummary.Text = string.Join("\n", Errors.Select(error => error.Key + "：" + error.Value));
        validationSummary.Visibility = Errors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh()
    {
        if (loading) return;
        var projected = Projected();
        ReadCapabilities(projected);
        IEnumerable<CatalogEntry> entries = scope is "configured" or "changed"
            ? catalog.ConfiguredEntries(originalModel, changes.Keys)
            : catalog.ExpandedEntries(projected.Model).Concat(catalog.UnlistedEntries(originalModel, changes.Keys));
        entries = entries.Where(e => e.SupportedPlatform);
        if (preferences) entries = CommonPaths.Select(path => entries.FirstOrDefault(e => e.Path == path)).OfType<CatalogEntry>().ToArray();
        else
        {
            var filter = scope == "changed" ? "changed" : category.SelectedValue as string ?? "all";
            if (advanced.IsChecked != true && scope != "configured" && filter != "changed" && Search.Text.Trim().Length == 0) entries = entries.Where(e => e.Stage is not ("removed" or "deprecated" or "under_development") && !e.Deprecated && e.Kind is not ("table" or "map" or "recursive"));
            if (filter == "changed") entries = entries.Where(e => changes.ContainsKey(e.Path));
            else if (filter != "all") entries = entries.Where(e => e.Category == filter);
        }
        var text = Search.Text.Trim();
        if (text.Length > 0)
            entries = entries.Any(e => e.Path.Equals(text, StringComparison.OrdinalIgnoreCase))
                ? entries.Where(e => e.Path.Equals(text, StringComparison.OrdinalIgnoreCase))
                : entries.Where(e => (e.Name + " " + e.Path + " " + e.HelpPath + " " + e.Chinese + " " + e.Description + " " + ConfigHelp.Find(e.HelpPath)?.SearchText).Contains(text, StringComparison.OrdinalIgnoreCase));
        foreach (var (id, button) in scopeButtons)
        {
            button.SetResourceReference(BackgroundProperty, id == scope ? "AccentSoft" : "Canvas");
            button.SetResourceReference(ForegroundProperty, id == scope ? "Accent" : "Muted");
        }
        category.IsEnabled = !preferences;
        category.Visibility = preferences ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumnSpan(searchShell, preferences ? 2 : 1);
        searchShell.Margin = preferences ? new(0) : new(0, 0, 12, 0);
        filteredEntries = entries.ToArray();
        VisibleCount = filteredEntries.Length; count.Text = $"{VisibleCount} 项 · {catalog.SchemaVersion} · 核查内核 {catalog.VerifiedRuntime}";
        count.Visibility = Visibility.Visible;
        scopeButtons["changed"].Content = $"已修改 {changes.Count}";
        empty.Visibility = VisibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        pageIndex = 0; RenderPage();
    }
    private void ReadCapabilities(TomlDocument projected)
    {
        var capabilityModel = OrdinaryProfile ? projected.String("model") ?? selectedModel : selectedModel;
        capabilities = configHome is null ? null : ModelCapabilities.Read(configHome, projected, capabilityModel)
            ?? (capabilityBaseline is null ? null : ModelCapabilities.Read(configHome, new TomlDocument(capabilityBaseline), capabilityModel));
    }

    private CatalogEntry? FindDraftEntry(string path)
    {
        try { return catalog.Find(path); }
        catch (ArgumentException) { return null; }
    }

    private void RenderPage()
    {
        var pages = Math.Max(1, (filteredEntries.Length + PageSize - 1) / PageSize);
        paging.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        pageIndex = Math.Clamp(pageIndex, 0, pages - 1);
        rows.Children.Clear(); inputs.Clear();
        foreach (var entry in filteredEntries.Skip(pageIndex * PageSize).Take(PageSize))
        {
            if (preferences && entry.Path is "model_reasoning_effort" or "web_search" or "approval_policy" or "analytics.enabled")
            {
                var title = new TextBlock { Text = entry.Path switch { "model_reasoning_effort" => "模型与速度", "web_search" => "搜索与上下文", "approval_policy" => "执行与权限", _ => "隐私与统计" }, Margin = new(0, 18, 0, 8), FontSize = 16, FontWeight = FontWeights.SemiBold };
                rows.Children.Add(title);
            }
            rows.Children.Add(BuildRow(entry));
        }
        pageLabel.Text = $"{pageIndex + 1} / {pages}";
        PreviousPage.IsEnabled = pageIndex > 0; NextPage.IsEnabled = pageIndex + 1 < pages;
        scroll.ScrollToTop();
    }

    private FrameworkElement BuildRow(CatalogEntry entry)
    {
        var shell = new Border { BorderThickness = new(0, 0, 0, 1), Padding = new(0, 14, 0, 14) };
        shell.SetResourceReference(Border.BorderBrushProperty, "Line");
        var body = new StackPanel(); shell.Child = body;
        var line = new Grid(); line.ColumnDefinitions.Add(new()); line.ColumnDefinitions.Add(new() { Width = new(260) });
        line.RowDefinitions.Add(new() { Height = GridLength.Auto }); line.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var label = new StackPanel { Margin = new(0, 3, 18, 0) };
        var displayName = ConfigHelp.Name(entry.HelpPath);
        if (entry.DefinitionPath is not null && !entry.Dynamic) displayName += " · " + entry.Path;
        var title = new TextBlock { Text = displayName, FontSize = 14, FontWeight = FontWeights.Medium };
        label.Children.Add(title);
        label.Children.Add(Helper(ConfigHelp.Summary(entry), new(0, 5, 0, 0)));
        line.Children.Add(label); body.Children.Add(line);
        var right = new StackPanel(); Grid.SetColumn(right, 1); line.Children.Add(right);
        line.SizeChanged += (_, _) =>
        {
            var compact = line.ActualWidth < 580;
            Grid.SetColumnSpan(label, compact ? 2 : 1); Grid.SetColumnSpan(right, compact ? 2 : 1);
            Grid.SetRow(right, compact ? 1 : 0); Grid.SetColumn(right, compact ? 0 : 1);
            right.Margin = compact ? new(0, 12, 0, 0) : new(0);
            label.Margin = compact ? new(0) : new(0, 3, 24, 0);
        };
        var editable = (entry.CanEdit || ConfigSchema.PreferencePaths.Contains(entry.Path) && entry.Stage != "removed" || OrdinaryProfile && entry.Path == "model")
            && (!OrdinaryProfile || ProfileStore.CanEditPath(entry.Path));
        if (!entry.Dynamic && entry.HelpPath.StartsWith("model_providers.<name>.", StringComparison.Ordinal))
        {
            var parts = TomlDocument.ParsePath(entry.Path);
            if (parts[1] is "openai" or "ollama" or "lmstudio" or "amazon-bedrock" && OriginalValue(TomlDocument.FormatPath(parts.Take(2))) is null)
            {
                editable = false;
                right.Children.Add(Helper("当前是内置供应商，能力由执行端提供；添加自定义供应商后可编辑。", new(0, 6, 0, 0)));
            }
        }
        var path = dynamicPaths.GetValueOrDefault(entry.Path) ?? entry.Path;
        var details = new Expander { Header = "了解更多", Margin = new(0, 10, 0, 0) };
        var detailBody = new StackPanel { Margin = new(0, 10, 0, 0) }; details.Content = detailBody;
        body.Children.Add(details);
        details.Visibility = Visibility.Collapsed;
        label.Children.RemoveAt(0);
        var titleLine = new DockPanel { LastChildFill = true };
        var info = new Button { Content = "了解更多", MinHeight = 22, FontSize = 11, Padding = new(6, 0, 6, 0), Margin = new(8, 0, 0, 0) };
        info.SetResourceReference(StyleProperty, "QuietButton"); DockPanel.SetDock(info, Dock.Right);
        titleLine.Children.Add(info); titleLine.Children.Add(title); label.Children.Insert(0, titleLine);
        info.Click += (_, _) => { details.Visibility = details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; details.IsExpanded = details.Visibility == Visibility.Visible; };
        details.Collapsed += (_, _) => details.Visibility = Visibility.Collapsed;
        TextBox? pathBox = null;
        if (entry.Dynamic)
        {
            pathBox = new TextBox { Text = path, Margin = new(0, 0, 0, 9) };
            AutomationProperties.SetName(pathBox, entry.Name + "的配置路径"); detailBody.Children.Add(pathBox);
            detailBody.Children.Add(Helper("将 <name> 替换为名称；包含点或路径的名称请使用 TOML 引号。", new(0, 0, 0, 10)));
            var nameButton = new Button { Content = "设置名称", HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 7, 0, 0) };
            nameButton.SetResourceReference(StyleProperty, "QuietButton"); right.Children.Add(nameButton);
            nameButton.Click += (_, _) => { details.Visibility = Visibility.Visible; details.IsExpanded = true; pathBox.Focus(); pathBox.SelectAll(); };
        }
        detailBody.Children.Add(HelpContent.Details(entry.HelpPath, entry));
        if (!entry.Dynamic) detailBody.Children.Add(Helper("修改目标：" + entry.Path + "\n" + ConfigScope.Describe(entry.Path), new(0, 8, 0, 0)));
        if (entry.Path is "model_reasoning_effort" or "service_tier")
            detailBody.Children.Add(Helper(capabilities is null ? "当前模型目录未提供可核实的能力；档位由服务支持情况决定。" :
                $"模型能力：{capabilities.Model} · {capabilities.SourcePath}\n更新时间：{capabilities.UpdatedAt.LocalDateTime:g}" + (capabilities.ContextWindow is { } window ? $" · 目录上下文 {window:N0} Token" : ""), new(0, 8, 0, 0)));
        var current = Helper("", new(0, 11, 0, 0)); detailBody.Children.Add(current);
        var errorText = Helper("", new(0, 7, 0, 0)); errorText.SetResourceReference(TextBlock.ForegroundProperty, "Error"); body.Children.Add(errorText);
        bool rowLoading = true;
        string Path() => pathBox?.Text.Trim() ?? path;
        void State()
        {
            var key = Path(); var value = OriginalValue(key);
            title.Text = displayName + (changes.ContainsKey(key) ? " · 未保存" : "");
            current.Text = "本文件：" + (value is null ? "未设置 · 继承值待检测" : Sensitive(key) ? "••••••••（已隐藏）" : entry.Kind is "table" or "map" or "recursive" ? "已设置（查看子项）" : ConfigSchema.Literal(value));
            if (changes.TryGetValue(key, out var repr)) current.Text += repr is null ? "\n未保存：取消本文件覆盖" : "\n未保存：" + (Sensitive(key) ? "••••••••" : repr);
            errorText.Text = Errors.GetValueOrDefault(key) ?? ""; errorText.Visibility = errorText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        void Change(string? repr)
        {
            if (rowLoading) return;
            var key = Path();
            try
            {
                Validate(key, repr);
                var old = OriginalValue(key);
                var same = repr is null ? old is null : old is not null && ConfigSchema.Literal(TomlDocument.ParseModel("value = " + repr)["value"]) == ConfigSchema.Literal(old);
                if (same) changes.Remove(key); else changes[key] = repr;
                Errors.Remove(key);
            }
            catch (Exception error) { changes[key] = repr; Errors[key] = SafeError(key, error); }
            Revalidate(); State(); scopeButtons["changed"].Content = $"已修改 {changes.Count}"; Changed?.Invoke();
        }
        Action loadInput;
        if (!editable)
        {
            var text = Helper("", new(0, 8, 0, 0)); right.Children.Add(text);
            if (entry.ManagedConnection)
            {
                var link = new Button { Content = "前往服务连接", HorizontalAlignment = HorizontalAlignment.Right };
                link.SetResourceReference(StyleProperty, "QuietButton"); link.Click += (_, _) => ConnectionRequested?.Invoke(); right.Children.Add(link);
            }
            loadInput = () => text.Text = OriginalValue(Path()) is { } value ? Sensitive(Path()) ? "已设置（已隐藏）" : entry.Kind is "table" or "map" or "recursive" ? "已设置（查看子项）" : ConfigSchema.Literal(value) : "未设置";
        }
        else if (entry.Kind is "boolean" or "enum" or "bool_or_table" || entry.Path is "model_reasoning_effort" or "service_tier")
        {
            var choices = new List<ValueChoice> { new("跟随默认", null) };
            if (entry.Kind is "boolean" or "bool_or_table") { choices.Add(new("开启", "true")); choices.Add(new("关闭", "false")); }
            else
            {
                var options = entry.Path switch
                {
                    "model_reasoning_effort" when capabilities is { ReasoningLevels.Length: > 0 } => capabilities.ReasoningLevels,
                    "model_reasoning_effort" when entry.EnumValues.Length == 0 => new[] { "none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra" },
                    "service_tier" => new[] { "default" }.Concat(capabilities?.ServiceTiers ?? []).Distinct().ToArray(),
                    _ => entry.EnumValues
                };
                foreach (var option in options) choices.Add(new(Friendly(option), TomlDocument.Quote(option)));
            }
            var box = new ComboBox { DisplayMemberPath = "Label", ItemsSource = choices };
            AutomationProperties.SetName(box, entry.Name); right.Children.Add(box); inputs[entry.Path] = box;
            TextBox? custom = null;
            var granularFields = new Dictionary<string, ComboBox>();
            StackPanel? granularPanel = null;
            string GranularValue() => "{ granular = { " + string.Join(", ", granularFields.Where(field => field.Value.SelectedItem is ValueChoice { Repr: not null })
                .Select(field => field.Key + " = " + ((ValueChoice)field.Value.SelectedItem).Repr)) + " } }";
            if (entry.Path == "approval_policy")
            {
                choices.Add(new("细分规则", "$granular"));
                granularPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new(0, 10, 0, 0) };
                granularPanel.Children.Add(Helper("分别选择是否允许弹出审批提示；标为必填的项需明确选择。空白可选项使用执行端默认。", new(0, 0, 0, 8)));
                var required = catalog.Find("approval_policy.granular")!.RequiredProperties;
                foreach (var field in catalog.Entries.Where(item => item.Path.StartsWith("approval_policy.granular.", StringComparison.Ordinal) && item.Kind == "boolean"))
                {
                    var key = field.Path.Split('.')[^1];
                    var input = new ComboBox { ItemsSource = new ValueChoice[] { new(required.Contains(key) ? "请选择（必填）" : "跟随默认", null), new("允许提示", "true"), new("拒绝提示", "false") }, DisplayMemberPath = "Label" };
                    granularPanel.Children.Add(Helper(field.Name + (required.Contains(key) ? "（必填）" : ""), new(0, 8, 0, 5))); granularPanel.Children.Add(input);
                    AutomationProperties.SetName(input, field.Path); inputs[field.Path] = input; granularFields[key] = input;
                    input.SelectionChanged += (_, _) => { if (!rowLoading && box.SelectedItem is ValueChoice { Repr: "$granular" }) Change(GranularValue()); };
                }
                detailBody.Children.Insert(pathBox is null ? 1 : 2, granularPanel);
            }
            else if (entry.Kind == "bool_or_table")
            {
                choices.Add(new("自定义 TOML", "$custom"));
                custom = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxHeight = 200, VerticalContentAlignment = VerticalAlignment.Top, Margin = new(0, 10, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                AutomationProperties.SetName(custom, entry.Name + "的自定义 TOML"); detailBody.Children.Insert(pathBox is null ? 1 : 2, custom);
                custom.TextChanged += (_, _) => { if (!rowLoading) Change(string.IsNullOrWhiteSpace(custom.Text) ? null : custom.Text.Trim()); };
            }
            box.Items.Refresh();
            loadInput = () =>
            {
                var repr = Value(Path()); var choice = choices.FirstOrDefault(c => c.Repr == repr);
                if (granularPanel is not null)
                {
                    Tomlyn.Model.TomlTable? granular = null;
                    try { if (repr is not null && TomlDocument.ParseModel("value = " + repr)["value"] is Tomlyn.Model.TomlTable policy && policy.TryGetValue("granular", out var rules)) granular = rules as Tomlyn.Model.TomlTable; }
                    catch (InvalidDataException) { }
                    foreach (var (key, input) in granularFields)
                    {
                        var value = granular?.TryGetValue(key, out var flag) == true ? ConfigSchema.Literal(flag) : null;
                        input.SelectedItem = input.Items.Cast<ValueChoice>().FirstOrDefault(item => item.Repr == value) ?? input.Items[0];
                    }
                    if (granular is not null) choice = choices.First(item => item.Repr == "$granular");
                }
                else if (custom is not null)
                {
                    custom.Text = repr is not (null or "true" or "false") ? repr : "";
                    if (choice is null) choice = choices.First(c => c.Repr == "$custom");
                }
                if (choice is null) { choice = new(repr ?? "跟随默认", repr); choices.Add(choice); box.Items.Refresh(); }
                box.SelectedItem = choice;
            };
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is not ValueChoice choice) return;
                if (granularPanel is not null) granularPanel.Visibility = choice.Repr == "$granular" ? Visibility.Visible : Visibility.Collapsed;
                if (choice.Repr == "$custom") { if (!rowLoading) { details.Visibility = Visibility.Visible; details.IsExpanded = true; custom?.Focus(); } }
                else if (choice.Repr == "$granular") { if (!rowLoading) { details.Visibility = Visibility.Visible; details.IsExpanded = true; Change(GranularValue()); } }
                else Change(choice.Repr);
            };
        }
        else
        {
            var longText = entry.Kind == "string" && (entry.Path is "developer_instructions" or "compact_prompt" || OriginalValue(Path()) is string text && (text.Contains('\n') || text.Length > 160));
            var structured = entry.Kind is "array" or "union" or "raw" || longText;
            var secret = Sensitive(Path());
            if (secret && entry.Kind == "string")
            {
                var password = new PasswordBox(); right.Children.Add(password); inputs[entry.Path] = password;
                AutomationProperties.SetName(password, entry.Name);
                loadInput = () => password.Password = "";
                password.PasswordChanged += (_, _) => Change(TomlDocument.Quote(password.Password));
                right.Children.Add(Helper("已设置的值隐藏，输入新值才替换。", new(0, 5, 0, 0)));
            }
            else
            {
                var box = new TextBox { MinHeight = structured ? 90 : 38, AcceptsReturn = structured, TextWrapping = structured ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalContentAlignment = structured ? VerticalAlignment.Top : VerticalAlignment.Center, MaxHeight = 200, VerticalScrollBarVisibility = structured ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
                AutomationProperties.SetName(box, entry.Name); inputs[entry.Path] = box;
                if (structured)
                {
                    detailBody.Children.Insert(pathBox is null ? 1 : 2, box);
                    var edit = new Button { Content = longText ? "编辑长文本" : "编辑结构化值", HorizontalAlignment = HorizontalAlignment.Right };
                    edit.SetResourceReference(StyleProperty, "QuietButton"); right.Children.Add(edit);
                    edit.Click += (_, _) => { details.Visibility = Visibility.Visible; details.IsExpanded = true; box.Focus(); };
                }
                else right.Children.Add(box);
                loadInput = () =>
                {
                    var repr = Value(Path());
                    if (secret) box.Text = "";
                    else if (entry.Kind == "string" && repr is not null)
                    {
                        try { box.Text = TomlDocument.ParseModel("value = " + repr)["value"] as string ?? ""; }
                        catch (Exception) { box.Text = repr; }
                    }
                    else box.Text = repr ?? "";
                };
                box.TextChanged += (_, _) => Change(entry.Path == "service_tier" && string.IsNullOrWhiteSpace(box.Text) ? null : entry.Kind == "string" ? TomlDocument.Quote(box.Text) : string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim());
                if (!structured) box.ToolTip = entry.Kind is "integer" or "number" || entry.Path == "service_tier" ? "留空时跟随默认" : "直接填写文本；恢复默认会移除覆盖值";
            }
        }
        if (editable)
        {
            var actions = new WrapPanel { Margin = new(0, 10, 0, 0) };
            var reset = new Button { Content = "取消本文件覆盖", Margin = new(0, 0, 8, 0) };
            var undo = new Button { Content = "撤销此项修改" };
            reset.SetResourceReference(StyleProperty, "QuietButton"); undo.SetResourceReference(StyleProperty, "QuietButton");
            actions.Children.Add(reset); actions.Children.Add(undo); detailBody.Children.Add(actions);
            reset.Click += (_, _) => { Change(null); rowLoading = true; loadInput(); rowLoading = false; State(); };
            undo.Click += (_, _) => { changes.Remove(Path()); Errors.Remove(Path()); rowLoading = true; loadInput(); rowLoading = false; State(); Changed?.Invoke(); };
        }
        if (pathBox is not null) pathBox.TextChanged += (_, _) =>
        {
            var previousPath = dynamicPaths.GetValueOrDefault(entry.Path) ?? entry.Path;
            var moveInvalid = Errors.ContainsKey(previousPath) && changes.TryGetValue(previousPath, out _);
            var invalidValue = changes.GetValueOrDefault(previousPath);
            dynamicPaths[entry.Path] = Path();
            if (moveInvalid)
            {
                changes.Remove(previousPath); Errors.Remove(previousPath); Change(invalidValue);
            }
            rowLoading = true; loadInput(); rowLoading = false; State();
        };
        loadInput(); rowLoading = false; State(); return shell;
    }

    private object? OriginalValue(string path)
    {
        try { return TomlDocument.ValueAt(originalModel, path); }
        catch (ArgumentException) { return null; }
    }
    private string? Value(string path) => changes.TryGetValue(path, out var repr) ? repr : OriginalValue(path) is { } value ? ConfigSchema.Literal(value) : null;
    private static TextBlock Helper(string text, Thickness margin) { var block = new TextBlock { Text = text, Margin = margin }; block.SetResourceReference(StyleProperty, "Helper"); return block; }
    private static string Friendly(string value) => value switch { "none" => "无 · none", "minimal" => "最低 · minimal", "low" => "低 · low", "medium" => "中 · medium", "high" => "高 · high", "xhigh" => "较高 · xhigh", "disabled" => "关闭", "cached" => "缓存搜索", "indexed" => "索引约束搜索", "live" => "实时搜索", _ => value };
    internal static bool Sensitive(string path) => TomlDocument.IsSensitivePath(path);
    private static string SafeError(string path, Exception error) => Sensitive(path) || error is InvalidDataException ? "输入值格式不正确，请检查该配置的类型与 TOML 格式。" : error.Message;
    internal sealed record ValueChoice(string Label, string? Repr);
}
