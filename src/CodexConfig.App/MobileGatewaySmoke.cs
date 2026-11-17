using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunMobileSmokeAsync(string output, List<string> checks)
    {
        smokeMobileConfirm = _ => false;
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
        var fixture = new MainWindow(store, home, output, environment) { allowSmokeGateway = true };
        fixture.Loaded -= fixture.WindowLoaded;
        var fixtureManager = fixture.MobileManager();
        try
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var config = await fixtureManager.SaveAsync("https://phone.example.com", "tester", "fake-tray-mobile-password", port);
            await fixtureManager.StartAsync();
            await fixture.RefreshGatewaySummaryAsync();
            Assert(fixture.MobileGatewaySummary.Content.ToString()!.Contains("运行中") && fixture.ModelGatewaySummary.Content.ToString()!.Contains("直连无需启动"), "Persistent header shows the running mobile gateway and the unused direct-connection model gateway separately");
            fixture.InitializeTray(); await fixture.tray!.RefreshGatewayActionsAsync();
            Assert(fixture.tray.StopMobileItem.Enabled && !fixture.tray.StopModelItem.Enabled, "Tray shows the running mobile gateway independently");
            await fixture.RefreshMobilePageAsync();
            fixture.ModelBox.Text = "unsaved-mobile-tray-model";
            fixture.mobileUrl.Text = "https://unsaved-mobile.example.com";
            fixture.mobilePassword.Password = "unsaved-mobile-password";
            fixture.tray.RestartMobileItem.PerformClick();
            for (var i = 0; i < 200 && fixture.tray.GatewayActionInProgress; i++) await Task.Delay(100);
            await fixture.tray.RefreshGatewayActionsAsync();
            Assert(!fixture.tray.GatewayActionInProgress && fixture.Dirty && fixture.ModelBox.Text == "unsaved-mobile-tray-model" &&
                fixture.mobileUrl.Text == "https://unsaved-mobile.example.com" && fixture.mobilePassword.Password == "unsaved-mobile-password" &&
                fixture.tray.StopMobileItem.Enabled && await fixtureManager.StatusAsync() is not null && fixtureManager.Load() == config && before.SameContents(store.Load(home)),
                "Tray restarts mobile gateway from saved settings while preserving form and model drafts");
            fixture.Show(); fixture.ShowPage("gateways"); await fixture.Settle();
            Assert(fixture.RestartMobileGatewayButton.IsVisible && !fixture.GlobalSaveBar.IsVisible, "Mobile gateway offers restart on the independent gateway page");
            fixture.Hide();
            checks.Add("Gateway page and tray restart the mobile worker without saving form or model drafts");
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

    private async Task RunSharedMobileSaveSmokeAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var directory = Path.Combine(output, "wpf-mobile-save-home"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "config.toml"), "model='unchanged-wpf-mobile'\n");
        var fixture = new MainWindow(store, directory, output, environment) { allowSmokeGateway = true };
        fixture.Loaded -= fixture.WindowLoaded; var manager = fixture.MobileManager(); var before = store.Load(directory);
        try
        {
            fixture.Show(); fixture.ShowPage("mobile"); await fixture.RefreshMobilePageAsync();
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            fixture.mobileUrl.Text = "https://wpf-phone.invalid"; fixture.mobileUsername.Text = "fixture"; fixture.mobilePassword.Password = "fake-wpf-phone-password"; fixture.mobilePort.Text = port.ToString();
            fixture.smokeMobileConfirm = review => { Check(!review.Description.Contains("fake-wpf-phone-password") && review.Description.Contains("wpf-phone.invalid"), "WPF shared phone confirmation hides the password and identifies the saved entry"); return false; };
            await fixture.RunMobileActionAsync(fixture.SaveAndStartMobileAsync);
            Check(manager.Load() is null && fixture.mobilePassword.Password.Length > 0, "Returning from WPF phone confirmation preserves draft and performs no write");
            fixture.smokeMobileConfirm = _ => true;
            fixture.ModelBox.Text = "separate-wpf-model-draft";
            await fixture.RunMobileActionAsync(fixture.SaveAndStartMobileAsync);
            Check(manager.Load() is not null && await manager.StatusAsync() is not null && fixture.mobilePassword.Password.Length == 0 &&
                fixture.ModelBox.Text == "separate-wpf-model-draft" && before.SameContents(store.Load(directory)), "WPF shared phone save encrypts settings starts its worker and keeps model drafts untouched");
            fixture.mobilePort.Text = (port == 65535 ? port - 1 : port + 1).ToString();
            fixture.smokeMobileConfirm = _ => { var path = manager.SettingsPath; var bytes = File.ReadAllBytes(path); File.WriteAllBytes(path, bytes.Concat(new byte[] { 0 }).ToArray()); return true; };
            await fixture.MobileActionAsync(fixture.SaveAndStartMobileAsync);
            Check((fixture.mobileStatus.Text.Contains("变化") || fixture.mobileStatus.Text.Contains("读取")) && fixture.mobilePassword.Password.Length == 0 && before.SameContents(store.Load(directory)), "WPF shared phone save refuses a setting that changed after confirmation");
        }
        finally
        {
            // The deliberate corrupt-settings check needs the original encrypted
            // record restored before the isolated worker can authenticate stop.
            if (File.Exists(manager.SettingsPath))
            {
                var bytes = File.ReadAllBytes(manager.SettingsPath);
                if (bytes.Length > 0 && bytes[^1] == 0) File.WriteAllBytes(manager.SettingsPath, bytes[..^1]);
            }
            await manager.StopAsync(); fixture.exitReady = true; fixture.Close();
        }
    }
}
