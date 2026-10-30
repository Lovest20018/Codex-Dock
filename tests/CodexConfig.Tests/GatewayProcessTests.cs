using System.Diagnostics;
using CodexConfig.Core;

internal static class GatewayProcessTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("gateway outlives its launcher, restarts and stops when the saved route is disabled", async () =>
        {
            var home = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            File.WriteAllBytes(Path.Combine(home, "auth.json"), LegacyConnectionTests.Auth());
            var store = new ConfigStore(new FakeEnvironment(), _ => null);
            var settings = new ConnectionSettings("https://fixture.invalid/v1", "fake-upstream", "fixture-model") { Kind = ConnectionKind.ResponsesApi, Mode = ConnectionMode.OfficialWithApi };
            store.Apply(store.Plan(store.Load(home), settings)); var snapshot = store.Load(home); var connection = MixedConnection.Read(snapshot.Bridge!);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = home };
            start.ArgumentList.Add("--gateway-launch-once"); start.ArgumentList.Add(home);
            try
            {
                using var launcher = Process.Start(start)!; await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                if (launcher.ExitCode != 0 || await BridgeProcess.StatusAsync(connection) is not { ProtocolVersion: ModelBridge.ProtocolVersion }) throw new Exception("Gateway died with launcher");
                await BridgeProcess.EnsureAsync(home, snapshot.Bridge!);
                await BridgeProcess.StopAsync(snapshot.Bridge!);
                if (await BridgeProcess.StatusAsync(connection) is not null) throw new Exception("Explicit stop did not release the port");
                await BridgeProcess.EnsureAsync(home, snapshot.Bridge!);
                store.Apply(store.Plan(store.Load(home), store.ReadSettings(store.Load(home)) with { Mode = ConnectionMode.Direct }));
                for (var i = 0; i < 40 && await BridgeProcess.StatusAsync(connection) is not null; i++) await Task.Delay(100);
                if (await BridgeProcess.StatusAsync(connection) is not null) throw new Exception("Disabled gateway remained active");
            }
            finally { await BridgeProcess.StopAsync(snapshot.Bridge!); }
        }));
    }
}
