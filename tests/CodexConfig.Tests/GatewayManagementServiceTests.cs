using System.Net;
using System.Net.Sockets;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class GatewayManagementServiceTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("shared gateway preferences default off remain home scoped and do not start a worker", async () =>
        {
            var f = new Fixture(root); var preferences = f.Service.Preferences(f.Home);
            Assert(f.Service.LoadPreferences(f.Home) == new GatewayStartupSettings() && !File.Exists(preferences.SettingsPath));
            f.Service.SetAutomaticStart(f.Home, GatewayKind.Model, true); f.Service.SetAutomaticStart(f.Home, GatewayKind.Mobile, true);
            Assert(f.Service.LoadPreferences(f.Home) == new GatewayStartupSettings(true, true));
            Assert(f.Service.LoadPreferences(f.Home + "-other") == new GatewayStartupSettings() && f.Runtime.Trace.Count == 0);
            var state = await f.Refresh(); Assert(state is not null && !state.Model.Configured && !state.Mobile.Configured && f.Runtime.Trace.Count == 0);
        }));
        tests.Add(("shared gateway refresh keeps unverified listeners unknown and isolates one damaged configuration", async () =>
        {
            var f = new Fixture(root); f.Runtime.Configured = true; f.Runtime.Running[GatewayKind.Model] = null;
            var state = await f.Refresh(); Assert(state!.Model.Running is null && state.Model.Status == "暂未确认" && state.Mobile.Running == false);
            f.Runtime.LoadError = GatewayKind.Model; state = await f.Refresh();
            Assert(state!.Model.Error is not null && state.Mobile.Error is null && f.Runtime.Trace.Count == 0);
        }));
        tests.Add(("shared gateway refresh coalesces repeated probes and drops results for an abandoned home", async () =>
        {
            var f = new Fixture(root); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Runtime.Probe = async kind => { if (kind == GatewayKind.Model) { entered.TrySetResult(); await release.Task; } };
            var first = f.Refresh(); await entered.Task; var second = f.Refresh(); Assert(ReferenceEquals(first, second));
            f.Eligible = false; release.TrySetResult(); Assert(await first is null && f.Service.Overview is null);
        }));
        tests.Add(("shared gateway operations use the same lease as configuration save and desktop application", async () =>
        {
            var f = new Fixture(root); f.Runtime.Configured = true;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Runtime.Start = async (_, cancellation) => { entered.TrySetResult(); await release.Task.WaitAsync(cancellation); };
            f.Session.MarkDraft(true); var starting = f.Execute(GatewayKind.Model, GatewayCommand.Start); await entered.Task;
            Assert(f.Service.IsBusy && f.Operations.TryBeginConfigurationSave() is null && f.Operations.TryBeginDesktopApplication() is null);
            Assert((await f.Execute(GatewayKind.Mobile, GatewayCommand.Start)).Outcome == GatewayOperationOutcome.Busy);
            release.TrySetResult(); Assert((await starting).Succeeded && !f.Service.IsBusy && !f.Operations.IsBusy && f.Session.State.HasDraft);
        }));
        tests.Add(("shared gateway restart never starts after a refused stop or a changed saved revision", async () =>
        {
            foreach (var change in new[] { "stop", "revision", "context" })
            {
                var f = new Fixture(root); f.Runtime.Configured = true;
                if (change == "stop") f.Runtime.StopFailure = GatewayKind.Model;
                if (change == "revision") f.Runtime.Stopped = _ => f.Runtime.Revision = "new-revision";
                if (change == "context") f.Runtime.Stopped = _ => f.Eligible = false;
                var result = await f.Execute(GatewayKind.Model, GatewayCommand.Restart);
                Assert(!result.Succeeded && f.Runtime.Trace.SequenceEqual(new[] { "stop:Model" }) && !f.Operations.IsBusy);
            }
        }));
        tests.Add(("shared gateway cancellation releases its lease and does not stop an unrelated worker", async () =>
        {
            var f = new Fixture(root); f.Runtime.Configured = true; f.Runtime.Running[GatewayKind.Mobile] = true;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var cancellation = new CancellationTokenSource();
            f.Runtime.Start = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); };
            var pending = f.Service.ExecuteAsync(GatewayKind.Model, GatewayCommand.Start, f.Home, () => f.Eligible, cancellation.Token);
            await entered.Task; cancellation.Cancel(); var result = await pending;
            Assert(result.Outcome == GatewayOperationOutcome.Cancelled && !f.Service.IsBusy && !f.Operations.IsBusy && f.Runtime.Running[GatewayKind.Mobile] == true);
        }));
        tests.Add(("shared gateway launch runs opted-in targets once and one failure does not block the other gateway", async () =>
        {
            var f = new Fixture(root); f.Runtime.Configured = true; f.Runtime.StartFailure = GatewayKind.Model;
            f.Service.SetAutomaticStart(f.Home, GatewayKind.Model, true); f.Service.SetAutomaticStart(f.Home, GatewayKind.Mobile, true);
            var result = await f.Service.StartOnLaunchAsync(f.Home, () => f.Eligible);
            Assert(result.Attempted && result.Results.Count == 2 && !result.Results[0].Succeeded && result.Results[1].Succeeded);
            await f.Execute(GatewayKind.Mobile, GatewayCommand.Stop); var trace = f.Runtime.Trace.Count;
            Assert(!(await f.Service.StartOnLaunchAsync(f.Home, () => true)).Attempted && f.Runtime.Trace.Count == trace && f.Runtime.Running[GatewayKind.Mobile] == false);
            f.Service.SetAutomaticStart(f.Home, GatewayKind.Mobile, false); Assert(f.Runtime.Trace.Count == trace);
        }));
        tests.Add(("shared gateway corrupt startup preferences start neither process and preserve the damaged file", async () =>
        {
            var f = new Fixture(root); var path = f.Service.Preferences(f.Home).SettingsPath; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            const string damaged = "{\"ModelOnDockStart\":\"invalid\"}"; File.WriteAllText(path, damaged);
            var result = await f.Service.StartOnLaunchAsync(f.Home, () => true);
            Assert(result.Error is not null && f.Runtime.Trace.Count == 0 && File.ReadAllText(path) == damaged);
        }));
        tests.Add(("shared gateway start rejects unconfigured or unauthorized definitions before native operations", async () =>
        {
            var f = new Fixture(root); Assert(!(await f.Execute(GatewayKind.Model, GatewayCommand.Start)).Succeeded);
            f.Runtime.Configured = true; f.Runtime.Authorized = false;
            Assert(!(await f.Execute(GatewayKind.Model, GatewayCommand.Restart)).Succeeded && f.Runtime.Trace.Count == 0);
        }));
        tests.Add(("native gateway read-only status distinguishes a stopped port from an unrelated open listener", async () =>
        {
            var home = Path.Combine(root, "native-gateway-probe-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            var store = new ConfigStore(new MemoryEnvironment(), _ => null);
            var connection = MixedConnection.Create(new("https://native-gateway.invalid/v1", "fake-probe-key", "fixture") { Mode = ConnectionMode.OfficialWithApi, Kind = ConnectionKind.ResponsesApi });
            File.WriteAllBytes(Path.Combine(home, MixedConnection.FileName), connection.Protect());
            File.WriteAllText(Path.Combine(home, "config.toml"), "model='fixture'\nopenai_base_url='" + connection.NativeBaseUrl + "'\n");
            var native = new NativeGatewayManagementRuntime(store, Path.Combine(home, "mobile-state")); var before = store.Load(home);
            var definition = await native.LoadAsync(GatewayKind.Model, home, CancellationToken.None);
            Assert((await native.ProbeAsync(definition, CancellationToken.None)).Running == false);
            using var listener = new TcpListener(IPAddress.Loopback, connection.Port); listener.Start();
            Assert((await native.ProbeAsync(definition, CancellationToken.None)).Running is null);
            var refused = false; try { await native.StopAsync(definition, CancellationToken.None); } catch (IOException) { refused = true; }
            Assert(refused && before.SameContents(store.Load(home)) && !Directory.Exists(Path.Combine(home, "mobile-state")));
        }));
        tests.Add(("native shared gateway operations start stop and restart isolated model and bundled phone workers", async () =>
        {
            var home = Path.Combine(root, "native-gateway-workers-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            var store = new ConfigStore(new MemoryEnvironment(), _ => null);
            var connection = MixedConnection.Create(new("https://never-contacted-gateway.invalid/v1", "fake-native-gateway-key", "fixture") { Mode = ConnectionMode.OfficialWithApi, Kind = ConnectionKind.ResponsesApi });
            File.WriteAllBytes(Path.Combine(home, MixedConnection.FileName), connection.Protect());
            File.WriteAllText(Path.Combine(home, "config.toml"), "model='fixture'\nopenai_base_url='" + connection.NativeBaseUrl + "'\n");
            var mobileRoot = Path.Combine(home, "mobile-state"); var mobile = new MobileGatewayManager(home, mobileRoot);
            using var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); var number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
            var savedMobile = await mobile.SaveAsync("https://phone-gateway.invalid", "fixture", "fake-native-mobile-password", number);
            var mobileExecutable = Path.GetFullPath("artifacts/mobile-worker/CodexDock.Mobile/CodexDock.Mobile.exe");
            Assert(File.Exists(mobileExecutable));
            var native = new NativeGatewayManagementRuntime(store, mobileRoot, Environment.ProcessPath!, mobileExecutable);
            var service = new GatewayManagementService(native, new(new WorkspaceSession(home)), Path.Combine(home, "preferences"));
            var before = store.Load(home);
            try
            {
                foreach (var kind in new[] { GatewayKind.Model, GatewayKind.Mobile })
                {
                    var result = await service.ExecuteAsync(kind, GatewayCommand.Start, home, () => true); Assert(result.Succeeded);
                    result = await service.ExecuteAsync(kind, GatewayCommand.Restart, home, () => true); Assert(result.Succeeded);
                }
                Assert((await BridgeProcess.StatusAsync(connection))?.Requests == 0 && await mobile.StatusAsync() is not null);
                foreach (var kind in new[] { GatewayKind.Model, GatewayKind.Mobile }) Assert((await service.ExecuteAsync(kind, GatewayCommand.Stop, home, () => true)).Succeeded);
                Assert(await BridgeProcess.StatusAsync(connection) is null && await mobile.StatusAsync() is null && mobile.Load() == savedMobile && before.SameContents(store.Load(home)));
            }
            finally { await mobile.StopAsync(); await BridgeProcess.StopAsync(before.Bridge!); }
        }));
    }

    private sealed class Fixture
    {
        internal readonly string Home;
        internal readonly FakeRuntime Runtime = new();
        internal readonly WorkspaceSession Session;
        internal readonly WorkspaceApplyCoordinator Operations;
        internal readonly GatewayManagementService Service;
        internal bool Eligible = true;
        internal Fixture(string root)
        {
            Home = Path.Combine(root, "gateway-service-" + Guid.NewGuid().ToString("N"));
            Session = new(Home); Operations = new(Session); Service = new(Runtime, Operations, Path.Combine(Home, "preferences"));
        }
        internal Task<GatewayOverview?> Refresh() => Service.RefreshAsync(Home, () => Eligible);
        internal Task<GatewayOperationResult> Execute(GatewayKind kind, GatewayCommand command) => Service.ExecuteAsync(kind, command, Home, () => Eligible);
    }
    private sealed class FakeRuntime : IGatewayManagementRuntime
    {
        internal bool Configured, Authorized = true;
        internal string Revision = "initial";
        internal GatewayKind? LoadError, StartFailure, StopFailure;
        internal Func<GatewayKind, CancellationToken, Task>? Start;
        internal Func<GatewayKind, Task>? Probe;
        internal Action<GatewayKind>? Stopped;
        internal readonly Dictionary<GatewayKind, bool?> Running = new() { [GatewayKind.Model] = false, [GatewayKind.Mobile] = false };
        internal readonly List<string> Trace = [];
        public Task<GatewayDefinition> LoadAsync(GatewayKind kind, string home, CancellationToken cancellation)
        { if (LoadError == kind) throw new InvalidDataException("fixture damaged settings"); return Task.FromResult(new GatewayDefinition(kind, home, Configured, Configured && Authorized, "fixture definition", Revision)); }
        public async Task<GatewayProbe> ProbeAsync(GatewayDefinition definition, CancellationToken cancellation)
        { if (Probe is not null) await Probe(definition.Kind); return new(Running[definition.Kind]); }
        public async Task StartAsync(GatewayDefinition definition, CancellationToken cancellation)
        { Trace.Add("start:" + definition.Kind); if (Start is not null) await Start(definition.Kind, cancellation); if (StartFailure == definition.Kind) throw new IOException("fixture startup failed"); Running[definition.Kind] = true; }
        public Task StopAsync(GatewayDefinition definition, CancellationToken cancellation)
        { Trace.Add("stop:" + definition.Kind); if (StopFailure == definition.Kind) throw new IOException("fixture shutdown failed"); Running[definition.Kind] = false; Stopped?.Invoke(definition.Kind); return Task.CompletedTask; }
    }
    private sealed class MemoryEnvironment : IUserEnvironment { public string? Read() => null; public void Write(string? value) => throw new InvalidOperationException("No real environment writes"); }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Shared gateway management regression"); }
}
