using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private TrayIcon? tray;
    private bool exitRequested, exitReady, smokeLifecycle;
    private bool stopGatewaysOnExit;
    private Action<VerifiedInstaller>? smokeInstallerLaunch;

    private void InitializeWindowLifecycle() => Closed += (_, _) => { tray?.Dispose(); tray = null; };

    private void InitializeTray()
    {
        try
        {
            tray = new TrayIcon(() => Dispatcher.Invoke(RestoreWindow), () => Dispatcher.Invoke(() => RequestExit()), () => Dispatcher.Invoke(() => RequestExit(true)),
                () => Dispatcher.InvokeAsync(ReadTrayGatewaysAsync).Task.Unwrap(),
                () => Dispatcher.InvokeAsync(() => RunMobileActionAsync(StopMobileGatewayAsync)).Task.Unwrap(),
                () => Dispatcher.InvokeAsync(StopModelGatewayAsync).Task.Unwrap(),
                () => Dispatcher.InvokeAsync(() => RunMobileActionAsync(RestartMobileGatewayAsync)).Task.Unwrap(),
                () => Dispatcher.InvokeAsync(RestartModelGatewayAsync).Task.Unwrap(), notifications: smokeOutput is null);
        }
        catch (Exception) { SetStatus("系统托盘暂不可用，关闭窗口将退出程序。", true); }
    }

    private async Task<TrayGatewayState> ReadTrayGatewaysAsync()
    {
        var expectedHome = home;
        var manager = MobileManager();
        async Task<bool?> MobileRunning()
        {
            try
            {
                if (await manager.StatusAsync(lifetime.Token) is not null) return true;
                return manager.Load() is { } config ? await UnresponsiveGatewayStateAsync(config.ControlPort, lifetime.Token) : false;
            }
            catch { return null; }
        }
        async Task<bool?> ModelRunning()
        {
            try
            {
                var saved = await Task.Run(() => store.Load(expectedHome), lifetime.Token);
                if (saved.Bridge is not { } bytes) return false;
                var connection = MixedConnection.Read(bytes);
                if (await BridgeProcess.StatusAsync(connection, lifetime.Token) is { ProtocolVersion: ModelBridge.ProtocolVersion }) return true;
                return await UnresponsiveGatewayStateAsync(connection.Port, lifetime.Token);
            }
            catch { return null; }
        }
        // Status checks never start a stopped worker or apply unsaved drafts.
        var mobile = MobileRunning(); var model = ModelRunning();
        await Task.WhenAll(mobile, model);
        return closing || expectedHome != home ? new(null, null) : new(mobileBusy ? null : mobile.Result, gatewayPolling ? null : model.Result);
    }

    internal void RestoreWindow()
    {
        if (closing) return;
        Show(); ShowInTaskbar = true; WindowState = WindowState.Normal; Activate();
    }
    private void HideToTrayClicked(object sender, RoutedEventArgs e)
    {
        if (tray is not null) Hide();
        else SetStatus("系统托盘暂不可用，请使用任务栏最小化。", true);
    }
    private void MinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void ExitClicked(object sender, RoutedEventArgs e) => RequestExit();
    private void StopAndExitClicked(object sender, RoutedEventArgs e) => RequestExit(true);
    private void RequestExit(bool stopGateways = false)
    {
        if (closing) return;
        if (mobileBusy || gatewayPolling || tray?.GatewayActionInProgress == true)
        { RestoreWindow(); SetStatus("网关正在处理操作，请完成后再退出。", true); return; }
        stopGatewaysOnExit = stopGateways;
        exitRequested = true; RestoreWindow(); Close();
    }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (desktopRestartBusy) { e.Cancel = true; RestoreWindow(); SetStatus("正在处理 Codex 重启，请等待操作完成。", true); return; }
        if (exitReady || smokeOutput is not null && !smokeLifecycle)
        {
            closing = true; lifetime.Cancel(); previewTimer.Stop(); localStateTimer.Stop();
            loginCancellation?.Cancel(); ClearStagedLogin();
            SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
            if (!busy) probe.Dispose();
            return;
        }
        e.Cancel = true;
        if (!exitRequested && tray is not null) { Hide(); return; }
        if (closing) return;
        if (!ResolveUnsaved()) { exitRequested = false; stopGatewaysOnExit = false; return; }
        closing = true; localStateTimer.Stop(); MainShell.IsEnabled = false;
        SetStatus("正在退出…");
        try
        {
            using var verifiedFile = pendingInstaller?.OpenVerified();
            if (stopGatewaysOnExit || pendingInstaller is not null)
            {
                SetStatus("正在停止手机和模型网关，并等待后台进程退出…");
                await StopSavedGatewaysForExitAsync();
            }
            if (pendingInstaller is not null)
            {
                if (smokeOutput is not null)
                {
                    if (smokeInstallerLaunch is null) throw new InvalidOperationException("隔离检查不能启动安装器。");
                    smokeInstallerLaunch(pendingInstaller);
                }
                else _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pendingInstaller.Path) { UseShellExecute = true })
                    ?? throw new IOException("无法启动更新安装器。");
            }
            exitReady = true;
            await Dispatcher.InvokeAsync(Close, DispatcherPriority.Background);
        }
        catch (Exception error)
        {
            pendingInstaller = null;
            stopGatewaysOnExit = false; closing = false; exitRequested = false; MainShell.IsEnabled = true;
            localStateTimer.Start(); RestoreWindow(); ShowError(error);
        }
    }

    private async Task StopSavedGatewaysForExitAsync()
    {
        var expectedHome = home;
        var saved = await Task.Run(() => store.Load(expectedHome));
        await MobileManager().StopAsync();
        if (saved.Bridge is { } bridge) await BridgeProcess.StopAsync(bridge);
        stoppedGatewayHomes.Add(expectedHome); gatewayStatus = null;
    }
}
