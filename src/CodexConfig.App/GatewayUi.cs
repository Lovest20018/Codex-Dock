using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CodexConfig.Core;
using Microsoft.Win32;

namespace CodexConfig.App;

public partial class MainWindow
{
    private readonly GatewaySession gateways = new();
    private readonly DispatcherTimer gatewayTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private TrayIcon? tray;
    private bool gatewayOperating, gatewayPolling, gatewayRunning, exitRequested, exitReady, smokeLifecycle;

    private void InitializeGatewayUi()
    {
        gatewayTimer.Tick += async (_, _) => await PollGatewayAsync();
        Closed += (_, _) => { gatewayTimer.Stop(); tray?.Dispose(); tray = null; };
    }

    private void InitializeTray()
    {
        try
        {
            tray = new TrayIcon(
                () => Dispatcher.Invoke(RestoreWindow),
                () => Dispatcher.Invoke(() => BridgeStatusClicked(this, new())),
                () => Dispatcher.Invoke(() => StopBridgeClicked(this, new())),
                () => Dispatcher.Invoke(RequestExit));
            gatewayTimer.Start();
        }
        catch (Exception) { SetStatus("系统托盘暂不可用，关闭窗口将退出并停止网关。", true); }
    }

    private void ConfigureGatewayView()
    {
        gatewayRunning = false;
        GatewayUpdatedText.Text = "";
        if (snapshot?.Bridge is { } bytes)
        {
            var connection = MixedConnection.Read(bytes);
            GatewayAddressBox.Text = connection.LocalBaseUrl;
            GatewayUpstreamText.Text = connection.BaseUrl;
            SetGatewayState("待检查", "正在读取已保存网关的运行状态。");
        }
        else
        {
            GatewayAddressBox.Text = "尚未保存网关配置";
            GatewayUpstreamText.Text = "尚未配置";
            SetGatewayState(loadedSettings.Mode == ConnectionMode.Direct ? "不适用" : "未配置", "在连接与账号中保存“官方账号 + API”后，自动启动本地网关。");
        }
        RefreshProxyDisplay();
    }

    private void SetGatewayState(string state, string? detail = null, BridgeStatus? status = null)
    {
        gatewayRunning = status is not null;
        GatewayStateText.Text = state;
        GatewaySummaryText.Text = "网关 · " + state;
        if (detail is not null) BridgeStatusText.Text = detail;
        GatewayCountsText.Text = status is null ? "网关启动后显示本次运行的请求计数。"
            : $"本次运行 · 模型请求 {status.Requests} 次 · 加速参数 {status.FastRequests} 次 · 审核模型 {status.AutoReviewRequests} 次";
        if (state is "运行中" or "已停止") GatewayUpdatedText.Text = "最近检查：" + DateTime.Now.ToString("HH:mm:ss");
        UpdateGatewayActions();
        RefreshOverview();
    }

    private void UpdateGatewayActions()
    {
        var configured = snapshot?.Bridge is not null && !store.HasPending(home);
        var available = configured && !busy && !gatewayOperating && !closing;
        BridgeStatusButton.IsEnabled = available && !Dirty && !gatewayRunning;
        StopBridgeButton.IsEnabled = available && gatewayRunning;
        RefreshGatewayButton.IsEnabled = available;
        CopyGatewayButton.IsEnabled = configured;
        GatewayProxyBox.IsEnabled = !busy && !closing && (configured || ConnectionModeBox.SelectedIndex == (int)ConnectionMode.OfficialWithApi);
        tray?.Update(GatewayStateText.Text, BridgeStatusButton.IsEnabled, StopBridgeButton.IsEnabled);
    }

    private void GatewayClicked(object sender, RoutedEventArgs e) => ShowPage("gateway");
    private void CopyGatewayClicked(object sender, RoutedEventArgs e)
    {
        try { if (snapshot?.Bridge is not null) { Clipboard.SetText(GatewayAddressBox.Text); SetStatus("已复制本地网关地址。"); } }
        catch (Exception) { SetStatus("剪贴板暂不可用，可直接选择地址复制。", true); }
    }

    private void GatewayProxyChanged(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        RefreshProxyDisplay(); MarkDirty();
    }

    private void RefreshProxyDisplay()
    {
        try
        {
            var scheme = Uri.TryCreate(BaseUrlBox.Text, UriKind.Absolute, out var upstream) ? upstream.Scheme : "https";
            var detected = SystemHttpProxy.Detect(scheme);
            var active = loadedSettings.UseSystemProxy ? "当前已保存：使用本机代理。" : "当前已保存：直连模型服务。";
            GatewayProxyText.Text = active + (detected is null ? "未检测到固定 HTTP 代理。" : "检测到代理：" + detected.GetLeftPart(UriPartial.Authority));
            if (GatewayProxyBox.IsChecked != loadedSettings.UseSystemProxy) GatewayProxyText.Text += " 此项有未保存的修改。";
        }
        catch (Exception) { GatewayProxyText.Text = "无法读取本机代理设置，请检查 Windows 系统代理。"; }
    }

    private async void BridgeStatusClicked(object sender, RoutedEventArgs e)
    {
        if (BridgeStatusButton.IsEnabled) await RefreshBridgeAsync();
    }

    private async Task RefreshBridgeAsync()
    {
        if (gatewayOperating || closing || snapshot?.Bridge is not { } bytes || smokeOutput is not null || store.HasPending(home)) return;
        var expectedHome = home;
        gatewayOperating = true; SetGatewayState("启动中", "正在启动本地网关…");
        try
        {
            var status = await gateways.EnsureAsync(expectedHome, bytes);
            if (!closing && expectedHome == home && snapshot?.Bridge is { } current && current.SequenceEqual(bytes))
                SetGatewayState("运行中", "Codex 的模型请求通过此地址转发。关闭窗口后在系统托盘继续运行。", status);
        }
        catch (Exception error) { if (!closing && expectedHome == home) SetGatewayState("启动失败", error.Message); }
        finally { gatewayOperating = false; if (!closing) UpdateGatewayActions(); }
    }

    private async void StopBridgeClicked(object sender, RoutedEventArgs e)
    {
        if (!StopBridgeButton.IsEnabled || snapshot?.Bridge is not { } bytes || smokeOutput is not null) return;
        await StopGatewayAsync(bytes);
    }

    private async Task StopGatewayAsync(byte[] bytes)
    {
        var expectedHome = home;
        gatewayOperating = true; SetGatewayState("停止中", "正在停止本地网关…");
        bool SameConnection() => !closing && home == expectedHome && snapshot?.Bridge is { } current && current.SequenceEqual(bytes);
        try { await gateways.StopAsync(bytes); if (SameConnection()) SetGatewayState("已停止", "网关已停止。再次使用 Codex 前，请点击启动网关。"); }
        catch (Exception error) { if (SameConnection()) SetGatewayState("停止失败", error.Message); }
        finally { gatewayOperating = false; if (!closing) { UpdateGatewayActions(); await PollGatewayAsync(); } }
    }

    private async void RefreshGatewayClicked(object sender, RoutedEventArgs e) => await PollGatewayAsync();
    private async Task PollGatewayAsync()
    {
        if (gatewayPolling || gatewayOperating || closing || snapshot?.Bridge is not { } bytes) return;
        gatewayPolling = true;
        var expectedHome = home;
        try
        {
            var status = await BridgeProcess.StatusAsync(MixedConnection.Read(bytes));
            if (!closing && expectedHome == home && snapshot?.Bridge is { } current && current.SequenceEqual(bytes))
            {
                SetGatewayState(status is null ? "暂时无法确认" : "运行中", status is null ? "健康检查未成功。请刷新状态或尝试启动网关。" : "Codex 的模型请求通过此地址转发。关闭窗口后在系统托盘继续运行。", status);
                RefreshProxyDisplay();
            }
        }
        finally { gatewayPolling = false; }
    }

    internal void RestoreWindow()
    {
        if (closing) return;
        Show(); ShowInTaskbar = true; WindowState = WindowState.Normal; Activate();
    }
    private void HideToTrayClicked(object sender, RoutedEventArgs e)
    {
        if (tray is not null) Hide();
        else if (smokeOutput is null) SetStatus("系统托盘暂不可用，请使用任务栏最小化。", true);
    }
    private void MinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void ExitClicked(object sender, RoutedEventArgs e) => RequestExit();
    private void RequestExit()
    {
        if (closing) return;
        exitRequested = true; RestoreWindow(); Close();
    }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (exitReady || smokeOutput is not null && !smokeLifecycle)
        {
            closing = true; lifetime.Cancel(); previewTimer.Stop(); gatewayTimer.Stop();
            SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
            ClearPendingLogin(); if (!busy) probe.Dispose();
            return;
        }
        e.Cancel = true;
        if (!exitRequested && tray is not null) { Hide(); return; }
        if (closing) return;
        if (!ResolveUnsaved()) { exitRequested = false; return; }
        closing = true; gatewayTimer.Stop(); MainShell.IsEnabled = false;
        SetStatus("正在停止网关并退出…"); UpdateGatewayActions();
        try
        {
            await gateways.StopAllAsync();
            if (pendingInstaller is not null)
            {
                if (smokeOutput is not null) throw new InvalidOperationException("隔离检查不能启动安装器。");
                using var verifiedFile = pendingInstaller.OpenVerified();
                _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pendingInstaller.Path) { UseShellExecute = true })
                    ?? throw new IOException("无法启动更新安装器。");
            }
            exitReady = true; await Dispatcher.InvokeAsync(Close, DispatcherPriority.Background);
        }
        catch (Exception error)
        {
            pendingInstaller = null; await gateways.ResumeAsync();
            closing = false; exitRequested = false; MainShell.IsEnabled = true;
            gatewayTimer.Start(); RestoreWindow(); ShowError(error); UpdateGatewayActions();
        }
    }

    private async Task RunGatewaySmokeAsync(string output, List<string> checks)
    {
        if (snapshot?.Bridge is not { } bytes) throw new InvalidOperationException("Missing isolated gateway fixture.");
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var connection = MixedConnection.Read(bytes);
        ShowPage("gateway");
        Assert(GatewayPage.Visibility == Visibility.Visible && GatewayAddressBox.Text == connection.LocalBaseUrl && CopyGatewayButton.IsEnabled, "Gateway page address");
        Assert(ShowInTaskbar && ResizeMode == ResizeMode.CanResize, "Native taskbar minimize available");
        GatewayProxyBox.IsChecked = true;
        Assert(Dirty && SaveCurrent() && store.ReadSettings(store.Load(home)).UseSystemProxy, "Gateway proxy setting save");
        GatewayProxyBox.IsChecked = false;
        Assert(SaveCurrent() && !store.ReadSettings(store.Load(home)).UseSystemProxy, "Gateway proxy setting disable");
        bytes = snapshot!.Bridge!;
        var status = await gateways.EnsureAsync(home, bytes);
        SetGatewayState("运行中", "Codex 的模型请求通过此地址转发。关闭窗口后在系统托盘继续运行。", status);
        ShowPage("overview"); await Settle();
        var overviewAction = FindVisual<System.Windows.Controls.Button>(OverviewBody).First(); overviewAction.Focus();
        SetGatewayState("运行中", status: status with { Requests = status.Requests + 1 });
        Assert(ReferenceEquals(overviewAction, FindVisual<System.Windows.Controls.Button>(OverviewBody).First()) && overviewAction.IsKeyboardFocused, "Gateway polling preserves overview controls and keyboard focus");
        checks.Add("Gateway counter updates preserve overview controls and keyboard focus");
        ShowPage("gateway");
        await Settle(); Capture(output, "gateway.png");
        Width = 960; Height = 640; await Settle(); Capture(output, "gateway-small.png");
        Width = 1160; Height = 800;

        var originalHome = home;
        try
        {
            var directoryStop = StopGatewayAsync(bytes);
            home = System.IO.Path.Combine(originalHome, "stop-directory-race"); Reload();
            var expectedState = GatewayStateText.Text;
            await directoryStop;
            Assert(GatewayStateText.Text == expectedState && !gatewayRunning, "Stopping old gateway must not update the newly selected directory");
            checks.Add("In-flight gateway stop cannot overwrite another directory's status");
        }
        finally { home = originalHome; Reload(); }

        // Exercise the same close and exit handlers in another isolated window.
        var fixture = new MainWindow(store, home, output, environment) { smokeLifecycle = true };
        fixture.Loaded -= fixture.WindowLoaded;
        fixture.Show(); fixture.InitializeTray(); fixture.Reload();
        Assert(fixture.tray is not null, "System tray icon created");
        await fixture.gateways.EnsureAsync(home, bytes);
        fixture.GatewayProxyBox.IsChecked = true;
        fixture.Close();
        Assert(!fixture.IsVisible && fixture.Dirty && await BridgeProcess.StatusAsync(connection) is not null, "Close hides to tray, preserves draft and gateway");
        fixture.RestoreWindow();
        Assert(fixture.IsVisible && fixture.ShowInTaskbar, "Tray restores window and taskbar");
        fixture.MinimizeClicked(this, new());
        Assert(fixture.WindowState == WindowState.Minimized && fixture.ShowInTaskbar, "Minimize keeps taskbar entry");
        fixture.RestoreWindow(); fixture.Reload();
        fixture.RequestExit();
        for (var attempt = 0; attempt < 100 && (!fixture.exitReady || fixture.tray is not null); attempt++) await Task.Delay(100);
        Assert(fixture.exitReady && fixture.tray is null && await BridgeProcess.StatusAsync(connection) is null, "Tray exit stops gateway and disposes icon");
        await gateways.StopAllAsync();

        var racing = new GatewaySession();
        var starting = racing.EnsureAsync(home, bytes);
        var stopping = racing.StopAllAsync();
        await Task.WhenAll(starting, stopping);
        Assert(await BridgeProcess.StatusAsync(connection) is null, "Exit waits for concurrent gateway startup");
        checks.AddRange(["Gateway address, status and small window layout", "Proxy preference save and disable", "Close hides to tray and preserves draft", "Tray restore and taskbar minimize", "Tray exit stops gateway and removes icon", "Gateway startup/exit race leaves no listener"]);
        await PollGatewayAsync();
    }
}
