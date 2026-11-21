using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private bool dialogOpen, closing, closePrompt, allowClose;
    private bool applying => busy.IsActive(WindowOperation.ApplyDesktop);
    private DesktopApplyResult? lastApplication;
    private string applicationStage = "准备应用";
#if CODEX_DOCK_WINUI_SMOKE
    private Func<ContentDialog, Task<ContentDialogResult>>? smokeDialog;
#endif
    private readonly DispatcherTimer runtimeTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly OperationTracker busy = new();
    private bool ManualApplicationBusy => busy.IsBusy;
    private bool ApplicationBusy => ManualApplicationBusy || automaticEnhancements.IsBusy;

    private async void ApplyClicked(object sender, RoutedEventArgs e) => await ApplyDesktopAsync();
    private async void ForceExitClicked(object sender, RoutedEventArgs e)
    {
        ApplicationDetailsButton.Flyout.Hide();
        await ApplyDesktopAsync(forceExit: true);
    }

    private async Task ApplyDesktopAsync(bool forceExit = false, bool enhancedLaunch = false, ReviewedDesktopChoice? modelChoice = null)
    {
        if (ApplicationBusy || dialogOpen || closing || HasPendingRecovery || connectionDraft.HasDraft || !viewModel.PreferencesAvailable || !viewModel.CanApplyConfiguration || viewModel.Snapshot is not { } saved) return;
        using var operation = busy.Begin(WindowOperation.ApplyDesktop, "正在应用到 Codex…");
        ApplicationTarget.Visibility = Visibility.Collapsed;
        SetApplicationProgress(new("准备应用", "正在核对配置和本机 Codex 实例…"));
        RefreshView();
        try
        {
            lastApplication = await desktopApplication.ApplyAsync(new(saved, ForceExit: forceExit, EnhancedLaunch: enhancedLaunch), new WinUiDesktopApplyInteraction(this, modelChoice), lifetime.Token);
            if (lastApplication.DesktopOpened) quickModelAwaitingApply = false;
            viewModel.ApplicationFinished(lastApplication);
            if (desktopEnhancements.AutomaticStatus is { } status) automaticStatus = status;
        }
        finally { operation.Dispose(); RefreshView(); }
    }

    private async Task ReloadWorkspaceAsync()
    {
        if (ApplicationBusy || dialogOpen || closing) return;
        try
        {
            if (!await ResolveProfileDraftAsync() || !await ResolveTemplateEditAsync() || !await ResolveConnectionDraftAsync() || !await ResolveMobileDraftAsync()) return;
            var current = store.Load(viewModel.Home);
            if ((lastApplication?.Outcome == DesktopApplyOutcome.Conflict || configurationResult?.Outcome == ConfigurationSaveOutcome.Conflict) && viewModel.Snapshot is { } previous)
            {
                var changes = DesktopApplicationService.DescribeChanges(previous, current);
                if (!await ConfirmDialogAsync("核对外部变更", changes.Length > 0 ? changes : "仅格式或相同账号的登录刷新，设置值未改变。", "读取最新配置")) return;
            }
            desktopApplication.Reset(); lastApplication = null;
            configurationResult = null;
            shellError = "";
            LoadWorkspace(current); RefreshView();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or InvalidOperationException)
        { desktopApplication.Reset(); lastApplication = null; configurationResult = null; shellError = ""; LoadWorkspace(viewModel.Home); RefreshView(); }
    }

    private void RefreshApplicationActions()
    {
        if (ApplyButton is null) return;
        ChooseHomeButton.IsEnabled = WorkspacePanel.ReloadButton.IsEnabled = !ApplicationBusy && !dialogOpen && !closing;
        ApplyButton.IsEnabled = viewModel.PreferencesAvailable && viewModel.CanApplyConfiguration && !HasPendingRecovery && !connectionDraft.HasDraft && !ApplicationBusy && !dialogOpen && !closing;
        ApplyButton.Content = automaticEnhancements.IsBusy ? "正在自动接回…" : applying ? "正在应用…" : desktopApplication.PendingRetry is not null ? "重试客户端增强" : "打开或重启 Codex 并应用";
        ForceExitButton.Visibility = !ApplicationBusy && lastApplication?.Outcome == DesktopApplyOutcome.ExitBlocked && desktopApplication.BlockedExit is not null
            ? Visibility.Visible : Visibility.Collapsed;
        ApplicationDetailsButton.Content = ForceExitButton.Visibility == Visibility.Visible ? "详情与重试" : "详情";
        ForceExitButton.IsEnabled = !ApplicationBusy && !connectionDraft.HasDraft && !dialogOpen && !closing && viewModel.PreferencesAvailable && viewModel.CanApplyConfiguration;
        if (lastApplication is null && !ApplicationBusy)
        {
            var stage = connectionDraft.HasDraft ? "有未保存的配置修改" : !viewModel.CanApplyConfiguration ? viewModel.SetupGuide.Title :
                configurationResult?.Outcome == ConfigurationSaveOutcome.Saved ? "配置已保存，等待应用" : "准备应用";
            var message = connectionDraft.HasDraft ? "请先查看并确认修改，再应用到 Codex。" :
                !viewModel.CanApplyConfiguration ? "完成连接设置或修复后重新读取，再核对实例并应用。" :
                configurationResult?.Succeeded == true ? configurationResult.Message : "先核对当前目录，应用时会再次确认目标实例。";
            SetApplicationProgress(new(stage, message));
            ApplicationTarget.Visibility = Visibility.Collapsed;
        }
        ApplicationStage.Text = CurrentOperationName ?? applicationStage;
        ApplicationError.Visibility = ApplicationError.IsOpen && (currentPage == WorkspaceNav || currentPage == EnhancementsNav || currentPage.EditsConfiguration)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetApplicationProgress(DesktopApplyProgress progress)
    {
        applicationStage = progress.Stage;
        ApplicationStage.Text = CurrentOperationName ?? applicationStage;
        ApplicationStatus.Text = progress.Message;
        ApplicationError.Title = progress.Stage;
        ApplicationError.Message = progress.Message;
        ApplicationError.IsOpen = progress.IsError;
    }

    private string? CurrentOperationName => applying ? applicationStage :
        busy.CurrentName ?? (automaticEnhancements.IsBusy ? "正在自动接回…" : null);

    private void StartRuntimeObservation()
    {
        if (options.IsolatedDataDirectory is not null) return;
        runtimeTimer.Tick += async (_, _) =>
        {
            if (ApplicationBusy || dialogOpen || closing) return;
            if (!ApplicationError.IsOpen && desktopEnhancements.Session is { } session && (!session.IsRunning || !session.IsApplied))
            {
                SetApplicationProgress(new("客户端增强需要重新核对", session.Status));
            }
            if (currentPage == WorkspaceNav) await ObserveDesktopTargetsAsync();
            await TryAutomaticEnhancementsAsync();
            if (++gatewayTicks % 5 == 0) await RefreshGatewaysAsync();
            if (IsPhonePage) await MobilePairTickAsync();
        };
        runtimeTimer.Start();
    }

    private async Task<CodexDesktopTarget?> ChooseDesktopAsync(IReadOnlyList<CodexDesktopTarget> targets, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var (dialog, choices, exitOthers) = CreateTargetPicker(targets);
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary || choices.SelectedIndex < 0) return null;
        var target = targets[choices.SelectedIndex];
        var others = targets.Where(candidate => !ReferenceEquals(candidate, target)).ToArray();
        if (exitOthers.IsChecked != true) return target;
#if CODEX_DOCK_WINUI_SMOKE
        if (options.SmokeOutput is not null) return target with { AlsoExit = others };
#endif
        return target.WithOtherInstancesToExit(others);
    }

    private (ContentDialog Dialog, ListBox Choices, CheckBox ExitOthers) CreateTargetPicker(IReadOnlyList<CodexDesktopTarget> targets)
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "选择重开后使用的资料目录。需要合并为一个实例时，可勾选同时退出其他实例。", TextWrapping = TextWrapping.Wrap });
        var choices = new ListBox { SelectionMode = SelectionMode.Single, MaxHeight = 320, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        foreach (var target in targets)
            choices.Items.Add(new ListBoxItem { Content = new TextBlock { Text = DesktopApplicationService.DescribeTarget(target), TextWrapping = TextWrapping.Wrap, Margin = new(8) } });
        body.Children.Add(choices);
        var exitOthers = new CheckBox { Content = new TextBlock { Text = "同时退出其他 " + (targets.Count - 1) + " 个实例，全部退出后仅打开一个 Codex", TextWrapping = TextWrapping.Wrap } };
        body.Children.Add(exitOthers);
        var dialog = new ContentDialog { Title = "选择本次 Codex 实例", Content = body, PrimaryButtonText = "继续核对", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false, XamlRoot = Root.XamlRoot };
        choices.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = choices.SelectedIndex >= 0;
        return (dialog, choices, exitOthers);
    }

    private async Task<bool> ConfirmDialogAsync(string title, string message, string primary)
    {
        var dialog = new ContentDialog { Title = title, PrimaryButtonText = primary, CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
            XamlRoot = Root.XamlRoot, Content = new ScrollViewer { MaxHeight = 400, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } } };
        return await ShowDialogAsync(dialog) == ContentDialogResult.Primary;
    }

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (dialogOpen) throw new InvalidOperationException("请先完成当前确认。");
        dialogOpen = true;
        try
        {
#if CODEX_DOCK_WINUI_SMOKE
            if (options.SmokeOutput is not null && !nativeAcceptanceInteractive)
                return smokeDialog is not null ? await smokeDialog(dialog) : throw new InvalidOperationException("隔离检查没有提供确认操作，未操作真实桌面。");
#endif
            return await dialog.ShowAsync();
        }
        finally { dialogOpen = false; }
    }

    private async void WindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowClose) return;
        args.Cancel = true;
        if (ManualApplicationBusy || dialogOpen)
        {
            ApplicationStatus.Text = "请先完成或取消当前确认与应用，再关闭窗口。";
            return;
        }
        if (TryHideToTray()) return;
#if CODEX_DOCK_WINUI_SMOKE
        if (options.InteractivePreview) { await ExitWindowAsync(); return; }
#endif
        await RequestWindowExitAsync();
    }

    private async Task<bool> StopBeforeCloseAsync(bool confirmed = false, bool stopGateways = false)
    {
        if (ManualApplicationBusy || dialogOpen || closePrompt || closing) return false;
        closePrompt = true;
        try
        {
            if (!await ResolveProfileDraftAsync() || !await ResolveTemplateEditAsync() || !await ResolveConnectionDraftAsync() || !await ResolveMobileDraftAsync()) return false;
            if (!confirmed && applicationRuntime.IsEnhancementRunning &&
                !await ConfirmDialogAsync("退出 Codex Dock", "退出后，本窗口提供的客户端增强将停止。Codex 和已启动的两个网关继续运行。", "仅退出 Dock")) return false;
            if (stopGateways && !await StopGatewaysForExitAsync()) return false;
            closing = true;
            await automaticEnhancements.CancelAndWaitAsync();
            await applicationRuntime.StopEnhancementsAsync();
            return true;
        }
        catch (Exception error)
        { closing = false; SetShellError("暂时无法退出", error.Message); return false; }
        finally { closePrompt = false; if (!closing) RefreshView(); }
    }

    private void Dispatch(Action action)
    {
        if (DispatcherQueue.HasThreadAccess) { action(); return; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        })) throw new OperationCanceledException("窗口已关闭。", lifetime.Token);
        completion.Task.WaitAsync(lifetime.Token).GetAwaiter().GetResult();
    }

    private sealed class WinUiDesktopApplyInteraction(MainWindow window, ReviewedDesktopChoice? modelChoice = null) : IDesktopApplyInteraction
    {
        public string ActiveHome => window.viewModel.Home;
        public void ValidateContext()
        { if (window.closing) throw new OperationCanceledException("窗口正在关闭。"); }
        public Task<CodexDesktopTarget?> ChooseAsync(IReadOnlyList<CodexDesktopTarget> targets, CancellationToken cancellation)
            => modelChoice is null ? window.ChooseDesktopAsync(targets, cancellation) :
                Task.FromResult<CodexDesktopTarget?>(targets.SingleOrDefault(modelChoice.Matches) ?? throw new IOException("所选 Codex 实例已改变，请重新选择后应用。"));
        public Task<bool> ConfirmAsync(DesktopApplyConfirmation confirmation, CancellationToken cancellation)
        {
            if (modelChoice is null)
                return window.ConfirmDialogAsync(confirmation.Title, confirmation.Message, confirmation.ForceExit ? "结束所选实例并重开" : "确认并应用");
            // The model dialog already confirmed a single target. Reuse that confirmation only for the same action.
            modelChoice.Validate(confirmation, window.viewModel.EnhancementPreferences.Load());
            return Task.FromResult(true);
        }
        public void Report(DesktopApplyProgress progress)
        {
            window.SetApplicationProgress(progress);
            window.RefreshView();
            if (window.ApplicationError.Visibility == Visibility.Visible)
                window.ApplicationError.StartBringIntoView();
        }
        public void TargetSelected(CodexDesktopTarget target, ConfigSnapshot saved)
        {
            window.ApplicationTarget.Text = "本次目标\n" + DesktopApplicationService.DescribeTarget(target) + "\n配置目录：" + saved.Home;
            window.ApplicationTarget.Visibility = Visibility.Visible;
        }
        public void DesktopOpened(CodexDesktopTarget target, ConfigSnapshot saved)
            => window.RecordOpenedDesktop(target, saved.Home);
    }
}
