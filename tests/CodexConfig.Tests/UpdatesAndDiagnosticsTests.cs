using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class UpdatesAndDiagnosticsTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("shared updater preserves Core origin and checksum verification through offline HTTP", async () =>
        {
            var bytes = Encoding.UTF8.GetBytes("offline fixture installer, never execute"); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var prefix = GitHubUpdates.Repository + "/releases/download/v99.0.0/"; const string name = "CodexDock-99.0.0-Setup-win-x64.exe";
            var requests = new List<string>();
            var runtime = new GitHubUpdateRuntime(() => new(new Handler(request =>
            {
                Assert(request.Headers.Authorization is null); var uri = request.RequestUri!.AbsoluteUri; requests.Add(uri);
                if (uri == GitHubUpdates.LatestApi) return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { tag_name = "v99.0.0", draft = false, prerelease = false,
                    assets = new[] { new { name, size = bytes.Length, browser_download_url = prefix + name, digest = "sha256:" + hash },
                        new { name = "SHA256SUMS-99.0.0.txt", size = 100, browser_download_url = prefix + "SHA256SUMS-99.0.0.txt", digest = "" } } })) };
                if (uri == prefix + "SHA256SUMS-99.0.0.txt") return new(HttpStatusCode.OK) { Content = new StringContent(hash + "  " + name) };
                if (uri == prefix + name) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                throw new InvalidOperationException("Unexpected fixture request.");
            })));
            var service = new UpdateDownloadService(runtime); var release = await service.CheckAsync(new(1, 0, 0)); Assert(release is not null);
            var installer = await service.DownloadAsync(release!, Path.Combine(root, "shared-updater-" + Guid.NewGuid().ToString("N")));
            using var verified = installer.OpenVerified(); Assert(verified.Length == bytes.Length && requests.Count == 3 && !service.IsBusy);
        }));
        tests.Add(("shared updater cancellation and overlap never return delayed releases and release their busy state", async () =>
        {
            var gate = new TaskCompletionSource<ReleaseUpdate?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var runtime = new DownloadFixture { Check = _ => gate.Task }; var service = new UpdateDownloadService(runtime);
            using var cancellation = new CancellationTokenSource(); var pending = service.CheckAsync(new(1, 0, 0), cancellation.Token);
            await ThrowsAsync<InvalidOperationException>(() => service.CheckAsync(new(1, 0, 0))); Assert(service.IsBusy && runtime.Checks == 1);
            cancellation.Cancel(); gate.SetResult(Release()); await ThrowsAsync<OperationCanceledException>(() => pending);
            Assert(!service.IsBusy); runtime.Check = _ => Task.FromResult<ReleaseUpdate?>(null); Assert(await service.CheckAsync(new(1, 0, 0)) is null);
        }));
        tests.Add(("already cancelled update checks and downloads never invoke their runtime", async () =>
        {
            var runtime = new DownloadFixture(); var service = new UpdateDownloadService(runtime); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => service.CheckAsync(new(1, 0, 0), cancellation.Token));
            await ThrowsAsync<OperationCanceledException>(() => service.DownloadAsync(Release(), root, cancellation: cancellation.Token)); Assert(runtime.Checks == 0 && runtime.Downloads == 0);
        }));
        tests.Add(("shared updater failures preserve retryability and never synthesize a verified installer", async () =>
        {
            var runtime = new DownloadFixture { Download = _ => throw new InvalidDataException("fixture checksum failure") }; var service = new UpdateDownloadService(runtime);
            await ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Release(), root)); Assert(!service.IsBusy && runtime.Downloads == 1);
            runtime.Check = _ => throw new HttpRequestException("fixture offline"); await ThrowsAsync<HttpRequestException>(() => service.CheckAsync(new(1, 0, 0))); Assert(!service.IsBusy);
        }));
        tests.Add(("update installation keeps the verified file locked and launches only after both confirmed stops", async () =>
        {
            var f = new InstallFixture(root); f.Runtime.OnLaunch = installer =>
            {
                Assert(f.Gateways.Running.Values.All(value => value == false) && f.Runtime.Trace.SequenceEqual(new[] { "enhancements", "launch" }));
                Throws<IOException>(() => File.WriteAllText(installer.Path, "cannot replace locked file"));
                Throws<IOException>(() => File.Delete(installer.Path));
            };
            await f.Service.InstallAsync(f.Installer, f.Home, () => true);
            Assert(f.Runtime.Launched == 1 && f.Gateways.Trace.SequenceEqual(new[] { "stop:Model", "stop:Mobile" }) && !f.Operations.IsBusy);
            File.AppendAllText(f.Installer.Path, "handle was released");
        }));
        tests.Add(("update installation rejects tampering or an ineligible home before stopping anything", async () =>
        {
            var f = new InstallFixture(root); await ThrowsAsync<OperationCanceledException>(() => f.Service.InstallAsync(f.Installer, f.Home, () => false));
            Assert(f.Runtime.Trace.Count == 0 && f.Gateways.Trace.Count == 0);
            File.AppendAllText(f.Installer.Path, "tamper"); await ThrowsAsync<InvalidDataException>(() => f.Service.InstallAsync(f.Installer, f.Home, () => true));
            Assert(f.Runtime.Trace.Count == 0 && f.Gateways.Trace.Count == 0 && !f.Operations.IsBusy);
        }));
        tests.Add(("update installation shares the workspace operation gate with saves and gateway actions", async () =>
        {
            var f = new InstallFixture(root); using (var lease = f.Operations.TryBeginConfigurationSave())
                await ThrowsAsync<InvalidOperationException>(() => f.Service.InstallAsync(f.Installer, f.Home, () => true));
            Assert(f.Runtime.Trace.Count == 0);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); f.Runtime.Stop = _ => gate.Task;
            var pending = f.Service.InstallAsync(f.Installer, f.Home, () => true); Assert(f.Operations.IsBusy && f.Operations.TryBeginProfile() is null);
            await ThrowsAsync<InvalidOperationException>(() => f.Service.InstallAsync(f.Installer, f.Home, () => true)); gate.SetResult(); await pending;
            Assert(f.Runtime.Launched == 1 && !f.Operations.IsBusy);
        }));
        tests.Add(("update installation verifies first then waits for automatic reconnect to release its shared lease", async () =>
        {
            var f = new InstallFixture(root); var automatic = f.Operations.TryBeginDesktopApplication()!;
            f.Runtime.Wait = _ =>
            {
                Throws<IOException>(() => File.AppendAllText(f.Installer.Path, "must remain locked while waiting"));
                automatic.Dispose(); return Task.CompletedTask;
            };
            await f.Service.InstallAsync(f.Installer, f.Home, () => true);
            Assert(f.Runtime.Launched == 1 && !f.Operations.IsBusy);
        }));
        tests.Add(("update installation refuses uncertain gateway states and stop failures without launching", async () =>
        {
            var f = new InstallFixture(root); f.Gateways.FailStop = true;
            await ThrowsAsync<IOException>(() => f.Service.InstallAsync(f.Installer, f.Home, () => true)); Assert(f.Runtime.Launched == 0 && !f.Operations.IsBusy);
            f = new(root); f.Gateways.Uncertain = true;
            await ThrowsAsync<IOException>(() => f.Service.InstallAsync(f.Installer, f.Home, () => true)); Assert(f.Runtime.Launched == 0 && !f.Operations.IsBusy);
        }));
        tests.Add(("update installation cancels on context or gateway revision changes after shutdown", async () =>
        {
            var f = new InstallFixture(root); var eligible = true; f.Runtime.Stop = _ => { eligible = false; return Task.CompletedTask; };
            await ThrowsAsync<OperationCanceledException>(() => f.Service.InstallAsync(f.Installer, f.Home, () => eligible)); Assert(f.Runtime.Launched == 0);
            f = new(root); f.Gateways.ChangeAfterStop = true;
            await ThrowsAsync<IOException>(() => f.Service.InstallAsync(f.Installer, f.Home, () => true)); Assert(f.Runtime.Launched == 0);
        }));
        tests.Add(("update installation cancellation and launcher errors release the file and workspace lease", async () =>
        {
            var f = new InstallFixture(root); using var cancellation = new CancellationTokenSource();
            f.Runtime.Stop = _ => { cancellation.Cancel(); return Task.CompletedTask; };
            await ThrowsAsync<OperationCanceledException>(() => f.Service.InstallAsync(f.Installer, f.Home, () => true, cancellation.Token)); Assert(f.Runtime.Launched == 0 && !f.Operations.IsBusy);
            f = new(root); f.Runtime.OnLaunch = _ => throw new IOException("fixture launch failure");
            await ThrowsAsync<IOException>(() => f.Service.InstallAsync(f.Installer, f.Home, () => true));
            Assert(!f.Operations.IsBusy); File.AppendAllText(f.Installer.Path, "released after failed launch");
        }));
        tests.Add(("session diagnostics retain whitelisted parameters and ignore malformed or private payloads", async () =>
        {
            var home = DiagnosticHome(root); var path = Path.Combine(home, "sessions", "rollout-parameters.jsonl");
            File.WriteAllText(path, """
                []
                {"type":"event_msg","payload":{"type":"thread_settings_applied","thread_settings":[]}}
                {"type":"event_msg","payload":{"type":"thread_settings_applied","thread_settings":{"model":"observed-model","reasoning_effort":"low","service_tier":"fast","approvals_reviewer":"auto_review"}}}
                {"type":"response_item","payload":{"type":"message","content":"fake-private-prompt"}}
                {"type":"response_item","payload":{"type":"function_call","arguments":"fake-private-tool-arguments"}}
                {"type":"response_item","payload":{"type":"configuration_update","reasoning":[]}}
                {"type":"response_item","payload":{"type":"configuration_update","reasoning":{"effort":"high"}}}
                {"type":"event_msg","payload":{"type":"auto_approval_review_completed","status":"approved"}}
                {"unfinished":
                """);
            var service = new SessionDiagnosticService(); var listing = await service.ListAsync(home, () => home); var file = listing.Files.Single();
            var observed = await service.ReadAsync(listing, file, () => (home, path)); var text = SessionDiagnosticService.Describe(observed);
            Assert(observed.Model == "observed-model" && observed.Reasoning == "high" && observed.ServiceTier == "fast" && observed.ContainsConfigurationUpdate && observed.ReviewStatus == "approved");
            Assert(!JsonSerializer.Serialize(observed).Contains("fake-private") && text.Contains("本记录未提供") && text.Contains("approved"));
        }));
        tests.Add(("session diagnostics require a listed selection and reject paths outside the current home", async () =>
        {
            var home = DiagnosticHome(root); var path = Path.Combine(home, "sessions", "rollout-inside.jsonl"); File.WriteAllText(path, "{}");
            var service = new SessionDiagnosticService(); var listing = await service.ListAsync(home, () => home);
            var outside = Path.Combine(root, "rollout-outside.jsonl"); File.WriteAllText(outside, "{}"); var file = new SessionDiagnosticFile(Path.GetFileName(outside), outside, DateTime.UtcNow);
            await ThrowsAsync<InvalidDataException>(() => service.ReadAsync(listing, file, () => (home, outside)));
            Throws<InvalidDataException>(() => SessionDiagnostics.Read(home, outside));
            await ThrowsAsync<OperationCanceledException>(() => service.ReadAsync(listing, listing.Files.Single(), () => (home + "-other", path)));
        }));
        tests.Add(("session diagnostics reject late home and selection changes without returning old observations", async () =>
        {
            var home = DiagnosticHome(root); var path = Path.Combine(home, "sessions", "rollout-delayed.jsonl"); File.WriteAllText(path, "{}");
            var runtime = new DiagnosticFixture(); var service = new SessionDiagnosticService(runtime); var currentHome = home; string? currentPath = path;
            runtime.List = _ => { currentHome = home + "-other"; return Task.FromResult<IReadOnlyList<SessionDiagnosticFile>>([]); };
            await ThrowsAsync<OperationCanceledException>(() => service.ListAsync(home, () => currentHome));
            currentHome = home; runtime.List = _ => Task.FromResult<IReadOnlyList<SessionDiagnosticFile>>([new(Path.GetFileName(path), path, DateTime.UtcNow)]);
            var listing = await service.ListAsync(home, () => home);
            runtime.Read = _ => { currentPath = null; return Task.FromResult(Observation()); };
            await ThrowsAsync<OperationCanceledException>(() => service.ReadAsync(listing, listing.Files.Single(), () => (currentHome, currentPath)));
            currentPath = path; runtime.Read = _ => { currentHome = home + "-other"; return Task.FromResult(Observation()); };
            await ThrowsAsync<OperationCanceledException>(() => service.ReadAsync(listing, listing.Files.Single(), () => (currentHome, currentPath)));
        }));
        tests.Add(("session diagnostic cancellation prevents runtime work and discards uncooperative late results", async () =>
        {
            var home = DiagnosticHome(root); var path = Path.Combine(home, "sessions", "rollout-cancel.jsonl");
            var runtime = new DiagnosticFixture { List = _ => Task.FromResult<IReadOnlyList<SessionDiagnosticFile>>([new(Path.GetFileName(path), path, DateTime.UtcNow)]) };
            var service = new SessionDiagnosticService(runtime); var listing = await service.ListAsync(home, () => home); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => service.ListAsync(home, () => home, cancellation.Token));
            await ThrowsAsync<OperationCanceledException>(() => service.ReadAsync(listing, listing.Files.Single(), () => (home, path), cancellation.Token)); Assert(runtime.Reads == 0);
            using var delayed = new CancellationTokenSource(); runtime.Read = _ => { delayed.Cancel(); return Task.FromResult(Observation()); };
            await ThrowsAsync<OperationCanceledException>(() => service.ReadAsync(listing, listing.Files.Single(), () => (home, path), delayed.Token));
            Throws<OperationCanceledException>(() => SessionDiagnostics.Read(home, path, cancellation.Token));
        }));
        tests.Add(("session diagnostic bounded tail accepts active append and preserves only recent safe fields", () =>
        {
            var home = DiagnosticHome(root); var path = Path.Combine(home, "sessions", "rollout-tail.jsonl");
            File.WriteAllText(path, new string('x', 9 * 1024 * 1024) + "\n{\"type\":\"turn_context\",\"payload\":{\"model\":\"recent-model\",\"effort\":\"high\"}}\n");
            using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var result = SessionDiagnostics.Read(home, path); Assert(result.Model == "recent-model" && result.Reasoning == "high"); return Task.CompletedTask;
        }));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request)); }
    private sealed class DownloadFixture : IUpdateDownloadRuntime
    {
        internal int Checks, Downloads;
        internal Func<CancellationToken, Task<ReleaseUpdate?>> Check = _ => Task.FromResult<ReleaseUpdate?>(Release());
        internal Func<CancellationToken, Task<VerifiedInstaller>> Download = _ => throw new InvalidOperationException("No fixture download configured.");
        public Task<ReleaseUpdate?> CheckAsync(Version current, CancellationToken cancellation) { Checks++; return Check(cancellation); }
        public Task<VerifiedInstaller> DownloadAsync(ReleaseUpdate update, string directory, IProgress<int>? progress, CancellationToken cancellation) { Downloads++; return Download(cancellation); }
    }
    private sealed class InstallFixture
    {
        internal readonly string Home; internal readonly VerifiedInstaller Installer; internal readonly WorkspaceApplyCoordinator Operations;
        internal readonly InstallationRuntime Runtime = new(); internal readonly InstallationGateways Gateways = new(); internal readonly UpdateInstallationService Service;
        internal InstallFixture(string root)
        {
            Home = Path.Combine(root, "installation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Home);
            var path = Path.Combine(Home, "never-executed-setup.exe"); File.WriteAllText(path, "offline fixture, never execute"); Installer = new(path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            Operations = new(new WorkspaceSession(Home)); Service = new(Operations, Gateways, Runtime);
        }
    }
    private sealed class InstallationRuntime : IUpdateInstallationRuntime
    {
        internal readonly List<string> Trace = []; internal int Launched; internal Action<VerifiedInstaller>? OnLaunch; internal Func<CancellationToken, Task>? Stop, Wait;
        public Task WaitForAutomaticAsync(CancellationToken cancellation) => Wait?.Invoke(cancellation) ?? Task.CompletedTask;
        public Task StopEnhancementsAsync(CancellationToken cancellation) { Trace.Add("enhancements"); return Stop?.Invoke(cancellation) ?? Task.CompletedTask; }
        public void Launch(VerifiedInstaller installer) { Trace.Add("launch"); OnLaunch?.Invoke(installer); Launched++; }
    }
    private sealed class InstallationGateways : IGatewayManagementRuntime
    {
        internal readonly Dictionary<GatewayKind, bool?> Running = new() { [GatewayKind.Model] = true, [GatewayKind.Mobile] = true };
        internal readonly List<string> Trace = []; internal bool FailStop, Uncertain, ChangeAfterStop;
        public Task<GatewayDefinition> LoadAsync(GatewayKind kind, string home, CancellationToken cancellation)
            => Task.FromResult(new GatewayDefinition(kind, home, true, true, "fixture", ChangeAfterStop && Trace.Count > 0 ? "changed" : "baseline"));
        public Task<GatewayProbe> ProbeAsync(GatewayDefinition definition, CancellationToken cancellation) => Task.FromResult(new GatewayProbe(Uncertain ? null : Running[definition.Kind]));
        public Task StartAsync(GatewayDefinition definition, CancellationToken cancellation) => throw new InvalidOperationException("Update must never start a gateway.");
        public Task StopAsync(GatewayDefinition definition, CancellationToken cancellation)
        { Trace.Add("stop:" + definition.Kind); if (FailStop) throw new IOException("fixture stop failure"); Running[definition.Kind] = false; return Task.CompletedTask; }
    }
    private sealed class DiagnosticFixture : ISessionDiagnosticRuntime
    {
        internal int Reads; internal Func<CancellationToken, Task<IReadOnlyList<SessionDiagnosticFile>>> List = _ => Task.FromResult<IReadOnlyList<SessionDiagnosticFile>>([]);
        internal Func<CancellationToken, Task<SessionObservation>> Read = _ => Task.FromResult(Observation());
        public Task<IReadOnlyList<SessionDiagnosticFile>> ListAsync(string home, CancellationToken cancellation) => List(cancellation);
        public Task<SessionObservation> ReadAsync(string home, string path, CancellationToken cancellation) { Reads++; return Read(cancellation); }
    }
    private static string DiagnosticHome(string root) { var home = Path.Combine(root, "diagnostic-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(home, "sessions")); return home; }
    private static SessionObservation Observation() => new("rollout-fixture.jsonl", "old-result", null, null, null, null, null, false);
    private static ReleaseUpdate Release() => new(new(99, 0, 0), new(GitHubUpdates.Repository + "/releases/tag/v99.0.0"), new(GitHubUpdates.Repository + "/releases/download/v99.0.0/setup.exe"), 1, new(GitHubUpdates.Repository + "/releases/download/v99.0.0/checksums.txt"), null);
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Update/diagnostic regression"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
