using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private byte[]? stagedOfficialAuth;
    private bool officialLoginBusy, gatewayPolling, allowSmokeGateway;
    private bool migratingLegacyPat;
    private string? loginFeedback;
    private bool loginFeedbackIsError;
    private readonly HashSet<string> stoppedGatewayHomes = new(StringComparer.OrdinalIgnoreCase);
    private BridgeStatus? gatewayStatus;
    private const string DirectConnectionLabel = "API Key 直连";
    private const string MixedConnectionLabel = "官方账号 + 独立模型入口";
    private bool EditingMixedConnection => ConnectionModeBox.SelectedIndex == (int)ConnectionMode.OfficialWithApi;
    private byte[]? OfficialAuthForDraft => EditingMixedConnection ? stagedOfficialAuth : null;

    private void ConnectionModeLayoutChanged(object sender, SizeChangedEventArgs e)
    {
        if (FindVisual<UniformGrid>(ConnectionModeBox).FirstOrDefault() is { } options)
            options.Columns = e.NewSize.Width < 500 ? 1 : 2;
    }

    private string SavedConnectionLabel => loadedSettings.Mode == ConnectionMode.OfficialWithApi ? MixedConnectionLabel
        : OfficialCredentials.HasLogin(snapshot?.Auth) ? "官方账号（未启用独立模型入口）"
        : loadedSettings.NeedsApiKeyMigration ? "旧 PAT 直连（待迁移）"
        : loadedSettings.ApiKey.Length > 0 ? DirectConnectionLabel : "尚未配置 API Key 连接";

    private void RefreshConnectionModeHint()
    {
        SavedConnectionModeText.Text = snapshot is null ? "尚未读取配置" : "当前文件：" + SavedConnectionLabel;
        var changed = ConnectionModeBox.SelectedIndex != (int)loadedSettings.Mode;
        ConnectionModeHint.Text = changed
            ? "待切换为“" + (EditingMixedConnection ? MixedConnectionLabel : DirectConnectionLabel) + "”，确认写入后再重新打开 Codex。"
            : "选择只修改草稿；确认写入后，再重新打开 Codex 读取新连接。";
        if (!EditingMixedConnection && stagedOfficialAuth is not null)
            ConnectionModeHint.Text += " 本次官方登录已暂存，切回可继续使用。";
        else if (!EditingMixedConnection && OfficialCredentials.HasLogin(snapshot?.Auth))
            ConnectionModeHint.Text += " 写入直连时将改用服务凭据，原官方登录会纳入备份。";
    }

    private void ClearStagedLogin()
    {
        if (stagedOfficialAuth is not null) CryptographicOperations.ZeroMemory(stagedOfficialAuth);
        stagedOfficialAuth = null;
        loginFeedback = null; loginFeedbackIsError = false;
    }

    private void RefreshMixedUi()
    {
        if (OfficialPanel is null || GatewayPanel is null) return;
        var mixed = EditingMixedConnection;
        var saved = loadedSettings.Mode == ConnectionMode.OfficialWithApi && snapshot?.Bridge is not null;
        RefreshConnectionModeHint();
        BackgroundModeHint.Text = saved
            ? "混合连接需要本地模型网关运行；退出 Dock 后网关继续服务。手机访问另需手机网关，两个网关均在“网关管理”中启停。"
            : "直连保存后由 Codex 读取配置。手机访问另需手机网关，可在“网关管理”中启停；退出 Dock 后已启动的手机网关继续运行。";
        OfficialPanel.Visibility = mixed ? Visibility.Visible : Visibility.Collapsed;
        DirectFastCompatibilityHint.Visibility = mixed ? Visibility.Collapsed : Visibility.Visible;
        MixedGatewayOptions.Visibility = mixed ? Visibility.Visible : Visibility.Collapsed;
        LegacyPatPanel.Visibility = !mixed && loadedSettings.NeedsApiKeyMigration ? Visibility.Visible : Visibility.Collapsed;
        MigratePatButton.IsEnabled = !officialLoginBusy && !closing;
        UpdateCredentialHint();
        RefreshOfficialLoginButton.IsEnabled = !officialLoginBusy && !closing;
        RestoreOfficialLoginButton.IsEnabled = !officialLoginBusy && !closing && !OfficialCredentials.HasLogin(snapshot?.Auth);
        OfficialStatusText.SetResourceReference(TextBlock.ForegroundProperty, loginFeedbackIsError ? "Error" : "Muted");
        if (!officialLoginBusy)
        {
            var storage = snapshot is null ? null : new TomlDocument(snapshot.Config).String("cli_auth_credentials_store");
            OfficialStatusText.Text = loginFeedback ?? (OfficialCredentials.HasLogin(snapshot?.Auth) ? "使用当前文件中的官方登录；刷新与有效期由 Codex 管理。"
                : stagedOfficialAuth is not null ? "已备份的官方登录已暂存，确认保存后启用。"
                : storage is not (null or "file") ? $"当前使用 {storage} 凭据存储。请在 Codex 使用文件凭据登录并重新读取，或选择 API Key 直连。"
                : "尚无可用的官方文件凭据。请在 Codex 桌面登录后重新读取，或使用本目录已备份的官方登录。");
        }
        if (gatewayPolling || closing) StartGatewayButton.IsEnabled = RestartGatewayButton.IsEnabled = StopGatewayButton.IsEnabled = false;
        if (!saved) GatewayStatusText.Text = "请先保存“官方账号 + 独立模型入口”，再到此页启动。API Key 直连无需本机模型网关。";
    }

    private void MigratePatClicked(object sender, RoutedEventArgs e)
    {
        if (snapshot is null || !loadedSettings.NeedsApiKeyMigration || EditingMixedConnection || officialLoginBusy || closing) return;
        migratingLegacyPat = true; MarkDirty(); OpenConfigurationReview();
    }

    private void ConnectionModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || OfficialPanel is null) return;
        RefreshMixedUi(); MarkDirty();
    }
    private void MixedOptionChanged(object sender, RoutedEventArgs e) => MarkDirty();

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
                if (!closing && home == expectedHome && ReferenceEquals(snapshot, expectedSnapshot))
                { gatewayStatus = null; GatewayStatusText.Text = "HTTP 站点未确认或确认已撤销，无法启动转发。重新确认并保存后可启动。"; }
                if (start) HttpConnectionPolicy.RequireAuthorization(connection.BaseUrl, connection.HttpOrigin);
                return;
            }
            var status = await BridgeProcess.StatusAsync(connection, lifetime.Token);
            if ((status is null || status.ProtocolVersion != ModelBridge.ProtocolVersion) && start)
                status = await BridgeProcess.EnsureAsync(home, bytes, cancellation: lifetime.Token);
            if (closing || home != expectedHome || !ReferenceEquals(snapshot, expectedSnapshot)) return;
            gatewayStatus = status;
            GatewayStatusText.Text = status is null ? "网关已停止。点击启动可运行；需要随 Dock 启动时，请勾选自动启动。"
                : $"运行中 · {connection.NativeBaseUrl}\n本次请求 {status.Requests} 次" +
                  (status.ProtocolVersion >= 6 ? $" · Fast {Math.Max(0, status.FastRequests - status.UltrafastRequests)} · Ultra Fast {status.UltrafastRequests} · 审核 {status.AutoReviewRequests}"
                      : "\n旧网关未提供新的速度与审核分类；手动重启后可查看。") +
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
            if (start) throw;
        }
        finally { gatewayPolling = false; if (!closing) RefreshMixedUi(); }
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
        await ModelGatewayActionAsync(StartModelGatewayAsync);
    }
    private async Task StartModelGatewayAsync()
    {
        if (desktopRestartBusy) throw new InvalidOperationException("正在应用 Codex 配置，请等待完成后再操作模型网关。");
        if (gatewayPolling || closing) throw new InvalidOperationException("本机模型网关正在处理操作，请稍后重试。");
        var expectedHome = home;
        gatewayPolling = true; RefreshMixedUi();
        try
        {
            var saved = await Task.Run(() => store.Load(expectedHome), lifetime.Token);
            if (saved.Bridge is not { } bytes || store.ReadSettings(saved).Mode != ConnectionMode.OfficialWithApi)
                throw new InvalidOperationException("请先在“连接与账号”保存“官方账号 + 独立模型入口”。");
            var connection = MixedConnection.Read(bytes);
            HttpConnectionPolicy.RequireAuthorization(connection.BaseUrl, connection.HttpOrigin);
            var status = await BridgeProcess.EnsureAsync(expectedHome, bytes, cancellation: lifetime.Token);
            if (!closing && home == expectedHome) { stoppedGatewayHomes.Remove(expectedHome); gatewayStatus = status; }
        }
        finally { gatewayPolling = false; if (!closing) await RefreshAllGatewaysAsync(); }
    }
    private async void RestartGatewayClicked(object sender, RoutedEventArgs e)
    {
        await ModelGatewayActionAsync(RestartModelGatewayAsync);
    }
    private async void StopGatewayClicked(object sender, RoutedEventArgs e)
    {
        await ModelGatewayActionAsync(StopModelGatewayAsync);
    }

    private async Task StopModelGatewayAsync()
    {
        if (desktopRestartBusy) throw new InvalidOperationException("正在应用 Codex 配置，请等待完成后再操作模型网关。");
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
            if (!closing && home == expectedHome) await RefreshAllGatewaysAsync();
        }
    }

    private async Task RestartModelGatewayAsync()
    {
        if (desktopRestartBusy) throw new InvalidOperationException("正在应用 Codex 配置，请等待完成后再操作模型网关。");
        if (gatewayPolling || closing) throw new InvalidOperationException("模型网关正在处理操作，请稍后重试。");
        var expectedHome = home;
        gatewayPolling = true; stoppedGatewayHomes.Add(expectedHome); RefreshMixedUi();
        GatewayStatusText.Text = "正在停止旧进程并重启模型网关…";
        try
        {
            var saved = await Task.Run(() => store.Load(expectedHome), lifetime.Token);
            if (saved.Bridge is not { } bytes || store.ReadSettings(saved).Mode != ConnectionMode.OfficialWithApi)
                throw new InvalidOperationException("请先保存“官方账号 + 独立模型入口”，再重启本机模型网关。");
            var connection = MixedConnection.Read(bytes);
            HttpConnectionPolicy.RequireAuthorization(connection.BaseUrl, connection.HttpOrigin);
            var status = await BridgeProcess.RestartAsync(expectedHome, bytes, cancellation: lifetime.Token);
            if (home == expectedHome) { stoppedGatewayHomes.Remove(expectedHome); gatewayStatus = status; }
        }
        finally
        {
            gatewayPolling = false;
            if (!closing && home == expectedHome) await RefreshAllGatewaysAsync();
        }
    }
}
