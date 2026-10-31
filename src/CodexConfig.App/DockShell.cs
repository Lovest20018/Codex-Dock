using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private (ConfigSnapshot? Snapshot, ConnectionSettings Settings, bool Dirty, CodexRuntimeStatus? Runtime, bool ExternalChange, string Error, string Home)? overviewState;
    private void ModelPageClicked(object sender, RoutedEventArgs e) => ShowPage("preferences");
    private void LibraryTemplatesClicked(object sender, RoutedEventArgs e) => ShowPage("templates");
    private void LibraryBackupClicked(object sender, RoutedEventArgs e) => ShowPage("backup");

    private void InitializeDockLayout()
    {
        RootPanel.SizeChanged += (_, _) =>
        {
            var compact = RootPanel.ActualWidth < 820;
            SidebarColumn.Width = new(compact ? 176 : 196);
            PageHeading.Margin = compact ? new(28, 14, 28, 12) : new(28, 22, 28, 20);
        };
        foreach (var (nav, glyph) in new[] { (OverviewNav, "\uE80F"), (ConnectionNav, "\uE774"), (CatalogNav, "\uE9D9"), (MobileNav, "\uE8EA"), (TemplatesNav, "\uE8F1"), (AdvancedNav, "\uE9E9"), (AppNav, "\uE713") })
        {
            var label = ((TextBlock)nav.Content).Text;
            var content = new Grid();
            content.ColumnDefinitions.Add(new() { Width = new(27) }); content.ColumnDefinitions.Add(new());
            content.Children.Add(new TextBlock { Text = glyph, FontFamily = new("Segoe MDL2 Assets"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1); content.Children.Add(text); nav.Content = content;
        }
        SaveBarLayout.SizeChanged += (_, _) =>
        {
            var compact = SaveBarLayout.ActualWidth < 650;
            Grid.SetRow(SaveBarActions, compact ? 1 : 0); Grid.SetColumn(SaveBarActions, compact ? 0 : 1);
            Grid.SetColumnSpan(SaveBarActions, compact ? 2 : 1);
            SaveBarActions.Margin = compact ? new(0, 8, 0, 0) : new(0);
            SaveBarActions.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        };
    }

    private static TextBlock OverviewText(string value, string brush = "Ink", double size = 14)
    {
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size };
        text.SetResourceReference(TextBlock.ForegroundProperty, brush); return text;
    }
    private static Border OverviewSurface(UIElement body, Thickness margin)
    {
        var surface = new Border { Child = body, Margin = margin };
        surface.SetResourceReference(StyleProperty, "SurfacePanel"); return surface;
    }
    private Button OverviewAction(string label, string page, bool primary = false)
    {
        var button = new Button { Content = label, Margin = new(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
        if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        button.Click += (_, _) => ShowPage(page); return button;
    }

    private void RefreshOverview()
    {
        if (OverviewBody is null) return;
        var state = (snapshot, loadedSettings, Dirty, codexRuntime, externalConfigurationChanged, localStateError, home);
        if (overviewState is { } previous && previous == state) return;
        overviewState = state;
        OverviewBody.Children.Clear();
        AddLocalCodexStatus();
        if (snapshot is null)
        {
            OverviewBody.Children.Add(OverviewText("配置暂不可用", size: 20));
            OverviewBody.Children.Add(OverviewText("请检查配置位置，或按页面提示恢复未完成的保存。", "Muted")); return;
        }
        if (snapshot.Config is null && snapshot.Auth is null)
        {
            var welcome = new StackPanel();
            welcome.Children.Add(new System.Windows.Controls.Image { Source = (ImageSource)FindResource("BrandMark"), Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 20) });
            welcome.Children.Add(OverviewText("连接模型，从这里开始", size: 24));
            var note = OverviewText("设置模型服务，或先调整 Codex 的常用配置。所有修改都可以在保存前预览。", "Muted"); note.Margin = new(0, 12, 0, 24); welcome.Children.Add(note);
            var actions = new WrapPanel(); actions.Children.Add(OverviewAction("设置连接", "connection", true)); actions.Children.Add(OverviewAction("模型与行为", "preferences")); welcome.Children.Add(actions);
            OverviewBody.Children.Add(OverviewSurface(welcome, new(0))); return;
        }
        var summary = new StackPanel(); summary.Children.Add(OverviewText("当前连接", "Muted", 12));
        var mixed = loadedSettings.Mode == ConnectionMode.OfficialWithApi;
        var mode = OverviewText(mixed ? "官方账号 + 独立模型入口" : loadedSettings.ApiKey.Length > 0 ? loadedSettings.Authentication == AuthenticationChoice.Pat ? "PAT 连接" : "PAT / API Key 连接" : "待设置 PAT", size: 22);
        mode.FontWeight = FontWeights.SemiBold; mode.Margin = new(0, 8, 0, 20); summary.Children.Add(mode);
        var route = new Grid();
        var stages = mixed ? new[] { ("Codex", "保留官方账号身份"), ("本地网关", "独立模型凭据"), ("模型入口", "接收模型请求") } : new[] { ("Codex", "读取本地配置"), ("模型入口", "由客户端连接") };
        for (var i = 0; i < stages.Length; i++)
        {
            if (i > 0)
            {
                route.ColumnDefinitions.Add(new() { Width = new(28) });
                var arrow = OverviewText("→", "Muted", 18); arrow.HorizontalAlignment = HorizontalAlignment.Center; arrow.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(arrow, i * 2 - 1); route.Children.Add(arrow);
            }
            route.ColumnDefinitions.Add(new());
            var step = new StackPanel();
            var name = OverviewText(stages[i].Item1, "Accent", 14); name.FontWeight = FontWeights.SemiBold; step.Children.Add(name);
            var caption = OverviewText(stages[i].Item2, "Muted", 12); caption.Margin = new(0, 5, 0, 0); step.Children.Add(caption);
            var tile = new Border { Padding = new(14, 12, 14, 12), CornerRadius = new(8), Child = step };
            tile.SetResourceReference(Border.BackgroundProperty, "AccentSoft"); Grid.SetColumn(tile, i * 2); route.Children.Add(tile);
        }
        summary.Children.Add(route); OverviewBody.Children.Add(OverviewSurface(summary, new(0, 0, 0, 16)));
        var columns = new Grid(); columns.ColumnDefinitions.Add(new()); columns.ColumnDefinitions.Add(new());
        columns.RowDefinitions.Add(new() { Height = GridLength.Auto }); columns.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var left = new StackPanel(); var right = new StackPanel();
        void Title(StackPanel panel, string label)
        {
            var text = OverviewText(label, size: 16); text.FontWeight = FontWeights.SemiBold; text.Margin = new(0, 0, 0, 20); panel.Children.Add(text);
        }
        void Detail(StackPanel panel, string label, string value, double size = 14)
        {
            panel.Children.Add(OverviewText(label, "Muted", 12));
            var text = OverviewText(value, size: size); text.Margin = new(0, 6, 0, 18); panel.Children.Add(text);
        }
        var config = new TomlDocument(snapshot.Config);
        Title(left, "模型与连接"); Detail(left, "默认模型 · 配置文件", config.String("model") is { Length: > 0 } model ? model : "沿用客户端默认值", 20);
        Detail(left, "模型服务商", loadedSettings.ProviderName + " · " + loadedSettings.ProviderId);
        if (loadedSettings.Details.Name.Length > 0) Detail(left, "连接名称", loadedSettings.Details.Name);
        if (loadedSettings.Details.Note.Length > 0) Detail(left, "备注", loadedSettings.Details.Note, 12);
        Detail(left, "模型服务", string.IsNullOrEmpty(loadedSettings.BaseUrl) ? "沿用现有客户端配置" : loadedSettings.BaseUrl);
        var leftActions = new WrapPanel(); leftActions.Children.Add(OverviewAction("管理连接", "connection")); leftActions.Children.Add(OverviewAction("调整模型", "preferences")); left.Children.Add(leftActions);
        Title(right, "保存与凭据");
        Detail(right, "连接方式", mixed ? "本机网关转发；关闭编辑器后继续运行" : "Codex 直接读取本机配置");
        Detail(right, "凭据来源", loadedSettings.CredentialSource);
        Detail(right, "保存与恢复", "保存前自动创建加密备份");
        if (config.String("profile") is { Length: > 0 } profile) Detail(right, "配置中的 profile", profile + " · 可能覆盖本地默认设置");
        right.Children.Add(OverviewAction("备份恢复", "backup"));
        var leftCard = OverviewSurface(left, new(0, 0, 8, 0)); var rightCard = OverviewSurface(right, new(8, 0, 0, 0));
        Grid.SetColumn(rightCard, 1); columns.Children.Add(leftCard); columns.Children.Add(rightCard);
        columns.SizeChanged += (_, _) =>
        {
            var compact = columns.ActualWidth < 660;
            Grid.SetColumn(rightCard, compact ? 0 : 1); Grid.SetRow(rightCard, compact ? 1 : 0);
            Grid.SetColumnSpan(leftCard, compact ? 2 : 1); Grid.SetColumnSpan(rightCard, compact ? 2 : 1);
            leftCard.Margin = compact ? new(0, 0, 0, 16) : new(0, 0, 8, 0); rightCard.Margin = compact ? new(0) : new(8, 0, 0, 0);
        };
        OverviewBody.Children.Add(columns);
        var footer = OverviewText("每 5 秒刷新本地状态。配置显示当前目录的文件值；会话、项目或 profile 覆盖由 Codex 决定。进程状态不代表正在执行任务。", "Muted", 12); footer.Margin = new(2, 16, 0, 0); OverviewBody.Children.Add(footer);
        if (Dirty)
        {
            var note = OverviewText("有未保存的修改，此处仍显示已保存配置。", "Accent", 12); note.Margin = new(2, 8, 0, 0); OverviewBody.Children.Add(note);
        }
    }

    private async Task RunLayoutReviewAsync(string output, List<string> checks)
    {
        var originalWidth = Width; var originalHeight = Height; var originalPath = CurrentDirectoryText.Text;
        var originalModel = ModelBox.Text;
        try
        {
            Width = 960; Height = 640;
            CurrentDirectoryText.Text = @"D:\isolated-layout-review\" + new string('a', 220) + @"\config";
            ModelBox.Text = originalModel + "-layout-review";
            foreach (var scale in new[] { 1.0, 1.25, 1.5 })
            {
                RootPanel.LayoutTransform = new ScaleTransform(scale, scale);
                foreach (var page in new[] { "overview", "connection", "preferences", "templates", "backup", "app" })
                {
                    ShowPage(page); await Settle();
                    if (CurrentDirectoryText.ActualHeight > 22) throw new InvalidOperationException("Long configuration path expanded the header.");
                    if (GatewaySummaryBar.ActualHeight > 34) throw new InvalidOperationException("Gateway status must fit one compact header row.");
                    var footer = NavigationFooter.TransformToAncestor(RootPanel).TransformBounds(new Rect(NavigationFooter.RenderSize));
                    if (footer.Bottom > RootPanel.ActualHeight + 1 || NavigationScroll.ActualHeight < 70)
                        throw new InvalidOperationException("Navigation footer overlaps the available navigation area.");
                    var save = SaveButton.TransformToAncestor(RootPanel).TransformBounds(new Rect(SaveButton.RenderSize));
                    if (save.Right > RootPanel.ActualWidth + 1 || save.Bottom > RootPanel.ActualHeight + 1)
                        throw new InvalidOperationException("Save action extends outside the compact window.");
                    if (page is "templates" or "backup")
                    {
                        var tabs = LibraryTabs.TransformToAncestor(RootPanel).TransformBounds(new Rect(LibraryTabs.RenderSize));
                        FrameworkElement content = page == "templates" ? TemplateEditor : BackupPage;
                        var bounds = content.TransformToAncestor(RootPanel).TransformBounds(new Rect(content.RenderSize));
                        if (LibraryTabs.ActualHeight > 55 || tabs.Bottom > bounds.Top + 1)
                            throw new InvalidOperationException("Library tabs stretch over page content.");
                    }
                    if (page == "preferences")
                    {
                        var scroll = FindVisual<ScrollViewer>(Editor).First();
                        if (scroll.ActualHeight < 100 || scroll.ScrollableHeight <= 0)
                            throw new InvalidOperationException("Compact model page must retain a usable page scroll area.");
                        Editor.Input("model_reasoning_effort").BringIntoView(); await Settle();
                    }
                    Capture(output, $"review-{page}-{(int)(scale * 100)}.png");
                }
            }
            checks.AddRange(["Long path stays on one header line", "Navigation remains scrollable above footer at minimum size", "Save actions remain within window at 100/125/150 percent", "Library tabs do not stretch over page content", "Settings retain a usable page scroll area", "Six pages captured at minimum size and three scales"]);
        }
        finally
        {
            RootPanel.LayoutTransform = Transform.Identity; Width = originalWidth; Height = originalHeight;
            CurrentDirectoryText.Text = originalPath; ModelBox.Text = originalModel; ShowPage("overview"); await Settle();
        }
    }
}
