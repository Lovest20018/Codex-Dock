using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private ExperimentalFeatureService experimentalFeatures = new();
    private ExperimentalFeatureRead? featureRead;
    private CancellationTokenSource? featureCancellation;
    private bool renderingFeatures;
    private bool readingFeatures => busy.IsActive(WindowOperation.ReadFeatures);
    private string featureMessage = "尚未读取此执行端的实验功能。", renderedFeatureKey = "";
    private int featurePage;
    private readonly Dictionary<string, ComboBox> featureDraftInputs = new(StringComparer.Ordinal);
    private sealed record FeatureChoice(string Label, bool? Enabled) { public override string ToString() => Label; }
    private static readonly FeatureChoice[] FeatureChoices = [new("移除主配置覆盖 · 跟随执行端", null), new("主配置开启", true), new("主配置关闭", false)];

    private void InvalidateFeatures()
    {
        featureCancellation?.Cancel(); featureRead = null; featurePage = 0; renderedFeatureKey = "";
        featureMessage = "读取条件或已保存配置已改变，请重新读取实验功能；现有草稿保留。";
        if (SourcesPanel.FeatureRows is not null) { SourcesPanel.FeatureRows.Children.Clear(); featureDraftInputs.Clear(); SourcesPanel.FeatureCountText.Text = ""; }
    }
    private void RefreshFeatureControls()
    {
        if (SourcesPanel.ReadFeaturesButton is null) return;
        var editable = !ManualApplicationBusy && !dialogOpen && !closing;
        SourcesPanel.ReadFeaturesButton.IsEnabled = editable;
        SourcesPanel.CancelFeaturesButton.Visibility = readingFeatures ? Visibility.Visible : Visibility.Collapsed; SourcesPanel.CancelFeaturesButton.IsEnabled = readingFeatures;
        SourcesPanel.FeatureDraftButton.Visibility = connectionDraft.HasDraft ? Visibility.Visible : Visibility.Collapsed; SourcesPanel.FeatureDraftButton.IsEnabled = editable;
        SourcesPanel.FeatureStatusText.Text = readingFeatures ? "正在读取所选独立执行端的功能列表…" : featureMessage;
        var key = (featureRead is null ? "none" : featureRead.Snapshot.ReadAt.ToString("O")) + "|" + connectionDraft.Revision + "|" + SourcesPanel.FeatureSearchInput.Text + "|" + featurePage + "|" + editable + "|" + HasPendingRecovery;
        if (key != renderedFeatureKey && !renderingFeatures) { RenderFeatureRows(editable); renderedFeatureKey = key; }
    }
    internal async void ReadFeaturesClicked(object sender, RoutedEventArgs args) => await ReadFeaturesAsync();
    internal void CancelFeaturesClicked(object sender, RoutedEventArgs args) => featureCancellation?.Cancel();
    private async Task ReadFeaturesAsync()
    {
        if (ManualApplicationBusy || dialogOpen || closing) return;
        RefreshAutomaticSourceExecutable();
        InvalidateFeatures(); using var operation = busy.Begin(WindowOperation.ReadFeatures, "正在读取实验功能…"); RefreshView();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); featureCancellation = cancellation;
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            featureRead = await experimentalFeatures.ReadAsync(SourceRequest(), SourceRequest, cancellation.Token);
            if (closing) return;
            var snapshot = featureRead.Snapshot;
            featureMessage = "独立执行端观察 · " + snapshot.ReadAt.LocalDateTime.ToString("g") + "\n执行端：" + featureRead.Request.Executable +
                "\n内核报告：" + (snapshot.RuntimeInfo.Length == 0 ? "未提供" : snapshot.RuntimeInfo) +
                (featureRead.Request.Profile is null ? "\n下方修改加入主配置草稿。" : "\n当前选择了 Profile，仅显示观察结果；未修改主配置。") +
                "\n不是当前桌面实例或现有聊天的生效证明。保存后需重新读取核对，并按需应用到 Codex。";
        }
        catch (OperationCanceledException) { if (!closing) featureMessage = "读取已取消、超时或条件已改变；实验功能状态未确认。"; }
        catch (NotSupportedException error) { if (!closing) featureMessage = error.Message; }
        catch (Exception) { if (!closing) featureMessage = "无法读取实验功能。若已更新 Codex，可自动查找或重新选择执行端。也可核对项目与 Profile。"; }
        finally { featureCancellation = null; operation.Dispose(); renderedFeatureKey = ""; if (!closing) RefreshView(); }
    }
    internal void FeatureSearchChanged(TextBox sender, TextBoxTextChangingEventArgs args)
    { if (initialized && !updatingControls) { featurePage = 0; RefreshView(); } }
    internal void PreviousFeatureClicked(object sender, RoutedEventArgs args) { featurePage--; RefreshView(); }
    internal void NextFeatureClicked(object sender, RoutedEventArgs args) { featurePage++; RefreshView(); }
    private void RenderFeatureRows(bool editable)
    {
        renderingFeatures = true;
        try
        {
            SourcesPanel.FeatureRows.Children.Clear(); featureDraftInputs.Clear();
            var search = SourcesPanel.FeatureSearchInput.Text.Trim();
            var rows = featureRead?.Snapshot.Features.Where(feature => (feature.Name + " " + feature.Title + " " + feature.Description + " " + feature.StageLabel).Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            var pages = Math.Max(1, (rows.Length + 19) / 20); featurePage = Math.Clamp(featurePage, 0, pages - 1);
            var saved = new TomlDocument(connectionDraft.Snapshot?.Config); var draft = connectionDraft.Document();
            foreach (var feature in rows.Skip(featurePage * 20).Take(20))
            {
                var body = new StackPanel { Spacing = 8 }; var path = feature.Path;
                body.Children.Add(new TextBlock { Text = feature.Title.Length == 0 ? path : feature.Title + " · " + path, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                body.Children.Add(new TextBlock { Text = feature.StageLabel + " · 独立执行端观察：" + FeatureFlag(feature.Enabled) + " · 执行端报告的默认：" + FeatureFlag(feature.DefaultEnabled), TextWrapping = TextWrapping.Wrap });
                var description = ConfigHelp.Find(path)?.Summary ?? feature.Description;
                if (description.Length > 0) body.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
                body.Children.Add(new TextBlock { Text = "主配置已保存：" + FeatureOverride(saved.Get(path)) + "\n当前主配置草稿：" + FeatureOverride(draft.Get(path)), TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
                var input = new ComboBox { ItemsSource = FeatureChoices, HorizontalAlignment = HorizontalAlignment.Stretch,
                    SelectedItem = FeatureChoices.Single(choice => choice.Enabled == (draft.Get(path) is bool value ? value : null)),
                    IsEnabled = editable && !HasPendingRecovery && connectionDraft.Available && featureRead!.Request.Profile is null && feature.CanStage && (draft.Get(path) is null or bool) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, path + " 主配置草稿");
                input.SelectionChanged += (_, _) =>
                {
                    if (renderingFeatures || updatingControls || !CanEditConnection || featureRead is null || input.SelectedItem is not FeatureChoice choice) return;
                    try { ExperimentalFeatureService.Stage(featureRead, feature.Name, choice.Enabled, connectionDraft, SourceRequest()); }
                    catch (Exception error) { featureMessage = error.Message; RefreshView(); }
                };
                featureDraftInputs[feature.Name] = input; body.Children.Add(input);
                if (draft.Get(path) is { } existing && existing is not bool)
                    body.Children.Add(new TextBlock { Text = "现有值不是布尔开关，请在配置草稿中检查；此处保留现有结构。", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
                if (feature.Stage is "underDevelopment" or "deprecated" or "removed" or "unknown")
                    body.Children.Add(new TextBlock { Text = feature.Stage == "underDevelopment" ? "开发中功能可能改变。加入草稿后仍需核对差异，保存不代表现有桌面已启用。" : "此阶段不提供快捷修改；仅保留观察结果。", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
                SourcesPanel.FeatureRows.Children.Add(new Border { Child = body, Padding = new(16), CornerRadius = new(8), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(12, 128, 128, 128)) });
            }
            SourcesPanel.FeatureCountText.Text = featureRead is null ? "" : rows.Length + " 项 · 第 " + (featurePage + 1) + " / " + pages + " 页";
            SourcesPanel.PreviousFeatureButton.IsEnabled = editable && featureRead is not null && featurePage > 0;
            SourcesPanel.NextFeatureButton.IsEnabled = editable && featureRead is not null && featurePage + 1 < pages;
        }
        finally { renderingFeatures = false; }
    }
    private static string FeatureFlag(bool? value) => value is null ? "未提供" : value.Value ? "开启" : "关闭";
    private static string FeatureOverride(object? value) => value is bool enabled ? FeatureFlag(enabled) : value is null ? "未设置覆盖" : "现有结构保留，未在此编辑";
}
