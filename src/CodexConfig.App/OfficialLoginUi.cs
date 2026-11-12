using System.IO;
using System.Security.Cryptography;
using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async void RefreshOfficialLoginClicked(object sender, RoutedEventArgs e) => await ReadOfficialLoginAsync(fromBackup: false);
    private async void RestoreOfficialLoginClicked(object sender, RoutedEventArgs e) => await ReadOfficialLoginAsync(fromBackup: true);

    private async Task ReadOfficialLoginAsync(bool fromBackup)
    {
        if (officialLoginBusy || closing || !EditingMixedConnection || snapshot is null) return;
        var expectedHome = home; var expectedSnapshot = snapshot;
        officialLoginBusy = true; loginFeedback = null; loginFeedbackIsError = false;
        OfficialStatusText.Text = "正在读取当前配置目录的官方登录…";
        RefreshMixedUi(); UpdateActions();
        byte[]? auth = null;
        try
        {
            var latest = await Task.Run(() => store.Load(expectedHome), lifetime.Token);
            if (closing || home != expectedHome || !EditingMixedConnection || !ReferenceEquals(snapshot, expectedSnapshot)) return;
            if (!CodexApplySnapshot.Compare(expectedSnapshot, latest with { Auth = expectedSnapshot.Auth }).CanContinue)
                throw new IOException("配置或连接资料也已更改。请先核对外部修改并重新读取配置；当前草稿已保留。");
            _ = store.ReadSettings(latest);
            var live = OfficialCredentials.HasLogin(latest.Auth);
            auth = live ? OfficialCredentials.ForMixed(latest.Auth)
                : fromBackup ? await Task.Run(() => store.ReadOfficialLoginBackup(expectedHome), lifetime.Token) : null;
            if (closing || home != expectedHome || !EditingMixedConnection || !ReferenceEquals(snapshot, expectedSnapshot)) return;
            if (auth is null)
                throw new InvalidOperationException(fromBackup
                    ? "当前目录没有可用的官方登录备份。请先在 Codex 桌面登录 ChatGPT，再回来重新读取。"
                    : "当前目录未检测到官方文件登录。请先在 Codex 桌面完成 ChatGPT 登录；已使用 API Key 时也可选择恢复本目录的官方登录备份。");
            AdoptOfficialLoginSnapshot(latest);
            ClearStagedLogin();
            if (!live || new TomlDocument(latest.Config).String("cli_auth_credentials_store") is not (null or "file"))
            {
                stagedOfficialAuth = auth; auth = null;
            }
            loginFeedback = live ? "已读取当前官方登录，模型入口与未保存草稿已保留。"
                : "已从当前目录的加密备份暂存官方登录，确认保存后启用。有效期与刷新由 Codex 管理。";
            MarkDirty(); SetStatus(loginFeedback);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            loginFeedback = error.Message; loginFeedbackIsError = true;
            if (!closing && home == expectedHome) SetStatus(loginFeedback, true);
        }
        finally
        {
            if (auth is not null) CryptographicOperations.ZeroMemory(auth);
            officialLoginBusy = false;
            if (!closing && home == expectedHome) { RefreshMixedUi(); UpdateActions(); UpdatePreview(); }
        }
    }

    private void AdoptOfficialLoginSnapshot(ConfigSnapshot latest)
    {
        var draft = Settings() with { Model = PendingModelText ? behaviorModel.Text : ModelBox.Text }; var edits = detailedChanges.ToArray();
        var pendingCatalog = catalogDraft; var pendingReview = reviewDraft; var pendingSource = catalogSourceDraft;
        if (!Reload(latest)) throw new InvalidDataException("官方登录读取未完成，请检查配置格式；尚未写入文件。");
        initializing = true;
        try
        {
            BaseUrlBox.Text = draft.BaseUrl; ApiKeyBox.Password = VisibleKeyBox.Text = draft.ApiKey; ModelBox.Text = draft.Model;
            KindBox.SelectedIndex = (int)draft.Kind; ConnectionModeBox.SelectedIndex = (int)draft.Mode; GatewayProxyBox.IsChecked = draft.UseSystemProxy;
            ConnectionNameBox.Text = draft.Details.Name; ConnectionNoteBox.Text = draft.Details.Note; draftHttpOrigin = draft.Details.HttpOrigin;
            detailedChanges.Clear(); foreach (var (path, value) in edits) detailedChanges[path] = value;
            catalogDraft = pendingCatalog; reviewDraft = pendingReview; catalogSourceDraft = pendingSource;
            behaviorModel.Text = draft.Model;
            Editor.Configure(latest.Config, detailedChanges); TemplateEditor.Configure(latest.Config, detailedChanges);
        }
        finally { initializing = false; }
        SynchronizeLinePreset(); RefreshHttpUi(); RefreshMixedUi();
    }
}
