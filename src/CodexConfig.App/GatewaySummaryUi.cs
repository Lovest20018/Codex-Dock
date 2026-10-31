using System.Windows;
using System.Windows.Controls;
using System.Net;
using System.Net.Sockets;

namespace CodexConfig.App;

public partial class MainWindow
{
    private bool gatewaySummaryPolling;

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

    private void ModelGatewaySummaryClicked(object sender, RoutedEventArgs e) => ShowPage("connection");
    private void MobileGatewaySummaryClicked(object sender, RoutedEventArgs e) => ShowPage("mobile");
    private async void GatewaySummaryRefreshClicked(object sender, RoutedEventArgs e) => await RefreshGatewaySummaryAsync();

    private void ResetGatewaySummary()
    {
        PaintGatewaySummary(ModelGatewaySummary, "本地网关", null);
        PaintGatewaySummary(MobileGatewaySummary, "手机网关", null);
    }

    private static void PaintGatewaySummary(Button button, string label, bool? running, bool operating = false)
    {
        button.Content = label + " · " + (operating ? "操作中" : running is true ? "● 运行中" : running is false ? "○ 已停止" : "? 未确认");
        button.SetResourceReference(ForegroundProperty, running is true && !operating ? "Success" : running is false && !operating ? "Muted" : "Warning");
        button.SetResourceReference(BackgroundProperty, running is true && !operating ? "SuccessSoft" : "Surface");
        System.Windows.Automation.AutomationProperties.SetName(button, button.Content.ToString());
    }

    private async Task RefreshGatewaySummaryAsync()
    {
        if (gatewaySummaryPolling || closing) return;
        gatewaySummaryPolling = true;
        var expectedHome = home;
        try
        {
            // Reuse the tray's authenticated, read-only probes. Never Ensure or apply drafts here.
            var state = await ReadTrayGatewaysAsync();
            if (closing || home != expectedHome) return;
            PaintGatewaySummary(ModelGatewaySummary, "本地网关", state.ModelRunning, gatewayPolling);
            PaintGatewaySummary(MobileGatewaySummary, "手机网关", state.MobileRunning, mobileBusy);
        }
        finally { gatewaySummaryPolling = false; }
    }
}
