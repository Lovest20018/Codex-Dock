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

    private void InitializeWindowLifecycle() => Closed += (_, _) => { tray?.Dispose(); tray = null; };

    private void InitializeTray()
    {
        try
        {
            tray = new TrayIcon(() => Dispatcher.Invoke(RestoreWindow), () => Dispatcher.Invoke(RequestExit),
                () => Dispatcher.InvokeAsync(ReadTrayGatewaysAsync).Task.Unwrap(),
                () => Dispatcher.InvokeAsync(() => RunMobileActionAsync(StopMobileGatewayAsync)).Task.Unwrap(),
                () => Dispatcher.InvokeAsync(StopModelGatewayAsync).Task.Unwrap(), notifications: smokeOutput is null);
        }
        catch (Exception) { SetStatus("系统托盘暂不可用，关闭窗口将退出程序。", true); }
    }

    private async Task<TrayGatewayState> ReadTrayGatewaysAsync()
    {
        var expectedHome = home;
        var manager = MobileManager();
        async Task<bool?> MobileRunning()
        {
            try { return await manager.StatusAsync(lifetime.Token) is not null; }
            catch { return null; }
        }
        async Task<bool?> ModelRunning()
        {
            try
            {
                var saved = await Task.Run(() => store.Load(expectedHome), lifetime.Token);
                return saved.Bridge is { } bytes && await BridgeProcess.StatusAsync(MixedConnection.Read(bytes), lifetime.Token) is not null;
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
    private void RequestExit()
    {
        if (closing) return;
        exitRequested = true; RestoreWindow(); Close();
    }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
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
        if (!ResolveUnsaved()) { exitRequested = false; return; }
        closing = true; localStateTimer.Stop(); MainShell.IsEnabled = false;
        SetStatus("正在退出…");
        try
        {
            if (pendingInstaller is not null)
            {
                if (smokeOutput is not null) throw new InvalidOperationException("隔离检查不能启动安装器。");
                using var verifiedFile = pendingInstaller.OpenVerified();
                if (snapshot?.Bridge is { } bridge) await CodexConfig.Core.BridgeProcess.StopAsync(bridge);
                _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pendingInstaller.Path) { UseShellExecute = true })
                    ?? throw new IOException("无法启动更新安装器。");
            }
            exitReady = true;
            await Dispatcher.InvokeAsync(Close, DispatcherPriority.Background);
        }
        catch (Exception error)
        {
            pendingInstaller = null;
            closing = false; exitRequested = false; MainShell.IsEnabled = true;
            localStateTimer.Start(); RestoreWindow(); ShowError(error);
        }
    }
}
