using CodexConfig.Application;
using CodexConfig.Core;

internal static class AutomaticEnhancementServiceTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("shared automatic reconnect defaults off and waits for an explicitly remembered instance", async () =>
        {
            var fixture = new Fixture(root, remember: false);
            await fixture.Try(); Assert(fixture.Runtime.Discoveries == 0 && !File.Exists(fixture.Preferences.AutomaticSettings.SettingsPath));
            fixture.Preferences.SetAutomaticReconnect(true); await fixture.Try();
            Assert(fixture.Runtime.Discoveries == 0 && fixture.Service.Status.Contains("等待首次")); fixture.RequireUnchanged();
        }));
        tests.Add(("shared automatic reconnect serializes reentrant attempts and resumes only the remembered endpoint", async () =>
        {
            var fixture = new Fixture(root); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Runtime.Discover = async cancellation => { entered.SetResult(); await release.Task.WaitAsync(cancellation); };
            var first = fixture.Try(); await entered.Task;
            var second = fixture.Try(); Assert(ReferenceEquals(first, second) && fixture.Service.IsBusy);
            release.SetResult(); await first;
            Assert(fixture.Runtime.Resumes == 1 && fixture.Runtime.Port == 12345 && fixture.Runtime.Stops == 1 && !fixture.Service.IsBusy);
            fixture.Service.Cancel(); fixture.RequireUnchanged();
        }));
        tests.Add(("shared automatic reconnect rejects snapshot process profile and patch changes before touching a connection", async () =>
        {
            foreach (var change in new[] { "snapshot", "process", "profile", "executable", "plan" })
            {
                var fixture = new Fixture(root);
                if (change == "snapshot") File.AppendAllText(Path.Combine(fixture.Home, "config.toml"), "service_tier='priority'\n");
                if (change == "process") fixture.Runtime.Targets = [fixture.Target with { Processes = [new(42, 1, 101, fixture.Target.Executable)] }];
                if (change == "profile") fixture.Runtime.Targets = [fixture.Target with { UserDataDirectory = fixture.Home + "-other" }];
                if (change == "executable") fixture.Runtime.Targets = [fixture.Target with { Executable = Path.Combine(fixture.Home, "other.exe") }];
                if (change == "plan") fixture.Runtime.Plan = fixture.Runtime.Plan with { SpeedEnabled = true };
                await fixture.Try(); Assert(fixture.Runtime.Resumes == 0 && fixture.Runtime.Stops == 0 && !fixture.Service.IsBusy);
            }
        }));
        tests.Add(("shared automatic reconnect rechecks drafts directory options and saved bytes after asynchronous discovery", async () =>
        {
            foreach (var change in new[] { "draft", "directory", "options", "saved" })
            {
                var fixture = new Fixture(root);
                fixture.Runtime.Inspect = () =>
                {
                    if (change == "draft") fixture.Eligible = false;
                    if (change == "directory") fixture.Preferences.SetHome(fixture.Home + "-other");
                    if (change == "options") fixture.Preferences.SetConnectionRecovery(false);
                    if (change == "saved") File.AppendAllText(Path.Combine(fixture.Home, "config.toml"), "service_tier='priority'\n");
                };
                await fixture.Try(); Assert(fixture.Runtime.Resumes == 0 && fixture.Runtime.Stops == 0);
            }
        }));
        tests.Add(("shared automatic reconnect failure backs off until time advances or explicit retry resets it", async () =>
        {
            var fixture = new Fixture(root); fixture.Runtime.Targets = [];
            await fixture.Try(); await fixture.Try(); Assert(fixture.Runtime.Discoveries == 1);
            fixture.Clock.Now = fixture.Clock.Now.AddSeconds(21); await fixture.Try(); Assert(fixture.Runtime.Discoveries == 2);
            fixture.Service.ResetRetry(); await fixture.Try(); Assert(fixture.Runtime.Discoveries == 3 && fixture.Runtime.Resumes == 0);
        }));
        tests.Add(("shared automatic reconnect cancellation drains the pending operation and never retains a disposed token source", async () =>
        {
            var fixture = new Fixture(root); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Runtime.Discover = async cancellation => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellation); };
            var pending = fixture.Try(); await entered.Task;
            fixture.Preferences.SetAutomaticReconnect(false); await fixture.Service.CancelAndWaitAsync(); await pending;
            fixture.Service.Cancel(); Assert(!fixture.Service.IsBusy && fixture.Runtime.Resumes == 0 && fixture.Runtime.Stops == 0);
            await fixture.Try(); Assert(fixture.Runtime.Discoveries == 1); fixture.RequireUnchanged();
        }));
        tests.Add(("shared automatic reconnect retains a live session when feature choices change", async () =>
        {
            var fixture = new Fixture(root); fixture.Runtime.IsRunning = true;
            fixture.Runtime.MatchingFeatures = false;
            await fixture.Try(); Assert(fixture.Service.Status.Contains("选项已改变") && fixture.Runtime.Stops == 0 && fixture.Runtime.Discoveries == 0);
        }));
        tests.Add(("shared automatic reconnect cleans up a failed attachment without leaving busy state", async () =>
        {
            var fixture = new Fixture(root); fixture.Runtime.ResumeError = new IOException("fixture resume failure");
            await fixture.Try(); Assert(fixture.Runtime.Resumes == 1 && fixture.Runtime.Stops == 2 && !fixture.Service.IsBusy && fixture.Service.Status.Contains("fixture resume failure"));
            fixture.Service.Cancel(); fixture.RequireUnchanged();
        }));
        tests.Add(("shared automatic reconnect skips damaged opt-in storage without discovering desktops", async () =>
        {
            var fixture = new Fixture(root); File.WriteAllText(fixture.Preferences.AutomaticSettings.SettingsPath, "{\"Enabled\":\"invalid\"}");
            await fixture.Try(); Assert(fixture.Runtime.Discoveries == 0 && fixture.Service.Status.Contains("无法读取") && !fixture.Service.IsBusy);
            fixture.Service.Cancel();
        }));
    }

    private sealed class Fixture
    {
        internal readonly string Home;
        internal readonly ConfigStore Store = new(new MemoryEnvironment(), _ => null);
        internal readonly ClientEnhancementPreferencesService Preferences;
        internal readonly Runtime Runtime;
        internal readonly AutomaticEnhancementService Service;
        internal readonly ManualClock Clock = new();
        internal readonly ConfigSnapshot Saved;
        internal readonly CodexDesktopTarget Target;
        internal bool Eligible = true;
        internal Fixture(string root, bool remember = true)
        {
            Home = Path.Combine(root, "auto-service-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Home);
            File.WriteAllText(Path.Combine(Home, "config.toml"), "model='fixture'\n"); Saved = Store.Load(Home);
            Preferences = new(Home, Path.Combine(Home, "preferences"), Path.Combine(Home, "preferences"), Path.Combine(Home, "preferences"));
            Preferences.SetConnectionRecovery(true);
            Target = new(Path.Combine(Home, "fixture.exe"), [new(42, 1, 100, Path.Combine(Home, "fixture.exe"))]) { UserDataDirectory = Home };
            Runtime = new() { Targets = [Target] };
            if (remember)
            {
                Preferences.AutomaticSettings.Remember(new(Target.Executable, Home, 42, 100, 12345, DesktopEnhancementSettings.SnapshotHash(Saved), DesktopSpeedSession.PlanHash(Runtime.Plan)));
                Preferences.SetAutomaticReconnect(true);
            }
            Service = new(Store, Preferences, Runtime, Clock);
        }
        internal Task Try() => Service.TryResumeAsync(Saved, () => Eligible, CancellationToken.None);
        internal void RequireUnchanged() => Assert(Saved.SameContents(Store.Load(Home)));
    }

    private sealed class ManualClock : TimeProvider
    { internal DateTimeOffset Now = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Runtime : IAutomaticEnhancementRuntime
    {
        public bool IsRunning { get; set; }
        public bool IsApplied => IsRunning;
        internal bool MatchingFeatures = true;
        internal int Discoveries, Resumes, Stops, Port;
        internal IReadOnlyList<CodexDesktopTarget> Targets = [];
        internal DesktopSpeedPlan Plan = new([], new Dictionary<string, int>()) { SpeedEnabled = false, ConnectionsEnabled = true };
        internal Func<CancellationToken, Task>? Discover;
        internal Action? Inspect;
        internal Exception? ResumeError;
        public bool HasFeatures(bool speed, bool connections) => MatchingFeatures;
        public async Task<IReadOnlyList<CodexDesktopTarget>> FindTargetsAsync(CancellationToken cancellation)
        { Discoveries++; if (Discover is not null) await Discover(cancellation); return Targets; }
        public Task<DesktopSpeedPlan> InspectAsync(CodexDesktopTarget target, bool speed, bool connections, CancellationToken cancellation)
        { Inspect?.Invoke(); return Task.FromResult(Plan); }
        public Task StopAsync() { Stops++; IsRunning = false; return Task.CompletedTask; }
        public Task ResumeAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port, CancellationToken cancellation)
        { Resumes++; Port = port; if (ResumeError is not null) throw ResumeError; IsRunning = true; return Task.CompletedTask; }
    }
    private sealed class MemoryEnvironment : IUserEnvironment
    { public string? Read() => null; public void Write(string? value) => throw new InvalidOperationException(); }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Automatic enhancement service assertion failed."); }
}
