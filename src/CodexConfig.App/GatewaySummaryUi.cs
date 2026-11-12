using System.Windows;
using System.Windows.Controls;
using System.Net;
using System.Net.Sockets;

namespace CodexConfig.App;

public partial class MainWindow
{
    private bool gatewaySummaryPolling;
    private Task? gatewaySummaryTask;

    private static async Task<bool?> UnresponsiveGatewayStateAsync(int port, CancellationToken cancellation)
    {
        // Missing authenticated health is not proof of a stopped process.
        // A refused loopback connection confirms the port is closed; an open,
        // busy, unrelated or inaccessible listener leaves the result unknown.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var client = new TcpClient();
        try { await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token); return null; }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.ConnectionRefused) { return false; }
        catch { return null; }
    }

    private void ModelGatewaySummaryClicked(object sender, RoutedEventArgs e) { ShowPage("gateways"); GatewayPanel.BringIntoView(); }
    private void MobileGatewaySummaryClicked(object sender, RoutedEventArgs e) { ShowPage("gateways"); MobileGatewayPanel.BringIntoView(); }
    private async void GatewaySummaryRefreshClicked(object sender, RoutedEventArgs e) => await RefreshAllGatewaysAsync();

    private void ResetGatewaySummary()
    {
        PaintGatewaySummary(ModelGatewaySummary, "本机模型网关", null);
        PaintGatewaySummary(MobileGatewaySummary, "手机网关", null);
    }

    private static void PaintGatewaySummary(Button button, string label, bool? running, bool operating = false)
    {
        button.Content = label + " · " + (operating ? "操作中" : running is true ? "● 运行中" : running is false ? "○ 已停止" : "? 未确认");
        button.SetResourceReference(ForegroundProperty, running is true && !operating ? "Success" : running is false && !operating ? "Muted" : "Warning");
        button.SetResourceReference(BackgroundProperty, running is true && !operating ? "SuccessSoft" : "Surface");
        System.Windows.Automation.AutomationProperties.SetName(button, button.Content.ToString());
    }

    private Task RefreshGatewaySummaryAsync() => gatewaySummaryTask is { IsCompleted: false } running ? running
        : gatewaySummaryTask = RefreshGatewaySummaryCoreAsync();

    private async Task RefreshGatewaySummaryCoreAsync()
    {
        if (gatewaySummaryPolling || closing) return;
        gatewaySummaryPolling = true;
        var expectedHome = home;
        try
        {
            // Reuse the tray's authenticated, read-only probes. Never Ensure or apply drafts here.
            var state = await ReadTrayGatewaysAsync();
            if (closing || home != expectedHome) return;
            PaintGatewaySummary(ModelGatewaySummary, "本机模型网关", state.ModelRunning, gatewayPolling);
            if (loadedSettings.Mode == CodexConfig.Core.ConnectionMode.Direct && state.ModelRunning is not true && !gatewayPolling)
            {
                ModelGatewaySummary.Content = "本机模型网关 · 直连无需启动";
                System.Windows.Automation.AutomationProperties.SetName(ModelGatewaySummary, ModelGatewaySummary.Content.ToString());
                ModelGatewaySummary.SetResourceReference(ForegroundProperty, "Muted");
            }
            PaintGatewaySummary(MobileGatewaySummary, "手机网关", state.MobileRunning, mobileBusy);
            PaintGatewayManagement(state);
        }
        finally
        {
            gatewaySummaryPolling = false;
            if (!closing && home != expectedHome) await RefreshGatewaySummaryCoreAsync();
        }
    }
}
