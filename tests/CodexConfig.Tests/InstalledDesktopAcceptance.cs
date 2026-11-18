using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CodexConfig.Core;

// Explicit opt-in acceptance of the installed app's lifecycle. Never part of
// the automatic suite and never selects or closes an existing user's instance.
internal static class InstalledDesktopAcceptance
{
    internal sealed record CompatibilityFixture(int DebugPort, int ProviderPort);
    internal static async Task RunAsync(string root, bool inspect = false, CompatibilityFixture? compatibility = null)
    {
        if (compatibility is not null && (compatibility.DebugPort is < 1024 or > 65535 || compatibility.ProviderPort is < 1024 or > 65535 || compatibility.DebugPort == compatibility.ProviderPort))
            throw new ArgumentException("The compatibility fixture requires two distinct loopback ports.");
        root = Path.GetFullPath(root);
        var allowed = Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar;
        if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new ArgumentException("Installed desktop acceptance requires a new directory under this checkout's artifacts.");
        Directory.CreateDirectory(root);
        var home = Path.Combine(root, "codex-home"); var profile = Path.Combine(root, "electron-profile");
        Directory.CreateDirectory(home); Directory.CreateDirectory(profile);
        var port = compatibility?.ProviderPort ?? 9;
        File.WriteAllText(Path.Combine(home, "config.toml"), $"# Isolated desktop fixture; all model traffic stays on loopback\nmodel='fixture-model'\nmodel_provider='desktop_fixture'\n[model_providers.desktop_fixture]\nname='Desktop acceptance fixture'\nbase_url='http://127.0.0.1:{port}/v1'\nwire_api='responses'\nrequires_openai_auth=true\n");
        var auth = JsonSerializer.Serialize(new { auth_mode = "apikey", OPENAI_API_KEY = "fixture-key-do-not-send" });
        File.WriteAllText(Path.Combine(home, "auth.json"), auth);
        // A new profile otherwise stops in first-run onboarding, which has no
        // application menu. Seed only the flags observed after skipping it in
        // this empty fixture; never copy another profile or import its data.
        File.WriteAllText(Path.Combine(home, ".codex-global-state.json"), JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["electron-persisted-atom-state"] = new Dictionary<string, object>
            { ["electron:onboarding-projectless-completed"] = true, ["last_completed_onboarding"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
            ["electron-owner-atoms:signed-out"] = new Dictionary<string, object>
            {
                ["electron:onboarding-welcome-v2-role-state"] = new
                { completedConversationalOnboarding = true, conversationalOnboardingSkipped = true, roles = Array.Empty<string>(), personalizedSuggestionsEnabled = false, workMode = "coding" }
            }
        }));
        var native = new WindowsCodexDesktopPlatform(); var before = native.Running();
        var executable = native.Installed().Single();
        var platform = new ScopedPlatform(native, before, home, profile, compatibility);
        var desktop = new CodexDesktop(platform, startTimeout: TimeSpan.FromSeconds(30));
        var target = new CodexDesktopTarget(executable, []) { UserDataDirectory = profile, OtherProcesses = before, RequiresProfile = before.Count > 0 };
        var checks = new List<string>(); var openedIds = new List<int>();
        void Check(bool ok, string message)
        { if (!ok) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(root, "progress.log"), "PASS " + message + "\n"); }
        void Validate()
        {
            foreach (var existing in before.Where(process => process.Arguments.Role == DesktopProcessRole.Main))
                if (!native.Running().Any(current => CodexDesktop.SameProcess(current, existing)))
                    throw new IOException("A pre-existing desktop changed; the acceptance run must stop.");
        }
        async Task InspectAsync(CodexDesktopProcess process, string stage)
        {
            if (!inspect) return;
            File.WriteAllText(Path.Combine(root, "fixture" + stage + ".json"), JsonSerializer.Serialize(new { MainProcess = process, profile, debugPort = compatibility?.DebugPort }));
            var inspection = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(root, "continue" + stage)))
            { if (inspection.Elapsed > TimeSpan.FromMinutes(compatibility is null ? 15 : 4)) throw new TimeoutException("Isolated inspection timed out"); await Task.Delay(200); }
        }
        try
        {
            var first = await desktop.RestartAsync(target, home, null, Validate, speedDebugPort: compatibility?.DebugPort);
            openedIds.Add(first.MainProcess!.Id);
            Check(first.IsRunning && first.UserDataDirectory == profile && before.All(old => !CodexDesktop.SameProcess(old, first.MainProcess)),
                "The installed Store application opens a separate explicit profile and a temporary Codex home");
            await WaitForWindowAsync(native, first.MainProcess);
            await Task.Delay(10000); // Cold renderer startup is outside the user's restart request.
            Validate();
            Check(Directory.EnumerateFileSystemEntries(profile).Any() && File.ReadAllText(Path.Combine(home, "auth.json")) == auth,
                "The installed application uses only a fictional API credential and a loopback provider without importing an account");
            await InspectAsync(first.MainProcess, "");
            // Use a fresh inventory carrying the actual process identities.
            var selected = desktop.FindTargets().Single(candidate => candidate.UserDataDirectory == profile);
            var next = await desktop.RestartAsync(selected, home, null, Validate, speedDebugPort: compatibility?.DebugPort);
            openedIds.Add(next.MainProcess!.Id);
            Check(next.MainProcess.Id != first.MainProcess.Id && next.UserDataDirectory == profile && platform.Starts == 2 && platform.Forced == 0,
                "Native Quit restarts the visible installed desktop in the same profile without ending its process");
            var window = await WaitForWindowAsync(native, next.MainProcess);
            await Task.Delay(10000); // Test an initialized app being closed to its tray.
            await InspectAsync(next.MainProcess, "-next");
            Validate(); platform.ValidateOwned(next.MainProcess);
            if (!PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero)) throw new IOException("Could not close the isolated fixture to its tray.");
            var hiding = Stopwatch.StartNew();
            while (IsWindowVisible(window))
            { if (hiding.Elapsed > TimeSpan.FromSeconds(10)) throw new IOException("The isolated fixture did not hide to its tray."); await Task.Delay(100); }
            platform.ValidateOwned(next.MainProcess);
            Check(true, "The isolated desktop remains running after its actual window closes to the tray");
            await InspectAsync(next.MainProcess, "-tray");
            selected = desktop.FindTargets().Single(candidate => candidate.UserDataDirectory == profile);
            var fromTray = await desktop.RestartAsync(selected, home, null, Validate, speedDebugPort: compatibility?.DebugPort);
            openedIds.Add(fromTray.MainProcess!.Id);
            Check(fromTray.MainProcess.Id != next.MainProcess.Id && fromTray.UserDataDirectory == profile && platform.Starts == 3 && platform.Forced == 0,
                "Native Quit restarts the tray-hidden installed desktop without force and without a duplicate profile");
            await WaitForWindowAsync(native, fromTray.MainProcess);
            await Task.Delay(15000);
            Validate(); Check(true, "Every pre-existing main desktop process retained its original identity through the restart");
        }
        catch when (inspect)
        {
            var current = desktop.FindTargets().SingleOrDefault(candidate => candidate.UserDataDirectory == profile);
            if (current?.MainProcess is { } main) await InspectAsync(main, "-failure");
            throw;
        }
        finally
        {
            // Only request normal close for a process whose parsed profile is ours.
            foreach (var owned in CodexDesktop.Group(native.Running()).Where(candidate => candidate.UserDataDirectory == profile))
                if (owned.MainProcess is { } main) { try { await platform.CloseAsync(main, CancellationToken.None); } catch (Exception error) when (error is InvalidOperationException or IOException) { } }
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(25) && native.Running().Any(process => process.Arguments.UserDataDirectory == profile))
                await Task.Delay(100);
            if (native.Running().Any(process => process.Arguments.UserDataDirectory == profile))
            { platform.Forced++; await CleanupAsync(root); }
        }
        Validate(); Check(true, "The temporary installed desktop is cleaned up and leaves existing desktops running");
        Check(platform.Forced == 0, "Normal restart and final cleanup never request the force-exit path");
        await File.WriteAllTextAsync(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new
        {
            status = "PASS", scope = compatibility is null ? "Installed desktop visible and tray-hidden normal Quit/restart in an isolated empty profile; no force-exit, login, task, message or enhancement injection" :
                "Isolated pure API fixture with loopback model service, normal Quit/restart and loopback renderer observation",
            executable, home, profile, openedIds, tests = checks,
            forceExitRequests = platform.Forced,
            quitHelperSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "mobile", "runtime", "client-helpers", "codex-quit-helper.exe")))),
            preservedMainProcesses = before.Where(process => process.Arguments.Role == DesktopProcessRole.Main).Select(process => new { process.Id, process.Started }),
            coreSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CodexDesktop).Assembly.Location)))
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static async Task CleanupAsync(string root)
    {
        root = Path.GetFullPath(root); var profile = Path.Combine(root, "electron-profile");
        if (!root.StartsWith(Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(root, "codex-home", "config.toml")))
            throw new ArgumentException("Cleanup requires the created acceptance fixture.");
        var native = new WindowsCodexDesktopPlatform();
        foreach (var owned in CodexDesktop.Group(native.Running()).Where(target => target.UserDataDirectory == profile))
            if (owned.MainProcess is { } main) native.ForceClose(main);
        for (var i = 0; i < 100 && native.Running().Any(process => process.Arguments.UserDataDirectory == profile); i++) await Task.Delay(100);
        if (native.Running().Any(process => process.Arguments.UserDataDirectory == profile)) throw new IOException("Test desktop cleanup did not finish.");
        Console.WriteLine("PASS cleanup of the exact empty acceptance profile only");
    }

    private sealed class ScopedPlatform(WindowsCodexDesktopPlatform native, IReadOnlyList<CodexDesktopProcess> protectedProcesses, string home, string profile,
        CompatibilityFixture? compatibility) : ICodexDesktopPlatform
    {
        internal int Starts, Forced;
        public IReadOnlyList<CodexDesktopProcess> Running() => native.Running();
        public IReadOnlyList<string> Installed() => native.Installed();
        public void PrepareStart(ProcessStartInfo start) => native.PrepareStart(start);
        internal void ValidateOwned(CodexDesktopProcess process)
        {
            if (protectedProcesses.Any(existing => CodexDesktop.SameProcess(existing, process)) ||
                !native.Running().Any(current => CodexDesktop.SameProcess(current, process) && current.Arguments.UserDataDirectory == profile))
                throw new IOException("Refused to close an unowned desktop process.");
        }
        public void Close(CodexDesktopProcess process) => CloseAsync(process, CancellationToken.None).GetAwaiter().GetResult();
        public Task CloseAsync(CodexDesktopProcess process, CancellationToken cancellation)
        { ValidateOwned(process); return native.CloseAsync(process, cancellation, home); }
        public Task CloseAsync(CodexDesktopProcess process, CancellationToken cancellation, string savedHome)
        { if (savedHome != home) throw new IOException("Refused another Codex home"); return CloseAsync(process, cancellation); }
        public void ForceClose(CodexDesktopProcess process)
        {
            if (protectedProcesses.Any(existing => CodexDesktop.SameProcess(existing, process)) ||
                !native.Running().Any(current => CodexDesktop.SameProcess(current, process) && current.Arguments.UserDataDirectory == profile))
                throw new IOException("Refused to end an unowned desktop process.");
            native.ForceClose(process); Forced++;
        }
        public void Start(ProcessStartInfo start)
        {
            string[] arguments = compatibility is null ? ["--user-data-dir=" + profile] :
                ["--user-data-dir=" + profile, "--remote-debugging-address=127.0.0.1", "--remote-debugging-port=" + compatibility.DebugPort];
            if (start.Environment["CODEX_HOME"] != home || start.Environment["CODEX_ELECTRON_USER_DATA_PATH"] != profile || !start.ArgumentList.SequenceEqual(arguments))
                throw new IOException("Refused a launch outside the isolated profile.");
            foreach (var name in start.Environment.Keys.Where(name => name.EndsWith("_KEY", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("_TOKEN", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase) && name is not "CODEX_HOME" and not "CODEX_ELECTRON_USER_DATA_PATH" ||
                name is "OPENAI_BASE_URL" or "NODE_OPTIONS" or "ELECTRON_RUN_AS_NODE").ToArray())
                start.Environment.Remove(name);
            if (compatibility is not null)
            {
                start.ArgumentList.Add("--proxy-server=http://127.0.0.1:9");
                start.ArgumentList.Add("--proxy-bypass-list=127.0.0.1;localhost");
                start.Environment["HTTP_PROXY"] = start.Environment["HTTPS_PROXY"] = start.Environment["ALL_PROXY"] = "http://127.0.0.1:9";
                start.Environment["NO_PROXY"] = "127.0.0.1,localhost,::1";
            }
            native.Start(start); Starts++;
        }
    }

    private static async Task<IntPtr> WaitForWindowAsync(WindowsCodexDesktopPlatform native, CodexDesktopProcess expected)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (!native.Running().Any(process => CodexDesktop.SameProcess(process, expected))) throw new IOException("The isolated desktop changed before the tray test.");
            var windows = new List<IntPtr>();
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var pid);
                if (pid == expected.Id && IsWindowVisible(window))
                { var name = new StringBuilder(128); GetClassName(window, name, name.Capacity); if (name.ToString() == "Chrome_WidgetWin_1") windows.Add(window); }
                return true;
            }, IntPtr.Zero);
            if (windows.Count == 1) return windows[0];
            await Task.Delay(100);
        }
        throw new TimeoutException("The isolated desktop main window did not appear.");
    }
    private delegate bool EnumWindow(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
