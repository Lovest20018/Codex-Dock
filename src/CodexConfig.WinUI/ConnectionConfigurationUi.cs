using System.ComponentModel;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private bool loadingConnection, setupProtocolSelected;
    private bool savingConfiguration => busy.IsActive(WindowOperation.SaveConfiguration);
    private ConfigurationSaveResult? configurationResult;

    private void LoadWorkspace(string home) { quickModelAwaitingApply = false; viewModel.Load(home); LoadConnectionDraft(); LoadGatewayPreferences(); ResetMobileHome(); ResetProfileHome(); DiagnosticsPanel.ResetHome(); }
    private void LoadWorkspace(ConfigSnapshot saved) { quickModelAwaitingApply = false; viewModel.Load(saved); LoadConnectionDraft(); LoadGatewayPreferences(); ResetMobileHome(); ResetProfileHome(); DiagnosticsPanel.ResetHome(); }
    private void LoadConnectionDraft()
    {
        InvalidateSavedPreview();
        ReadRecoveryStatus(); templateApplication = null;
        officialLoginStatus = ""; setupNativeStatus = ""; ClearConnectionReview(refresh: false); ResetModelDiscovery();
        loadingConnection = true;
        try { connectionDraft.Load(viewModel.ConfigurationAvailable && !HasPendingRecovery ? viewModel.Snapshot : null, firstConnection: viewModel.NeedsConnectionSetup); }
        finally { loadingConnection = false; }
        setupProtocolSelected = false;
        setupUsesChatGpt = connectionDraft.HasCurrentOfficialLogin && !connectionDraft.HasSavedApiConnection;
        ConnectionPanel.ConnectionOptions.IsExpanded = false;
        RefreshView();
    }

    private void ConnectionDraftChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ConnectionDraftViewModel.Revision) || loadingConnection || closing) return;
        automaticEnhancements.Cancel(); desktopApplication.ClearEnhancementRetry(); ClearConnectionReview(refresh: false);
        lastApplication = null; configurationResult = null; shellError = "";
        viewModel.Session.MarkDraft(connectionDraft.HasDraft); RefreshView();
    }

    private void RefreshConnectionControls()
    {
        if (ConnectionPanel.ConnectionFields is null) return;
        var editable = connectionDraft.Available && !HasPendingRecovery && !ManualApplicationBusy && !dialogOpen && !closing;
        ConnectionPanel.RenderDraft(connectionDraft, editable, configurationResult?.BackupPath);
        RefreshOfficialLoginControls(editable);
    }

    private void RefreshConfigurationFooter()
    {
        var configurationScope = currentPage.EditsConfiguration && !(currentPage == TemplatesNav && editingTemplate is not null) && !(currentPage == ConnectionNav && ShowingNativeLogin);
        ConnectionDraftActions.Visibility = configurationScope && connectionDraft.HasDraft ? Visibility.Visible : Visibility.Collapsed;
        var showApply = currentPage == EnhancementsNav || configurationScope && configurationResult?.Succeeded == true;
        DesktopApplicationActions.Visibility = showApply && !(configurationScope && connectionDraft.HasDraft) ? Visibility.Visible : Visibility.Collapsed;
        ApplicationFooter.Visibility = configurationScope && connectionDraft.HasDraft || showApply ? Visibility.Visible : Visibility.Collapsed;
        SaveConnectionButton.Content = savingConfiguration ? "正在保存…" : "查看差异并保存";
        SaveConnectionButton.IsEnabled = DiscardConnectionButton.IsEnabled = connectionDraft.HasDraft && !ManualApplicationBusy && !dialogOpen && !closing;
        RefreshConnectionSetupReview(connectionDraft.Available && !HasPendingRecovery && !ManualApplicationBusy && !dialogOpen && !closing);
    }

    internal void OpenConnectionClicked(object sender, RoutedEventArgs args) => NavigateTo(ConnectionNav, revealStart: true);
    private bool CanEditConnection => initialized && !updatingControls && !loadingConnection && !HasPendingRecovery && !ManualApplicationBusy && !dialogOpen && !closing;
    internal void ConnectionTextChanged(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        if (!CanEditConnection) return;
        if (ReferenceEquals(sender, ConnectionPanel.BaseUrlInput))
        {
            var address = sender.Text;
            connectionDraft.BaseUrl = address;
            if (viewModel.NeedsConnectionSetup && !setupProtocolSelected && Uri.TryCreate(address, UriKind.Absolute, out var uri))
            {
                var path = uri.AbsolutePath.TrimEnd('/');
                if (path.EndsWith("/models", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
                    path = path[..path.LastIndexOf('/')];
                connectionDraft.Kind = path is "/backend-api/codex" or "/codex/v1" ? ConnectionKind.CodexRelay : ConnectionKind.ResponsesApi;
            }
        }
        else if (ReferenceEquals(sender, ConnectionPanel.ConnectionNameInput)) connectionDraft.Name = ConnectionPanel.ConnectionNameInput.Text;
        else if (ReferenceEquals(sender, ConnectionPanel.ConnectionNoteInput)) connectionDraft.Note = ConnectionPanel.ConnectionNoteInput.Text;
        else if (ReferenceEquals(sender, ConnectionPanel.GatewayHeaderWaitInput)) connectionDraft.GatewayHeaderTimeout = sender.Text;
    }
    internal void ConnectionKeyChanging(PasswordBox sender, PasswordBoxPasswordChangingEventArgs args) { if (CanEditConnection) connectionDraft.ApiKey = sender.Password; }
    internal void ConnectionSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!CanEditConnection) return;
        if (ReferenceEquals(sender, ConnectionPanel.ConnectionModeInput) && ConnectionPanel.ConnectionModeInput.SelectedIndex >= 0) connectionDraft.Mode = (ConnectionMode)ConnectionPanel.ConnectionModeInput.SelectedIndex;
        if (ReferenceEquals(sender, ConnectionPanel.ConnectionKindInput) && ConnectionPanel.ConnectionKindInput.SelectedIndex >= 0)
        { setupProtocolSelected = true; connectionDraft.Kind = (ConnectionKind)ConnectionPanel.ConnectionKindInput.SelectedIndex; }
    }
    internal void ConnectionProxyChanged(object sender, RoutedEventArgs args) { if (CanEditConnection) connectionDraft.UseSystemProxy = ConnectionPanel.ConnectionProxyInput.IsChecked == true; }
    internal void HttpConsentChanged(object sender, RoutedEventArgs args) { if (CanEditConnection) connectionDraft.HttpAuthorized = ConnectionPanel.HttpConsentInput.IsChecked == true; }
    internal void MigrateKeyClicked(object sender, RoutedEventArgs args) { if (CanEditConnection) connectionDraft.MigrateLegacyPat = true; }
    private async void SaveConnectionClicked(object sender, RoutedEventArgs args)
    {
        if (currentPage == ConnectionNav && !ShowingNativeLogin)
        {
            if (connectionReview is null) await PrepareConnectionReviewAsync();
            else await ConfirmConnectionReviewAsync(apply: true);
        }
        else await SaveConnectionAsync();
    }
    private void DiscardConnectionClicked(object sender, RoutedEventArgs args)
    {
        if (ApplicationBusy || dialogOpen || closing) return;
        configurationResult = null; shellError = ""; LoadConnectionDraft(); viewModel.Session.MarkDraft(false); RefreshView();
    }

    private async Task<bool> SaveConnectionAsync((ConfigurationSaveReview Review, long Revision)? inlineConfirmation = null, bool modelSave = false)
    {
        if (ManualApplicationBusy || dialogOpen || closing || !connectionDraft.HasDraft || viewModel.Snapshot is not { } snapshot) return false;
        if (string.IsNullOrWhiteSpace(connectionDraft.Model))
        { FocusMissingConnectionModel(); return false; }
        using var operation = busy.Begin(WindowOperation.SaveConfiguration, "正在备份并保存配置…"); shellError = ""; RefreshView();
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            var revision = inlineConfirmation?.Revision ?? connectionDraft.Revision;
            var review = inlineConfirmation?.Review ?? configurationSave.PrepareReview(connectionDraft.Request());
            if (inlineConfirmation is null && review.HasChanges && await ShowDialogAsync(CreateConfigurationReviewDialog(review)) != ContentDialogResult.Primary) return false;
            configurationResult = configurationSave.Save(review, snapshot, () => viewModel.Home, () =>
            {
                if (closing || connectionDraft.Revision != revision) throw new OperationCanceledException("草稿已改变，请重新查看修改。");
            }, lifetime.Token);
            if (!configurationResult.Succeeded || configurationResult.Saved is not { } saved)
            { SetShellError("保存未完成，修改已保留", configurationResult.Message); return false; }
            if (configurationResult.WasWritten) quickModelAwaitingApply = modelSave;
            desktopApplication.Reset(); lastApplication = null;
            viewModel.ConfigurationSaved(saved);
            loadingConnection = true;
            try { connectionDraft.Load(saved); }
            finally { loadingConnection = false; }
            officialLoginStatus = ""; ClearConnectionReview(refresh: false); ResetModelDiscovery();
            ReadRecoveryStatus(); templateApplication = null;
            InvalidateSource();
            InvalidateSavedPreview();
            return true;
        }
        catch (Exception error)
        { SetShellError("保存未完成，修改已保留", error.Message); return false; }
        finally { operation.Dispose(); RefreshView(); }
    }

    private ContentDialog CreateConfigurationReviewDialog(ConfigurationSaveReview review)
    {
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(new TextBlock { Text = "确认后自动加密备份，再写入以下本地文件。保存不会启动或重启 Codex。", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(ConfigurationReviewBody(review));
        return new ContentDialog { Title = "确认写入 " + review.Changes.Count + " 项修改", PrimaryButtonText = "确认保存", CloseButtonText = "返回调整",
            DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, Content = new ScrollViewer { MaxHeight = 420, Content = content } };
    }

    private static StackPanel ConfigurationReviewBody(ConfigurationSaveReview review)
    {
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(new TextBlock { Text = "本次写入目标：\n" + string.Join("\n", review.Targets), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Opacity = 0.7 });
        foreach (var change in review.Changes)
        {
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(new TextBlock { Text = change.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new TextBlock { Text = change.Path, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new TextBlock { Text = "本地现值：" + change.Previous + "\n修改为：" + change.Next, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            content.Children.Add(row);
        }
        if (review.Notes.Count > 0) content.Children.Add(new TextBlock { Text = string.Join("\n", review.Notes), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
        return content;
    }

    private async Task<bool> ResolveConnectionDraftAsync()
    {
        if (!connectionDraft.HasDraft) return true;
        if (ManualApplicationBusy || dialogOpen || closing) return false;
        var dialog = new ContentDialog { Title = "有未保存的配置修改", Content = "可先查看并保存，或放弃修改后继续。", PrimaryButtonText = "查看并保存",
            SecondaryButtonText = "放弃修改", CloseButtonText = "继续编辑", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot };
        var result = await ShowDialogAsync(dialog);
        if (result == ContentDialogResult.Primary) return await SaveConnectionAsync();
        if (result != ContentDialogResult.Secondary) return false;
        LoadConnectionDraft(); viewModel.Session.MarkDraft(false); return true;
    }
}
