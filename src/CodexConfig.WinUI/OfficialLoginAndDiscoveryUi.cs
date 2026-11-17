using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private bool readingOfficialLogin, discoveringModels;
    private string officialLoginStatus = "", discoveryStatus = "";
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
        DiscoverModelsButton.IsEnabled = editable;
        DiscoverModelsButton.Content = discoveringModels ? "正在获取…" : "获取服务模型";
        CancelDiscoveryButton.Visibility = discoveringModels ? Visibility.Visible : Visibility.Collapsed;
        CancelDiscoveryButton.IsEnabled = discoveringModels;
        string? fingerprint;
        try { fingerprint = (connectionDraft.Settings() with { Model = "model-catalog" }).Fingerprint(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { fingerprint = null; }
        if (discoveredFingerprint is not null && discoveredFingerprint != fingerprint)
        { discoveredModels = []; discoveredFingerprint = null; discoveryStatus = "连接设置已更改，请重新获取模型。"; }
        if (DiscoveredModelsInput.ItemsSource != discoveredModels) DiscoveredModelsInput.ItemsSource = discoveredModels;
        DiscoveredModelsInput.Visibility = discoveredModels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DiscoveredModelsInput.IsEnabled = editable;
        DiscoveredModelsInput.SelectedItem = discoveredModels.FirstOrDefault(model => model == connectionDraft.Model);
        ModelDiscoveryStatus.Text = discoveringModels ? "正在读取当前服务的模型目录…" : discoveryStatus.Length > 0 ? discoveryStatus :
            "只向当前服务读取模型目录；不会发送对话或自动更改模型。";
    }
    private void ResetModelDiscovery()
    { discoveredModels = []; discoveredFingerprint = null; discoveryStatus = ""; }
    private async void ReadOfficialLoginClicked(object sender, RoutedEventArgs args) => await ReadOfficialLoginAsync(false);
    private async void RestoreOfficialLoginClicked(object sender, RoutedEventArgs args) => await ReadOfficialLoginAsync(true);
    private async Task ReadOfficialLoginAsync(bool backup)
    {
        if (!CanEditConnection || connectionDraft.Mode != ConnectionMode.OfficialWithApi || connectionDraft.Snapshot is not { } before) return;
        readingOfficialLogin = true; officialLoginStatus = ""; shellError = ""; RefreshView();
        var revision = connectionDraft.Revision;
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            using var read = await officialLoginReader.ReadAsync(before, backup, () => !closing && viewModel.Home == before.Home &&
                connectionDraft.Revision == revision && ReferenceEquals(connectionDraft.Snapshot, before) &&
                connectionDraft.Mode == ConnectionMode.OfficialWithApi, lifetime.Token);
            loadingConnection = true;
            try { connectionDraft.AdoptOfficialLogin(read); }
            finally { loadingConnection = false; }
            viewModel.AdoptConfigurationRead(read.Snapshot, connectionDraft.HasDraft);
            InvalidateSavedPreview();
            officialLoginStatus = read.Message; configurationResult = null; lastApplication = null;
        }
        catch (OperationCanceledException) { officialLoginStatus = "工作区或草稿已改变，未采用登录读取结果。"; }
        catch (Exception error) { officialLoginStatus = error.Message; SetShellError("官方登录读取未完成", error.Message); }
        finally { readingOfficialLogin = false; if (!closing) RefreshView(); }
    }
    private async void DiscoverModelsClicked(object sender, RoutedEventArgs args) => await DiscoverModelsAsync();
    private void CancelDiscoveryClicked(object sender, RoutedEventArgs args) => modelDiscoveryCancellation?.Cancel();
    private async Task DiscoverModelsAsync()
    {
        if (!CanEditConnection || connectionDraft.Snapshot is null) return;
        discoveringModels = true; shellError = ""; RefreshView();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        modelDiscoveryCancellation = cancellation;
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            var home = viewModel.Home; var settings = connectionDraft.Settings();
            var result = await modelDiscovery.ReadAsync(home, settings, () => (viewModel.Home, connectionDraft.Settings()), cancellation.Token);
            if (closing) return;
            discoveredModels = result.Models; discoveredFingerprint = (settings with { Model = "model-catalog" }).Fingerprint();
            discoveryStatus = result.Models.Count > 0 ? "已获取 " + result.Models.Count + " 个模型 · " + result.Milliseconds + " ms，当前选择保持不变。" :
                "服务返回空模型目录，可继续手填模型 ID；当前选择保持不变。";
        }
        catch (OperationCanceledException error) { if (!closing) discoveryStatus = error.Message.Contains("连接设置", StringComparison.Ordinal) ? error.Message : "获取已取消，当前模型选择保持不变。"; }
        catch (Exception error) { if (!closing) { discoveryStatus = error.Message; SetShellError("获取模型未完成", error.Message); } }
        finally { modelDiscoveryCancellation = null; discoveringModels = false; if (!closing) RefreshView(); }
    }
    private void DiscoveredModelChanged(object sender, SelectionChangedEventArgs args)
    { if (CanEditConnection && DiscoveredModelsInput.SelectedItem is string model) connectionDraft.Model = model; }
}
