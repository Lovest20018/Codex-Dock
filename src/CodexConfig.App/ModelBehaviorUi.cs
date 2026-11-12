using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CodexConfig.Core;
using Microsoft.Win32;

namespace CodexConfig.App;

public partial class MainWindow
{
    private readonly StackPanel modelSection = new(), reviewSection = new();
    private readonly ComboBox behaviorModel = new() { IsEditable = true };
    private readonly CheckBox dedicatedReview = new() { Content = "固定使用 codex-auto-review 审核" };
    private readonly TextBlock catalogState = new() { TextWrapping = TextWrapping.Wrap }, reviewerState = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button syncCatalog = new() { Content = "同步模型目录", Margin = new(0, 0, 8, 8) };
    private readonly DispatcherTimer modelTyping = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool PendingModelText => modelTyping.IsEnabled && behaviorModel.Text != ModelBox.Text;
    private bool catalogDraft, reviewDraft, updatingBehavior;
    private string? catalogSourceDraft;
    private ConfigSnapshot? lastAppliedSnapshot;
    private DateTimeOffset? lastAppliedAt;

    private void InitializeModelBehavior()
    {
        // The same draft and the same editor own both common and advanced controls.
        modelSection.Children.Add(behaviorModel);
        behaviorModel.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "默认主模型");
        modelTyping.Tick += (_, _) => { modelTyping.Stop(); if (!updatingBehavior && !initializing && !closing) ModelBox.Text = behaviorModel.Text; };
        behaviorModel.LostKeyboardFocus += (_, _) => { if (!behaviorModel.IsKeyboardFocusWithin) { modelTyping.Stop(); if (!updatingBehavior && !initializing && !closing) ModelBox.Text = behaviorModel.Text; } };
        behaviorModel.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            if (updatingBehavior || initializing) return;
            modelTyping.Stop(); modelTyping.Start(); MarkDirty();
        }));
        behaviorModel.SelectionChanged += (_, _) =>
        {
            if (!updatingBehavior && !initializing && behaviorModel.SelectedItem is string id) ModelBox.Text = id;
        };
        catalogState.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); catalogState.Margin = new(0, 10, 0, 12);
        modelSection.Children.Add(catalogState);
        var actions = new WrapPanel(); modelSection.Children.Add(actions); actions.Children.Add(syncCatalog);
        var import = new Button { Content = "导入能力目录…", Margin = new(0, 0, 8, 8) }; actions.Children.Add(import);
        var clientCatalog = new Button { Content = "读取客户端目录", Margin = new(0, 0, 8, 8) }; actions.Children.Add(clientCatalog);
        clientCatalog.ToolTip = "使用当前配置目录中的 models_cache.json，并将专用审核策略重新应用后预览。";
        clientCatalog.Click += (_, _) => StageCatalog(Path.Combine(home, "models_cache.json"));
        var more = new Button { Content = "来源与高级配置", Margin = new(0, 0, 0, 8) }; actions.Children.Add(more);
        more.Click += (_, _) => { ShowPage("catalog"); configurationTools.IsExpanded = true; };
        syncCatalog.Click += (_, _) => StageCatalog();
        import.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "选择当前模型服务的原生能力目录", Filter = "模型目录 JSON|*.json", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) StageCatalog(dialog.FileName);
        };
        reviewSection.Margin = new(0, 12, 0, 0); reviewSection.Children.Add(dedicatedReview);
        reviewerState.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); reviewerState.Margin = new(0, 8, 0, 0); reviewSection.Children.Add(reviewerState);
        dedicatedReview.Checked += (_, _) =>
        {
            if (initializing || updatingBehavior) return;
            reviewDraft = true;
            var doc = DraftDocument();
            detailedChanges["approvals_reviewer"] = TomlDocument.Quote("auto_review");
            if (doc.Get("approval_policy") is null) detailedChanges["approval_policy"] = TomlDocument.Quote("on-request");
            Editor.Configure(snapshot?.Config, detailedChanges); MarkDirty();
        };
        dedicatedReview.Unchecked += (_, _) =>
        {
            if (initializing || updatingBehavior) return;
            // Turning off automatic approval returns approval requests to the user; no silent model fallback.
            reviewDraft = false; catalogDraft = false; catalogSourceDraft = null;
            detailedChanges["approvals_reviewer"] = TomlDocument.Quote("user");
            Editor.Configure(snapshot?.Config, detailedChanges); MarkDirty();
        };
        ApplicationBody.Children.Remove(SpeedCompatibilityCard);
        Editor.ModelSection = modelSection; Editor.SpeedSection = SpeedCompatibilityCard; Editor.ReviewSection = reviewSection;
        var speedLink = new Button { Content = "管理模型与速度兼容", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 0) };
        speedLink.Click += SpeedSettingsClicked; ApplicationBody.Children.Insert(0, speedLink);
    }
    private TomlDocument DraftDocument()
    {
        var doc = new TomlDocument(snapshot?.Config);
        foreach (var (key, value) in detailedChanges)
            try { if (value is null) doc.Remove(key); else doc.SetRaw(key, value); }
            catch (Exception error) when (error is InvalidDataException or ArgumentException) { } // Keep the last valid view while a field is being typed.
        return doc;
    }
    private void ReloadModelBehavior()
    {
        modelTyping.Stop(); updatingBehavior = true; behaviorModel.Text = ModelBox.Text; updatingBehavior = false;
        catalogDraft = reviewDraft = false; catalogSourceDraft = null;
        RefreshModelBehavior();
    }
    private void RefreshModelBehavior()
    {
        if (snapshot is null || updatingBehavior) return;
        updatingBehavior = true;
        try
        {
            if (!behaviorModel.IsKeyboardFocusWithin && behaviorModel.Text != ModelBox.Text) behaviorModel.Text = ModelBox.Text;
            if (!ReferenceEquals(behaviorModel.ItemsSource, ModelBox.ItemsSource)) behaviorModel.ItemsSource = ModelBox.ItemsSource;
            var doc = DraftDocument();
            var info = ManagedModelCatalogStore.Inspect(snapshot);
            var automatic = doc.String("approvals_reviewer") == "auto_review";
            dedicatedReview.IsChecked = automatic && (reviewDraft || info.ReviewModel == "codex-auto-review");
            catalogState.Text = info.ModelCount == 0 ? "尚无原生能力目录。可导入当前服务提供的目录。" :
                $"{info.ModelCount} 个模型 · " + (info.Managed ? "Dock 管理" : info.ReviewModel == "codex-auto-review" ? "检测到已有专用审核配置，可同步纳入管理" : "本地能力目录") +
                (info.HasUpdate ? " · 有更新待同步" : "") + (catalogDraft ? " · 同步待保存" : "");
            catalogState.ToolTip = info.SourcePath;
            reviewerState.Text = dedicatedReview.IsChecked == true ? "审核模型：codex-auto-review · 与主模型共用服务。审核不可用时停止执行。" :
                automatic ? "当前由原生客户端选择审核模型。勾选后固定使用专用模型。" : "由你审批。开启专用审核不会修改现有沙箱与细分审批规则。";
            syncCatalog.IsEnabled = !closing && !desktopRestartBusy && (info.Managed || dedicatedReview.IsChecked == true);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        { catalogState.Text = "模型目录暂不可用：" + error.Message; }
        finally { updatingBehavior = false; }
    }
    private void StageCatalog(string? source = null)
    {
        if (snapshot is null) return;
        try
        {
            catalogSourceDraft = source;
            catalogDraft = true;
            _ = CreateConfigurationPlan();
            Editor.Configure(snapshot.Config, detailedChanges); MarkDirty(); UpdatePreview();
            SetStatus("模型目录与专用审核已加入草稿，请查看并确认修改。");
        }
        catch (Exception error) { ShowError(error); MarkDirty(); }
    }
    private ConfigPlan CreateConfigurationPlan()
    {
        if (PendingModelText) { modelTyping.Stop(); ModelBox.Text = behaviorModel.Text; }
        var before = snapshot ?? throw new InvalidOperationException("请先读取配置。");
        var plan = store.Plan(before, Settings(), detailedChanges, updateConnection: ConnectionChanged,
            updateModel: ModelBox.Text != loadedSettings.Model, updateDetails: DetailsChanged, officialAuth: OfficialAuthForDraft);
        var doc = new TomlDocument(plan.Config);
        if (catalogDraft || reviewDraft && doc.String("approvals_reviewer") == "auto_review")
            return ManagedModelCatalogStore.Prepare(plan, new(), catalogSourceDraft);
        if (before.CatalogIndex is not null && ManagedModelCatalogStore.Inspect(before).Managed && doc.String("approvals_reviewer") == "auto_review")
        {
            if (ManagedModelCatalogStore.ConnectionId(doc, plan.Bridge) != ManagedModelCatalogStore.ConnectionId(new TomlDocument(before.Config), before.Bridge))
                throw new InvalidOperationException("模型服务已改变，请导入当前服务的能力目录后确认保存。");
            // Validate coverage against the currently saved version; source changes wait for an explicit sync.
            ManagedModelCatalogStore.ValidateCoverage(before, doc);
        }
        return plan;
    }
    private async void ApplySavedConfigurationClicked(object sender, RoutedEventArgs e)
    {
        if (closing || desktopRestartBusy) return;
        if (Dirty) { OpenConfigurationReview(); return; }
        if (snapshot is null) return;
        OpenDesktopApplicationPanel();
        await RestartCodexFromReviewAsync();
    }
    private void AddApplicationOverview()
    {
        var body = new StackPanel();
        var applicationState = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var title = OverviewText("应用状态", size: 16); title.FontWeight = FontWeights.SemiBold; body.Children.Add(title);
        var runtime = OverviewText(codexRuntime?.Summary ?? "正在核对 Codex 进程…", "Muted", 12); runtime.Margin = new(0, 8, 0, 0); body.Children.Add(runtime);
        var appliedToCurrent = lastAppliedDesktopTarget is { } applied &&
            codexRuntime is { Available: true } runtimeState && SameDesktopRoot(applied, new(applied.Executable, runtimeState.DesktopProcesses));
        var sameSettings = false;
        try { sameSettings = lastAppliedSnapshot is not null && snapshot is not null && CodexApplySnapshot.Compare(lastAppliedSnapshot, snapshot).CanContinue; } catch (IOException) { }
        applicationState.Text = desktopRestartBusy ? desktopApplicationStage : Dirty ? "有草稿待确认" :
            codexRuntime is { Available: true, ProcessCount: 0 } ? "本地文件已保存 · Codex 未运行" : sameSettings && appliedToCurrent ?
            "已通过 Dock 打开当前实例 · " + lastAppliedAt?.LocalDateTime.ToString("HH:mm") : "本地文件已保存 · 当前进程是否加载未确认";
        applicationState.Margin = new(0, 10, 0, 10); body.Children.Add(applicationState);
        var actions = new WrapPanel();
        var apply = new Button { Content = desktopRestartBusy ? "正在应用…" : Dirty ? "查看并应用修改" : DesktopApplicationAction,
            IsEnabled = !desktopRestartBusy && !closing, Margin = new(0, 0, 8, 0) };
        apply.SetResourceReference(StyleProperty, "PrimaryButton"); apply.Click += ApplySavedConfigurationClicked; actions.Children.Add(apply);
        var model = OverviewAction("模型与审核", "preferences"); actions.Children.Add(model); body.Children.Add(actions);
        var diagnostic = new Button { Content = "查看聊天参数", Margin = new(0, 0, 0, 0) }; diagnostic.Click += ShowSessionDiagnostics; actions.Children.Add(diagnostic);
        if (desktopApplicationHome == home && desktopApplicationMessage.Length > 0)
        {
            var recent = OverviewText("最近应用：" + desktopApplicationStage + "\n" + desktopApplicationMessage, "Muted", 12);
            recent.Margin = new(0, 12, 0, 0); body.Children.Add(recent);
        }
        if (externalConfigurationChanged || localStateError.Length > 0)
        {
            body.Children.Add(OverviewText(localStateError.Length > 0 ? localStateError : "外部配置已改变，当前草稿保留，请重新读取后核对。", "Warning", 12));
            var reload = new Button { Content = "重新读取配置", HorizontalAlignment = HorizontalAlignment.Left }; reload.Click += ReloadClicked; body.Children.Add(reload);
        }
        var note = OverviewText("进程已打开与当前聊天采用的参数分别核对。服务端接收状态需对照真实请求。", "Muted", 12); note.Margin = new(0, 10, 0, 0); body.Children.Add(note);
        OverviewBody.Children.Add(OverviewSurface(body, new(0, 0, 0, 16)));
    }
}
