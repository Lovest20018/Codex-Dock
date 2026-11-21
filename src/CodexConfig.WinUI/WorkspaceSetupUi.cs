using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private sealed record ConnectionTarget(string Label, CodexDesktopTarget Target);
    private sealed record ConnectionReview(ConfigSnapshot Snapshot, ConfigurationSaveReview Review, long Revision,
        IReadOnlyList<ConnectionTarget> Targets, string TargetError, bool NeedsGateway, bool Speed, bool Connections);
    private ConnectionReview? connectionReview;
    private bool setupUsesChatGpt;
    private string setupNativeStatus = "";
    private bool ShowingNativeLogin => setupUsesChatGpt && !connectionDraft.HasSavedApiConnection && connectionDraft.Available;

    private void RefreshWorkspaceSetupControls()
    {
        if (WorkspaceSetupCard is null) return;
        var guide = viewModel.SetupGuide;
        WorkspaceSetupCard.Visibility = guide.Kind == WorkspaceSetupKind.Unreadable && (currentPage == WorkspaceNav || currentPage == ConnectionNav)
            ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceSetupTitle.Text = guide.Title; WorkspaceSetupMessage.Text = guide.Message;
        var editable = !ApplicationBusy && !dialogOpen && !closing;
        SetupReloadButton.IsEnabled = SetupRecoveryButton.IsEnabled = editable;
        SetupRecoveryButton.Visibility = guide.Kind == WorkspaceSetupKind.Unreadable || HasPendingRecovery ? Visibility.Visible : Visibility.Collapsed;

        if (currentPage != ConnectionNav) return;
        ConnectionPanel.SetupAuthenticationChoices.Visibility = connectionDraft.Available && !connectionDraft.HasSavedApiConnection ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPanel.SetupApiChoice.IsChecked = !ShowingNativeLogin;
        ConnectionPanel.SetupChatGptChoice.IsChecked = ShowingNativeLogin;
        var canChoose = editable && connectionDraft.Available && !HasPendingRecovery;
        ConnectionPanel.SetupChatGptChoice.IsEnabled = ConnectionPanel.SetupApiChoice.IsEnabled = ConnectionPanel.OpenNativeLoginButton.IsEnabled = ConnectionPanel.RereadNativeLoginButton.IsEnabled = canChoose;
        ConnectionPanel.SetupNativeCard.Visibility = ShowingNativeLogin ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPanel.SetupApiCard.Visibility = !ShowingNativeLogin && connectionReview is null && connectionDraft.Available ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPanel.OpenNativeLoginButton.Content = connectionDraft.HasCurrentOfficialLogin ? "打开 Codex" : "打开 Codex 登录";
        ConnectionPanel.SetupNativeHint.Text = connectionDraft.HasCurrentOfficialLogin ? "已读取此目录中的 ChatGPT 登录。" : "在 Codex 窗口完成 ChatGPT 登录，回到此页重新读取。";
        ConnectionPanel.SetupNativeStatus.Text = setupNativeStatus;
        ConnectionPanel.SetupNativeStatus.Visibility = setupNativeStatus.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void SetupChatGptClicked(object sender, RoutedEventArgs args) => SelectSetupAuthentication(native: true);
    internal void SetupApiClicked(object sender, RoutedEventArgs args) => SelectSetupAuthentication(native: false);
    private void SelectSetupAuthentication(bool native)
    {
        if (!CanEditConnection || connectionDraft.HasSavedApiConnection) return;
        setupUsesChatGpt = native;
        shellError = "";
        ClearConnectionReview(refresh: false);
        RefreshView();
    }

    private void FocusMissingConnectionModel()
    {
        SetShellError("先选择默认模型", "读取可用模型后选择一项，或直接填写模型 ID。");
        OpenModelsClicked(SaveConnectionButton, new());
    }

    private async Task PrepareConnectionReviewAsync()
    {
        if (!CanEditConnection || currentPage != ConnectionNav || ShowingNativeLogin || !connectionDraft.HasDraft || connectionDraft.Snapshot is not { } snapshot) return;
        if (string.IsNullOrWhiteSpace(connectionDraft.Model)) { FocusMissingConnectionModel(); return; }
        using var operation = busy.Begin(WindowOperation.ReviewConnection, "正在核对连接…"); shellError = ""; RefreshView();
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            var revision = connectionDraft.Revision;
            var request = connectionDraft.Request();
            var review = configurationSave.PrepareReview(request);
            var (targets, targetError) = await FindDesktopTargetsAsync(lifetime.Token);
            if (closing || currentPage != ConnectionNav || connectionDraft.Revision != revision || !ReferenceEquals(connectionDraft.Snapshot, snapshot)) return;
            var choices = targets.Select(target => new ConnectionTarget(DesktopChoiceLabel(target), target)).ToArray();
            connectionReview = new(snapshot, review, revision, choices, targetError, request.Settings.Mode == ConnectionMode.OfficialWithApi,
                viewModel.SpeedCompatibilityEnabled, viewModel.ConnectionRecoveryEnabled);
            ConnectionPanel.ConnectionTargetInput.ItemsSource = choices;
            ConnectionPanel.ConnectionTargetInput.SelectedIndex = choices.Length == 1 ? 0 : -1;
            ConnectionPanel.ConnectionReviewDetails.IsExpanded = false;
            ConnectionPanel.ConnectionReviewDetails.Content = ConfigurationReviewBody(review);
            ConnectionPanel.ConnectionReviewSummary.Text = (connectionDraft.Name.Trim() is { Length: > 0 } name ? name : viewModel.ProfileName) +
                "\n模型 " + request.Settings.Model.Trim() + " · " + ServiceOrigin(request.Settings.BaseUrl) + "\n" +
                (!request.UpdateConnection ? "保留现有登录方式。" : request.Settings.Mode == ConnectionMode.OfficialWithApi ? "保留 ChatGPT 登录，通过模型网关使用服务商密钥。" :
                    connectionDraft.HasOfficialLogin ? "改用服务商密钥登录。原有 ChatGPT 登录纳入加密备份。" : "使用服务商密钥登录。") +
                (review.HasChanges ? "\n确认后将加密备份并保存 " + review.Changes.Count + " 项修改。" : "\n设置已是所选值。");
            PageScroll.ChangeView(null, 0, null, true);
        }
        catch (OperationCanceledException) when (closing || lifetime.IsCancellationRequested) { }
        catch (Exception error) { SetShellError("暂时无法保存连接", error.Message); }
        finally { operation.Dispose(); if (!closing) RefreshView(); }
    }

    private void RefreshConnectionSetupReview(bool editable)
    {
        var onConnection = currentPage == ConnectionNav && !ShowingNativeLogin;
        var review = onConnection ? connectionReview : null;
        ConnectionPanel.ConnectionReviewCard.Visibility = review is not null ? Visibility.Visible : Visibility.Collapsed;
        SetupSaveOnlyButton.Visibility = SetupEditReviewButton.Visibility = review is not null ? Visibility.Visible : Visibility.Collapsed;
        SetupSaveOnlyButton.IsEnabled = SetupEditReviewButton.IsEnabled = editable;
        DiscardConnectionButton.Visibility = review is null ? Visibility.Visible : Visibility.Collapsed;
        if (!onConnection || savingConfiguration) return;
        var target = (ConnectionPanel.ConnectionTargetInput.SelectedItem as ConnectionTarget)?.Target;
        SaveConnectionButton.Content = review is null ? "核对连接" : target?.IsRunning == true ? "保存并重启 Codex" : "保存并打开 Codex";
        if (review is null) return;
        SaveConnectionButton.IsEnabled = editable && target is not null && viewModel.PreferencesAvailable;
        ConnectionPanel.ConnectionTargetInput.Visibility = review.Targets.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPanel.ConnectionTargetInput.IsEnabled = editable;
        ConnectionPanel.ConnectionReviewImpact.Text = target is not null && viewModel.PreferencesAvailable
            ? DesktopChoiceLabel(target) + "\n" + (target.IsRunning ? "重启可能中断正在进行的任务。" : "保存后打开 Codex。") +
                (review.NeedsGateway ? " 将先准备模型网关。" : "") + (review.Speed || review.Connections ? " 将加载已启用的增强。" : "")
            : review.Targets.Count > 1 && target is null ? "选择目标窗口，或先保存设置。" :
                (review.TargetError.Length > 0 ? review.TargetError + " " : "") + "可先保存，稍后从首页继续。";
    }

    internal void ConnectionTargetChanged(object sender, SelectionChangedEventArgs args)
    { if (initialized && !updatingControls && connectionReview is not null) RefreshView(); }
    private void SetupEditReviewClicked(object sender, RoutedEventArgs args) => ClearConnectionReview();
    private async void SetupSaveOnlyClicked(object sender, RoutedEventArgs args) => await ConfirmConnectionReviewAsync(apply: false);
    private void ClearConnectionReview(bool refresh = true)
    {
        if (connectionReview is null) return;
        connectionReview = null;
        ConnectionPanel.ConnectionTargetInput.ItemsSource = null;
        ConnectionPanel.ConnectionReviewDetails.Content = null;
        if (refresh && !closing) RefreshView();
    }

    private async Task ConfirmConnectionReviewAsync(bool apply)
    {
        if (!CanEditConnection || currentPage != ConnectionNav || connectionReview is not { } review ||
            connectionDraft.Revision != review.Revision || !ReferenceEquals(connectionDraft.Snapshot, review.Snapshot)) return;
        ReviewedDesktopChoice? choice = null;
        if (apply)
        {
            if (ConnectionPanel.ConnectionTargetInput.SelectedItem is not ConnectionTarget selected || !viewModel.PreferencesAvailable) return;
            choice = new(review.Snapshot.Home, selected.Target, review.Targets.Count, review.NeedsGateway, review.Speed, review.Connections);
            using var operation = busy.Begin(WindowOperation.ReviewConnection, "正在核对连接…"); RefreshView();
            try
            {
                var (targets, _) = await FindDesktopTargetsAsync(lifetime.Token);
                if (closing || currentPage != ConnectionNav || !ReferenceEquals(review, connectionReview)) return;
                if (targets.Count != choice.TargetCount || !targets.Any(choice.Matches) ||
                    choice.Speed != viewModel.SpeedCompatibilityEnabled || choice.Connections != viewModel.ConnectionRecoveryEnabled)
                {
                    ClearConnectionReview(refresh: false);
                    SetShellError("请重新核对连接", "Codex 窗口或增强选项已变化。你的设置仍在草稿中。");
                    return;
                }
            }
            catch (OperationCanceledException) when (closing || lifetime.IsCancellationRequested) { return; }
            finally { operation.Dispose(); if (!closing) RefreshView(); }
        }
        if (await SaveConnectionAsync((review.Review, review.Revision)) && choice is not null)
            await ApplyDesktopAsync(modelChoice: choice);
    }
}
