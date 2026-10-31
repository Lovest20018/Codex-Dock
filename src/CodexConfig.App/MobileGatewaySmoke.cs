using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunMobileSmokeAsync(string output, List<string> checks)
    {
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var before = store.Load(home); var previousModel = ModelBox.Text;
        ModelBox.Text += "-mobile-draft";
        ShowPage("mobile"); await RefreshMobilePageAsync(); await Settle();
        Assert(MobileNav.IsChecked == true && MobilePage.IsVisible && !ConnectionPage.IsVisible && !GlobalSaveBar.IsVisible, "mobile page has independent save controls");
        var manager = MobileManager();
        Assert(manager.Load() is null && manager.StateDirectory.StartsWith(output, StringComparison.OrdinalIgnoreCase), "smoke mode uses isolated mobile state only");
        Assert(mobilePort.Text == "13090", "default mapped-access port remains editable");
        Assert(mobileActions.Any(button => Equals(button.Content, "管理 Codex 接入")), "local Codex enrollment manager is available");
        mobileUrl.Text = "https://phone.example.com"; mobileUsername.Text = "test-user"; mobilePassword.Password = "fake-mobile-ui-password"; mobilePort.Text = "13092";
        ShowPage("connection"); ShowPage("mobile"); await Settle();
        Assert(mobileUrl.Text == "https://phone.example.com" && mobilePort.Text == "13092" && mobilePassword.Password == "fake-mobile-ui-password" && ModelBox.Text.EndsWith("-mobile-draft"), "mobile and model drafts survive navigation independently");
        Capture(output, "mobile-connection.png");
        mobileUrl.Text = "http://phone.example.com";
        await MobileActionAsync(SaveAndStartMobileAsync);
        Assert(mobileStatus.Text.Contains("HTTPS") && manager.Load() is null && before.SameContents(store.Load(home)), "invalid mobile input cannot start a worker or modify Codex files");
        mobileUrl.Text = "https://phone.example.com";
        mobilePort.Text = "65536"; await MobileActionAsync(SaveAndStartMobileAsync);
        Assert(mobileStatus.Text.Contains("端口") && manager.Load() is null, "invalid port cannot start the service"); mobilePort.Text = "13090";
        var width = Width; var height = Height; Width = 960; Height = 640; await Settle();
        Assert(MobilePage.ScrollableHeight > 0 && mobileUrl.ActualWidth > 200, "mobile form remains scrollable at minimum window size");
        Capture(output, "mobile-compact.png");
        using var devices = JsonDocument.Parse("{\"sessions\":[{\"id\":\"fake-device-id\",\"userAgent\":\"BridgeMobile/2.1.7-Android\",\"ip\":\"192.0.2.7\",\"lastSeen\":1793404800}],\"autoBlocks\":[{\"ip\":\"192.0.2.8\"}]}");
        RenderMobileDevices(devices.RootElement); MobilePage.ScrollToBottom(); await Settle();
        Assert(mobileDevices.Children.Count == 2, "device revocation and login unblock controls are visible");
        Capture(output, "mobile-devices-compact.png"); ThemeManager.Apply(true); await Settle(); Capture(output, "mobile-devices-dark.png"); ThemeManager.Apply(false);
        Width = width; Height = height; MobilePage.ScrollToTop();
        mobilePassword.Clear(); mobileUrl.Clear(); mobileUsername.Text = "codex"; ModelBox.Text = previousModel;
        ClearMobileQr(); await RefreshMobilePageAsync();
        Assert(before.SameContents(store.Load(home)) && !File.Exists(manager.SettingsPath), "viewing mobile UI leaves native auth, config, and worker settings untouched");
        var fixture = new MainWindow(store, home, output, environment);
        fixture.Loaded -= fixture.WindowLoaded;
        var fixtureManager = fixture.MobileManager();
        try
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var config = await fixtureManager.SaveAsync("https://phone.example.com", "tester", "fake-tray-mobile-password", port);
            await fixtureManager.StartAsync();
            await fixture.RefreshGatewaySummaryAsync();
            Assert(fixture.MobileGatewaySummary.Content.ToString()!.Contains("运行中") && fixture.ModelGatewaySummary.Content.ToString()!.Contains("已停止"), "Persistent header shows mobile and model states separately");
            fixture.InitializeTray(); await fixture.tray!.RefreshGatewayActionsAsync();
            Assert(fixture.tray.StopMobileItem.Enabled && !fixture.tray.StopModelItem.Enabled, "Tray shows the running mobile gateway independently");
            fixture.ModelBox.Text = "unsaved-mobile-tray-model";
            fixture.tray.StopMobileItem.PerformClick();
            for (var i = 0; i < 150 && fixture.tray.GatewayActionInProgress; i++) await Task.Delay(100);
            Assert(!fixture.tray.GatewayActionInProgress && !fixture.IsVisible && fixture.Dirty && fixture.ModelBox.Text == "unsaved-mobile-tray-model" &&
                await fixtureManager.StatusAsync() is null && !fixture.tray.StopMobileItem.Enabled && fixtureManager.Load() == config && before.SameContents(store.Load(home)),
                "Tray stops the actual mobile worker while hidden and preserves drafts, credentials and configuration");
            await fixture.RefreshGatewaySummaryAsync();
            Assert(fixture.MobileGatewaySummary.Content.ToString()!.Contains("已停止"), "Header updates after stopping mobile worker");
            using var occupied = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, config.ControlPort); occupied.Start();
            await fixture.RefreshGatewaySummaryAsync();
            Assert(fixture.MobileGatewaySummary.Content.ToString()!.Contains("未确认") && await fixtureManager.StatusAsync() is null, "A listening port without authenticated health is unknown and refresh never starts a worker");
            occupied.Stop();
            checks.AddRange(["Tray reports mobile gateway independently", "Tray stops mobile worker while hidden without changing credentials or drafts", "Persistent header reports running, stopped and unknown gateways using read-only probes"]);
        }
        finally
        {
            await fixtureManager.StopAsync(); fixture.exitReady = true; fixture.Close();
        }
        ShowPage("connection"); await Settle();
        checks.AddRange(["Independent mobile navigation and save actions", "Mobile and model drafts survive navigation", "Mobile URL validation before process launch", "Mobile smoke isolates all device data", "Mobile minimum-size and dark layouts", "Device revocation and IP unblock controls", "Mobile UI leaves native login and model configuration unchanged", "Editable port 13090 without a proxy IP field; invalid port cannot start service", "Local Codex account and CLI enrollment entry"]);
    }
}
