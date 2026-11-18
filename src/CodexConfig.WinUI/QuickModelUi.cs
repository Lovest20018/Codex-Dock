using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private CancellationTokenSource? quickModelRead;
    private bool quickModelAwaitingApply;

    private sealed record ReviewedDesktopChoice(string Home, CodexDesktopTarget Target, int TargetCount,
        bool NeedsGateway, bool Speed, bool Connections)
    {
        public bool Matches(CodexDesktopTarget current) =>
            Target.IsRunning == current.IsRunning && current.AlsoExit.Count == 0 &&
            string.Equals(Target.Executable, current.Executable, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Target.UserDataDirectory, current.UserDataDirectory, StringComparison.OrdinalIgnoreCase) &&
            (!current.IsRunning || DesktopApplicationService.SameRoot(Target, current));

        public void Validate(DesktopApplyConfirmation actual, ClientEnhancementPreferences preferences)
        {
            if (actual.ForceExit || !Matches(actual.Target) || actual.AvailableTargets != TargetCount ||
                !string.Equals(actual.Saved.Home, Home, StringComparison.OrdinalIgnoreCase) ||
                actual.NeedsGateway != NeedsGateway || preferences.SpeedCompatibilityEnabled != Speed ||
                preferences.ConnectionRecoveryEnabled != Connections)
                throw new IOException("目标实例或连接选项已改变。设置已保存，请从首页重新核对并应用。");
        }
    }

    private static string ServiceOrigin(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
        ? uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.SafeUnescaped) : "当前服务";

    private void RefreshQuickModelEntry(bool editable)
    {
        var available = connectionDraft.Available && !HasPendingRecovery && !viewModel.NeedsConnectionSetup;
        QuickModelButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        QuickModelButton.IsEnabled = available && editable && quickModelRead is null;
        QuickModelButton.Content = connectionDraft.HasDraft && connectionDraft.Model.Length > 0 && connectionDraft.Model != viewModel.ModelLabel
            ? "继续更换模型" : "更换模型";
        CancelQuickModelReadButton.Visibility = quickModelRead is null ? Visibility.Collapsed : Visibility.Visible;
        QuickModelApplyDetailsButton.Visibility = quickModelAwaitingApply && lastApplication?.Outcome is DesktopApplyOutcome.ExitBlocked or DesktopApplyOutcome.Failed
            ? Visibility.Visible : Visibility.Collapsed;
        QuickModelHint.Text = quickModelRead is not null ? "正在读取可用模型和本机 Codex 状态…" :
            savingConfiguration && currentPage == WorkspaceNav ? "正在备份并保存…" :
            applying ? ApplicationStage.Text :
            (quickModelAwaitingApply || configurationResult?.WasWritten == true) && lastApplication is { } result ? result.Progress.Message :
            quickModelAwaitingApply ? "正在使用 Codex 时，可以等当前任务结束再应用。" :
            connectionDraft.ConnectionChanged ? "先核对本次连接修改，再读取可用模型。" : "";
        QuickModelHint.Visibility = available && QuickModelHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void QuickModelClicked(object sender, RoutedEventArgs args) => await OpenQuickModelAsync();
    private void CancelQuickModelReadClicked(object sender, RoutedEventArgs args) => quickModelRead?.Cancel();

    private async Task OpenQuickModelAsync()
    {
        if (ApplicationBusy || dialogOpen || closing || HasPendingRecovery || currentPage != WorkspaceNav ||
            viewModel.NeedsConnectionSetup || connectionDraft.Snapshot is not { } snapshot) return;
        if (connectionDraft.ConnectionChanged) { NavigateTo(ConnectionNav, revealStart: true); return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        quickModelRead = cancellation;
        shellError = ""; RefreshView();
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            var revision = connectionDraft.Revision;
            var source = connectionDraft.Settings();
            var modelRead = ReadQuickModelListAsync(snapshot, source, cancellation.Token);
            var targetRead = FindDesktopTargetsAsync(cancellation.Token);
            await Task.WhenAll(modelRead, targetRead);
            cancellation.Token.ThrowIfCancellationRequested();
            if (closing || currentPage != WorkspaceNav || revision != connectionDraft.Revision) return;

            var (models, service, status) = await modelRead;
            var (targets, targetError) = await targetRead;
            quickModelRead = null;
            var savedModel = store.ReadSettings(snapshot).Model;
            var picker = new QuickModelDialog(models, status, savedModel, connectionDraft.Model, targets, targetError,
                source.Mode == ConnectionMode.OfficialWithApi, viewModel.SpeedCompatibilityEnabled, viewModel.ConnectionRecoveryEnabled,
                viewModel.PreferencesAvailable, model => configurationSave.PrepareReview(connectionDraft.RequestForModel(model, service, source)))
                { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, PrimaryButtonStyle = WorkspaceNextButton.Style };
            var result = await ShowDialogAsync(picker);
            if (closing || revision != connectionDraft.Revision)
            {
                if (!closing) SetShellError("请重新核对模型", "配置草稿已改变，请重新打开“更换模型”。");
                return;
            }
            if (picker.SelectedModel.Length > 0) connectionDraft.Model = picker.SelectedModel;
            if (picker.AdvancedRequested) { NavigateTo(ModelsNav, revealStart: true); return; }
            if (result == ContentDialogResult.None || picker.Review is not { HasChanges: true } review) return;
            var choice = result == ContentDialogResult.Primary && picker.SelectedTarget is { } target
                ? new ReviewedDesktopChoice(snapshot.Home, target, targets.Count, picker.NeedsGateway, picker.Speed, picker.Connections) : null;
            if (result == ContentDialogResult.Primary && choice is null) return;
            if (await SaveConnectionAsync((review, connectionDraft.Revision), modelSave: true) && choice is not null)
                await ApplyDesktopAsync(modelChoice: choice);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { if (!closing) SetShellError("换模型未完成", error.Message); }
        finally { quickModelRead = null; if (!closing) RefreshView(); }
    }

    private async Task<(IReadOnlyList<string> Models, ModelCatalogResult? Service, string Status)> ReadQuickModelListAsync(
        ConfigSnapshot snapshot, ConnectionSettings source, CancellationToken cancellation)
    {
        ModelCatalogResult? service = null;
        var status = "";
        if (source.ApiKey.Length > 0)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                service = await modelDiscovery.ReadAsync(snapshot.Home, source, () => (viewModel.Home, connectionDraft.Settings()), timeout.Token);
                var visible = service.Models.Where(model => model != "codex-auto-review").ToArray();
                if (visible.Length > 0) return (visible, service, "可用模型来自 " + ServiceOrigin(source.BaseUrl) + "。");
                status = "当前服务没有返回可选模型。";
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellation.IsCancellationRequested)
            { status = "读取模型超时。"; }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or HttpRequestException or InvalidOperationException)
            { status = error.Message; }
        }
        cancellation.ThrowIfCancellationRequested();
        try
        {
            var catalog = snapshot.Catalog?.Content;
            if (snapshot.Catalog is null && source.ApiKey.Length == 0 && source.ProviderId == "openai")
                catalog = ManagedModelCatalogStore.Read(Path.Combine(snapshot.Home, "models_cache.json"));
            if (catalog is not null)
            {
                var models = ManagedModelCatalogStore.ModelNames(catalog);
                if (models.Count > 0) return (models, service, (status + " 可选用本机已保存的模型列表。").Trim());
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { status = (status + " 本机模型资料暂不可读。").Trim(); }
        return ([], service, status);
    }

    private async Task<(IReadOnlyList<CodexDesktopTarget> Targets, string Error)> FindDesktopTargetsAsync(CancellationToken cancellation)
    {
        try { return (await applicationRuntime.FindTargetsAsync(cancellation), ""); }
        catch (Exception error) when (error is not OperationCanceledException) { return ([], error.Message); }
    }

    private sealed class QuickModelDialog : ContentDialog
    {
        private readonly string savedModel;
        private readonly IReadOnlyList<CodexDesktopTarget> targets;
        internal readonly StackPanel ModelChoices = new();
        internal readonly ComboBox TargetList = new() { Header = "选择要重启的 Codex", HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBlock summary = Wrapped("");
        private readonly TextBlock impact = Wrapped("");
        private readonly TextBlock detailText = Wrapped("");
        private readonly Expander details = new() { Header = "查看本次修改明细", HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly HyperlinkButton keepOriginal = new() { Content = "保留原模型", Visibility = Visibility.Collapsed };
        private readonly Func<string, ConfigurationSaveReview> preview;
        private readonly bool canApply;
        public bool NeedsGateway { get; }
        public bool Speed { get; }
        public bool Connections { get; }
        public bool AdvancedRequested { get; private set; }
        public string SelectedModel { get; private set; }
        public CodexDesktopTarget? SelectedTarget => targets.Count == 1 ? targets[0] :
            TargetList.SelectedIndex >= 0 ? targets[TargetList.SelectedIndex] : null;
        public ConfigurationSaveReview? Review { get; private set; }

        private static TextBlock Wrapped(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
        public QuickModelDialog(IReadOnlyList<string> choices, string status, string savedModel, string draftModel,
            IReadOnlyList<CodexDesktopTarget> targets, string targetError, bool needsGateway, bool speed, bool connections,
            bool canApply, Func<string, ConfigurationSaveReview> preview)
        {
            this.savedModel = savedModel; SelectedModel = draftModel; this.targets = targets;
            this.canApply = canApply; this.preview = preview;
            NeedsGateway = needsGateway; Speed = speed; Connections = connections;
            Title = "更换默认模型"; SecondaryButtonText = "仅保存"; CloseButtonText = "返回首页";
            DefaultButton = ContentDialogButton.None;
            var body = new StackPanel { Spacing = 10 };
            var furtherActions = new ActionWrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(Wrapped("已保存的默认模型　" + (savedModel.Length > 0 ? savedModel : "跟随 Codex")));
            if (status.Length > 0) body.Children.Add(Wrapped(status));
            if (choices.Count > 0)
            {
                var list = choices.Prepend(savedModel).Prepend(draftModel).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
                var manual = new TextBox { Header = "模型名称", MaxLength = 512, Text = SelectedModel, Visibility = Visibility.Collapsed };
                var filterHint = Wrapped("");
                void ShowChoices(string filter)
                {
                    const int visibleLimit = 20;
                    var matches = list.Where(value => value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)).Take(visibleLimit + 1).ToArray();
                    ModelChoices.Children.Clear();
                    foreach (var choice in matches.Take(visibleLimit))
                    {
                        var button = new RadioButton
                        {
                            Content = Wrapped(choice), Tag = choice, GroupName = "QuickModelChoices",
                            IsChecked = SelectedModel == choice, HorizontalAlignment = HorizontalAlignment.Stretch
                        };
                        button.Checked += (_, _) => { SelectModel(choice); manual.Text = choice; };
                        ModelChoices.Children.Add(button);
                    }
                    filterHint.Text = matches.Length == 0 ? "没有匹配的模型。" :
                        matches.Length > visibleLimit ? "输入名称可查找其余模型。" : "";
                    filterHint.Visibility = filterHint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                }
                AutomationProperties.SetName(ModelChoices, "新聊天使用的模型");
                if (list.Length > 8)
                {
                    var search = new TextBox { PlaceholderText = "筛选模型名称" };
                    AutomationProperties.SetName(search, search.PlaceholderText);
                    search.TextChanged += (_, _) => ShowChoices(search.Text);
                    body.Children.Add(search);
                }
                ShowChoices("");
                body.Children.Add(new ScrollViewer { MaxHeight = 144, Content = ModelChoices });
                body.Children.Add(filterHint);
                manual.TextChanging += (sender, _) =>
                {
                    if (sender.Text.Trim() == SelectedModel) return;
                    foreach (var button in ModelChoices.Children.OfType<RadioButton>()) button.IsChecked = false;
                    SelectModel(sender.Text.Trim());
                };
                var enter = new HyperlinkButton { Content = "手动填写模型", Padding = new(0) };
                enter.Click += (_, _) =>
                {
                    manual.Visibility = Visibility.Visible;
                    enter.Visibility = Visibility.Collapsed;
                    manual.Focus(FocusState.Keyboard);
                    manual.SelectAll();
                };
                body.Children.Add(manual);
                furtherActions.Children.Add(enter);
                keepOriginal.Click += (_, _) =>
                {
                    SelectModel(savedModel);
                    manual.Text = savedModel;
                    foreach (var button in ModelChoices.Children.OfType<RadioButton>()) button.IsChecked = Equals(button.Tag, savedModel);
                };
            }
            else
            {
                body.Children.Add(Wrapped("未读到可用列表。知道模型名称时可以直接填写，也可在 Codex 中选择。"));
                var input = new TextBox { Header = "模型名称", Text = SelectedModel, MaxLength = 512 };
                input.TextChanged += (_, _) => SelectModel(input.Text.Trim());
                keepOriginal.Click += (_, _) => input.Text = savedModel;
                body.Children.Add(input);
            }
            if (targets.Count > 1)
            {
                TargetList.ItemsSource = targets.Select(target => TargetName(target) + (target.MainProcess is { } process ? $" · 窗口 {process.Id}" : "")).ToArray();
                TargetList.SelectionChanged += (_, _) => UpdateActions();
                body.Children.Add(TargetList);
            }
            if (targetError.Length > 0) body.Children.Add(Wrapped(targetError + " 可先仅保存。"));
            AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
            body.Children.Add(summary); body.Children.Add(keepOriginal); body.Children.Add(impact);
            detailText.IsTextSelectionEnabled = true; details.Content = detailText; body.Children.Add(details);
            var advanced = new HyperlinkButton { Content = "模型与高级设置", Padding = new(0) };
            advanced.Click += (_, _) => { AdvancedRequested = true; Hide(); };
            furtherActions.Children.Add(advanced);
            body.Children.Add(furtherActions);
            Content = new ScrollViewer { MaxHeight = 440, Content = body };
            UpdateReview();
        }

        private static string TargetName(CodexDesktopTarget target) => target.UserDataDirectory is { Length: > 0 } directory
            ? "Codex · " + Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)) : "Codex";

        private void SelectModel(string model)
        {
            if (SelectedModel == model) return;
            SelectedModel = model;
            UpdateReview();
        }

        private void UpdateReview()
        {
            Review = null;
            keepOriginal.Visibility = Visibility.Collapsed;
            try
            {
                summary.Text = "请选择另一个模型。";
                if (SelectedModel.Length > 0 && SelectedModel != savedModel && preview(SelectedModel) is { HasChanges: true } review)
                {
                    Review = review;
                    var reviewerChanges = review.Changes.Any(change => change.Path == "Dock 管理的能力目录");
                    var other = review.Changes.Where(change => change.Path != "model" &&
                        !(reviewerChanges && (change.Path is "model_catalog_json" or "Dock 管理的能力目录"))).ToList();
                    summary.Text = "默认模型将改为 " + SelectedModel + "。保存前会自动备份。";
                    if (reviewerChanges) summary.Text += " 同时更新专用审核资料。";
                    if (other.Count > 0) summary.Text += "\n一起保存 " + string.Join("、", other.Select(change => change.Name)) + " 的修改。";
                    details.IsExpanded = other.Count > 0;
                    detailText.Text = review.Description + "\n\n" + string.Join("\n", review.Targets.Concat(review.Notes));
                }
            }
            catch (ModelCatalogCoverageException)
            {
                summary.Text = "此模型缺少专用审核所需的资料。可以选择其他模型，或在高级设置中导入资料。";
                keepOriginal.Visibility = savedModel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception error) { summary.Text = error.Message + "\n可选择其他模型，或打开高级设置核对。"; }
            details.Visibility = Review is null ? Visibility.Collapsed : Visibility.Visible;
            FrameworkElementAutomationPeer.FromElement(summary)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            UpdateActions();
        }

        private void UpdateActions()
        {
            var target = SelectedTarget;
            PrimaryButtonText = target?.IsRunning == true ? "保存并重启 Codex" : target is null ? "保存并应用" : "保存并打开 Codex";
            IsPrimaryButtonEnabled = target is not null && canApply && Review is not null;
            IsSecondaryButtonEnabled = Review is not null;
            impact.Text = target is null ? targets.Count > 1 ? "请先选择目标实例，或仅保存设置。" : "可先保存，稍后再打开 Codex。" :
                TargetName(target) + "\n" +
                (target.IsRunning ? "重启可能中断正在进行的任务。也可仅保存，稍后从首页继续。" : "保存后将打开 Codex。") +
                (NeedsGateway ? " 应用时会准备模型网关。" : "") +
                (Speed ? " 已选速度兼容将在支持时加载。" : "") + (Connections ? " 将加载旧聊天接续。" : "");
        }
    }
}
