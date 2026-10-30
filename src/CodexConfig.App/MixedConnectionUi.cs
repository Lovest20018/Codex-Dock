using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;
using Microsoft.Win32;

namespace CodexConfig.App;

public partial class MainWindow
{
    private byte[]? stagedOfficialAuth;
    private CancellationTokenSource? loginCancellation;
    private bool loginInProgress, gatewayPolling, allowSmokeGateway;
    private readonly HashSet<string> stoppedGatewayHomes = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset nextGatewayAttempt;
    private BridgeStatus? gatewayStatus;

    private void ClearStagedLogin()
    {
        if (stagedOfficialAuth is not null) CryptographicOperations.ZeroMemory(stagedOfficialAuth);
        stagedOfficialAuth = null;
    }

    private void RefreshMixedUi()
    {
        if (OfficialPanel is null || GatewayPanel is null) return;
        var mixed = ConnectionModeBox.SelectedIndex == (int)ConnectionMode.OfficialWithApi;
        OfficialPanel.Visibility = GatewayPanel.Visibility = mixed ? Visibility.Visible : Visibility.Collapsed;
        LoginButton.IsEnabled = !loginInProgress && !closing;
        CancelLoginButton.Visibility = loginInProgress ? Visibility.Visible : Visibility.Collapsed;
        if (!loginInProgress)
        {
            var storage = snapshot is null ? null : new TomlDocument(snapshot.Config).String("cli_auth_credentials_store");
            OfficialStatusText.Text = stagedOfficialAuth is not null ? "登录已暂存，点击保存配置后启用。"
                : storage is not (null or "file") ? $"当前使用 {storage} 凭据存储。需要通过下方登录明确选择文件凭据，或继续 PAT / API 直连。"
                : OfficialCredentials.HasLogin(snapshot?.Auth) ? "检测到文件中的官方登录凭据，尚未在线验证。刷新与有效期由 Codex 管理。"
                : "尚无可用于混合连接的官方文件凭据，请先登录。";
        }
        var saved = loadedSettings.Mode == ConnectionMode.OfficialWithApi && snapshot?.Bridge is not null;
        StartGatewayButton.IsEnabled = saved && !gatewayPolling && !closing;
        StopGatewayButton.IsEnabled = saved && !gatewayPolling && gatewayStatus is not null && !closing;
        if (!saved) GatewayStatusText.Text = "保存混合连接后启动。模型入口是否可用需要单独验证。";
    }

    private void ConnectionModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || OfficialPanel is null) return;
        if (ConnectionModeBox.SelectedIndex == 0 && !loginInProgress) ClearStagedLogin();
        RefreshMixedUi(); MarkDirty();
    }
    private void MixedOptionChanged(object sender, RoutedEventArgs e) => MarkDirty();

    private void ChooseCodexClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择原生 Codex 程序", Filter = "Codex 程序|codex.exe|应用程序|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) CodexPathBox.Text = dialog.FileName;
    }

    private async void LoginClicked(object sender, RoutedEventArgs e)
    {
        if (loginInProgress || closing) return;
        if (smokeOutput is not null) { ShowError(new InvalidOperationException("隔离检查不会打开真实账号登录。")); return; }
        var expectedHome = home;
        loginInProgress = true;
        loginCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        OfficialStatusText.Text = "正在等待原生 Codex 完成浏览器登录，可随时取消。";
        RefreshMixedUi(); UpdateActions();
        try
        {
            var executable = string.IsNullOrWhiteSpace(CodexPathBox.Text) ? CodexLoginClient.FindExecutable() : CodexPathBox.Text.Trim();
            if (executable is null) throw new InvalidOperationException("未找到原生 codex.exe，请展开程序位置并选择。");
            var auth = await new CodexLoginClient().LoginAsync(executable, Path.Combine(Path.GetTempPath(), "CodexDock-login"), loginCancellation.Token);
            if (closing || home != expectedHome || ConnectionModeBox.SelectedIndex == 0) { CryptographicOperations.ZeroMemory(auth); return; }
            ClearStagedLogin(); stagedOfficialAuth = auth; MarkDirty();
        }
        catch (OperationCanceledException) { SetStatus("登录已取消，已保存配置保持原值。"); }
        catch (Exception error) { ShowError(error); }
        finally
        {
            loginInProgress = false; loginCancellation.Dispose(); loginCancellation = null;
            if (!closing) { RefreshMixedUi(); UpdateActions(); UpdatePreview(); }
        }
    }
    private void CancelLoginClicked(object sender, RoutedEventArgs e) => loginCancellation?.Cancel();

    private async Task RefreshGatewayAsync(bool start = false)
    {
        if (gatewayPolling || closing || snapshot?.Bridge is not { } bytes || loadedSettings.Mode != ConnectionMode.OfficialWithApi || smokeOutput is not null && !allowSmokeGateway) return;
        var expectedHome = home; var expectedSnapshot = snapshot;
        gatewayPolling = true; RefreshMixedUi();
        try
        {
            var connection = MixedConnection.Read(bytes);
            var status = await BridgeProcess.StatusAsync(connection, lifetime.Token);
            if ((status is null || status.ProtocolVersion != ModelBridge.ProtocolVersion) && (start || !stoppedGatewayHomes.Contains(home) && DateTimeOffset.UtcNow >= nextGatewayAttempt))
            {
                nextGatewayAttempt = DateTimeOffset.UtcNow.AddSeconds(15);
                status = await BridgeProcess.EnsureAsync(home, bytes, cancellation: lifetime.Token);
            }
            if (closing || home != expectedHome || !ReferenceEquals(snapshot, expectedSnapshot)) return;
            gatewayStatus = status;
            GatewayStatusText.Text = status is null ? "网关已暂时停止，下次打开工具会重新启动；切换并保存直连可停用混合模式。"
                : $"运行中 · {connection.NativeBaseUrl}\n本次请求 {status.Requests} 次" +
                  (status.LastUpstreamStatus is { } code ? $" · 最近上游 HTTP {code}" : "") +
                  (status.LastRequestId is { } requestId ? $"\n请求 ID：{requestId}" : "") +
                  (status.LastUpstreamCode is { } upstreamCode ? $" · {upstreamCode}" : "") +
                  (status.LastError is { } error ? "\n" + error : "");
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception)
        {
            if (!closing && home == expectedHome) { gatewayStatus = null; GatewayStatusText.Text = "网关未能运行，请检查端口占用，或点击启动重试。"; }
        }
        finally { gatewayPolling = false; if (!closing) RefreshMixedUi(); }
    }
    private async void StartGatewayClicked(object sender, RoutedEventArgs e)
    {
        stoppedGatewayHomes.Remove(home); await RefreshGatewayAsync(start: true);
    }
    private async void RefreshGatewayClicked(object sender, RoutedEventArgs e) => await RefreshGatewayAsync();
    private async void StopGatewayClicked(object sender, RoutedEventArgs e)
    {
        if (snapshot?.Bridge is not { } bytes || gatewayPolling) return;
        stoppedGatewayHomes.Add(home); gatewayPolling = true; RefreshMixedUi();
        try { await BridgeProcess.StopAsync(bytes, lifetime.Token); gatewayStatus = null; }
        catch (Exception) { GatewayStatusText.Text = "网关尚未停止，请稍后重试。"; }
        finally { gatewayPolling = false; await RefreshGatewayAsync(); }
    }
}
