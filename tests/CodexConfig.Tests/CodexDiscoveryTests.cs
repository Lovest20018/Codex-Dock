using CodexConfig.Core;

internal static class CodexDiscoveryTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
        string FileAt(params string[] parts) { var path = Path.Combine(parts); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "fixture only: never execute"); return path; }
        void Assert(bool value) { if (!value) throw new InvalidOperationException("Codex executable discovery assertion failed."); }
        Test("desktop-only install is discovered without PATH and running native CLI wins over desktop GUI", () =>
        {
            var home = Path.Combine(root, Guid.NewGuid().ToString("N")); var local = Path.Combine(home, "Local"); var roaming = Path.Combine(home, "Roaming");
            var exe = FileAt(local, "OpenAI", "Codex", "bin", "runtime-id", "codex.exe");
            var gui = FileAt(home, "WindowsApps", "OpenAI.Codex", "app", "Codex.exe");
            Assert(CodexExecutableLocator.Find("", local, roaming, [gui]) == exe);
            var active = FileAt(local, "OpenAI", "Codex", "bin", "active-runtime", "codex.exe");
            Assert(CodexExecutableLocator.Find("", local, roaming, [gui, active]) == active);
        });
        Test("npm shim-only installation resolves its current native platform package", () =>
        {
            var home = Path.Combine(root, Guid.NewGuid().ToString("N")); var roaming = Path.Combine(home, "Roaming"); var prefix = Path.Combine(roaming, "npm");
            FileAt(prefix, "codex.cmd"); FileAt(prefix, "codex.ps1");
            var exe = FileAt(prefix, "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
            Assert(CodexExecutableLocator.Find("", Path.Combine(home, "Local"), roaming) == exe);
        });
        Test("custom npm prefix supports old vendor layout and quoted PATH", () =>
        {
            var home = Path.Combine(root, Guid.NewGuid().ToString("N")); var prefix = Path.Combine(home, "Custom npm");
            var exe = FileAt(prefix, "node_modules", "@openai", "codex", "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe");
            Assert(CodexExecutableLocator.Find(";\"" + prefix + "\";relative-prefix", Path.Combine(home, "Local"), Path.Combine(home, "Roaming")) == exe);
            var pathExe = FileAt(prefix, "codex.exe");
            Assert(CodexExecutableLocator.Find(prefix, Path.Combine(home, "Local"), Path.Combine(home, "Roaming")) == pathExe);
        });
        Test("absent or stale Codex candidates produce no selection", () =>
        {
            var home = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Assert(CodexExecutableLocator.Find(";relative-prefix", Path.Combine(home, "Local"), Path.Combine(home, "Roaming"), [Path.Combine(home, "bin", "codex.exe")]) is null);
        });
    }
}
