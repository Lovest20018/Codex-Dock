using System.IO;
using System.Security.Cryptography;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunGatewayLifecycleSmokeAsync(string output, List<string> checks)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        foreach (var update in new[] { false, true })
        {
            var fixtureHome = Path.Combine(home, "exit-gateways-" + update); Directory.CreateDirectory(fixtureHome);
            File.WriteAllText(Path.Combine(fixtureHome, "auth.json"), """
                {"auth_mode":"chatgpt","tokens":{"id_token":"fake-id","access_token":"fake-access","refresh_token":"fake-refresh"}}
                """);
            var fixtureEnvironment = new IsolatedGatewayEnvironment();
            var fixtureStore = new ConfigStore(fixtureEnvironment, _ => null);
            fixtureStore.Apply(fixtureStore.Plan(fixtureStore.Load(fixtureHome),
                new("https://offline.invalid/v1", "fake-exit-key", "fixture-model") { Kind = ConnectionKind.ResponsesApi, Mode = ConnectionMode.OfficialWithApi }));
            var saved = fixtureStore.Load(fixtureHome); var connection = MixedConnection.Read(saved.Bridge!);
            var fixture = new MainWindow(fixtureStore, fixtureHome, output, fixtureEnvironment) { smokeLifecycle = true, allowSmokeGateway = true };
            fixture.Loaded -= fixture.WindowLoaded;
            var manager = fixture.MobileManager(); var installerCalled = false;
            try
            {
                using var port = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); port.Start();
                var number = ((System.Net.IPEndPoint)port.LocalEndpoint).Port; port.Stop();
                var mobile = await manager.SaveAsync("https://phone.example.com", "tester", "fake-exit-mobile-password", number);
                await manager.StartAsync();
                await BridgeProcess.EnsureAsync(fixtureHome, saved.Bridge!);
                fixture.Show(); fixture.ShowPage("app");
                fixture.StopAndExitButton.BringIntoView(); await Settle();
                Check(fixture.StopAndExitButton.IsVisible, "Stop gateways and exit action is visible in application settings");
                if (!update) fixture.Capture(output, "stop-gateways-and-exit.png");
                if (update)
                {
                    var path = Path.Combine(fixtureHome, "never-executed-installer.exe"); File.WriteAllText(path, "isolated fixture, never execute");
                    fixture.downloadedInstaller = new(path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
                    fixture.smokeInstallerLaunch = _ =>
                    {
                        Check(manager.StatusAsync().GetAwaiter().GetResult() is null && BridgeProcess.StatusAsync(connection).GetAwaiter().GetResult() is null,
                            "Installer must be invoked only after both gateway shutdowns complete");
                        installerCalled = true;
                    };
                    fixture.InstallUpdateClicked(fixture, new());
                }
                else fixture.StopAndExitClicked(fixture, new());
                for (var attempt = 0; attempt < 250 && (!fixture.exitReady || fixture.IsVisible); attempt++) await Task.Delay(100);
                Check(fixture.exitReady && fixture.closing && !fixture.IsVisible,
                    $"Gateway shutdown exits through the real window lifecycle (update={update}, ready={fixture.exitReady}, closing={fixture.closing}): {fixture.StatusText.Text}");
                Check(await manager.StatusAsync() is null && await BridgeProcess.StatusAsync(connection) is null, "Both gateways remain stopped after exit");
                Check(manager.Load() == mobile && saved.SameContents(fixtureStore.Load(fixtureHome)), "Exit preserves settings, credentials and model routing");
                Check(installerCalled == update, "Update executes only its isolated installer delegate");
                checks.Add(update ? "Install update waits for both gateway processes before launching the verified installer" : "Stop gateways and exit shuts down both saved gateways without changing settings");
            }
            finally
            {
                await manager.StopAsync(); await BridgeProcess.StopAsync(saved.Bridge!);
                fixture.exitReady = true; fixture.Close();
            }
        }
    }
}
