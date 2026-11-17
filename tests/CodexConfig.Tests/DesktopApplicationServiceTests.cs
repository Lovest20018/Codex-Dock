using System.Text;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class DesktopApplicationServiceTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("desktop application service cancellation and unknown selections cannot launch or close a desktop", async () =>
        {
            var fixture = new Fixture(root); fixture.Interaction.Confirm = _ => Task.FromResult(false);
            var result = await fixture.Apply();
            Assert(result.Outcome == DesktopApplyOutcome.Cancelled && fixture.Runtime.Restarts == 0 && fixture.Session.State.Phase == WorkspacePhase.WaitingForUser && !fixture.Service.IsBusy);
            fixture.Runtime.Targets = [fixture.First, fixture.Second]; fixture.Interaction.Select = _ => null;
            result = await fixture.Apply(); Assert(result.Outcome == DesktopApplyOutcome.Cancelled && fixture.Runtime.Restarts == 0);
            fixture.Interaction.Select = _ => fixture.First with { Executable = Path.Combine(fixture.Home, "unlisted.exe") };
            result = await fixture.Apply(); Assert(result.Outcome == DesktopApplyOutcome.Failed && fixture.Runtime.Restarts == 0);
            fixture.RequireUnchanged();
        }));
        tests.Add(("desktop application service serializes execution and preserves an active operation on duplicate clicks", async () =>
        {
            var fixture = new Fixture(root); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Runtime.Restart = async () => { entered.SetResult(); await release.Task; };
            var applying = fixture.Apply(); await entered.Task;
            var duplicate = await fixture.Apply();
            Assert(duplicate.Outcome == DesktopApplyOutcome.Busy && fixture.Service.IsBusy && fixture.Runtime.Restarts == 1);
            release.SetResult(); var result = await applying;
            Assert(result.Outcome == DesktopApplyOutcome.Completed && !fixture.Service.IsBusy && fixture.Session.State.Phase == WorkspacePhase.Applied);
            fixture.RequireUnchanged();
        }));
        tests.Add(("desktop application service rechecks configuration directory cancellation and preferences after confirmation", async () =>
        {
            foreach (var change in new[] { "config", "home", "preferences", "cancellation" })
            {
                var fixture = new Fixture(root); using var cancellation = new CancellationTokenSource();
                fixture.Interaction.Confirm = _ =>
                {
                    if (change == "config") File.AppendAllText(Path.Combine(fixture.Home, "config.toml"), "service_tier='priority'\n");
                    if (change == "home") fixture.Interaction.ActiveHome = fixture.Home + "-other";
                    if (change == "preferences") fixture.Preferences.SetConnectionRecovery(true);
                    if (change == "cancellation") cancellation.Cancel();
                    return Task.FromResult(true);
                };
                var result = await fixture.Apply(cancellation.Token);
                Assert(result.Outcome == (change == "config" ? DesktopApplyOutcome.Conflict : change == "cancellation" ? DesktopApplyOutcome.Cancelled : DesktopApplyOutcome.Failed) && fixture.Runtime.Restarts == 0);
                Assert(!fixture.Service.IsBusy && !fixture.Session.State.Operation.IsBusy);
            }
        }));
        tests.Add(("desktop application service keeps enhancement-only retry on the verified opened instance", async () =>
        {
            var fixture = new Fixture(root); fixture.Preferences.SetConnectionRecovery(true);
            fixture.Runtime.Preparation = new(new([], new Dictionary<string, int>()) { SpeedEnabled = false, ConnectionsEnabled = true }, 12345);
            fixture.Runtime.AttachError = new IOException("fixture enhancement unavailable");
            var result = await fixture.Apply();
            Assert(result is { Outcome: DesktopApplyOutcome.Failed, DesktopOpened: true } && fixture.Runtime.Restarts == 1 && fixture.Service.PendingRetry is not null && fixture.Interaction.Opened == 1);
            fixture.Runtime.Targets = [fixture.Runtime.OpenedTarget]; fixture.Runtime.AttachError = null;
            result = await fixture.Apply();
            Assert(result is { Outcome: DesktopApplyOutcome.Completed, EnhancementsApplied: true } && fixture.Runtime.Restarts == 1 && fixture.Runtime.Attachments == 2 && fixture.Service.PendingRetry is null);
            fixture.RequireUnchanged();
        }));
        tests.Add(("desktop application service rejects changed enhancement retry choices and preserves the active old session on exit refusal", async () =>
        {
            var fixture = new Fixture(root); fixture.Preferences.SetConnectionRecovery(true);
            fixture.Runtime.Preparation = new(new([], new Dictionary<string, int>()) { SpeedEnabled = false, ConnectionsEnabled = true }, 12345);
            fixture.Runtime.AttachError = new IOException("fixture attachment failed");
            await fixture.Apply(); fixture.Runtime.Targets = [fixture.Runtime.OpenedTarget];
            fixture.Preferences.SetConnectionRecovery(false);
            var result = await fixture.Apply();
            Assert(result.Outcome == DesktopApplyOutcome.Failed && fixture.Runtime.Restarts == 1 && fixture.Service.PendingRetry is null);
            fixture.Service.Reset(); fixture.Runtime.Targets = [fixture.First]; fixture.Runtime.Preparation = new();
            fixture.Runtime.Running = true; fixture.Runtime.Restart = () => throw new CodexDesktopExitTimeoutException("veto");
            var stopped = fixture.Runtime.Stops;
            result = await fixture.Apply();
            Assert(result.Outcome == DesktopApplyOutcome.ExitBlocked && fixture.Runtime.Stops == stopped && fixture.Service.BlockedExit is not null);
        }));
        tests.Add(("desktop application service batch force retry requires separate confirmation and retains original captured roots", async () =>
        {
            var fixture = new Fixture(root); fixture.Runtime.Targets = [fixture.First, fixture.Second];
            var combined = fixture.Second with { AlsoExit = [fixture.First] };
            fixture.Interaction.Select = _ => combined;
            fixture.Runtime.Restart = () => throw new CodexDesktopExitTimeoutException("one old instance remains");
            var result = await fixture.Apply();
            Assert(result.Outcome == DesktopApplyOutcome.ExitBlocked && fixture.Service.BlockedExit?.Target.ExitRoots.Count == 2);
            fixture.Runtime.Targets = [fixture.First]; fixture.Interaction.Confirm = _ => Task.FromResult(false);
            result = await fixture.Service.ApplyAsync(new(fixture.Saved, ForceExit: true), fixture.Interaction);
            Assert(result.Outcome == DesktopApplyOutcome.Cancelled && fixture.Runtime.Restarts == 1);
            fixture.Interaction.Confirm = confirmation =>
            {
                Assert(confirmation.ForceExit && ReferenceEquals(confirmation.Target, combined) && confirmation.Message.Contains("全部退出"));
                return Task.FromResult(true);
            };
            fixture.Runtime.Restart = () => Task.CompletedTask;
            result = await fixture.Service.ApplyAsync(new(fixture.Saved, ForceExit: true), fixture.Interaction);
            Assert(result.Outcome == DesktopApplyOutcome.Completed && fixture.Runtime.ForceConfirmed && fixture.Runtime.Restarts == 2);
            fixture.Service.Reset();
            result = await fixture.Service.ApplyAsync(new(fixture.Saved, ForceExit: true), fixture.Interaction);
            Assert(result.Outcome == DesktopApplyOutcome.Failed && fixture.Runtime.Restarts == 2);
            fixture.RequireUnchanged();
        }));
        tests.Add(("desktop application service model gateway must be ready before any old desktop closes", async () =>
        {
            var fixture = new Fixture(root);
            var connection = MixedConnection.Create(new("https://fixture.invalid/v1", "fake-gateway-key", "fixture") { Kind = ConnectionKind.ResponsesApi });
            File.WriteAllBytes(Path.Combine(fixture.Home, MixedConnection.FileName), connection.Protect());
            File.WriteAllText(Path.Combine(fixture.Home, "config.toml"), "model='fixture'\nopenai_base_url='" + connection.NativeBaseUrl + "'\n");
            var saved = fixture.Store.Load(fixture.Home);
            fixture.Runtime.GatewayError = new IOException("fixture gateway unavailable");
            var result = await fixture.Service.ApplyAsync(new(saved), fixture.Interaction);
            Assert(result.Outcome == DesktopApplyOutcome.Failed && fixture.Runtime.Restarts == 0 && fixture.Runtime.Trace.SequenceEqual(new[] { "gateway" }));
            fixture.Runtime.GatewayError = null; fixture.Runtime.Trace.Clear();
            result = await fixture.Service.ApplyAsync(new(saved), fixture.Interaction);
            Assert(result.Outcome == DesktopApplyOutcome.Completed && fixture.Runtime.Trace.IndexOf("gateway") < fixture.Runtime.Trace.IndexOf("restart") && saved.SameContents(fixture.Store.Load(fixture.Home)));
        }));
        tests.Add(("desktop application shared conflict preview hides credential values and retains useful setting differences", () =>
        {
            var home = Path.Combine(root, "diff-fixture");
            var before = new ConfigSnapshot(home, Encoding.UTF8.GetBytes("model='first'\n[oauth.tokens]\naccess_token='fake-private-before'\n"), null, null);
            var after = before with { Config = Encoding.UTF8.GetBytes("model='second'\n[oauth.tokens]\naccess_token='fake-private-after'\n") };
            var preview = DesktopApplicationService.DescribeChanges(before, after);
            Assert(preview.Contains("first") && preview.Contains("second") && preview.Contains("oauth.tokens.access_token") && !preview.Contains("fake-private"));
            return Task.CompletedTask;
        }));
    }

    private sealed class Fixture
    {
        public readonly string Home;
        public readonly ConfigStore Store = new(new NoEnvironment(), _ => null);
        public readonly ConfigSnapshot Saved;
        public readonly WorkspaceSession Session;
        public readonly ClientEnhancementPreferencesService Preferences;
        public readonly Runtime Runtime;
        public readonly Interaction Interaction;
        public readonly DesktopApplicationService Service;
        public readonly CodexDesktopTarget First, Second;
        public Fixture(string root)
        {
            Home = Path.Combine(root, "desktop-apply-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Home);
            File.WriteAllText(Path.Combine(Home, "config.toml"), "model='fixture'\nopenai_base_url='https://fixture.invalid/v1'\n");
            File.WriteAllText(Path.Combine(Home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-apply-key\"}");
            Saved = Store.Load(Home); Session = new(Home); Session.Loaded(Home, Saved);
            Preferences = new(Home, Path.Combine(Home, "connections"), Path.Combine(Home, "speed"), Path.Combine(Home, "automatic"));
            var executable = Path.Combine(Home, "Codex.exe");
            First = new(executable, [new(101, 1, 134390198764749156, executable)]) { UserDataDirectory = Home + "-first" };
            Second = new(executable, [new(102, 1, 134390198764749157, executable)]) { UserDataDirectory = Home + "-second" };
            Runtime = new(First); Interaction = new(Home); Service = new(Store, new(Session), Preferences, Runtime);
        }
        public Task<DesktopApplyResult> Apply(CancellationToken cancellation = default) => Service.ApplyAsync(new(Saved), Interaction, cancellation);
        public void RequireUnchanged() => Assert(Saved.SameContents(Store.Load(Home)));
    }
    private sealed class Runtime(CodexDesktopTarget initial) : IDesktopApplyRuntime
    {
        public IReadOnlyList<CodexDesktopTarget> Targets = [initial];
        public CodexDesktopTarget OpenedTarget = initial with { Processes = [new(201, 1, 134390198764749166, initial.Executable)] };
        public DesktopEnhancementPreparation Preparation = new();
        public Func<Task> Restart = () => Task.CompletedTask;
        public Exception? AttachError, GatewayError;
        public readonly List<string> Trace = [];
        public int Restarts, Attachments, Stops;
        public bool Running, ForceConfirmed;
        public bool IsEnhancementRunning => Running;
        public Task<IReadOnlyList<CodexDesktopTarget>> FindTargetsAsync(CancellationToken cancellation) => Task.FromResult(Targets);
        public Task<DesktopEnhancementPreparation> PrepareEnhancementsAsync(CodexDesktopTarget target, ConfigSnapshot saved, CancellationToken cancellation) => Task.FromResult(Preparation);
        public Task PrepareGatewayAsync(ConfigSnapshot saved, CancellationToken cancellation)
        { Trace.Add("gateway"); if (GatewayError is not null) throw GatewayError; return Task.CompletedTask; }
        public async Task<CodexDesktopTarget> RestartAsync(CodexDesktopTarget target, ConfigSnapshot saved, Action validateSaved, int? port, Action<CodexDesktopPhase> progress, bool forceConfirmed, CancellationToken cancellation)
        { validateSaved(); Trace.Add("restart"); Restarts++; ForceConfirmed = forceConfirmed; progress(CodexDesktopPhase.Closing); await Restart(); validateSaved(); return OpenedTarget; }
        public Task StopEnhancementsAsync() { Stops++; Running = false; return Task.CompletedTask; }
        public Task AttachEnhancementsAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port, CancellationToken cancellation)
        { Attachments++; if (AttachError is not null) throw AttachError; Running = true; return Task.CompletedTask; }
    }
    private sealed class Interaction(string home) : IDesktopApplyInteraction
    {
        public string ActiveHome { get; set; } = home;
        public Func<DesktopApplyConfirmation, Task<bool>> Confirm = _ => Task.FromResult(true);
        public Func<IReadOnlyList<CodexDesktopTarget>, CodexDesktopTarget?> Select = targets => targets[0];
        public int Opened;
        public void ValidateContext() { }
        public Task<CodexDesktopTarget?> ChooseAsync(IReadOnlyList<CodexDesktopTarget> targets, CancellationToken cancellation) => Task.FromResult(Select(targets));
        public Task<bool> ConfirmAsync(DesktopApplyConfirmation confirmation, CancellationToken cancellation) => Confirm(confirmation);
        public void Report(DesktopApplyProgress progress) { }
        public void TargetSelected(CodexDesktopTarget target, ConfigSnapshot saved) { }
        public void DesktopOpened(CodexDesktopTarget target, ConfigSnapshot saved) => Opened++;
    }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Desktop application service regression"); }
    private sealed class NoEnvironment : IUserEnvironment { public string? Read() => null; public void Write(string? value) => throw new InvalidOperationException("No environment writes"); }
}
