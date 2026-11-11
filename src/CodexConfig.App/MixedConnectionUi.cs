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
    private Func<string?> findCodexExecutable = CodexLoginClient.FindExecutable;

    private void ClearStagedLogin()
    {
        if (stagedOfficialAuth is not null) CryptographicOperations.ZeroMemory(stagedOfficialAuth);
        stagedOfficialAuth = null;
    }

    private void RefreshMixedUi()
    {
        if (OfficialPanel is null || GatewayPanel is null) return;
        var mixed = ConnectionModeBox.SelectedIndex == (int)ConnectionMode.OfficialWithApi;
        BackgroundModeHint.Text = mixed
            ? "混合连接需要本地模型网关运行；退出 Dock 后网关继续服务。手机访问另需手机网关，可在“手机使用”中启停。"
            : "直连保存后由 Codex 读取配置。手机访问另需手机网关，可在“手机使用”中启停；退出 Dock 后已启动的手机网关继续运行。";
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
        var authorized = HttpConnectionPolicy.IsAuthorized(loadedSettings.BaseUrl, loadedSettings.Details.HttpOrigin);
        StartGatewayButton.IsEnabled = saved && authorized && !gatewayPolling && !closing;
        RestartGatewayButton.IsEnabled = saved && authorized && !gatewayPolling && !closing;
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

    private async Task<string> ResolveCodexExecutableAsync()
    {
        var executable = CodexPathBox.Text.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(executable)) executable = await Task.Run(findCodexExecutable);
        if (executable is null || !File.Exists(executable))
        {
            CodexProgramExpander.IsExpanded = true;
            CodexProgramExpander.BringIntoView(); CodexPathBox.Focus();
            throw new InvalidOperationException("未找到可用的原生 codex.exe。已展开程序位置，请选择本机 Codex CLI；支持桌面版运行时、独立安装和 npm 安装。");
        }
        CodexPathBox.Text = executable;
        return executable;
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
            var executable = await ResolveCodexExecutableAsync();
            if (closing || home != expectedHome || ConnectionModeBox.SelectedIndex == 0) return;
            loginCancellation.Token.ThrowIfCancellationRequested();
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
            if (!HttpConnectionPolicy.IsAuthorized(connection.BaseUrl, connection.HttpOrigin))
            {
                await BridgeProcess.StopAsync(bytes, lifetime.Token);
                if (!closing && home == expectedHome && ReferenceEquals(snapshot, expectedSnapshot))
                { gatewayStatus = null; GatewayStatusText.Text = "HTTP 站点未确认或确认已撤销，模型网关已停止。重新确认并保存后可启动。"; }
                return;
            }
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
                  BridgeMetricsText(status) +
                  (status.LastError is { } error ? "\n" + error : "");
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception)
        {
            if (!closing && home == expectedHome) { gatewayStatus = null; GatewayStatusText.Text = "网关未能运行，请检查端口占用，或点击启动重试。"; }
        }
        finally { gatewayPolling = false; if (!closing) { RefreshMixedUi(); await RefreshGatewaySummaryAsync(); } }
    }
    private string BridgeMetricsText(BridgeStatus status)
    {
        var document = new TomlDocument(snapshot?.Config);
        var provider = document.String("model_provider") ?? "openai";
        var protocol = document.Get(TomlDocument.FormatPath(["model_providers", provider, "capabilities", "remote_compaction"])) as string;
        var configured = protocol switch { "v2" => "文件允许 v2，服务支持需核对", "unsupported" => "文件选择普通摘要压缩", _ => "跟随执行端默认，能力未单独核实" };
        if (status.Metrics is not { } m) return "\n压缩配置：" + configured + "\n此网关版本未返回压缩指标。";
        string Ms(double? value) => value is { } ms ? $"{ms:N0} ms" : "未测得";
        return $"\n压缩配置：{configured}\n客户端压缩请求：旧端点 {m.LegacyCompactionRequests} 次 · v2 {m.V2Requests} 次（trigger {m.V2Triggers} 个）" +
            $"\n服务返回压缩条目 {m.CompactionItems} 个 · 确认完成 {m.SuccessfulCompactions} 次" +
            (m.LastCompactionAt is { } time ? $" · 最近 {time.LocalDateTime:g}（{m.LastCompactionProtocol} / {m.LastCompactionTransport}）" : " · 尚无成功完成记录") +
            (m.InspectionLimited > 0 ? $"\n{m.InspectionLimited} 段数据无法完整检查，计数可能不全。" : "") +
            $"\n连接：排队 {m.Waiting} · 活跃 {m.Active}\n最近一次：队列 {Ms(m.LastQueueMs)} · 上游响应头 {Ms(m.LastHeadersMs)} · 首数据 {Ms(m.LastFirstDataMs)} · 总计 {Ms(m.LastDurationMs)}" +
            (m.LastStreamEnd is { } end ? " · 结束：" + (end switch { "completed" => "完整转发", "upstream_error" => "上游错误", "client_disconnected" => "客户端断开", "timeout" => "等待超时", "peer_closed" => "连接关闭", "handshake_failed" => "握手失败", _ => "传输异常" }) : "") +
            "\n仅统计经本地模型网关转发的本次进程流量；不保存聊天或压缩正文。";
    }
    private async void StartGatewayClicked(object sender, RoutedEventArgs e)
    {
        stoppedGatewayHomes.Remove(home); await RefreshGatewayAsync(start: true);
    }
    private async void RefreshGatewayClicked(object sender, RoutedEventArgs e) => await RefreshGatewayAsync();
    private async void RestartGatewayClicked(object sender, RoutedEventArgs e)
    {
        try { await RestartModelGatewayAsync(); }
        catch (Exception error) { GatewayStatusText.Text = error is IOException or InvalidOperationException ? error.Message : "模型网关重启未完成，请刷新状态后重试。"; }
    }
    private async void StopGatewayClicked(object sender, RoutedEventArgs e)
    {
        try { await StopModelGatewayAsync(); }
        catch (Exception) { GatewayStatusText.Text = "网关尚未停止，请稍后重试。"; }
    }

    private async Task StopModelGatewayAsync()
    {
        if (gatewayPolling || closing) throw new InvalidOperationException("模型网关正在处理操作，请稍后重试。");
        var expectedHome = home;
        stoppedGatewayHomes.Add(expectedHome); gatewayPolling = true; RefreshMixedUi();
        try
        {
            // Stop the saved connection, preserving any draft in the editor.
            var saved = await Task.Run(() => store.Load(expectedHome), lifetime.Token);
            if (saved.Bridge is { } bytes) await BridgeProcess.StopAsync(bytes, lifetime.Token);
            if (home == expectedHome) gatewayStatus = null;
        }
        finally
        {
            gatewayPolling = false;
            if (!closing && home == expectedHome) await RefreshGatewayAsync();
        }
    }

    private async Task RestartModelGatewayAsync()
    {
        if (gatewayPolling || closing) throw new InvalidOperationException("模型网关正在处理操作，请稍后重试。");
        var expectedHome = home;
        gatewayPolling = true; stoppedGatewayHomes.Add(expectedHome); RefreshMixedUi();
        GatewayStatusText.Text = "正在停止旧进程并重启模型网关…";
        try
        {
            var saved = await Task.Run(() => store.Load(expectedHome), lifetime.Token);
            if (saved.Bridge is not { } bytes) throw new InvalidOperationException("请先保存混合连接，再重启模型网关。");
            var status = await BridgeProcess.RestartAsync(expectedHome, bytes, cancellation: lifetime.Token);
            if (home == expectedHome) { stoppedGatewayHomes.Remove(expectedHome); gatewayStatus = status; }
        }
        finally
        {
            gatewayPolling = false;
            if (!closing && home == expectedHome) await RefreshGatewayAsync();
        }
    }
}
