using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class MobileConnectionServiceTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("phone setup draft stays private in memory and password values never enter a save review", async () =>
        {
            var f = new Fixture(root); var draft = new MobileSetupDraftViewModel(); draft.Load(await f.Runtime.LoadAsync(f.Home, CancellationToken.None));
            Assert(!draft.HasDraft && draft.Password.Length == 0 && draft.Port == "13090");
            draft.PublicUrl = "https://phone-service.invalid"; draft.Username = "tester"; draft.Password = "fake-private-setup-password";
            var review = await f.Service.PrepareAsync(f.Home, draft.Input());
            Assert(draft.HasDraft && !review.Description.Contains(draft.Password) && review.Description.Contains("tester") && f.Runtime.Trace.Count == 0);
            draft.Load(draft.Settings); Assert(!draft.HasDraft && draft.Password.Length == 0);
        }));
        tests.Add(("shared phone setup rejects invalid entry password account and port before writing settings", async () =>
        {
            var f = new Fixture(root);
            foreach (var input in new[] { new MobileSetupInput("http://phone.invalid", "tester", "fake-private-password", 13090),
                new("https://phone.invalid", "tester", "short", 13090), new("https://phone.invalid", "", "fake-private-password", 13090),
                new("https://phone.invalid", "tester", "fake-private-password", 65536), new("https://phone.invalid", "tester", "", 13090) })
                await Throws<ArgumentException>(() => f.Service.PrepareAsync(f.Home, input));
            Assert(f.Runtime.Trace.Count == 0);
        }));
        tests.Add(("saving the phone entry alone consumes its review and leaves startup explicit", async () =>
        {
            var f = new Fixture(root);
            var review = await f.Prepare();
            var saved = await f.Service.SaveAsync(review, () => true);
            Assert(saved is { Succeeded: true, SettingsSaved: true } && saved.Value?.Configured == true && f.Runtime.Trace.SequenceEqual(new[] { "save" }));
            Assert(!(await f.Service.SaveAndStartAsync(review, () => true)).Succeeded && f.Runtime.Trace.SequenceEqual(new[] { "save" }));
            var start = await f.Service.PrepareAsync(f.Home, new(saved.Value!.PublicUrl, saved.Value.Username, "", saved.Value.Port));
            Assert(!start.HasChanges && (await f.Service.SaveAndStartAsync(start, () => true)).Succeeded &&
                f.Runtime.Trace.SequenceEqual(new[] { "save", "start" }));
        }));
        tests.Add(("shared phone settings save freezes one confirmation and serializes with desktop save and gateway actions", async () =>
        {
            var f = new Fixture(root); var review = await f.Prepare();
            using (var gate = f.Operations.TryBeginDesktopApplication()) Assert(!(await f.Service.SaveAndStartAsync(review, () => true)).Succeeded);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Runtime.StartHook = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
            var saving = f.Service.SaveAndStartAsync(review, () => f.Eligible); await entered.Task;
            Assert(f.Service.IsBusy && f.Operations.TryBeginConfigurationSave() is null && f.Operations.TryBeginGateway(false, "fixture") is null);
            Assert(!(await f.Service.SaveAndStartAsync(review, () => true)).Succeeded);
            release.TrySetResult(); Assert((await saving).Succeeded && !f.Operations.IsBusy && !f.Service.IsBusy);
            Assert(!(await f.Service.SaveAndStartAsync(review, () => true)).Succeeded && f.Runtime.Trace.Count(item => item == "save") == 1);
        }));
        tests.Add(("shared phone setup detects changes before save and reports a saved setting when startup later fails", async () =>
        {
            var f = new Fixture(root); var review = await f.Prepare(); f.Runtime.Settings = f.Runtime.Settings with { Revision = "changed-externally" };
            var result = await f.Service.SaveAndStartAsync(review, () => true); Assert(!result.Succeeded && !result.SettingsSaved && f.Runtime.Trace.Count == 0);
            review = await f.Prepare(); f.Runtime.StartError = true;
            result = await f.Service.SaveAndStartAsync(review, () => true);
            Assert(!result.Succeeded && result.SettingsSaved && result.Message.Contains("设置已保存") && f.Runtime.Settings.Configured && !f.Service.IsBusy);
        }));
        tests.Add(("shared phone operation cancellation keeps a saved setting distinct and always releases the lease", async () =>
        {
            var f = new Fixture(root); using var cancellation = new CancellationTokenSource(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Runtime.StartHook = async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); };
            var saving = f.Service.SaveAndStartAsync(await f.Prepare(), () => true, cancellation.Token); await entered.Task; cancellation.Cancel();
            var result = await saving; Assert(!result.Succeeded && result.SettingsSaved && f.Runtime.Settings.Configured && !f.Operations.IsBusy);
        }));
        tests.Add(("shared phone commands reject a stale saved identity and do not call the wrong control endpoint", async () =>
        {
            var f = new Fixture(root); var settings = (await f.Service.SaveAndStartAsync(await f.Prepare(), () => true)).Value!;
            f.Runtime.Settings = settings with { Revision = "new-settings" }; var trace = f.Runtime.Trace.Count;
            Assert(!(await f.Service.CheckEntryAsync(settings, () => true)).Succeeded && !(await f.Service.ManagerAsync(settings, () => true)).Succeeded && f.Runtime.Trace.Count == trace);
        }));
        tests.Add(("shared phone manager validates scheme host port path and one-use ticket before browser launch", async () =>
        {
            var f = new Fixture(root); var settings = (await f.Service.SaveAndStartAsync(await f.Prepare(), () => true)).Value!;
            foreach (var url in new[] { "https://outside.invalid/manager/#fixture-ticket-long", "http://127.0.0.1:32195/manager/#fixture-ticket-long",
                "http://127.0.0.1:32194/other/#fixture-ticket-long", "file:///C:/test", "http://127.0.0.1:32194/manager/?token=secret#fixture-ticket-long" })
            { f.Runtime.ManagerUrl = url; Assert(!(await f.Service.ManagerAsync(settings, () => true)).Succeeded); }
            f.Runtime.ManagerUrl = "http://127.0.0.1:32194/manager/#one-use-fixture-ticket";
            Assert((await f.Service.ManagerAsync(settings, () => true)).Succeeded);
        }));
        tests.Add(("shared phone pairing accepts only bounded PNG grants and validates expiry and saved context", async () =>
        {
            var f = new Fixture(root); var settings = (await f.Service.SaveAndStartAsync(await f.Prepare(), () => true)).Value!;
            var result = await f.Service.CreatePairAsync(settings, () => true); Assert(result.Succeeded && result.Value is { Image.Length: > 8 });
            f.Runtime.PairExpires = DateTimeOffset.UtcNow.AddSeconds(-1); Assert(!(await f.Service.CreatePairAsync(settings, () => true)).Succeeded);
            f.Runtime.PairExpires = DateTimeOffset.UtcNow.AddMinutes(20); Assert(!(await f.Service.CreatePairAsync(settings, () => true)).Succeeded);
            f.Runtime.PairExpires = DateTimeOffset.UtcNow.AddMinutes(5); f.Runtime.PairImage = "not-png"; Assert(!(await f.Service.CreatePairAsync(settings, () => true)).Succeeded);
            Assert(!(await f.Service.CreatePairAsync(settings, () => false)).Succeeded);
        }));
        tests.Add(("shared phone device actions require current membership and preserve other authorizations", async () =>
        {
            var f = new Fixture(root); var saved = (await f.Service.SaveAndStartAsync(await f.Prepare(), () => true)).Value!;
            f.Runtime.Devices.Add(new("device-one", "192.0.2.1")); f.Runtime.Devices.Add(new("device-two", "192.0.2.2")); f.Runtime.Blocks.Add("192.0.2.3");
            var result = await f.Service.RevokeDeviceAsync(saved, "device-one", () => true);
            Assert(result.Succeeded && result.Value!.Devices.Count == 1 && result.Value.Devices[0].Id == "device-two");
            Assert(!(await f.Service.RevokeDeviceAsync(saved, "missing", () => true)).Succeeded && !(await f.Service.UnblockAsync(saved, "192.0.2.4", () => true)).Succeeded);
            Assert((await f.Service.UnblockAsync(saved, "192.0.2.3", () => true)).Succeeded && f.Runtime.Blocks.Count == 0 && f.Runtime.Devices.Count == 1);
        }));
        tests.Add(("native phone reviewed revision rejects an external settings change under the manager lock", async () =>
        {
            var home = Path.Combine(root, "phone-revision-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            var manager = new MobileGatewayManager(home, Path.Combine(home, "mobile-state"));
            var first = await manager.SaveAsync("https://one-phone.invalid", "tester", "fake-phone-revision-password", Port());
            var revision = manager.SettingsRevision();
            var external = await manager.SaveAsync("https://two-phone.invalid", "tester", "", first.Port);
            await Throws<IOException>(() => manager.SaveAsync(first.PublicUrl, "tester", "", first.Port, expectedRevision: revision));
            Assert(manager.Load() == external && await manager.StatusAsync() is null);
        }));
        tests.Add(("native phone setup preserves device login on port-only changes and revocation blocks restoration", async () =>
        {
            var home = Path.Combine(root, "phone-service-native-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "config.toml"), "# unchanged\nmodel='phone-service-fixture'\n");
            var initialConfig = File.ReadAllBytes(Path.Combine(home, "config.toml")); var data = Path.Combine(home, "mobile-state");
            var manager = new MobileGatewayManager(home, data);
            var service = new MobileConnectionService(new NativeMobileConnectionRuntime(data, Path.GetFullPath("artifacts/mobile-worker/CodexDock.Mobile/CodexDock.Mobile.exe")), new(new WorkspaceSession(home)));
            var saved = await service.SaveAndStartAsync(await service.PrepareAsync(home, new("https://phone-service.invalid", "tester", "fake-phone-service-password", Port())), () => true);
            Assert(saved.Succeeded);
            using var handler = new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false }; using var client = new HttpClient(handler);
            try
            {
                var settings = saved.Value!;
                using var login = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + settings.Port + "/api/login");
                login.Headers.Host = "phone-service.invalid"; login.Headers.Add("Origin", settings.PublicUrl); login.Headers.UserAgent.ParseAdd("Fixture-Android");
                login.Content = new StringContent(JsonSerializer.Serialize(new { username = "tester", password = "fake-phone-service-password" }), Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(login); Assert(response.IsSuccessStatusCode);
                var cookie = response.Headers.GetValues("Set-Cookie").First().Split(';')[0];
                var view = await service.ReadAsync(home); Assert(view.Devices.Devices.Count == 1 && view.Devices.Devices[0].Label == "安卓 App");
                var originalAuth = manager.Load()!.Auth;
                var changed = await service.SaveAndStartAsync(await service.PrepareAsync(home, new(settings.PublicUrl, settings.Username, "", Port())), () => true);
                Assert(changed.Succeeded && manager.Load()!.Auth == originalAuth); settings = changed.Value!;
                using var auth = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + settings.Port + "/api/auth"); auth.Headers.Host = "phone-service.invalid"; auth.Headers.Add("Cookie", cookie); auth.Headers.UserAgent.ParseAdd("Fixture-Android");
                using var result = await client.SendAsync(auth); using var body = JsonDocument.Parse(await result.Content.ReadAsByteArrayAsync());
                Assert(body.RootElement.GetProperty("authenticated").GetBoolean());
                view = await service.ReadAsync(home); var device = view.Devices.Devices.Single();
                Assert((await service.RevokeDeviceAsync(settings, device.Id, () => true)).Succeeded);
                using var revoked = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + settings.Port + "/api/auth"); revoked.Headers.Host = "phone-service.invalid"; revoked.Headers.Add("Cookie", cookie);
                using var revokedResponse = await client.SendAsync(revoked); using var revokedBody = JsonDocument.Parse(await revokedResponse.Content.ReadAsByteArrayAsync());
                Assert(!revokedBody.RootElement.GetProperty("authenticated").GetBoolean() && initialConfig.SequenceEqual(File.ReadAllBytes(Path.Combine(home, "config.toml"))));
            }
            finally { await manager.StopAsync(); }
        }));
    }

    private sealed class Fixture
    {
        internal readonly string Home;
        internal readonly FakeRuntime Runtime;
        internal readonly WorkspaceApplyCoordinator Operations;
        internal readonly MobileConnectionService Service;
        internal bool Eligible = true;
        internal Fixture(string root)
        {
            Home = Path.Combine(root, "phone-service-" + Guid.NewGuid().ToString("N")); Runtime = new(Home); Operations = new(new WorkspaceSession(Home)); Service = new(Runtime, Operations);
        }
        internal Task<MobileSetupReview> Prepare() => Service.PrepareAsync(Home, new("https://phone-service.invalid", "tester", "fake-service-setup-password", 13090));
    }
    private sealed record FakeDevice(string Id, string Ip);
    private sealed class FakeRuntime(string home) : IMobileConnectionRuntime
    {
        internal MobileConnectionSettings Settings = new(home, false, "", "codex", 13090, 32194, "missing");
        internal readonly List<string> Trace = [];
        internal readonly List<FakeDevice> Devices = [];
        internal readonly List<string> Blocks = [];
        internal Func<CancellationToken, Task>? StartHook;
        internal bool StartError, Running;
        internal string ManagerUrl = "http://127.0.0.1:32194/manager/#one-use-fixture-ticket";
        internal string PairImage = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        internal DateTimeOffset PairExpires = DateTimeOffset.UtcNow.AddMinutes(5);
        public Task<MobileConnectionSettings> LoadAsync(string requested, CancellationToken cancellation) => Task.FromResult(Settings);
        public Task<MobileConnectionSettings> SaveAsync(string requested, MobileSetupInput input, string expectedRevision, CancellationToken cancellation)
        { Trace.Add("save"); Settings = Settings with { Configured = true, PublicUrl = input.PublicUrl, Username = input.Username, Port = input.Port, Revision = Guid.NewGuid().ToString("N") }; return Task.FromResult(Settings); }
        public async Task StartAsync(string requested, CancellationToken cancellation) { Trace.Add("start"); if (StartHook is not null) await StartHook(cancellation); if (StartError) throw new IOException("fixture startup error"); Running = true; }
        public Task<MobileGatewayStatus?> StatusAsync(string requested, CancellationToken cancellation) => Task.FromResult<MobileGatewayStatus?>(Running ? new("codex-dock-mobile", 4, "fixture", Settings.PublicUrl, Settings.Port, false) : null);
        public Task<JsonDocument> ControlAsync(string requested, string action, object body, CancellationToken cancellation)
        {
            Trace.Add(action); var value = JsonSerializer.SerializeToElement(body);
            if (action == "manager") return Task.FromResult(JsonSerializer.SerializeToDocument(new { url = ManagerUrl }));
            if (action == "pair") return Task.FromResult(JsonSerializer.SerializeToDocument(new { id = "fixture-pair", image = PairImage, expires = PairExpires.ToUnixTimeSeconds(), state = "active" }));
            if (action == "devices")
            {
                var command = value.GetProperty("action").GetString(); if (command == "revoke") Devices.RemoveAll(row => row.Id == value.GetProperty("id").GetString());
                if (command == "unblock") Blocks.Remove(value.GetProperty("ip").GetString()!);
                return Task.FromResult(JsonSerializer.SerializeToDocument(new { sessions = Devices.Select(device => new { id = device.Id, ip = device.Ip, userAgent = "Fixture-Android", lastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }),
                    autoBlocks = Blocks.Select(ip => new { ip }) }));
            }
            return Task.FromResult(JsonDocument.Parse("{}"));
        }
    }
    private static int Port() { using var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); return ((IPEndPoint)port.LocalEndpoint).Port; }
    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Shared phone connection regression"); }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
