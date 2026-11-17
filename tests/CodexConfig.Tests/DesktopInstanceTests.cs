using System.Diagnostics;
using CodexConfig.Core;

internal static class DesktopInstanceTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        var executable = Environment.ProcessPath!;
        string Home() { var home = Path.Combine(root, "instances-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home); return home; }
        CodexDesktopProcess Main(int id, string? profile, int parent = 1, long started = 1) => new(id, parent, started, executable)
            { Arguments = new(DesktopProcessRole.Main, profile) };
        CodexDesktopProcess Helper(int id, int parent, string? profile = null, long started = 2) => new(id, parent, started, executable)
            { Arguments = new(DesktopProcessRole.Helper, profile) };
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        Test("desktop argument facts retain only role and absolute user data path", () =>
        {
            var profile = Home();
            Assert(DesktopProcessArguments.Parse("\"C:\\Apps\\Codex.exe\" --remote-debugging-port=12345 --user-data-dir=\"" + profile + "\"") == new DesktopProcessArguments(DesktopProcessRole.Main, profile));
            Assert(DesktopProcessArguments.Parse("Codex.exe --type=crashpad-handler --user-data-dir \"" + profile + "\"") == new DesktopProcessArguments(DesktopProcessRole.Helper, profile));
            Assert(DesktopProcessArguments.Parse("Codex.exe --type renderer").Role == DesktopProcessRole.Helper);
            Assert(DesktopProcessArguments.Parse("Codex.exe --user-data-dir=relative") == DesktopProcessArguments.Unknown);
            Assert(DesktopProcessArguments.Parse("Codex.exe --user-data-dir=\"" + profile + "\" --user-data-dir=\"" + Home() + "\"") == DesktopProcessArguments.Unknown);
        });
        Test("desktop discovery groups two real roots and ignores orphaned renderer helpers", () =>
        {
            var first = Home(); var second = Home();
            var host = new ScopedPlatform(executable) { Processes = [Main(100, null), Helper(101, 100, first), Helper(102, 101), Main(200, second, 100), Helper(201, 200), Helper(999, 5000)] };
            var targets = new CodexDesktop(host).FindTargets();
            Assert(targets.Count == 2 && targets[0].MainProcess!.Id == 100 && targets[0].UserDataDirectory == first && targets[0].Processes.Select(p => p.Id).SequenceEqual([100, 101, 102]));
            Assert(targets[1].MainProcess!.Id == 200 && targets[1].Processes.Select(p => p.Id).SequenceEqual([200, 201]));
            host.Processes = [new(300, 1, 5, executable), new(301, 300, 4, executable)];
            Assert(new CodexDesktop(host).FindTargets().Count == 2); // Parent PID was reused after this child started.
        });
        tests.Add(("selected desktop restart preserves the other root and profile and admits both renderer trees", async () =>
        {
            var first = Home(); var second = Home(); var host = new ScopedPlatform(executable)
                { Processes = [Main(100, first), Helper(101, 100), Main(200, second), Helper(201, 200)] };
            var desktop = new CodexDesktop(host); var target = desktop.FindTargets()[0];
            host.Processes.Add(Helper(102, 101, started: 3)); host.Processes.Add(Helper(202, 201, started: 3));
            var opened = await desktop.RestartAsync(target, Home(), null);
            Assert(host.Closed.SequenceEqual([100]) && host.Forced.Count == 0 && host.Starts == 1 && host.Processes.Any(p => p.Id == 200) && host.Processes.Any(p => p.Id == 202));
            Assert(opened.MainProcess!.Id == 300 && opened.Processes.Count == 1 && opened.UserDataDirectory == first &&
                host.StartInfo!.ArgumentList.Contains("--user-data-dir=" + first) && host.StartInfo.Environment["CODEX_ELECTRON_USER_DATA_PATH"] == first);
        }));
        tests.Add(("multi-instance restart rejects unknown duplicate and conflicting profiles before closing", async () =>
        {
            var first = Home(); var second = Home(); var host = new ScopedPlatform(executable); var desktop = new CodexDesktop(host);
            foreach (var processes in new CodexDesktopProcess[][] {
                [Main(100, first), Main(200, null)], [Main(100, first), Main(200, first)],
                [Main(100, first), Helper(101, 100, second), Main(200, second)] })
            {
                host.Processes = processes.ToList();
                await ThrowsAsync<InvalidOperationException>(() => desktop.RestartAsync(desktop.FindTargets()[0], Home(), null));
                Assert(host.Closed.Count == 0 && host.Forced.Count == 0 && host.Starts == 0);
            }
        }));
        tests.Add(("scoped force fallback rejects replacement roots and never terminates the other instance", async () =>
        {
            var first = Home(); var second = Home(); var host = new ScopedPlatform(executable)
                { Processes = [Main(100, first), Main(200, second)], RefuseClose = true };
            var desktop = new CodexDesktop(host, TimeSpan.FromMilliseconds(120)); var target = desktop.FindTargets()[0];
            await ThrowsAsync<CodexDesktopExitTimeoutException>(() => desktop.RestartAsync(target, Home(), null));
            Assert(host.Forced.Count == 0 && host.Starts == 0);
            await desktop.RestartAsync(target, Home(), null, forceConfirmed: true);
            Assert(host.Forced.SequenceEqual([100]) && host.Processes.Any(p => p.Id == 200));
            host.Processes = [Main(100, first, started: 999), Main(200, second)];
            await ThrowsAsync<InvalidOperationException>(() => desktop.RestartAsync(target, Home(), null, forceConfirmed: true));
            Assert(host.Forced.Count == 1 && host.Starts == 1);
        }));
        tests.Add(("startup verification cannot accept an existing other instance or a new wrong profile", async () =>
        {
            var first = Home(); var second = Home(); var host = new ScopedPlatform(executable)
                { Processes = [Main(100, first), Main(200, second)], SuppressLaunch = true };
            var desktop = new CodexDesktop(host, startTimeout: TimeSpan.FromMilliseconds(120));
            await ThrowsAsync<IOException>(() => desktop.RestartAsync(desktop.FindTargets()[0], Home(), null));
            Assert(host.Processes.Single().Id == 200 && host.Starts == 1);
            host.Processes = [Main(100, first), Main(200, second)]; host.SuppressLaunch = false; host.WrongProfile = second;
            await ThrowsAsync<IOException>(() => desktop.RestartAsync(desktop.FindTargets()[0], Home(), null));
            Assert(host.Processes.Any(p => p.Id == 200) && host.Starts == 2);
        }));
        tests.Add(("new independent roots during scoped exit reject launch while other captured descendants remain allowed", async () =>
        {
            var host = new ScopedPlatform(executable) { Processes = [Main(100, Home()), Main(200, Home())] };
            var desktop = new CodexDesktop(host); var target = desktop.FindTargets()[0]; host.Processes.Add(Main(400, Home()));
            await ThrowsAsync<InvalidOperationException>(() => desktop.RestartAsync(target, Home(), null));
            Assert(host.Closed.Count == 0 && host.Starts == 0);
        }));
        tests.Add(("confirmed batch restart waits for every selected root and launches exactly once", async () =>
        {
            var first = Home(); var host = new ScopedPlatform(executable)
                { Processes = [Main(100, first), Helper(101, 100), Main(200, Home()), Helper(201, 200), Main(400, Home())] };
            var desktop = new CodexDesktop(host, TimeSpan.FromMilliseconds(120)); var targets = desktop.FindTargets();
            var target = targets[0].WithOtherInstancesToExit([targets[1]]); host.RefuseIds.Add(200);
            await ThrowsAsync<CodexDesktopExitTimeoutException>(() => desktop.RestartAsync(target, Home(), null));
            Assert(host.Closed.SequenceEqual([100, 200]) && host.Starts == 0 && host.Processes.Any(process => process.Id == 200) && host.Processes.Any(process => process.Id == 400));
            var opened = await desktop.RestartAsync(target, Home(), null, forceConfirmed: true);
            Assert(host.Forced.SequenceEqual([200]) && host.Starts == 1 && opened.UserDataDirectory == first && host.Processes.Any(process => process.Id == 400));
        }));
        tests.Add(("batch force restart closes both captured roots without accepting replacements or extra roots", async () =>
        {
            var host = new ScopedPlatform(executable) { Processes = [Main(100, Home()), Main(200, Home())] };
            var desktop = new CodexDesktop(host); var targets = desktop.FindTargets(); var target = targets[0].WithOtherInstancesToExit([targets[1]]);
            var captured = host.Processes.ToList(); host.Processes[1] = Main(200, targets[1].UserDataDirectory, started: 999);
            await ThrowsAsync<InvalidOperationException>(() => desktop.RestartAsync(target, Home(), null, forceConfirmed: true));
            Assert(host.Forced.Count == 0 && host.Starts == 0);
            host.Processes = captured;
            await desktop.RestartAsync(target, Home(), null, forceConfirmed: true);
            Assert(host.Forced.SequenceEqual([100, 200]) && host.Starts == 1 && host.Processes.Single().Id == 300);
        }));
    }
    private sealed class ScopedPlatform(string executable) : ICodexDesktopPlatform
    {
        internal List<CodexDesktopProcess> Processes = [];
        internal List<int> Closed = [], Forced = [];
        internal HashSet<int> RefuseIds = [];
        internal bool RefuseClose, SuppressLaunch;
        internal string? WrongProfile;
        internal int Starts;
        internal ProcessStartInfo? StartInfo;
        public IReadOnlyList<CodexDesktopProcess> Running() => Processes.ToArray();
        public IReadOnlyList<string> Installed() => [executable];
        public void Close(CodexDesktopProcess process) { Closed.Add(process.Id); if (!RefuseClose && !RefuseIds.Contains(process.Id)) Remove(process); }
        public void ForceClose(CodexDesktopProcess process) { Forced.Add(process.Id); Remove(process); }
        private void Remove(CodexDesktopProcess root)
        {
            var target = CodexDesktop.Group(Processes).Single(target => target.MainProcess!.Id == root.Id);
            Processes.RemoveAll(process => target.Processes.Contains(process));
        }
        public void Start(ProcessStartInfo start)
        {
            Starts++; StartInfo = start;
            start.Environment.TryGetValue("CODEX_ELECTRON_USER_DATA_PATH", out var profile);
            if (!SuppressLaunch) Processes.Add(new(300, 1, 10, executable)
                { Arguments = new(DesktopProcessRole.Main, WrongProfile ?? profile) });
        }
    }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Desktop instance isolation regression"); }
    private static async Task ThrowsAsync<T>(Func<Task> run) where T : Exception
    { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
