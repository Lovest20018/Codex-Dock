using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class ConnectionPage : UserControl
{
    public ConnectionPage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    internal void RenderDraft(ConnectionDraftViewModel connectionDraft, bool editable, string? backupPath)
    {
        ConnectionModelSummary.Text = "默认模型：" + (string.IsNullOrWhiteSpace(connectionDraft.Model) ? "尚未选择" : connectionDraft.Model);
        ChooseConnectionModelButton.IsEnabled = editable;
        foreach (var control in new Control[] { ConnectionModeInput, ConnectionKindInput, BaseUrlInput, ApiKeyInput,
            ConnectionNameInput, ConnectionNoteInput, ConnectionProxyInput, HttpConsentInput, GatewayHeaderWaitInput }) control.IsEnabled = editable;
        ConnectionModeInput.SelectedIndex = (int)connectionDraft.Mode; ConnectionKindInput.SelectedIndex = (int)connectionDraft.Kind;
        ConnectionModeInput.Visibility = connectionDraft.HasOfficialLogin || connectionDraft.HasSavedApiConnection || connectionDraft.Mode == ConnectionMode.OfficialWithApi
            ? Visibility.Visible : Visibility.Collapsed;
        if (BaseUrlInput.Text != connectionDraft.BaseUrl) BaseUrlInput.Text = connectionDraft.BaseUrl;
        if (ApiKeyInput.Password != connectionDraft.ApiKey) ApiKeyInput.Password = connectionDraft.ApiKey;
        if (ConnectionNameInput.Text != connectionDraft.Name) ConnectionNameInput.Text = connectionDraft.Name;
        if (ConnectionNoteInput.Text != connectionDraft.Note) ConnectionNoteInput.Text = connectionDraft.Note;
        if (GatewayHeaderWaitInput.Text != connectionDraft.GatewayHeaderTimeout) GatewayHeaderWaitInput.Text = connectionDraft.GatewayHeaderTimeout;
        GatewayWaitFields.Visibility = connectionDraft.Mode == ConnectionMode.OfficialWithApi ? Visibility.Visible : Visibility.Collapsed;
        ConnectionProxyInput.IsChecked = connectionDraft.UseSystemProxy;
        ConnectionProxyInput.Visibility = connectionDraft.Mode == ConnectionMode.OfficialWithApi ? Visibility.Visible : Visibility.Collapsed;
        ConnectionLoginInfo.Title = connectionDraft.HasStagedOfficialLogin ? "官方登录已暂存" : connectionDraft.HasOfficialLogin ? "已读取本地官方登录" : "使用服务提供的 API Key";
        ConnectionLoginInfo.IsOpen = connectionDraft.Mode == ConnectionMode.OfficialWithApi || connectionDraft.HasOfficialLogin;
        ConnectionLoginInfo.Visibility = ConnectionLoginInfo.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        ConnectionLoginInfo.Message = connectionDraft.HasStagedOfficialLogin ? "确认保存后才使用这份登录；切换为 API Key 直连时不会采用暂存身份。" :
            connectionDraft.HasOfficialLogin ? "只修改模型或备注会保留现有登录。切换为 API Key 直连时，原登录纳入加密备份。" :
            "“官方账号 + API 服务”需要在 Codex 桌面登录后读取当前文件；服务的 API Key 与官方账号分别使用。";
        ConnectionModeHint.Text = connectionDraft.Mode == ConnectionMode.OfficialWithApi
            ? "保留官方账号身份，模型请求通过本机网关使用下方 API Key。应用时会先准备网关。"
            : connectionDraft.HasOfficialLogin ? "保存纯密钥连接后，Codex 将使用服务商密钥登录。" : "";
        ConnectionModeHint.Visibility = ConnectionModeHint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        CredentialSourceText.Text = "当前凭据来源：" + connectionDraft.CredentialSource +
            (connectionDraft.ProviderName.Length > 0 ? "\n供应商：" + connectionDraft.ProviderName : "");
        LegacyKeyInfo.IsOpen = connectionDraft.NeedsApiKeyMigration;
        LegacyKeyInfo.Visibility = LegacyKeyInfo.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        LegacyKeyInfo.Message = connectionDraft.MigrateLegacyPat ? "迁移已加入草稿。确认保存时使用当前密钥写入 API Key 认证，并备份旧认证。" : "只读或修改普通设置会保留旧 PAT。确认迁移后才改用 API Key 认证。";
        MigrateKeyButton.IsEnabled = editable && !connectionDraft.MigrateLegacyPat;
        HttpConsentInput.Visibility = connectionDraft.HttpRequiredOrigin is null ? Visibility.Collapsed : Visibility.Visible;
        HttpConsentInput.IsChecked = connectionDraft.HttpAuthorized;
        HttpConsentText.Text = "确认凭据和模型内容将以明文发送到 " + connectionDraft.HttpRequiredOrigin;
        SavedBackupText.Visibility = backupPath is null ? Visibility.Collapsed : Visibility.Visible;
        SavedBackupText.Text = backupPath is { } backup ? "本次加密备份：\n" + backup : "";
    }

    private void ConnectionKeyChanging(PasswordBox sender, PasswordBoxPasswordChangingEventArgs args) => Host?.ConnectionKeyChanging(sender, args);

    private void ConnectionProxyChanged(object sender, RoutedEventArgs args) => Host?.ConnectionProxyChanged(sender, args);

    private void ConnectionSelectionChanged(object sender, SelectionChangedEventArgs args) => Host?.ConnectionSelectionChanged(sender, args);

    private void ConnectionTargetChanged(object sender, SelectionChangedEventArgs args) => Host?.ConnectionTargetChanged(sender, args);

    private void ConnectionTextChanged(TextBox sender, TextBoxTextChangingEventArgs args) => Host?.ConnectionTextChanged(sender, args);

    private void HttpConsentChanged(object sender, RoutedEventArgs args) => Host?.HttpConsentChanged(sender, args);

    private void MigrateKeyClicked(object sender, RoutedEventArgs args) => Host?.MigrateKeyClicked(sender, args);

    private void OpenModelsClicked(object sender, RoutedEventArgs args) => Host?.OpenModelsClicked(sender, args);

    private void OpenNativeLoginClicked(object sender, RoutedEventArgs args) => Host?.OpenNativeLoginClicked(sender, args);

    private void ReadOfficialLoginClicked(object sender, RoutedEventArgs args) => Host?.ReadOfficialLoginClicked(sender, args);

    private void RereadNativeLoginClicked(object sender, RoutedEventArgs args) => Host?.RereadNativeLoginClicked(sender, args);

    private void RestoreOfficialLoginClicked(object sender, RoutedEventArgs args) => Host?.RestoreOfficialLoginClicked(sender, args);

    private void SetupApiClicked(object sender, RoutedEventArgs args) => Host?.SetupApiClicked(sender, args);

    private void SetupChatGptClicked(object sender, RoutedEventArgs args) => Host?.SetupChatGptClicked(sender, args);
}
