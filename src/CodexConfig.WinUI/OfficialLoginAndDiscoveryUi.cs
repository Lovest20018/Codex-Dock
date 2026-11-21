using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Net;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private const int ModelListLimit = 20;
    private bool discoveringModels => busy.IsActive(WindowOperation.DiscoverModels);
    private bool readingOfficialLogin => busy.IsActive(WindowOperation.ReadOfficialLogin);
    private string officialLoginStatus = "", discoveryStatus = "";
    private bool discoveryFailed, discoveryKeyRejected;
    private IReadOnlyList<string> discoveredModels = [];
    private string? discoveredFingerprint;
    private CancellationTokenSource? modelDiscoveryCancellation;

    private void RefreshOfficialLoginControls(bool editable)
    {
        ConnectionPanel.OfficialLoginPanel.Visibility = connectionDraft.Mode == ConnectionMode.OfficialWithApi ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPanel.ReadOfficialLoginButton.IsEnabled = editable;
        ConnectionPanel.RestoreOfficialLoginButton.IsEnabled = editable && !connectionDraft.HasCurrentOfficialLogin;
        var storage = connectionDraft.Snapshot is { } snapshot ? new TomlDocument(snapshot.Config).String("cli_auth_credentials_store") : null;
        ConnectionPanel.OfficialLoginStatus.Text = readingOfficialLogin ? "正在读取当前目录的官方登录…" : officialLoginStatus.Length > 0 ? officialLoginStatus :
            connectionDraft.HasStagedOfficialLogin ? "官方登录已暂存；确认保存后使用。切换到直连不会采用这份暂存登录。" :
            connectionDraft.HasCurrentOfficialLogin ? "已读取当前文件登录；有效期和刷新由 Codex 管理，未在线验证。" :
            storage is not (null or "file") ? "当前使用 " + storage + " 凭据存储。请在 Codex 使用文件凭据登录后重新读取。" : "尚未检测到可用的官方文件登录。";
    }

    private bool CanReadModelCatalog => !string.IsNullOrWhiteSpace(connectionDraft.BaseUrl) &&
        !string.IsNullOrWhiteSpace(connectionDraft.ApiKey) && (connectionDraft.HttpRequiredOrigin is null || connectionDraft.HttpAuthorized);

    private static string[] FilterModelChoices(IEnumerable<string> choices, string filter)
    {
        var query = filter.Trim();
        return choices.Where(value => value.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(ModelListLimit + 1).ToArray();
    }

    private static string ModelFilterHint(int count) => count == 0 ? "没有匹配的模型。" :
        count > ModelListLimit ? "输入名称可查找其余模型。" : "";

    private void RefreshModelDiscoveryControls(bool editable)
    {
        if (discoveredFingerprint is not null)
        {
            string? fingerprint;
            try { fingerprint = (connectionDraft.Settings() with { Model = "model-catalog" }).Fingerprint(); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException) { fingerprint = null; }
            if (discoveredFingerprint != fingerprint)
            { ResetModelDiscovery(); discoveryStatus = "连接设置已更改，请重新读取模型。"; }
        }

        ModelsPanel.DiscoverModelsButton.IsEnabled = editable && CanReadModelCatalog;
        ModelsPanel.DiscoverModelsButton.Content = discoveringModels ? "正在读取…" : discoveryFailed ? "重试读取" : "读取可用模型";
        var needsConnection = !CanReadModelCatalog && (viewModel.NeedsConnectionSetup || connectionDraft.ConnectionChanged);
        ModelsPanel.CheckServiceKeyButton.Visibility = discoveryKeyRejected || needsConnection ? Visibility.Visible : Visibility.Collapsed;
        ModelsPanel.CheckServiceKeyButton.Content = discoveryKeyRejected ? "检查密钥" : "检查连接";
        ModelsPanel.CheckServiceKeyButton.IsEnabled = editable;
        ModelsPanel.ReviewModelConnectionButton.Visibility = !needsConnection && (viewModel.NeedsConnectionSetup || connectionDraft.ConnectionChanged)
            ? Visibility.Visible : Visibility.Collapsed;
        ModelsPanel.ReviewModelConnectionButton.IsEnabled = editable;
        ModelsPanel.CancelDiscoveryButton.Visibility = discoveringModels ? Visibility.Visible : Visibility.Collapsed;
        ModelsPanel.CancelDiscoveryButton.IsEnabled = discoveringModels;
        ModelsPanel.DiscoveredModelChoices.Visibility = discoveredModels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ModelsPanel.ModelFilterInput.IsEnabled = editable;
        if (discoveredModels.Count > 0)
        {
            var matches = FilterModelChoices(discoveredModels, ModelsPanel.ModelFilterInput.Text);
            var visible = matches.Take(ModelListLimit).ToArray();
            if (ModelsPanel.DiscoveredModelsInput.ItemsSource is not string[] rendered || !rendered.SequenceEqual(visible))
                ModelsPanel.DiscoveredModelsInput.ItemsSource = visible;
            ModelsPanel.DiscoveredModelsInput.IsEnabled = editable && visible.Length > 0;
            ModelsPanel.DiscoveredModelsInput.SelectedItem = visible.FirstOrDefault(model => model == connectionDraft.Model);
            ModelsPanel.ModelFilterStatus.Text = ModelFilterHint(matches.Length);
            ModelsPanel.ModelFilterStatus.Visibility = ModelsPanel.ModelFilterStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        ModelsPanel.ModelDiscoveryStatus.Text = discoveringModels ? "正在读取 " + ServiceOrigin(connectionDraft.BaseUrl) + " 的模型目录…" : discoveryStatus.Length > 0 ? discoveryStatus :
            CanReadModelCatalog ? "模型列表来自 " + ServiceOrigin(connectionDraft.BaseUrl) + "。" :
            needsConnection ? "填写服务地址和密钥后可读取模型，也可手动填写。" : "";
        ModelsPanel.ModelDiscoveryStatus.Visibility = ModelsPanel.ModelDiscoveryStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ResetModelDiscovery()
    { discoveredModels = []; discoveredFingerprint = null; discoveryStatus = ""; discoveryFailed = discoveryKeyRejected = false; }
    internal void ModelFilterChanged(TextBox sender, TextBoxTextChangingEventArgs args)
    { if (currentPage == ModelsNav && CanEditConnection) RefreshView(); }
    internal async void ReadOfficialLoginClicked(object sender, RoutedEventArgs args) => await ReadOfficialLoginAsync(false);
    internal async void RestoreOfficialLoginClicked(object sender, RoutedEventArgs args) => await ReadOfficialLoginAsync(true);
    internal async void RereadNativeLoginClicked(object sender, RoutedEventArgs args) => await ReadOfficialLoginAsync(backup: false, native: true);
    private async Task ReadOfficialLoginAsync(bool backup, bool native = false)
    {
        if (!CanEditConnection || (native ? !ShowingNativeLogin : connectionDraft.Mode != ConnectionMode.OfficialWithApi) || connectionDraft.Snapshot is not { } before) return;
        setupNativeStatus = native ? "正在读取此配置目录…" : setupNativeStatus;
        using var operation = busy.Begin(WindowOperation.ReadOfficialLogin, "正在读取官方登录…"); officialLoginStatus = ""; shellError = ""; RefreshView();
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
        finally { operation.Dispose(); if (!closing) RefreshView(); }
    }
    internal async void DiscoverModelsClicked(object sender, RoutedEventArgs args) => await DiscoverModelsAsync();
    internal void CancelDiscoveryClicked(object sender, RoutedEventArgs args) => modelDiscoveryCancellation?.Cancel();
    internal void CheckServiceKeyClicked(object sender, RoutedEventArgs args)
    {
        NavigateTo(ConnectionNav, revealStart: true);
        Control input = connectionDraft.BaseUrl.Length == 0 ? ConnectionPanel.BaseUrlInput :
            connectionDraft.HttpRequiredOrigin is not null && !connectionDraft.HttpAuthorized ? ConnectionPanel.HttpConsentInput : ConnectionPanel.ApiKeyInput;
        pageNavigationRevision++;
        Root.UpdateLayout();
        input.StartBringIntoView();
        input.Focus(FocusState.Keyboard);
    }
    private async Task DiscoverModelsAsync()
    {
        if (!CanEditConnection || !CanReadModelCatalog || currentPage != ModelsNav || connectionDraft.Snapshot is null) return;
        using var operation = busy.Begin(WindowOperation.DiscoverModels, "正在读取可用模型…"); discoveryFailed = discoveryKeyRejected = false; shellError = "";
        ModelsPanel.ModelFilterInput.Text = "";
        RefreshView();
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
                "服务没有返回可用模型。可手动填写模型 ID。";
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
            discoveryStatus = discoveryKeyRejected ? "服务未接受这把密钥。请检查密钥，再重试读取。" : error.Message + " 可重试读取，或手动填写模型 ID。";
        }
        finally { modelDiscoveryCancellation = null; operation.Dispose(); if (!closing) RefreshView(); }
    }
    internal void DiscoveredModelChanged(object sender, SelectionChangedEventArgs args)
    { if (CanEditConnection && ModelsPanel.DiscoveredModelsInput.SelectedItem is string model) connectionDraft.Model = model; }
}
