using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunGatewayManagementUiSmokeAsync(string output)
    {
        Directory.CreateDirectory(output); var checks = new List<string>();
        await RunGatewayManagementChecksAsync(output, checks);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { status = "PASS", tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RunGatewayManagementChecksAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var fixtureHome = Path.Combine(output, "gateway-fixture-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(fixtureHome);
        File.WriteAllText(Path.Combine(fixtureHome, "auth.json"), """
            {"auth_mode":"chatgpt","tokens":{"id_token":"fake-gateway-id","access_token":"fake-gateway-access","refresh_token":"fake-gateway-refresh"}}
            """);
        var fixtureEnvironment = new IsolatedGatewayEnvironment(); var fixtureStore = new ConfigStore(fixtureEnvironment, _ => null);
        fixtureStore.Apply(fixtureStore.Plan(fixtureStore.Load(fixtureHome),
            new("https://offline.invalid/v1", "fake-gateway-model-key", "gateway-fixture-model") { Kind = ConnectionKind.ResponsesApi, Mode = ConnectionMode.OfficialWithApi }));
        var before = fixtureStore.Load(fixtureHome); var connection = MixedConnection.Read(before.Bridge!);
        var startupFixture = new MemoryStartupRegistration(); var realStartup = new WindowsStartupRegistration().Read();
        var fixture = new MainWindow(fixtureStore, fixtureHome, output, fixtureEnvironment, startupFixture) { allowSmokeGateway = true };
        fixture.Loaded -= fixture.WindowLoaded; var manager = fixture.MobileManager();
        MainWindow? faultFixture = null;
        try
        {
            using var port = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); port.Start();
            var mobilePortNumber = ((System.Net.IPEndPoint)port.LocalEndpoint).Port; port.Stop();
            var mobile = await manager.SaveAsync("https://phone.example.com", "tester", "fake-gateway-mobile-password", mobilePortNumber);
            fixture.Show(); fixture.ShowPage("gateways"); await fixture.RefreshAllGatewaysAsync(); await fixture.Settle();
            Check(fixture.GatewayPage.IsVisible && fixture.GatewayNav.IsChecked == true && !fixture.GlobalSaveBar.IsVisible && fixture.GatewayPanel.IsVisible && fixture.MobileGatewayPanel.IsVisible,
                "Independent gateway navigation shows both workers without configuration save actions");
            Check(fixture.ModelGatewayAutoStartBox.IsChecked == false && fixture.MobileGatewayAutoStartBox.IsChecked == false && fixture.GatewayDockAutoStartBox.IsChecked == false,
                "Both gateway auto start choices and Windows launch remain opt in");
            Check(fixture.StartGatewayButton.IsEnabled && fixture.StartMobileGatewayButton.IsEnabled && !fixture.StopGatewayButton.IsEnabled && !fixture.StopMobileGatewayButton.IsEnabled,
                "Stopped configured gateways have independent start controls");
            fixture.ShowPage("connection"); fixture.ModelBox.Text = "unsaved-gateway-model";
            fixture.ShowPage("mobile"); await fixture.RefreshMobilePageAsync();
            fixture.mobileUrl.Text = "https://unsaved-phone.example.com"; fixture.mobilePassword.Password = "unsaved-gateway-mobile-password";
            fixture.ShowPage("gateways"); await fixture.RefreshAllGatewaysAsync();
            Check(await manager.StatusAsync() is null && await BridgeProcess.StatusAsync(connection) is null && before.SameContents(fixtureStore.Load(fixtureHome)),
                "Opening gateway management and refreshing do not start workers or save connection drafts");
            fixture.ModelGatewayAutoStartBox.IsChecked = true; fixture.MobileGatewayAutoStartBox.IsChecked = true;
            Check(fixture.GatewayPreferences().Load() == new GatewayStartupSettings(true, true) && fixture.Dirty && fixture.ModelBox.Text == "unsaved-gateway-model" && manager.Load() == mobile && before.SameContents(fixtureStore.Load(fixtureHome)),
                "Auto start preferences persist immediately without saving model or phone drafts");
            Check(await manager.StatusAsync() is null && await BridgeProcess.StatusAsync(connection) is null && startupFixture.Read() is null,
                "Enabling gateway auto start neither starts a worker immediately nor changes Windows startup");
            fixture.GatewayDockAutoStartBox.IsChecked = true;
            Check(startupFixture.Read() == StartupCommand.Create(Environment.ProcessPath!, fixtureHome) && fixture.AutoStartBox.IsChecked == true,
                "Windows launch choice synchronizes with app settings and explicitly targets this home");
            var otherHome = fixtureHome + "-other";
            var other = new MainWindow(fixtureStore, otherHome, output, fixtureEnvironment, startupFixture); other.Loaded -= other.WindowLoaded;
            try
            {
                Check(other.GatewayDockAutoStartBox.IsChecked == false && other.ModelGatewayAutoStartBox.IsChecked == false && other.MobileGatewayAutoStartBox.IsChecked == false && startupFixture.Read() == StartupCommand.Create(Environment.ProcessPath!, fixtureHome),
                    "Another home does not inherit auto start flags or claim the first home's Windows registration");
            }
            finally { other.exitReady = true; other.Close(); }

            await fixture.StartGatewaysOnDockLaunchAsync();
            Check(await manager.StatusAsync() is not null && await BridgeProcess.StatusAsync(connection) is not null && fixture.ModelGatewayStateText.Text == "运行中" && fixture.MobileGatewayStateText.Text == "运行中",
                "Dock launch starts both enabled gateways from their saved settings");
            Check(fixture.ModelBox.Text == "unsaved-gateway-model" && fixture.mobileUrl.Text == "https://unsaved-phone.example.com" && fixture.mobilePassword.Password == "unsaved-gateway-mobile-password" && before.SameContents(fixtureStore.Load(fixtureHome)) && manager.Load() == mobile,
                "Automatic startup preserves form drafts, saved routing and phone credentials");
            await fixture.StopModelGatewayAsync(); await fixture.RefreshLocalCodexStateAsync(); await fixture.StartGatewaysOnDockLaunchAsync();
            fixture.ShowPage("connection"); fixture.ShowPage("gateways"); await fixture.RefreshAllGatewaysAsync();
            Check(await BridgeProcess.StatusAsync(connection) is null && await manager.StatusAsync() is not null,
                "Manual model stop survives polling, page changes and a second launch callback without stopping the phone gateway");
            await fixture.StartModelGatewayAsync(); await fixture.RunMobileActionAsync(fixture.StopMobileGatewayAsync);
            Check(await BridgeProcess.StatusAsync(connection) is not null && await manager.StatusAsync() is null,
                "Manual phone stop leaves the model gateway running");
            await fixture.RunMobileActionAsync(fixture.StartMobileGatewayAsync);
            fixture.ModelGatewayAutoStartBox.IsChecked = false; fixture.MobileGatewayAutoStartBox.IsChecked = false;
            Check(await BridgeProcess.StatusAsync(connection) is not null && await manager.StatusAsync() is not null && fixture.GatewayPreferences().Load() == new GatewayStartupSettings(),
                "Disabling auto start preserves already running workers until an explicit stop");

            fixture.Width = 960; fixture.Height = 640; fixture.GatewayPage.ScrollToTop(); await fixture.Settle(); fixture.Capture(output, "gateways-startup-compact.png");
            fixture.GatewayPanel.BringIntoView(); await fixture.Settle(); fixture.Capture(output, "gateways-model-compact.png");
            fixture.MobileGatewayPanel.BringIntoView(); await fixture.Settle(); fixture.Capture(output, "gateways-mobile-compact.png");
            ThemeManager.Apply(true); await fixture.Settle(); fixture.Capture(output, "gateways-mobile-dark.png"); ThemeManager.Apply(false);
            fixture.RootPanel.LayoutTransform = new ScaleTransform(1.5, 1.5); fixture.StartMobileGatewayButton.BringIntoView(); await fixture.Settle();
            Check(fixture.GatewayPage.ScrollableHeight > 0 && fixture.MobileGatewayPanel.ActualWidth > 250, "Gateway controls remain scrollable at the minimum window and 150 percent layout scale");
            fixture.Capture(output, "gateways-scaled.png"); fixture.RootPanel.LayoutTransform = Transform.Identity;

            await fixture.StopModelGatewayAsync(); await fixture.RunMobileActionAsync(fixture.StopMobileGatewayAsync);
            var disabled = new MainWindow(fixtureStore, fixtureHome, output, fixtureEnvironment, startupFixture) { allowSmokeGateway = true }; disabled.Loaded -= disabled.WindowLoaded;
            try
            {
                await disabled.StartGatewaysOnDockLaunchAsync();
                Check(await BridgeProcess.StatusAsync(connection) is null && await manager.StatusAsync() is null, "Reopening Dock with both auto start options off leaves both configured gateways stopped");
            }
            finally { disabled.exitReady = true; disabled.Close(); }

            File.WriteAllBytes(Path.Combine(fixtureHome, MixedConnection.FileName), (connection with { BaseUrl = "http://198.51.100.7/v1", HttpOrigin = "" }).Protect());
            fixture.GatewayPreferences().Save(new(true, true));
            faultFixture = new MainWindow(fixtureStore, fixtureHome, output, fixtureEnvironment, startupFixture) { allowSmokeGateway = true }; faultFixture.Loaded -= faultFixture.WindowLoaded;
            faultFixture.Show(); faultFixture.ShowPage("gateways"); await faultFixture.StartGatewaysOnDockLaunchAsync();
            Check(await BridgeProcess.StatusAsync(connection) is null && await manager.StatusAsync() is not null && faultFixture.ModelGatewayOperationText.Visibility == Visibility.Visible && faultFixture.ModelGatewayOperationText.Text.Contains("HTTP"),
                "One gateway's authorization failure stays visible and does not block the other gateway from starting");
            Check(new WindowsStartupRegistration().Read() == realStartup, "Gateway UI regression never modifies the user's real Windows startup registration");
            Check(faultFixture.mobilePassword.Password.Length == 0 && manager.Load() == mobile, "Reopened gateway controls reuse phone credentials without exposing its password");
            await manager.StopAsync();
            var preferences = fixture.GatewayPreferences();
            const string invalidPreferences = "{\"ModelOnDockStart\":\"true\"}";
            File.WriteAllText(preferences.SettingsPath, invalidPreferences);
            var invalid = new MainWindow(fixtureStore, fixtureHome, output, fixtureEnvironment, startupFixture) { allowSmokeGateway = true }; invalid.Loaded -= invalid.WindowLoaded;
            try
            {
                await invalid.StartGatewaysOnDockLaunchAsync();
                Check(!invalid.ModelGatewayAutoStartBox.IsEnabled && !invalid.MobileGatewayAutoStartBox.IsEnabled && invalid.GatewayStartupFeedback.Visibility == Visibility.Visible &&
                    await BridgeProcess.StatusAsync(connection) is null && await manager.StatusAsync() is null && File.ReadAllText(preferences.SettingsPath) == invalidPreferences,
                    "Corrupt auto start settings leave Dock usable, show the error, preserve the file and start neither worker");
                preferences.Save(new()); invalid.ReloadGatewayStartupSettings();
                Check(invalid.ModelGatewayAutoStartBox.IsEnabled && invalid.MobileGatewayAutoStartBox.IsEnabled && invalid.GatewayStartupFeedback.Visibility == Visibility.Collapsed,
                    "Repairing auto start settings restores both controls and clears the read error");
            }
            finally { invalid.exitReady = true; invalid.Close(); }
        }
        finally
        {
            if (faultFixture is not null) { faultFixture.exitReady = true; faultFixture.Close(); }
            await manager.StopAsync(); await BridgeProcess.StopAsync(before.Bridge!);
            fixture.exitReady = true; fixture.Close(); ThemeManager.Apply(false);
        }
    }
}
