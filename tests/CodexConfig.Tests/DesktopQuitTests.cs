using System.Diagnostics;
using System.Text.Json;
using CodexConfig.Core;

internal static class DesktopQuitTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        string Folder() { var path = Path.Combine(root, "quit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
        var expected = new CodexDesktopProcess(123, 1, 456, Path.Combine(Path.GetFullPath(root), "app with spaces", "Codex.exe"));
        CodexDesktopQuitClient Client(string scenario, string folder, TimeSpan? timeout = null) =>
            new(Environment.ProcessPath!, ["--desktop-quit-fixture", scenario, folder], timeout ?? TimeSpan.FromSeconds(8));
        tests.Add(("native Quit helper receives exact PID, creation time and literal path once", async () =>
        {
            var folder = Folder(); await Client("submitted", folder).RequestAsync(expected);
            var launches = File.ReadAllLines(Path.Combine(folder, "calls.jsonl")); Assert(launches.Length == 1);
            var arguments = JsonSerializer.Deserialize<string[]>(launches[0])!;
            Assert(arguments.Length == 5 && arguments[0] == "123" && arguments[1] == "--quit" && arguments[2] == expected.Executable && arguments[3] == "456");
            Assert(!Directory.Exists(Path.GetDirectoryName(arguments[4])));
        }));
        tests.Add(("native Quit accepts exited receipt but rejects wrong identity, invalid output and pending prompts", async () =>
        {
            await Client("exited", Folder()).RequestAsync(expected);
            foreach (var scenario in new[] { "wrong-pid", "invalid-pid", "invalid", "oversized", "failed", "pending", "dispatch-pending" })
            {
                var folder = Folder(); var error = await ThrowsAsync<IOException>(() => Client(scenario, folder).RequestAsync(expected));
                Assert(File.ReadAllLines(Path.Combine(folder, "calls.jsonl")).Length == 1);
                if (scenario is "failed" or "pending" or "dispatch-pending") Assert(error.Message == "Fixture prompt; keep the desktop running");
            }
            await ThrowsAsync<IOException>(() => new CodexDesktopQuitClient(Path.Combine(Folder(), "missing.exe")).RequestAsync(expected));
        }));
        tests.Add(("desktop restart preserves its target when the Quit helper fails or needs confirmation", async () =>
        {
            foreach (var scenario in new[] { "failed", "dispatch-pending" })
            {
                var folder = Folder(); var host = new HelperPlatform(Client(scenario, folder)); var desktop = new CodexDesktop(host);
                var error = await ThrowsAsync<CodexDesktopCloseException>(() => desktop.RestartAsync(desktop.FindTarget(), folder, null));
                Assert(error.Message == "Fixture prompt; keep the desktop running" && host.Starts == 0 && host.Forced == 0 && host.Running().Count == 1);
            }
        }));
        tests.Add(("native Quit observes uncertain dispatch without replaying or ending the selected app", async () =>
        {
            foreach (var scenario in new[] { "lost-receipt", "dispatch-timeout" })
            {
                var folder = Folder();
                await Client(scenario, folder, scenario == "dispatch-timeout" ? TimeSpan.FromSeconds(2) : null).RequestAsync(expected);
                Assert(File.ReadAllLines(Path.Combine(folder, "calls.jsonl")).Length == 1);
                if (scenario == "dispatch-timeout") Assert(File.Exists(Path.Combine(folder, "cancel-observed")));
            }
        }));
        tests.Add(("native Quit cancellation and timeout stop only the helper and suppress late dispatch", async () =>
        {
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); var folder = Folder();
                await ThrowsAsync<OperationCanceledException>(() => Client("wait", folder).RequestAsync(expected, cancelled.Token));
                Assert(!File.Exists(Path.Combine(folder, "calls.jsonl")));
            }
            var pending = Folder(); using var cancel = new CancellationTokenSource();
            var operation = Client("wait", pending).RequestAsync(expected, cancel.Token);
            await WaitForFileAsync(Path.Combine(pending, "calls.jsonl")); cancel.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => operation);
            Assert(File.Exists(Path.Combine(pending, "cancel-observed")) && !File.Exists(Path.Combine(pending, "late-dispatch")));
            var blocked = Folder(); var watch = Stopwatch.StartNew();
            await ThrowsAsync<IOException>(() => Client("blocked", blocked, TimeSpan.FromSeconds(2)).RequestAsync(expected));
            Assert(watch.Elapsed < TimeSpan.FromSeconds(7));
            var args = JsonSerializer.Deserialize<string[]>(File.ReadAllLines(Path.Combine(blocked, "calls.jsonl")).Single())!;
            Assert(!Directory.Exists(Path.GetDirectoryName(args[4])) && !File.Exists(Path.Combine(blocked, "late-dispatch")));
        }));
        tests.Add(("desktop restart forwards cancellation while normal Quit is pending and does not force or relaunch", async () =>
        {
            var home = Folder(); using var cancellation = new CancellationTokenSource();
            var host = new PendingPlatform(); var desktop = new CodexDesktop(host);
            var operation = desktop.RestartAsync(desktop.FindTarget(), home, null, cancellation: cancellation.Token);
            await host.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => operation);
            Assert(host.Starts == 0 && host.Forced == 0 && host.Running().Count == 1 && host.Home == home);
        }));
    }

    // Runs only as a child test process. It never opens or closes a target PID.
    internal static async Task<int> RunFixtureAsync(string scenario, string root, string[] arguments)
    {
        if (arguments.Length != 5 || arguments[1] != "--quit") throw new ArgumentException("Invalid fixture arguments");
        var pid = int.Parse(arguments[0]);
        File.AppendAllText(Path.Combine(root, "calls.jsonl"), JsonSerializer.Serialize(arguments) + "\n");
        void Reply(string state) => Console.WriteLine(JsonSerializer.Serialize(new { quitState = state, pid, reason = "Fixture prompt; keep the desktop running" }));
        if (scenario is "dispatch-timeout" or "lost-receipt" or "dispatch-pending")
        { Console.WriteLine(JsonSerializer.Serialize(new { quitPhase = "dispatching", pid })); Console.Out.Flush(); }
        if (scenario is "wait" or "dispatch-timeout" or "blocked")
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(15))
            {
                if (scenario != "blocked" && File.Exists(arguments[4])) { File.WriteAllText(Path.Combine(root, "cancel-observed"), "yes"); return 0; }
                await Task.Delay(20);
            }
            File.WriteAllText(Path.Combine(root, "late-dispatch"), "unexpected"); return 1;
        }
        switch (scenario)
        {
            case "wrong-pid": pid++; Reply("submitted"); break;
            case "invalid-pid": Console.WriteLine("{\"pid\":\"123\",\"quitState\":\"submitted\",\"reason\":\"bad\"}"); break;
            case "invalid": Console.WriteLine("bad receipt"); break;
            case "oversized": Console.WriteLine(new string('x', 20000)); Reply("submitted"); break;
            case "lost-receipt": Console.WriteLine("bad receipt"); return 1;
            case "dispatch-pending": Reply("pending"); break;
            default: Reply(scenario); break;
        }
        return scenario == "failed" ? 1 : 0;
    }

    private sealed class PendingPlatform : ICodexDesktopPlatform
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Starts, Forced;
        internal string? Home;
        public IReadOnlyList<CodexDesktopProcess> Running() => [new(123, 1, 456, Environment.ProcessPath!)];
        public IReadOnlyList<string> Installed() => [Environment.ProcessPath!];
        public void Close(CodexDesktopProcess process) => throw new Exception("The synchronous close must not be used");
        public async Task CloseAsync(CodexDesktopProcess process, CancellationToken cancellation)
        { Entered.SetResult(); await Task.Delay(Timeout.Infinite, cancellation); }
        public Task CloseAsync(CodexDesktopProcess process, CancellationToken cancellation, string home)
        { Home = home; return CloseAsync(process, cancellation); }
        public void ForceClose(CodexDesktopProcess process) => Forced++;
        public void Start(ProcessStartInfo start) => Starts++;
    }
    private sealed class HelperPlatform(CodexDesktopQuitClient client) : ICodexDesktopPlatform
    {
        internal int Starts, Forced;
        public IReadOnlyList<CodexDesktopProcess> Running() => [new(123, 1, 456, Environment.ProcessPath!)];
        public IReadOnlyList<string> Installed() => [Environment.ProcessPath!];
        public void Close(CodexDesktopProcess process) => throw new Exception("The synchronous close must not be used");
        public Task CloseAsync(CodexDesktopProcess process, CancellationToken cancellation) => client.RequestAsync(process, cancellation);
        public void ForceClose(CodexDesktopProcess process) => Forced++;
        public void Start(ProcessStartInfo start) => Starts++;
    }
    private static async Task WaitForFileAsync(string path)
    {
        var watch = Stopwatch.StartNew();
        while (!File.Exists(path)) { if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException(path); await Task.Delay(20); }
    }
    private static void Assert(bool ok) { if (!ok) throw new Exception("Native Quit assertion failed"); }
    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T error) { return error; } throw new Exception("Expected " + typeof(T).Name); }
}
