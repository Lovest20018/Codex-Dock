using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private sealed record BehaviorChoice(string Label, string? Value) { public override string ToString() => Label; }
    private readonly Dictionary<string, ComboBox> behaviorChoices = new(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, string[]> behaviorDefaults = new Dictionary<string, string[]>
    {
        ["model_reasoning_effort"] = ["none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra"],
        ["service_tier"] = ["default", "flex", "priority", "ultrafast"],
        ["features.fast_mode"] = ["true", "false"], ["features.ultrafast_mode"] = ["true", "false"], ["features.context_management"] = ["true", "false"]
    };
    private void InitializeModelBehavior()
    {
        foreach (var path in new[] { "model_reasoning_effort", "model_reasoning_summary", "model_verbosity", "service_tier", "web_search", "features.fast_mode", "features.ultrafast_mode", "features.context_management" })
            AddBehaviorChoice(BehaviorFields, path);
        foreach (var path in new[] { "sandbox_mode", "approval_policy" }) AddBehaviorChoice(ApprovalFields, path);
    }
    private void AddBehaviorChoice(StackPanel root, string path)
    {
        var field = new ComboBox { Header = ConfigHelp.Name(path), HorizontalAlignment = HorizontalAlignment.Stretch, Tag = path };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(field, ConfigHelp.Name(path));
        field.SelectionChanged += (_, _) =>
        {
            if (!CanEditConnection || field.SelectedItem is not BehaviorChoice selected) return;
            try { connectionDraft.SetOverride(path, selected.Value); }
            catch (Exception error) { SetShellError("选项未修改", error.Message); }
        };
        root.Children.Add(field);
        var entry = ConfigSchema.Instance.Find(path);
        var hint = new TextBlock { Text = ConfigHelp.Find(path)?.Summary ?? (entry is not null ? ConfigHelp.Summary(entry) : ""), Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        root.Children.Add(hint); behaviorChoices[path] = field;
    }
    private void RefreshModelBehaviorControls()
    {
        if (BehaviorModelInput is null) return;
        var editable = connectionDraft.Available && !HasPendingRecovery && !ManualApplicationBusy && !dialogOpen && !closing;
        BehaviorModelInput.IsEnabled = editable;
        if (BehaviorModelInput.Text != connectionDraft.Model) BehaviorModelInput.Text = connectionDraft.Model;
        var document = connectionDraft.Document();
        var home = viewModel.Home;
        var capabilities = ModelCapabilities.Read(home, document, connectionDraft.Model);
        ModelCapabilityStatus.Text = capabilities is null ? "未读取此模型的原生能力目录。可手填模型；服务是否支持各档位需核对。" :
            "能力目录：" + capabilities.Model + (capabilities.ContextWindow is { } tokens ? " · 上下文 " + tokens.ToString("N0") + " tokens" : "") +
            "\n" + capabilities.SourcePath;
        foreach (var (path, field) in behaviorChoices)
        {
            var current = document.Get(path) is { } savedValue ? ConfigSchema.Literal(savedValue) : null;
            var values = path == "model_reasoning_effort" && capabilities?.ReasoningLevels is { Length: > 0 } levels ? levels :
                path == "service_tier" && capabilities?.ServiceTiers is { Length: > 0 } tiers ? tiers :
                behaviorDefaults.TryGetValue(path, out var fallback) ? fallback : ConfigSchema.Instance.Find(path)?.EnumValues ?? [];
            var options = new List<BehaviorChoice> { new("不设本地覆盖 · 跟随 Codex", null) };
            foreach (var value in values)
            {
                var literal = value is "true" or "false" ? value : TomlDocument.Quote(value);
                options.Add(new(value == "true" ? "开启" : value == "false" ? "关闭" : value, literal));
            }
            if (current is not null && !options.Any(option => option.Value == current)) options.Add(new("当前值（保留） · " +
                (document.Get(path) is Tomlyn.Model.TomlTable ? "结构化规则" : document.String(path) ?? current), current));
            if (field.ItemsSource is not IReadOnlyList<BehaviorChoice> existing || !existing.SequenceEqual(options)) field.ItemsSource = options;
            field.SelectedItem = ((IReadOnlyList<BehaviorChoice>)field.ItemsSource).FirstOrDefault(option => option.Value == current);
            field.IsEnabled = editable;
        }
        DedicatedReviewerInput.IsEnabled = editable;
        SynchronizeCatalogButton.IsEnabled = ImportCatalogButton.IsEnabled = ReadClientCatalogButton.IsEnabled = editable;
        try
        {
            var info = connectionDraft.Snapshot is { } snapshot ? ManagedModelCatalogStore.Inspect(snapshot) : null;
            var automatic = document.String("approvals_reviewer") == "auto_review";
            DedicatedReviewerInput.IsChecked = automatic && (info?.ReviewModel == "codex-auto-review" || connectionDraft.Edits.ContainsKey("approvals_reviewer"));
            ReviewerStatus.Text = DedicatedReviewerInput.IsChecked == true ? "固定审核模型：codex-auto-review，与主模型使用同一服务；审核不可用时停止执行。" :
                automatic ? "当前由原生客户端选择审核模型，尚未固定专用模型。" : "由你审批；细分审批规则和沙箱范围保持各自设置。";
            CatalogStatus.Text = (info?.ModelCount > 0 ? "模型 " + info.ModelCount + " 个 · " + (info.Managed ? "Dock 管理" : "本地能力目录") : "尚无可读取的原生能力目录") +
                (info?.HasUpdate == true ? " · 有更新待同步" : "") + (connectionDraft.SynchronizeCatalog ? " · 同步待保存" : "") +
                (connectionDraft.CatalogSource is { } source ? "\n本次来源：" + source : info is not null ? "\n来源：" + info.SourcePath : "");
            SynchronizeCatalogButton.IsEnabled = editable && (info?.Managed == true || DedicatedReviewerInput.IsChecked == true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        { CatalogStatus.Text = "能力目录读取失败：" + error.Message; }
    }
    private void BehaviorModelChanged(TextBox sender, TextBoxTextChangingEventArgs args)
    { if (CanEditConnection) connectionDraft.Model = sender.Text; }
    private void DedicatedReviewerChanged(object sender, RoutedEventArgs args)
    {
        if (!CanEditConnection) return;
        try { connectionDraft.SetDedicatedReviewer(DedicatedReviewerInput.IsChecked == true); }
        catch (Exception error) { SetShellError("审核选项未修改", error.Message); }
    }
    private void SynchronizeCatalogClicked(object sender, RoutedEventArgs args) => StageModelCatalog();
    private void ReadClientCatalogClicked(object sender, RoutedEventArgs args) => StageModelCatalog(Path.Combine(viewModel.Home, "models_cache.json"));
    private void StageModelCatalog(string? source = null)
    {
        if (!CanEditConnection) return;
        connectionDraft.StageCatalog(source);
        try { _ = configurationSave.PreparePlan(connectionDraft.Request()); }
        catch (Exception error) { SetShellError("能力目录尚不能保存", error.Message); }
    }
    private async void ImportCatalogClicked(object sender, RoutedEventArgs args)
    {
        if (!CanEditConnection) return;
        dialogOpen = true; RefreshView();
        string? source = null;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".json"); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            source = (await picker.PickSingleFileAsync())?.Path;
        }
        catch (Exception error) { SetShellError("未选择能力目录", error.Message); }
        finally { dialogOpen = false; RefreshView(); }
        if (source is not null) StageModelCatalog(source);
    }
}
