using System.IO;
using System.Security.Cryptography;
using System.Windows;
using CodexConfig.Core;
using CodexConfig.Application;

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
        try
        {
            using var read = await officialLoginReader.ReadAsync(expectedSnapshot, fromBackup,
                () => !closing && home == expectedHome && EditingMixedConnection && ReferenceEquals(snapshot, expectedSnapshot), lifetime.Token);
            AdoptOfficialLoginSnapshot(read.Snapshot);
            ClearStagedLogin();
            stagedOfficialAuth = read.TakeStagedAuthentication();
            loginFeedback = read.Message;
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
        UpdateCredentialHint(); RefreshHttpUi(); RefreshMixedUi();
    }
}
