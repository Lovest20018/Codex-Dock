using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CodexConfig.Core;

internal static class DesktopRestartTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        var executable = Environment.ProcessPath!;
        string Home() { var home = Path.Combine(root, "desktop-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home); return home; }
        CodexDesktopProcess Process(int id = 100, int parent = 1, long started = 1) => new(id, parent, started, executable);
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        Test("desktop discovery separates the Store GUI from CLI runtimes, ChatGPT and child processes", () =>
        {
            const string family = "OpenAI.Codex_2p2nqsd0c76g0";
            Assert(WindowsCodexDesktopPlatform.IsDesktopExecutable(@"C:\Store\app\ChatGPT.exe", family));
            Assert(WindowsCodexDesktopPlatform.IsDesktopExecutable(@"C:\Store\app\Codex.exe", family));
            Assert(!WindowsCodexDesktopPlatform.IsDesktopExecutable(@"C:\Store\app\resources\codex.exe", family));
            Assert(!WindowsCodexDesktopPlatform.IsDesktopExecutable(@"C:\Local\OpenAI\Codex\bin\hash\codex.exe", family));
            Assert(!WindowsCodexDesktopPlatform.IsDesktopExecutable(@"C:\Store\app\ChatGPT.exe", "OpenAI.ChatGPT_unrelated"));
            Assert(WindowsCodexDesktopPlatform.IsDesktopExecutable(@"C:\Store\app\ChatGPT.exe", null, [@"C:\Store\app\ChatGPT.exe"]));
            Assert(!WindowsCodexDesktopPlatform.IsDesktopExecutable(@"C:\Other\app\ChatGPT.exe", null, [@"C:\Store\app\ChatGPT.exe"]));
            Assert(!WindowsCodexDesktopPlatform.IsDesktopExecutable(@"C:\Store\app\ChatGPT.exe", "unrelated", [@"C:\Store\app\ChatGPT.exe"]));
            Assert(WindowsCodexDesktopPlatform.IsAncestor(100, 300, new Dictionary<int, int> { [300] = 200, [200] = 100 }));
            Assert(!WindowsCodexDesktopPlatform.IsAncestor(100, 300, new Dictionary<int, int> { [300] = 200, [200] = 400, [400] = 200 }));
            Assert(!WindowsCodexDesktopPlatform.IsAncestor(100, 300, new Dictionary<int, int> { [300] = 200 }));
            var host = new FakePlatform(executable) { Processes = [Process(), Process(101, 100)] };
            var desktop = new CodexDesktop(host); var target = desktop.FindTarget();
            Assert(target.IsRunning && target.Processes.Count == 2);
            host.Processes.Add(Process(102, 1)); Throws<InvalidOperationException>(() => desktop.FindTarget());
        });
        Test("desktop launch explicitly uses the saved home and replaces a stale authentication environment", () =>
        {
            var home = Home();
            var start = CodexDesktop.CreateStartInfo(executable, home, "https://fixture.invalid");
            Assert(!start.UseShellExecute && start.ArgumentList.Count == 0 && start.Environment["CODEX_HOME"] == home && start.Environment[WindowsUserEnvironment.Variable] == "https://fixture.invalid");
            Assert(!CodexDesktop.CreateStartInfo(executable, home, null).Environment.ContainsKey(WindowsUserEnvironment.Variable));
            Throws<ArgumentException>(() => CodexDesktop.CreateStartInfo("codex.exe", home, null));
        });
        Test("Store launch uses the package-specific alias and rejects stale or unrelated registrations before exit", () =>
        {
            var package = Home(); var gui = Path.Combine(package, "app", "ChatGPT.exe"); var local = Home();
            Directory.CreateDirectory(Path.GetDirectoryName(gui)!);
            File.WriteAllText(Path.Combine(package, "AppxManifest.xml"), """
                <Package xmlns="urn:fixture"><Applications><Application Id="App" Executable="app/ChatGPT.exe"><Extensions>
                <Extension Category="windows.appExecutionAlias"><AppExecutionAlias><ExecutionAlias Alias="chatgpt-desktop.exe" /></AppExecutionAlias></Extension>
                </Extensions></Application><Application Id="Other" Executable="other.exe" /></Applications></Package>
                """);
            byte[] Alias(string family, string app, string path)
            {
                var fields = System.Text.Encoding.Unicode.GetBytes(family + "\0" + app + "\0" + path + "\0" + "0\0");
                var data = new byte[12 + fields.Length];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data, 0x8000001b);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), checked((ushort)(fields.Length + 4)));
                fields.CopyTo(data, 12); return data;
            }
            var family = CodexStoreLaunch.Family; var expected = Path.Combine(local, "Microsoft", "WindowsApps", family, "chatgpt-desktop.exe");
            Assert(CodexStoreLaunch.Resolve(gui, local, path => { Assert(path == expected); return Alias(family, family + "!App", gui); }) == expected);
            Throws<IOException>(() => CodexStoreLaunch.Resolve(gui, local, _ => Alias(family, family + "!Other", gui)));
            Throws<IOException>(() => CodexStoreLaunch.Resolve(gui, local, _ => Alias(family, family + "!App", gui + ".old")));
            Throws<IOException>(() => CodexStoreLaunch.VerifyAlias([1, 2, 3], gui, family + "!App"));
        });
        tests.Add(("desktop restart honors vetoes, instance replacement, cancellation and changed saved files", async () =>
        {
            var host = new FakePlatform(executable) { Processes = [Process()], RefuseClose = true };
            var desktop = new CodexDesktop(host, TimeSpan.FromMilliseconds(120)); var home = Home();
            await ThrowsAsync<TimeoutException>(() => desktop.RestartAsync(desktop.FindTarget(), home, null));
            Assert(host.Starts == 0 && host.Processes.Count == 1);
            host.RefuseClose = false; var old = desktop.FindTarget(); host.Processes = [Process(started: 2)];
            await ThrowsAsync<InvalidOperationException>(() => desktop.RestartAsync(old, home, null)); Assert(host.Starts == 0);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => desktop.RestartAsync(desktop.FindTarget(), home, null, cancellation: cancellation.Token)); Assert(host.Starts == 0);
            var validates = 0;
            await ThrowsAsync<IOException>(() => desktop.RestartAsync(desktop.FindTarget(), home, null, () =>
            { if (++validates == 2) throw new IOException("Fixture file changed after exit"); }));
            Assert(validates == 2 && host.Processes.Count == 0 && host.Starts == 0);
        }));
        tests.Add(("desktop restart closes only its root and launches once after all captured GUI processes exit", async () =>
        {
            var host = new FakePlatform(executable) { Processes = [Process(), Process(101, 100)] };
            var desktop = new CodexDesktop(host); var target = desktop.FindTarget();
            await desktop.RestartAsync(target, Home(), "https://fixture.invalid");
            Assert(host.Closed.SequenceEqual([100]) && host.Starts == 1 && host.Processes.Single().Id == 200);
            Assert(host.StartInfo!.Environment[WindowsUserEnvironment.Variable] == "https://fixture.invalid");
        }));
        tests.Add(("desktop restart accepts new renderer descendants and reports actual launch stages", async () =>
        {
            var host = new FakePlatform(executable) { Processes = [Process()] }; var desktop = new CodexDesktop(host);
            var target = desktop.FindTarget(); host.Processes.Add(Process(101, 100, 2)); host.Processes.Add(Process(102, 101, 3));
            var stages = new List<CodexDesktopPhase>();
            var opened = await desktop.RestartAsync(target, Home(), null, progress: stages.Add);
            Assert(host.Closed.SequenceEqual([100]) && host.Forced.Count == 0 && opened.Processes.Single().Id == 200);
            Assert(stages.SequenceEqual([CodexDesktopPhase.Preparing, CodexDesktopPhase.Closing, CodexDesktopPhase.Starting, CodexDesktopPhase.Verifying]));
            host.Processes = [Process()]; target = desktop.FindTarget(); host.Processes.Add(Process(500));
            await ThrowsAsync<InvalidOperationException>(() => desktop.RestartAsync(target, Home(), null));
            Assert(host.Starts == 1 && host.Forced.Count == 0);
        }));
        tests.Add(("desktop forced fallback requires the explicit flag and captured identity before ending an instance", async () =>
        {
            var host = new FakePlatform(executable) { Processes = [Process()], RefuseClose = true };
            var desktop = new CodexDesktop(host, TimeSpan.FromMilliseconds(120)); var target = desktop.FindTarget();
            await ThrowsAsync<CodexDesktopExitTimeoutException>(() => desktop.RestartAsync(target, Home(), null));
            Assert(host.Forced.Count == 0 && host.Starts == 0);
            await desktop.RestartAsync(target, Home(), null, forceConfirmed: true);
            Assert(host.Forced.SequenceEqual([100]) && host.Starts == 1);
            host.Processes = [Process(started: 999)];
            await ThrowsAsync<InvalidOperationException>(() => desktop.RestartAsync(target, Home(), null, forceConfirmed: true));
            Assert(host.Forced.Count == 1 && host.Starts == 1);
        }));
        tests.Add(("desktop speed debugging is opt-in, loopback-only and validates the port before closing", async () =>
        {
            var host = new FakePlatform(executable) { Processes = [Process()] }; var desktop = new CodexDesktop(host);
            await ThrowsAsync<ArgumentOutOfRangeException>(() => desktop.RestartAsync(desktop.FindTarget(), Home(), null, speedDebugPort: 80));
            Assert(host.Closed.Count == 0 && host.Starts == 0);
            await desktop.RestartAsync(desktop.FindTarget(), Home(), null, speedDebugPort: 19283);
            Assert(host.StartInfo!.ArgumentList.SequenceEqual(["--remote-debugging-address=127.0.0.1", "--remote-debugging-port=19283"]));
        }));
        tests.Add(("desktop startup registration failures leave the running app untouched", async () =>
        {
            var host = new FakePlatform(executable) { Processes = [Process()], StartPreparationError = new IOException("Fixture alias is stale") };
            var desktop = new CodexDesktop(host);
            await ThrowsAsync<IOException>(() => desktop.RestartAsync(desktop.FindTarget(), Home(), null));
            Assert(host.Closed.Count == 0 && host.Starts == 0 && host.Processes.Count == 1);
        }));
        tests.Add(("native Windows desktop restart replaces an isolated GUI fixture without touching configuration", async () =>
        {
            var home = Home(); var config = Path.Combine(home, "config.toml"); File.WriteAllText(config, "# fixture\nmodel='unchanged'\n");
            var original = File.ReadAllBytes(config);
            var host = new FixturePlatform(executable, home); var desktop = new CodexDesktop(host);
            try
            {
                await desktop.RestartAsync(desktop.FindTarget(), home, "https://fixture.invalid");
                var first = desktop.FindTarget(); Assert(first.Processes.Count == 1);
                using var firstReport = await Report(first.Processes.Single().Id);
                await desktop.RestartAsync(first, home, null);
                var second = desktop.FindTarget(); Assert(second.IsRunning && second.Processes.Single().Id != first.Processes.Single().Id);
                using var report = await Report(second.Processes.Single().Id);
                Assert(report.RootElement.GetProperty("home").GetString() == home && report.RootElement.GetProperty("auth").ValueKind == JsonValueKind.Null);
                Assert(original.SequenceEqual(File.ReadAllBytes(config)) && host.Starts == 2);
                await desktop.RestartAsync(second, home, null, forceConfirmed: true);
                var third = desktop.FindTarget();
                using var forcedReport = await Report(third.Processes.Single().Id);
                Assert(third.Processes.Single().Id != second.Processes.Single().Id && host.Starts == 3 &&
                    forcedReport.RootElement.GetProperty("home").GetString() == home && original.SequenceEqual(File.ReadAllBytes(config)));
                async Task<JsonDocument> Report(int pid)
                {
                    var timer = Stopwatch.StartNew();
                    while (timer.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        try
                        {
                            var value = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(home, "launched.json")));
                            if (value.RootElement.GetProperty("pid").GetInt32() == pid) return value;
                            value.Dispose();
                        }
                        catch (Exception error) when (error is IOException or JsonException) { }
                        await Task.Delay(50);
                    }
                    throw new TimeoutException("Isolated desktop fixture did not create its window");
                }
            }
            finally { await host.Cleanup(); }
        }));
    }

    private sealed class FakePlatform(string executable) : ICodexDesktopPlatform
    {
        internal List<CodexDesktopProcess> Processes = [];
        internal List<int> Closed = [];
        internal List<int> Forced = [];
        internal bool RefuseClose;
        internal int Starts;
        internal ProcessStartInfo? StartInfo;
        internal Exception? StartPreparationError;
        public IReadOnlyList<CodexDesktopProcess> Running() => Processes.ToArray();
        public IReadOnlyList<string> Installed() => [executable];
        public void Close(CodexDesktopProcess process) { Closed.Add(process.Id); if (!RefuseClose) Processes.Clear(); }
        public void ForceClose(CodexDesktopProcess process) { Assert(Processes.Contains(process)); Forced.Add(process.Id); Processes.Clear(); }
        public void PrepareStart(ProcessStartInfo start) { if (StartPreparationError is not null) throw StartPreparationError; }
        public void Start(ProcessStartInfo start) { Starts++; StartInfo = start; Processes = [new(200, 1, 200, executable)]; }
    }

    private sealed class FixturePlatform(string executable, string output) : ICodexDesktopPlatform
    {
        private readonly WindowsCodexDesktopPlatform native = new(executable);
        private readonly List<Process> owned = [];
        internal int Starts;
        public IReadOnlyList<CodexDesktopProcess> Running() => native.Running();
        public IReadOnlyList<string> Installed() => native.Installed();
        public void Close(CodexDesktopProcess process) => native.Close(process);
        public void ForceClose(CodexDesktopProcess process) => native.ForceClose(process);
        public void Start(ProcessStartInfo start)
        {
            start.ArgumentList.Add("--desktop-restart-fixture"); start.ArgumentList.Add(output);
            owned.Add(Process.Start(start)!); Starts++;
        }
        internal async Task Cleanup()
        {
            foreach (var process in owned)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.CloseMainWindow();
                        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                        catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                    }
                }
                finally { process.Dispose(); }
            }
        }
    }

    // A separate off-screen GUI process exercises normal WM_CLOSE, native identity checks and launch environment.
    // It has no access to real Codex state and automatically closes after thirty seconds if the test fails.
    internal static int RunFixture(string output)
    {
        var name = "CodexDock.RestartFixture." + Environment.ProcessId;
        WindowProcedure callback = (window, message, wparam, lparam) =>
        {
            if (message is 0x0010 or 0x0113) { DestroyWindow(window); return IntPtr.Zero; }
            if (message == 0x0002) { PostQuitMessage(0); return IntPtr.Zero; }
            return DefWindowProc(window, message, wparam, lparam);
        };
        var windowClass = new WindowClass { Procedure = callback, ClassName = name, Instance = GetModuleHandle(null) };
        if (RegisterClass(ref windowClass) == 0) return 2;
        var window = CreateWindowEx(0, name, name, 0x10cf0000, -30000, -30000, 240, 120, IntPtr.Zero, IntPtr.Zero, windowClass.Instance, IntPtr.Zero);
        if (window == IntPtr.Zero) return 3;
        SetTimer(window, new UIntPtr(1), 30000, IntPtr.Zero);
        File.WriteAllText(Path.Combine(output, "launched.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, home = Environment.GetEnvironmentVariable("CODEX_HOME"), auth = Environment.GetEnvironmentVariable(WindowsUserEnvironment.Variable) }));
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
        GC.KeepAlive(callback); return 0;
    }

    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Desktop restart regression"); }

    // Inspect a newly created, suspended Store process, then discard it without
    // ever running the app's entry point or contacting its existing instance.
    internal static int CheckStoreLaunch()
    {
        var platform = new WindowsCodexDesktopPlatform(); var target = new CodexDesktop(platform).FindTarget();
        var start = CodexDesktop.CreateStartInfo(target.Executable, Path.GetFullPath(".test-home"), null);
        platform.PrepareStart(start);
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcess(start.FileName, new System.Text.StringBuilder("\"" + start.FileName + "\""), IntPtr.Zero, IntPtr.Zero,
            false, 0x00000004, IntPtr.Zero, start.WorkingDirectory, ref startup, out var created))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            uint length = 512; var family = new System.Text.StringBuilder((int)length);
            Assert(GetPackageFamilyName(created.Process, ref length, family) == 0 && family.ToString() == CodexStoreLaunch.Family);
            uint size = 32768; var path = new System.Text.StringBuilder((int)size);
            Assert(QueryFullProcessImageName(created.Process, 0, path, ref size) && CodexDesktop.SamePath(path.ToString(), target.Executable));
            Console.WriteLine("Store launch PASS: package identity and executable verified while suspended; no application code executed.");
            return 0;
        }
        finally
        {
            TerminateProcess(created.Process, 0); CloseHandle(created.Thread); CloseHandle(created.Process);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size; public string? Reserved, Desktop, Title;
        public int X, Y, Width, Height, Columns, Rows, Fill, Flags; public short Show, ReservedSize;
        public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string executable, System.Text.StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation created);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFamilyName(IntPtr process, ref uint length, System.Text.StringBuilder name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, System.Text.StringBuilder path, ref uint length);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);

    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    {
        public uint Style; public WindowProcedure Procedure; public int ClassExtra, WindowExtra; public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName; public string ClassName;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        public IntPtr Window; public uint Id; public UIntPtr Wparam; public IntPtr Lparam; public uint Time; public int X, Y; public uint Private;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint extra, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr window, UIntPtr id, uint timeout, IntPtr callback);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out Message message, IntPtr window, uint first, uint last);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref Message message);
}
