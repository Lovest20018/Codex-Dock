using System.Diagnostics;
using System.Text.Json;
using CodexConfig.Core;

// Explicit opt-in acceptance of the installed app's lifecycle. Never part of
// the automatic suite and never selects or closes an existing user's instance.
internal static class InstalledDesktopAcceptance
{
    internal static async Task RunAsync(string root)
    {
        root = Path.GetFullPath(root);
        var allowed = Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar;
        if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new ArgumentException("Installed desktop acceptance requires a new directory under this checkout's artifacts.");
        Directory.CreateDirectory(root);
        var home = Path.Combine(root, "codex-home"); var profile = Path.Combine(root, "electron-profile");
        Directory.CreateDirectory(home); Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(home, "config.toml"), "# Isolated installed desktop acceptance; no account or tasks\nmodel='gpt-6.1-sol'\n");
        var native = new WindowsCodexDesktopPlatform(); var before = native.Running();
        var executable = native.Installed().Single();
        var platform = new ScopedPlatform(native, before, home, profile);
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
        try
        {
            var first = await desktop.RestartAsync(target, home, null, Validate);
            openedIds.Add(first.MainProcess!.Id);
            Check(first.IsRunning && first.UserDataDirectory == profile && before.All(old => !CodexDesktop.SameProcess(old, first.MainProcess)),
                "The installed Store application opens a separate explicit profile and a temporary Codex home");
            await Task.Delay(1500);
            Validate();
            Check(Directory.EnumerateFileSystemEntries(profile).Any() && !File.Exists(Path.Combine(home, "auth.json")),
                "The installed application initializes only the temporary profile without importing a signed-in account");
            // Use a fresh inventory carrying the actual process identities.
            var selected = desktop.FindTargets().Single(candidate => candidate.UserDataDirectory == profile);
            CodexDesktopTarget next;
            try { next = await desktop.RestartAsync(selected, home, null, Validate); }
            catch (CodexDesktopExitTimeoutException)
            {
                Check(platform.Starts == 1, "Close-to-tray blocks normal restart and does not launch a duplicate installed desktop");
                next = await desktop.RestartAsync(selected, home, null, Validate, forceConfirmed: true);
            }
            openedIds.Add(next.MainProcess!.Id);
            Check(next.MainProcess.Id != first.MainProcess.Id && next.UserDataDirectory == profile && platform.Starts == 2,
                "An explicitly confirmed restart of the isolated installed desktop returns a new verified root in the same profile");
            Validate(); Check(true, "Every pre-existing main desktop process retained its original identity through the restart");
        }
        finally
        {
            // Only request normal close for a process whose parsed profile is ours.
            foreach (var owned in CodexDesktop.Group(native.Running()).Where(candidate => candidate.UserDataDirectory == profile))
                if (owned.MainProcess is { } main) { try { platform.Close(main); } catch (InvalidOperationException) { } }
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(25) && native.Running().Any(process => process.Arguments.UserDataDirectory == profile))
                await Task.Delay(100);
            if (native.Running().Any(process => process.Arguments.UserDataDirectory == profile))
                await CleanupAsync(root);
        }
        Validate(); Check(true, "The temporary installed desktop is cleaned up and leaves existing desktops running");
        await File.WriteAllTextAsync(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new
        {
            status = "PASS", scope = "Installed desktop open and confirmed restart in an isolated profile; close-to-tray is reported as blocked and force confirmation is tested only on this empty fixture; no login, task, message or enhancement injection",
            executable, home, profile, openedIds, tests = checks,
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

    private sealed class ScopedPlatform(WindowsCodexDesktopPlatform native, IReadOnlyList<CodexDesktopProcess> protectedProcesses, string home, string profile) : ICodexDesktopPlatform
    {
        internal int Starts;
        public IReadOnlyList<CodexDesktopProcess> Running() => native.Running();
        public IReadOnlyList<string> Installed() => native.Installed();
        public void PrepareStart(ProcessStartInfo start) => native.PrepareStart(start);
        public void Close(CodexDesktopProcess process)
        {
            if (protectedProcesses.Any(existing => CodexDesktop.SameProcess(existing, process)) ||
                !native.Running().Any(current => CodexDesktop.SameProcess(current, process) && current.Arguments.UserDataDirectory == profile))
                throw new IOException("Refused to close an unowned desktop process.");
            native.Close(process);
        }
        public void ForceClose(CodexDesktopProcess process)
        {
            if (protectedProcesses.Any(existing => CodexDesktop.SameProcess(existing, process)) ||
                !native.Running().Any(current => CodexDesktop.SameProcess(current, process) && current.Arguments.UserDataDirectory == profile))
                throw new IOException("Refused to end an unowned desktop process.");
            native.ForceClose(process);
        }
        public void Start(ProcessStartInfo start)
        {
            if (start.Environment["CODEX_HOME"] != home || start.Environment["CODEX_ELECTRON_USER_DATA_PATH"] != profile ||
                start.ArgumentList.Count != 1 || start.ArgumentList[0] != "--user-data-dir=" + profile)
                throw new IOException("Refused a launch outside the isolated profile.");
            foreach (var name in start.Environment.Keys.Where(name => name.EndsWith("_KEY", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("_TOKEN", StringComparison.OrdinalIgnoreCase) || name is "OPENAI_BASE_URL" or "CODEX_AUTHAPI_BASE_URL" or "NODE_OPTIONS" or "ELECTRON_RUN_AS_NODE").ToArray())
                start.Environment.Remove(name);
            native.Start(start); Starts++;
        }
    }
}
