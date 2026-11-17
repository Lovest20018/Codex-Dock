using System.IO;
using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;
using CodexConfig.Application;

namespace CodexConfig.App;

public partial class MainWindow
{
    private bool desktopRestarted;
    private bool desktopRestartBusy => desktopApplication.IsBusy;
    private ConfigSnapshot? desktopRestartSnapshot;
    private CodexDesktopTarget? lastAppliedDesktopTarget;
    private string? desktopApplicationHome;
    private string desktopApplicationStage = "等待应用", desktopApplicationMessage = "";
    private DesktopEnhancementRetry? pendingSpeedRetry => desktopApplication.PendingRetry;
    private DesktopExitRetry? blockedDesktopExit => desktopApplication.BlockedExit;
    private Func<CodexDesktopTarget>? smokeDesktopTarget;
    private Func<IReadOnlyList<CodexDesktopTarget>>? smokeDesktopTargets;
    private Func<IReadOnlyList<CodexDesktopTarget>, CodexDesktopTarget?>? smokeDesktopChoose;
    private Func<CodexDesktopTarget, bool>? smokeDesktopConfirm;
    private Func<CodexDesktopTarget, string, string?, Action, CancellationToken, Task>? smokeDesktopRestart;
    private Func<ConfigSnapshot, Task>? smokeDesktopGateway;

    private string DesktopApplicationAction => pendingSpeedRetry is { } retry && retry.Saved.Home == home ? retry.Plan.SpeedEnabled ? "重试速度兼容" : "重试旧聊天接续" :
        codexRuntime is { Available: true, ProcessCount: 0 } ? "打开 Codex 并应用" :
        codexRuntime is { Available: true, ProcessCount: > 0 } ? "重启 Codex 并应用" : "打开或重启 Codex";

    private void SetDesktopRestartStatus(string text)
    {
        desktopApplicationHome = home; desktopApplicationMessage = text;
        DesktopRestartStatus.Text = text;
        DesktopRestartStatus.Visibility = Visibility.Visible;
        if (SpeedCompatibilityBox.IsChecked == true) { speedFeedback = text; RefreshSpeedSettings(); }
        overviewState = null; RefreshOverview();
    }

    private void SetDesktopApplicationStage(string stage, string text, bool error = false)
    {
        desktopApplicationStage = stage; DesktopApplyStageText.Text = stage;
        DesktopRestartStatus.SetResourceReference(TextBlock.ForegroundProperty, error ? "Error" : "Muted");
        SetDesktopRestartStatus(text);
        if (!desktopApplication.IsBusy)
        {
            if (error) workspace.Session.Fail(text, WorkspaceOperation.ApplyingDesktop);
            else workspace.Session.WaitForUser(text);
        }
    }

    private void PrepareDesktopRestart(bool preserveRetry = false)
    {
        desktopRestarted = false; desktopRestartSnapshot = snapshot;
        desktopApplication.Reset(preserveRetry && pendingSpeedRetry?.Saved.Home == home);
        RestartCodexButton.Content = DesktopApplicationAction; RestartCodexButton.IsEnabled = true;
        desktopApplicationStage = "等待应用"; DesktopApplyStageText.Text = desktopApplicationStage;
        DesktopApplyProgress.Visibility = Visibility.Collapsed;
        DesktopApplyTargetDetails.IsExpanded = false;
        DesktopApplyContext.Text = "配置目录：" + home;
        DesktopRestartStatus.Text = ""; DesktopRestartStatus.Visibility = Visibility.Collapsed;
        ReloadDesktopConfigurationButton.Visibility = ForceDesktopRestartButton.Visibility = DesktopConfigurationChanges.Visibility = Visibility.Collapsed;
        DesktopConfigurationChangesText.Text = "";
    }

    private void OpenDesktopApplicationPanel()
    {
        reviewPlan = null; reviewTemplate = null; reviewSaved = true; reviewWriteFailed = false;
        ReviewTitle.Text = "应用 Codex 配置";
        ReviewSummary.Text = "使用已保存的配置 · 本次操作不写入配置";
        ReviewTargets.Text = "当前配置目录：\n" + home; ReviewTargets.Visibility = Visibility.Collapsed;
        ReviewSuccessMessage.Text = "Dock 会先核对配置与连接，再打开或重启 Codex。完成后在新聊天中核对。";
        SavedGatewaysButton.Visibility = Visibility.Collapsed;
        PrepareDesktopRestart(preserveRetry: true);
        ReviewSuccess.Visibility = Visibility.Visible;
        ReviewTabs.Visibility = ReviewScroll.Visibility = PreviewBox.Visibility = ReviewError.Visibility = ConfirmWriteButton.Visibility = Visibility.Collapsed;
        ClosePreviewButton.Content = "关闭"; ReviewHint.Text = "本地文件与聊天实际参数分别核对";
        OpenOverlay(PreviewOverlay, ClosePreviewButton);
    }

    private async void RestartCodexClicked(object sender, RoutedEventArgs e) => await RestartCodexFromReviewAsync();

    private async void ForceDesktopRestartClicked(object sender, RoutedEventArgs e)
    {
        if (blockedDesktopExit is not { } blocked || desktopRestartBusy || closing || Dirty) return;
        desktopRestartSnapshot = blocked.Saved;
        await RestartCodexFromReviewAsync(forceTarget: blocked.Target);
    }

    private async Task RestartCodexFromReviewAsync(bool fromSpeedSettings = false, CodexDesktopTarget? forceTarget = null, bool enhancedLaunch = false)
    {
        if (closing || desktopRestartBusy || autoEnhancementBusy) return;
        if (Dirty) { OpenConfigurationReview(); return; }
        if (fromSpeedSettings)
        {
            if (snapshot is null || !SpeedPreferences().Load() || !DesktopSpeedSettings.Eligible(store, snapshot)) return;
            OpenDesktopApplicationPanel();
        }
        if (!reviewSaved || desktopRestartSnapshot is null || desktopRestarted) return;
        DesktopApplyProgress.Visibility = Visibility.Visible;
        ReloadDesktopConfigurationButton.Visibility = ForceDesktopRestartButton.Visibility = Visibility.Collapsed;
        RestartCodexButton.IsEnabled = ClosePreviewButton.IsEnabled = ConfigurationSourcesButton.IsEnabled = false;
        SavedGatewaysButton.IsEnabled = ReloadDesktopConfigurationButton.IsEnabled = ForceDesktopRestartButton.IsEnabled = false;
        try
        {
            var result = await desktopApplication.ApplyAsync(new(desktopRestartSnapshot, forceTarget is not null, enhancedLaunch), new WpfDesktopApplyInteraction(this), lifetime.Token);
            desktopRestartSnapshot = result.Saved;
            ReloadDesktopConfigurationButton.Visibility = result.Outcome == DesktopApplyOutcome.Conflict ? Visibility.Visible : Visibility.Collapsed;
            ForceDesktopRestartButton.Visibility = result.Outcome == DesktopApplyOutcome.ExitBlocked && blockedDesktopExit is not null ? Visibility.Visible : Visibility.Collapsed;
            if (result.Outcome == DesktopApplyOutcome.Completed && result.Target is { } target)
                CompleteDesktopApplication(result.Saved, target, result.EnhancementsApplied);
        }
        finally
        {
            DesktopApplyProgress.Visibility = Visibility.Collapsed;
            RestartCodexButton.Content = desktopRestarted ? "Codex 已打开" : DesktopApplicationAction;
            RestartCodexButton.IsEnabled = !desktopRestarted && !closing;
            ClosePreviewButton.IsEnabled = ConfigurationSourcesButton.IsEnabled = SavedGatewaysButton.IsEnabled = !closing;
            ReloadDesktopConfigurationButton.IsEnabled = !closing && !Dirty;
            ForceDesktopRestartButton.IsEnabled = !closing && !Dirty;
            if (fromSpeedSettings || SpeedCompatibilityBox.IsChecked == true) speedFeedback = DesktopRestartStatus.Text;
            overviewState = null; RefreshOverview(); RefreshSpeedSettings();
        }
    }

    private sealed class WpfDesktopApplyInteraction(MainWindow window) : IDesktopApplyInteraction
    {
        public string ActiveHome => window.home;
        public void ValidateContext()
        {
            if (window.closing || window.Dirty) throw new InvalidOperationException("配置草稿或窗口状态已改变，请重新核对后应用。");
            if (window.busy || window.officialLoginBusy || window.gatewayPolling || window.gatewayLaunchInProgress)
                throw new InvalidOperationException("连接或网关正在处理操作，请等待完成后再应用。");
        }
        public Task<CodexDesktopTarget?> ChooseAsync(IReadOnlyList<CodexDesktopTarget> targets, CancellationToken cancellation)
            => Task.FromResult(window.smokeOutput is not null ? window.smokeDesktopChoose?.Invoke(targets) : window.ChooseDesktopTarget(targets));
        public Task<bool> ConfirmAsync(DesktopApplyConfirmation confirmation, CancellationToken cancellation)
            => Task.FromResult(window.smokeOutput is not null ? window.smokeDesktopConfirm?.Invoke(confirmation.Target) == true :
                MessageBox.Show(window, confirmation.Message, confirmation.Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
        public void Report(DesktopApplyProgress progress)
        { window.SetDesktopApplicationStage(progress.Stage, progress.Message, progress.IsError); window.RefreshSpeedSettings(); }
        public void TargetSelected(CodexDesktopTarget target, ConfigSnapshot saved)
            => window.DesktopApplyContext.Text = DesktopTargetDescription(target) + "\n配置目录：" + saved.Home;
        public void DesktopOpened(CodexDesktopTarget target, ConfigSnapshot saved) => window.RecordDesktopApplication(saved, target);
    }

    private sealed class WpfDesktopApplyRuntime(MainWindow window) : IDesktopApplyRuntime
    {
        public bool IsEnhancementRunning => window.speedSession is { IsRunning: true };
        public async Task<IReadOnlyList<CodexDesktopTarget>> FindTargetsAsync(CancellationToken cancellation)
            => window.smokeOutput is not null
                ? window.smokeDesktopTargets?.Invoke() ?? (window.smokeDesktopTarget is not null ? [window.smokeDesktopTarget()] : throw new InvalidOperationException("隔离检查不能操作真实 Codex 桌面端。"))
                : await Task.Run(() => new CodexDesktop().FindTargets(), cancellation);
        public async Task<DesktopEnhancementPreparation> PrepareEnhancementsAsync(CodexDesktopTarget target, ConfigSnapshot saved, CancellationToken cancellation)
        { var prepared = await window.PrepareSpeedCompatibilityAsync(target, saved); return new(prepared.Plan, prepared.Port); }
        public Task PrepareGatewayAsync(ConfigSnapshot saved, CancellationToken cancellation) => window.PrepareDesktopGatewayAsync(saved);
        public async Task<CodexDesktopTarget> RestartAsync(CodexDesktopTarget target, ConfigSnapshot saved, Action validateSaved, int? port,
            Action<CodexDesktopPhase> progress, bool forceConfirmed, CancellationToken cancellation)
        {
            if (window.smokeOutput is not null)
            {
                if (window.smokeDesktopRestart is null) throw new InvalidOperationException("隔离检查不能启动真实 Codex。");
                progress(CodexDesktopPhase.Closing);
                await window.smokeDesktopRestart(target, saved.Home, saved.AuthApiBaseUrl, validateSaved, cancellation); return target;
            }
            return await Task.Run(() => new CodexDesktop().RestartAsync(target, saved.Home, saved.AuthApiBaseUrl,
                () => window.Dispatcher.Invoke(validateSaved), cancellation, port,
                phase => window.Dispatcher.Invoke(() => progress(phase)), forceConfirmed), cancellation);
        }
        public Task StopEnhancementsAsync() => window.StopSpeedSessionAsync();
        public Task AttachEnhancementsAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port, CancellationToken cancellation)
            => window.AttachSpeedCompatibilityAsync(target, saved, plan, port, cancellation: cancellation);
    }

    private async Task PrepareDesktopGatewayAsync(ConfigSnapshot saved)
    {
        if (store.ReadSettings(saved).Mode != ConnectionMode.OfficialWithApi) return;
        if (smokeOutput is not null)
        {
            if (smokeDesktopGateway is null) throw new InvalidOperationException("隔离检查不能启动真实模型网关。");
            await smokeDesktopGateway(saved); return;
        }
        if (gatewayPolling) throw new InvalidOperationException("模型网关正在处理操作，请稍后重试。");
        var bytes = saved.Bridge ?? throw new InvalidOperationException("当前混合连接缺少已保存的模型网关设置。");
        var connection = MixedConnection.Read(bytes);
        HttpConnectionPolicy.RequireAuthorization(connection.BaseUrl, connection.HttpOrigin);
        gatewayPolling = true;
        try
        {
            gatewayStatus = await BridgeProcess.EnsureAsync(saved.Home, bytes, cancellation: lifetime.Token);
            stoppedGatewayHomes.Remove(saved.Home);
        }
        finally { gatewayPolling = false; }
    }

    private void RecordDesktopApplication(ConfigSnapshot saved, CodexDesktopTarget target)
    {
        lastAppliedSnapshot = saved; lastAppliedAt = DateTimeOffset.Now; lastAppliedDesktopTarget = target;
        if (smokeOutput is null) codexRuntime = new(true, target.Processes.Count) { DesktopProcesses = target.Processes };
        // No draft can be edited while the application panel owns the main shell.
        if (snapshot is not null && !snapshot.SameContents(saved)) Reload(saved);
        desktopRestartSnapshot = saved;
    }

    private void CompleteDesktopApplication(ConfigSnapshot saved, CodexDesktopTarget target, bool speed)
    {
        desktopRestarted = true; RecordDesktopApplication(saved, target);
        var recovery = ConnectionPreferences().Load();
        ReviewHint.Text = recovery ? "本地配置已保存 · Codex 已打开 · 旧聊天接续已接入" : "本地配置已保存 · Codex 已打开 · 请在新聊天核对";
    }

    private static bool SameDesktopRoot(CodexDesktopTarget expected, CodexDesktopTarget current)
        => DesktopApplicationService.SameRoot(expected, current);

    private static string DesktopTargetDescription(CodexDesktopTarget target) => DesktopApplicationService.DescribeTarget(target);

    private CodexDesktopTarget? ChooseDesktopTarget(IReadOnlyList<CodexDesktopTarget> targets)
    {
        var (dialog, choices, confirm, exitOthers) = CreateDesktopTargetPicker(targets);
        if (dialog.ShowDialog() != true || choices.SelectedIndex < 0) return null;
        var selected = targets[choices.SelectedIndex];
        return exitOthers.IsChecked == true ? selected.WithOtherInstancesToExit(targets.Where(target => !ReferenceEquals(target, selected)).ToArray()) : selected;
    }

    private (Window Dialog, ListBox Choices, Button Confirm, CheckBox ExitOthers) CreateDesktopTargetPicker(IReadOnlyList<CodexDesktopTarget> targets)
    {
        var dialog = new Window { Owner = this, Title = "选择本次 Codex 启动方式", Width = 720, Height = 540, MinWidth = 560, MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.SetResourceReference(BackgroundProperty, "Canvas");
        var body = new DockPanel { Margin = new(22) }; dialog.Content = body;
        var heading = new StackPanel { Margin = new(0, 0, 0, 14) }; DockPanel.SetDock(heading, Dock.Top); body.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "选择重开后使用的资料目录", FontSize = 20, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(HelpContent.Note("选择一个实例作为重开目标。需要合并为一个 Codex 时，可同时退出列表中的其他实例。", new(0, 8, 0, 0)));
        var exitOthers = new CheckBox { Margin = new(0, 0, 0, 10), Content = new TextBlock { Text = "同时退出其他 " + (targets.Count - 1) + " 个实例，全部退出后只打开一个 Codex", TextWrapping = TextWrapping.Wrap } };
        DockPanel.SetDock(exitOthers, Dock.Bottom);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 14, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        body.Children.Add(exitOthers);
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new(0, 0, 8, 0) }; footer.Children.Add(cancel);
        var confirm = new Button { Content = "继续核对", IsEnabled = false, IsDefault = true }; confirm.SetResourceReference(StyleProperty, "PrimaryButton"); footer.Children.Add(confirm);
        var choices = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch }; body.Children.Add(choices);
        ScrollViewer.SetHorizontalScrollBarVisibility(choices, ScrollBarVisibility.Disabled);
        choices.SetResourceReference(Control.BackgroundProperty, "Surface"); choices.SetResourceReference(Control.ForegroundProperty, "Ink");
        choices.SetResourceReference(Control.BorderBrushProperty, "Line");
        foreach (var target in targets)
            choices.Items.Add(new ListBoxItem { Style = (Style)FindResource("ConnectionModeOption"), Margin = new(0, 0, 0, 8),
                Content = new TextBlock { Text = DesktopTargetDescription(target), TextWrapping = TextWrapping.Wrap } });
        choices.SelectionChanged += (_, _) => confirm.IsEnabled = choices.SelectedIndex >= 0;
        confirm.Click += (_, _) => { if (choices.SelectedIndex >= 0) dialog.DialogResult = true; };
        dialog.SourceInitialized += (_, _) => ThemeManager.UpdateTitleBar(dialog);
        return (dialog, choices, confirm, exitOthers);
    }

    private void ReloadDesktopConfigurationClicked(object sender, RoutedEventArgs e)
    {
        if (desktopRestartBusy || closing || Dirty || desktopRestartSnapshot is null) return;
        try
        {
            var previous = desktopRestartSnapshot; var current = store.Load(home);
            _ = store.ReadSettings(current);
            var summary = DesktopApplicationService.DescribeChanges(previous, current);
            if (!Reload(current)) return;
            PrepareDesktopRestart();
            reviewSaved = true;
            ReviewTitle.Text = "核对最新配置后应用";
            ReviewSummary.Text = "已读取最新本地配置 · 本次未写入文件";
            ReviewTargets.Text = "当前配置目录：\n" + home; ReviewTargets.Visibility = Visibility.Collapsed;
            ReviewSuccessMessage.Text = "以下变更来自保存后的本地文件。核对后点击应用，将使用当前文件；之前的保存备份仍可恢复。";
            DesktopConfigurationChangesText.Text = summary.Length > 0 ? summary : "仅格式或同一账号的登录刷新，设置值未改变。";
            DesktopConfigurationChanges.Visibility = Visibility.Visible; DesktopConfigurationChanges.IsExpanded = true;
            SetDesktopApplicationStage("最新配置已读取", "请核对本次外部变更，再点击应用。");
        }
        catch (Exception error) { SetDesktopApplicationStage("无法读取最新配置", error.Message, error: true); }
    }
}
