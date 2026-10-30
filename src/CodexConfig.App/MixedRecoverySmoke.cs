using System.IO;
using System.Text;
using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunMixedRecoverySmokeAsync(string output, List<string> checks)
    {
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var fixtureHome = Path.Combine(home, "mixed-ui-fixture"); Directory.CreateDirectory(fixtureHome);
        byte[] auth = Encoding.UTF8.GetBytes("{\"auth_mode\":\"chatgpt\",\"tokens\":{\"id_token\":\"fake-id\",\"access_token\":\"fake-access\",\"refresh_token\":\"fake-refresh\"}}");
        File.WriteAllBytes(Path.Combine(fixtureHome, "auth.json"), auth);
        var fixtureEnvironment = new IsolatedGatewayEnvironment();
        var fixtureStore = new ConfigStore(fixtureEnvironment, _ => null);
        var fixture = new MainWindow(fixtureStore, fixtureHome, output, fixtureEnvironment) { allowSmokeGateway = true };
        fixture.Loaded -= fixture.WindowLoaded; byte[]? bridge = null;
        try
        {
            fixture.Show(); fixture.ShowPage("connection");
            Assert(!fixture.OfficialPanel.IsVisible && !fixture.GatewayPanel.IsVisible && !fixture.Dirty, "Default connection remains a clean PAT editor");
            fixture.ConnectionModeBox.SelectedIndex = 1; fixture.BaseUrlBox.Text = "https://offline.invalid/v1";
            fixture.KindBox.SelectedIndex = (int)ConnectionKind.ResponsesApi; fixture.ApiKeyBox.Password = "fake-mixed-model-key"; fixture.ModelBox.Text = "fixture-model";
            Assert(fixture.OfficialPanel.IsVisible && fixture.GatewayPanel.IsVisible && fixture.SaveButton.IsEnabled && fixture.GlobalSaveBar.IsVisible, "Mixed controls are optional and save remains visible");
            Assert(fixture.SaveCurrent(), "Mixed connection save: " + fixture.StatusText.Text);
            bridge = fixtureStore.Load(fixtureHome).Bridge!;
            for (var i = 0; i < 100 && fixture.gatewayPolling; i++) await Task.Delay(100);
            await fixture.RefreshGatewayAsync(start: true);
            Assert(fixture.gatewayStatus is not null && !fixture.Dirty && !fixture.SaveButton.IsEnabled, "Saved connection starts an independent gateway and reloads cleanly");
            var connection = MixedConnection.Read(bridge);
            Assert(new TomlDocument(fixtureStore.Load(fixtureHome).Config).String("model_provider") == "openai" && File.ReadAllBytes(Path.Combine(fixtureHome, "auth.json")).SequenceEqual(auth), "Mixed UI preserves native provider identity and login");
            fixture.detailedChanges["analytics.enabled"] = "false"; fixture.MarkDirty();
            Assert(fixture.SaveCurrent() && File.ReadAllBytes(Path.Combine(fixtureHome, "auth.json")).SequenceEqual(auth), "Ordinary mixed-mode settings save does not rewrite the login");
            fixture.Width = 960; fixture.Height = 640; fixture.ConnectionPage.ScrollToHome(); await Settle(); fixture.Capture(output, "mixed-account.png");
            fixture.ConnectionPage.ScrollToEnd(); await Settle(); fixture.Capture(output, "mixed-gateway.png");
            Assert(fixture.GlobalSaveBar.IsVisible, "Gateway scroll keeps save actions visible");
            ThemeManager.Apply(true); await Settle(); fixture.Capture(output, "mixed-gateway-dark.png"); ThemeManager.Apply(false);
            fixture.exitReady = true; fixture.Close();
            Assert(await BridgeProcess.StatusAsync(connection) is not null, "Closing the actual editor must leave the gateway running");
            await BridgeProcess.StopAsync(bridge); Assert(await BridgeProcess.StatusAsync(connection) is null, "Independent worker can be explicitly stopped");
            checks.AddRange(["PAT default hides account and gateway controls", "Optional official account with independent API saves through the persistent save bar", "Mixed save preserves the native OpenAI provider and official login", "Ordinary mixed settings preserve authentication", "Independent worker survives the actual editor closing", "Explicit stop releases the port", "Mixed account and gateway layout at 960x640 in light and dark themes"]);
        }
        finally
        {
            if (bridge is not null) await BridgeProcess.StopAsync(bridge);
            fixture.exitReady = true; fixture.Close();
        }
    }
    private sealed class IsolatedGatewayEnvironment : IUserEnvironment
    {
        private string? value;
        public string? Read() => value;
        public void Write(string? newValue) => value = newValue;
    }
}
