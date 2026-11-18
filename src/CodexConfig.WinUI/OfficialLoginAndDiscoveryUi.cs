using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Net;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private bool readingOfficialLogin, discoveringModels;
    private string officialLoginStatus = "", discoveryStatus = "";
    private bool discoveryFailed, discoveryKeyRejected;
    private IReadOnlyList<string> discoveredModels = [];
    private string? discoveredFingerprint;
    private CancellationTokenSource? modelDiscoveryCancellation;

    private void RefreshOfficialAndDiscoveryControls(bool editable)
    {
        OfficialLoginPanel.Visibility = connectionDraft.Mode == ConnectionMode.OfficialWithApi ? Visibility.Visible : Visibility.Collapsed;
        ReadOfficialLoginButton.IsEnabled = editable;
        RestoreOfficialLoginButton.IsEnabled = editable && !connectionDraft.HasCurrentOfficialLogin;
        var storage = connectionDraft.Snapshot is { } snapshot ? new TomlDocument(snapshot.Config).String("cli_auth_credentials_store") : null;
        OfficialLoginStatus.Text = readingOfficialLogin ? "正在读取当前目录的官方登录…" : officialLoginStatus.Length > 0 ? officialLoginStatus :
            connectionDraft.HasStagedOfficialLogin ? "官方登录已暂存；确认保存后使用。切换到直连不会采用这份暂存登录。" :
            connectionDraft.HasCurrentOfficialLogin ? "已读取当前文件登录；有效期和刷新由 Codex 管理，未在线验证。" :
            storage is not (null or "file") ? "当前使用 " + storage + " 凭据存储。请在 Codex 使用文件凭据登录后重新读取。" : "尚未检测到可用的官方文件登录。";
        DiscoverModelsButton.IsEnabled = editable && !string.IsNullOrWhiteSpace(connectionDraft.BaseUrl) &&
            !string.IsNullOrWhiteSpace(connectionDraft.ApiKey) && (connectionDraft.HttpRequiredOrigin is null || connectionDraft.HttpAuthorized);
        DiscoverModelsButton.Content = discoveringModels ? "正在读取…" : discoveryFailed ? "重试读取" : "读取可用模型";
        CheckServiceKeyButton.Visibility = discoveryKeyRejected ? Visibility.Visible : Visibility.Collapsed;
        CheckServiceKeyButton.IsEnabled = editable;
        CancelDiscoveryButton.Visibility = discoveringModels ? Visibility.Visible : Visibility.Collapsed;
        CancelDiscoveryButton.IsEnabled = discoveringModels;
        string? fingerprint;
        try { fingerprint = (connectionDraft.Settings() with { Model = "model-catalog" }).Fingerprint(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { fingerprint = null; }
        if (discoveredFingerprint is not null && discoveredFingerprint != fingerprint)
        { ResetModelDiscovery(); discoveryStatus = "连接设置已更改，请重新读取模型。"; }
        if (DiscoveredModelsInput.ItemsSource != discoveredModels) DiscoveredModelsInput.ItemsSource = discoveredModels;
        DiscoveredModelsInput.Visibility = discoveredModels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DiscoveredModelsInput.IsEnabled = editable;
        DiscoveredModelsInput.SelectedItem = discoveredModels.FirstOrDefault(model => model == connectionDraft.Model);
        ModelDiscoveryStatus.Text = discoveringModels ? "正在读取 " + ServiceOrigin(connectionDraft.BaseUrl) + " 的模型目录…" : discoveryStatus.Length > 0 ? discoveryStatus :
            connectionDraft.BaseUrl.Length > 0 ? "从 " + ServiceOrigin(connectionDraft.BaseUrl) + " 读取模型。" : "";
    }
    private void ResetModelDiscovery()
    { discoveredModels = []; discoveredFingerprint = null; discoveryStatus = ""; discoveryFailed = discoveryKeyRejected = false; }
    private async void ReadOfficialLoginClicked(object sender, RoutedEventArgs args) => await ReadOfficialLoginAsync(false);
    private async void RestoreOfficialLoginClicked(object sender, RoutedEventArgs args) => await ReadOfficialLoginAsync(true);
    private async void RereadNativeLoginClicked(object sender, RoutedEventArgs args) => await ReadOfficialLoginAsync(backup: false, native: true);
    private async Task ReadOfficialLoginAsync(bool backup, bool native = false)
    {
        if (!CanEditConnection || (native ? !ShowingNativeLogin : connectionDraft.Mode != ConnectionMode.OfficialWithApi) || connectionDraft.Snapshot is not { } before) return;
        setupNativeStatus = native ? "正在读取此配置目录…" : setupNativeStatus;
        readingOfficialLogin = true; officialLoginStatus = ""; shellError = ""; RefreshView();
        var revision = connectionDraft.Revision;
        bool IsCurrent() => !closing && viewModel.Home == before.Home && connectionDraft.Revision == revision && ReferenceEquals(connectionDraft.Snapshot, before) &&
            (native ? ShowingNativeLogin : connectionDraft.Mode == ConnectionMode.OfficialWithApi);
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            // On the first native launch Codex may also write its defaults. With
            // a Dock draft open, require the existing conflict-safe login refresh.
            using var read = native && !connectionDraft.HasDraft ? null : await officialLoginReader.ReadAsync(before, backup, IsCurrent, lifetime.Token);
            var latest = read?.Snapshot ?? await Task.Run(() => store.Load(before.Home), lifetime.Token);
            if (!IsCurrent()) throw new OperationCanceledException();
            if (native && (store.HasPending(before.Home) || !OfficialCredentials.HasLogin(latest.Auth) || read?.HasStagedAuthentication == true ||
                new TomlDocument(latest.Config).String("cli_auth_credentials_store") is not (null or "file")))
                throw new InvalidOperationException("此目录尚未读到 ChatGPT 文件登录。请在使用本目录的 Codex 完成登录后重试；系统凭据由 Codex 管理。");
            loadingConnection = true;
            try { if (read is null) connectionDraft.Load(latest); else connectionDraft.AdoptOfficialLogin(read); }
            finally { loadingConnection = false; }
            viewModel.AdoptConfigurationRead(latest, connectionDraft.HasDraft);
            InvalidateSavedPreview(); InvalidateSource(); ResetModelDiscovery();
            if (native) setupNativeStatus = "已读到登录，可以在 Codex 中继续使用。";
            else officialLoginStatus = read!.Message;
            configurationResult = null; lastApplication = null;
        }
        catch (OperationCanceledException) { if (native) setupNativeStatus = "读取已取消。"; else officialLoginStatus = "工作区或草稿已改变，未采用登录读取结果。"; }
        catch (Exception error)
        {
            if (native) setupNativeStatus = error is InvalidOperationException ? error.Message : "暂时无法读取登录。请核对所选配置目录与 Codex 中的登录状态。";
            else { officialLoginStatus = error.Message; SetShellError("官方登录读取未完成", error.Message); }
        }
        finally { readingOfficialLogin = false; if (!closing) RefreshView(); }
    }
    private async void DiscoverModelsClicked(object sender, RoutedEventArgs args) => await DiscoverModelsAsync();
    private void CancelDiscoveryClicked(object sender, RoutedEventArgs args) => modelDiscoveryCancellation?.Cancel();
    private void CheckServiceKeyClicked(object sender, RoutedEventArgs args)
    { ApiKeyInput.StartBringIntoView(); ApiKeyInput.Focus(FocusState.Keyboard); }
    private async Task DiscoverModelsAsync()
    {
        if (!CanEditConnection || connectionDraft.Snapshot is null) return;
        discoveringModels = true; discoveryFailed = discoveryKeyRejected = false; shellError = ""; RefreshView();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        modelDiscoveryCancellation = cancellation;
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            var home = viewModel.Home; var settings = connectionDraft.Settings();
            discoveredFingerprint = (settings with { Model = "model-catalog" }).Fingerprint();
            var result = await modelDiscovery.ReadAsync(home, settings, () => (viewModel.Home, connectionDraft.Settings()), cancellation.Token);
            if (closing) return;
            discoveredModels = result.Models.Where(model => model != "codex-auto-review").ToArray();
            var stagedCapabilities = connectionDraft.AdoptDiscoveredCatalog(result, settings);
            discoveryStatus = discoveredModels.Count > 0 ? "已读取 " + discoveredModels.Count + " 个模型，可在下方选择。" :
                "服务没有返回可用模型。可在更多选项中填写已知的模型 ID。";
            if (stagedCapabilities) discoveryStatus += " 原生能力已暂存；确认保存后同步固定审核目录。";
            else if (result.NativeCatalog is null && connectionDraft.Document().String("approvals_reviewer") == "auto_review")
                discoveryStatus += " 服务未提供完整原生能力；固定审核需导入原生能力目录。";
        }
        catch (OperationCanceledException error) { if (!closing) discoveryStatus = error.Message.Contains("连接设置", StringComparison.Ordinal) ? error.Message : "获取已取消，当前模型选择保持不变。"; }
        catch (Exception error)
        {
            if (closing) return;
            discoveryFailed = true;
            discoveryKeyRejected = error is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden };
            discoveryStatus = discoveryKeyRejected ? "服务未接受这把密钥。请检查密钥，再重试读取。" : error.Message + " 可重试读取，或在更多选项手动填写模型 ID。";
        }
        finally { modelDiscoveryCancellation = null; discoveringModels = false; if (!closing) RefreshView(); }
    }
    private void DiscoveredModelChanged(object sender, SelectionChangedEventArgs args)
    { if (CanEditConnection && DiscoveredModelsInput.SelectedItem is string model) connectionDraft.Model = model; }
}
