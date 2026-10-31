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
        Assert(mobilePort.Text == "13090" && mobileProxy.Text == "", "default port remains editable with loopback-only access");
        mobileUrl.Text = "https://phone.example.com"; mobileUsername.Text = "test-user"; mobilePassword.Password = "fake-mobile-ui-password"; mobileProxy.Text = "192.168.8.2";
        ShowPage("connection"); ShowPage("mobile"); await Settle();
        Assert(mobileUrl.Text == "https://phone.example.com" && mobileProxy.Text == "192.168.8.2" && mobilePassword.Password == "fake-mobile-ui-password" && ModelBox.Text.EndsWith("-mobile-draft"), "mobile and model drafts survive navigation independently");
        Capture(output, "mobile-connection.png");
        mobileUrl.Text = "http://phone.example.com";
        await MobileActionAsync(SaveAndStartMobileAsync);
        Assert(mobileStatus.Text.Contains("HTTPS") && manager.Load() is null && before.SameContents(store.Load(home)), "invalid mobile input cannot start a worker or modify Codex files");
        mobileUrl.Text = "https://phone.example.com";
        mobileProxy.Text = "0.0.0.0"; await MobileActionAsync(SaveAndStartMobileAsync);
        Assert(mobileStatus.Text.Contains("IPv4") && manager.Load() is null, "invalid proxy cannot enable LAN access"); mobileProxy.Text = "192.168.8.2";
        var width = Width; var height = Height; Width = 960; Height = 640; await Settle();
        Assert(MobilePage.ScrollableHeight > 0 && mobileUrl.ActualWidth > 200, "mobile form remains scrollable at minimum window size");
        Capture(output, "mobile-compact.png");
        using var devices = JsonDocument.Parse("{\"sessions\":[{\"id\":\"fake-device-id\",\"userAgent\":\"BridgeMobile/2.1.7-Android\",\"ip\":\"192.0.2.7\",\"lastSeen\":1793404800}],\"autoBlocks\":[{\"ip\":\"192.0.2.8\"}]}");
        RenderMobileDevices(devices.RootElement); MobilePage.ScrollToBottom(); await Settle();
        Assert(mobileDevices.Children.Count == 2, "device revocation and login unblock controls are visible");
        Capture(output, "mobile-devices-compact.png"); ThemeManager.Apply(true); await Settle(); Capture(output, "mobile-devices-dark.png"); ThemeManager.Apply(false);
        Width = width; Height = height; MobilePage.ScrollToTop();
        mobilePassword.Clear(); mobileUrl.Clear(); mobileProxy.Clear(); mobileUsername.Text = "codex"; ModelBox.Text = previousModel;
        ClearMobileQr(); await RefreshMobilePageAsync();
        Assert(before.SameContents(store.Load(home)) && !File.Exists(manager.SettingsPath), "viewing mobile UI leaves native auth, config, and worker settings untouched");
        ShowPage("connection"); await Settle();
        checks.AddRange(["Independent mobile navigation and save actions", "Mobile and model drafts survive navigation", "Mobile URL validation before process launch", "Mobile smoke isolates all device data", "Mobile minimum-size and dark layouts", "Device revocation and IP unblock controls", "Mobile UI leaves native login and model configuration unchanged", "Editable port 13090 and optional exact LAN proxy; invalid proxy cannot start service"]);
    }
}
