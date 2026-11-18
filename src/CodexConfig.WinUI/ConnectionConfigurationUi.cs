using System.ComponentModel;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private bool savingConfiguration, loadingConnection;
    private ConfigurationSaveResult? configurationResult;

    private void LoadWorkspace(string home) { quickModelAwaitingApply = false; viewModel.Load(home); LoadConnectionDraft(); LoadGatewayPreferences(); ResetMobileHome(); ResetProfileHome(); ResetDiagnosticHome(); }
    private void LoadWorkspace(ConfigSnapshot saved) { quickModelAwaitingApply = false; viewModel.Load(saved); LoadConnectionDraft(); LoadGatewayPreferences(); ResetMobileHome(); ResetProfileHome(); ResetDiagnosticHome(); }
    private void LoadConnectionDraft()
    {
        InvalidateSavedPreview();
        ReadRecoveryStatus(); templateApplication = null;
        officialLoginStatus = ""; ResetModelDiscovery();
        loadingConnection = true;
        try { connectionDraft.Load(viewModel.ConfigurationAvailable && !HasPendingRecovery ? viewModel.Snapshot : null); }
        finally { loadingConnection = false; }
        RefreshView();
    }

    private void ConnectionDraftChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ConnectionDraftViewModel.Revision) || loadingConnection || closing) return;
        automaticEnhancements.Cancel(); desktopApplication.ClearEnhancementRetry();
        lastApplication = null; configurationResult = null; shellError = "";
        viewModel.Session.MarkDraft(connectionDraft.HasDraft); RefreshView();
    }

    private void RefreshConnectionControls()
    {
        if (ConnectionFields is null) return;
        var editable = connectionDraft.Available && !HasPendingRecovery && !ManualApplicationBusy && !dialogOpen && !closing;
        foreach (var control in new Control[] { ConnectionModeInput, ConnectionKindInput, BaseUrlInput, ApiKeyInput,
            ConnectionNameInput, ConnectionNoteInput, ConnectionProxyInput, HttpConsentInput, GatewayHeaderWaitInput }) control.IsEnabled = editable;
        ConnectionModeInput.SelectedIndex = (int)connectionDraft.Mode; ConnectionKindInput.SelectedIndex = (int)connectionDraft.Kind;
        if (BaseUrlInput.Text != connectionDraft.BaseUrl) BaseUrlInput.Text = connectionDraft.BaseUrl;
        if (ApiKeyInput.Password != connectionDraft.ApiKey) ApiKeyInput.Password = connectionDraft.ApiKey;
        if (ConnectionNameInput.Text != connectionDraft.Name) ConnectionNameInput.Text = connectionDraft.Name;
        if (ConnectionNoteInput.Text != connectionDraft.Note) ConnectionNoteInput.Text = connectionDraft.Note;
        if (GatewayHeaderWaitInput.Text != connectionDraft.GatewayHeaderTimeout) GatewayHeaderWaitInput.Text = connectionDraft.GatewayHeaderTimeout;
        GatewayWaitFields.Visibility = connectionDraft.Mode == ConnectionMode.OfficialWithApi ? Visibility.Visible : Visibility.Collapsed;
        ConnectionProxyInput.IsChecked = connectionDraft.UseSystemProxy;
        ConnectionProxyInput.Visibility = connectionDraft.Mode == ConnectionMode.OfficialWithApi ? Visibility.Visible : Visibility.Collapsed;
        ConnectionLoginInfo.Title = connectionDraft.HasStagedOfficialLogin ? "官方登录已暂存" : connectionDraft.HasOfficialLogin ? "已读取本地官方登录" : "使用服务提供的 API Key";
        ConnectionLoginInfo.IsOpen = connectionDraft.Mode == ConnectionMode.OfficialWithApi || connectionDraft.HasStagedOfficialLogin;
        ConnectionLoginInfo.Visibility = ConnectionLoginInfo.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        ConnectionLoginInfo.Message = connectionDraft.HasStagedOfficialLogin ? "确认保存后才使用这份登录；切换为 API Key 直连时不会采用暂存身份。" :
            connectionDraft.HasOfficialLogin ? "只修改模型或备注会保留现有登录。切换为 API Key 直连时，原登录纳入加密备份。" :
            "“官方账号 + API 服务”需要在 Codex 桌面登录后读取当前文件；服务的 API Key 与官方账号分别使用。";
        ConnectionModeHint.Text = connectionDraft.Mode == ConnectionMode.OfficialWithApi
            ? "保留官方账号身份，模型请求通过本机网关使用下方 API Key。应用时会先准备网关。"
            : "使用服务提供的 API Key。模型在“模型与行为”中选择。";
        CredentialSourceText.Text = "当前凭据来源：" + connectionDraft.CredentialSource +
            (connectionDraft.ProviderName.Length > 0 ? "\n供应商：" + connectionDraft.ProviderName : "");
        LegacyKeyInfo.IsOpen = connectionDraft.NeedsApiKeyMigration;
        LegacyKeyInfo.Message = connectionDraft.MigrateLegacyPat ? "迁移已加入草稿。确认保存时使用当前密钥写入 API Key 认证，并备份旧认证。" : "只读或修改普通设置会保留旧 PAT。确认迁移后才改用 API Key 认证。";
        MigrateKeyButton.IsEnabled = editable && !connectionDraft.MigrateLegacyPat;
        HttpConsentInput.Visibility = connectionDraft.HttpRequiredOrigin is null ? Visibility.Collapsed : Visibility.Visible;
        HttpConsentInput.IsChecked = connectionDraft.HttpAuthorized;
        HttpConsentText.Text = "确认凭据和模型内容将以明文发送到 " + connectionDraft.HttpRequiredOrigin;
        var configurationScope = currentPage.EditsConfiguration && !(currentPage == TemplatesNav && editingTemplate is not null);
        ConnectionDraftActions.Visibility = configurationScope && connectionDraft.HasDraft ? Visibility.Visible : Visibility.Collapsed;
        var showApply = currentPage == EnhancementsNav || configurationScope && configurationResult?.Succeeded == true;
        DesktopApplicationActions.Visibility = showApply && !(configurationScope && connectionDraft.HasDraft) ? Visibility.Visible : Visibility.Collapsed;
        ApplicationFooter.Visibility = configurationScope && connectionDraft.HasDraft || showApply ? Visibility.Visible : Visibility.Collapsed;
        SaveConnectionButton.Content = savingConfiguration ? "正在核对…" : "查看差异并保存";
        SaveConnectionButton.IsEnabled = DiscardConnectionButton.IsEnabled = connectionDraft.HasDraft && !ManualApplicationBusy && !dialogOpen && !closing;
        SavedBackupText.Visibility = configurationResult?.BackupPath is null ? Visibility.Collapsed : Visibility.Visible;
        SavedBackupText.Text = configurationResult?.BackupPath is { } backup ? "本次加密备份：\n" + backup : "";
        RefreshOfficialAndDiscoveryControls(editable);
    }

    private void OpenConnectionClicked(object sender, RoutedEventArgs args) => NavigateTo(ConnectionNav);
    private bool CanEditConnection => initialized && !updatingControls && !loadingConnection && !HasPendingRecovery && !ManualApplicationBusy && !dialogOpen && !closing;
    private void ConnectionTextChanged(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        if (!CanEditConnection) return;
        if (ReferenceEquals(sender, BaseUrlInput)) connectionDraft.BaseUrl = BaseUrlInput.Text;
        else if (ReferenceEquals(sender, ConnectionNameInput)) connectionDraft.Name = ConnectionNameInput.Text;
        else if (ReferenceEquals(sender, ConnectionNoteInput)) connectionDraft.Note = ConnectionNoteInput.Text;
        else if (ReferenceEquals(sender, GatewayHeaderWaitInput)) connectionDraft.GatewayHeaderTimeout = sender.Text;
    }
    private void ConnectionKeyChanging(PasswordBox sender, PasswordBoxPasswordChangingEventArgs args) { if (CanEditConnection) connectionDraft.ApiKey = sender.Password; }
    private void ConnectionSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!CanEditConnection) return;
        if (ReferenceEquals(sender, ConnectionModeInput) && ConnectionModeInput.SelectedIndex >= 0) connectionDraft.Mode = (ConnectionMode)ConnectionModeInput.SelectedIndex;
        if (ReferenceEquals(sender, ConnectionKindInput) && ConnectionKindInput.SelectedIndex >= 0) connectionDraft.Kind = (ConnectionKind)ConnectionKindInput.SelectedIndex;
    }
    private void ConnectionProxyChanged(object sender, RoutedEventArgs args) { if (CanEditConnection) connectionDraft.UseSystemProxy = ConnectionProxyInput.IsChecked == true; }
    private void HttpConsentChanged(object sender, RoutedEventArgs args) { if (CanEditConnection) connectionDraft.HttpAuthorized = HttpConsentInput.IsChecked == true; }
    private void MigrateKeyClicked(object sender, RoutedEventArgs args) { if (CanEditConnection) connectionDraft.MigrateLegacyPat = true; }
    private async void SaveConnectionClicked(object sender, RoutedEventArgs args) => await SaveConnectionAsync();
    private void DiscardConnectionClicked(object sender, RoutedEventArgs args)
    {
        if (ApplicationBusy || dialogOpen || closing) return;
        configurationResult = null; shellError = ""; LoadConnectionDraft(); viewModel.Session.MarkDraft(false); RefreshView();
    }

    private async Task<bool> SaveConnectionAsync((ConfigurationSaveReview Review, long Revision)? inlineConfirmation = null)
    {
        if (ManualApplicationBusy || dialogOpen || closing || !connectionDraft.HasDraft || viewModel.Snapshot is not { } snapshot) return false;
        if (string.IsNullOrWhiteSpace(connectionDraft.Model))
        { SetShellError("先选择默认模型", "填写模型 ID，或获取当前服务的模型后再保存。"); OpenModelsClicked(SaveConnectionButton, new()); return false; }
        savingConfiguration = true; shellError = ""; RefreshView();
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
            if (configurationResult.WasWritten) quickModelAwaitingApply = inlineConfirmation is not null;
            desktopApplication.Reset(); lastApplication = null;
            viewModel.ConfigurationSaved(saved);
            loadingConnection = true;
            try { connectionDraft.Load(saved); }
            finally { loadingConnection = false; }
            officialLoginStatus = ""; ResetModelDiscovery();
            ReadRecoveryStatus(); templateApplication = null;
            InvalidateSource();
            InvalidateSavedPreview();
            return true;
        }
        catch (Exception error)
        { SetShellError("保存未完成，修改已保留", error.Message); return false; }
        finally { savingConfiguration = false; RefreshView(); }
    }

    private ContentDialog CreateConfigurationReviewDialog(ConfigurationSaveReview review)
    {
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(new TextBlock { Text = "确认后自动加密备份，再写入以下本地文件。保存不会启动或重启 Codex。", TextWrapping = TextWrapping.Wrap });
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
        return new ContentDialog { Title = "确认写入 " + review.Changes.Count + " 项修改", PrimaryButtonText = "确认保存", CloseButtonText = "返回调整",
            DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, Content = new ScrollViewer { MaxHeight = 420, Content = content } };
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
