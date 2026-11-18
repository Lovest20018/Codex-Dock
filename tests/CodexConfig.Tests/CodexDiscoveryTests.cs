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
        Test("registered Store runtime supersedes stale extracted and unregistered package builds", () =>
        {
            var home = Path.Combine(root, Guid.NewGuid().ToString("N")); var local = Path.Combine(home, "Local"); var roaming = Path.Combine(home, "Roaming");
            var stale = FileAt(home, "WindowsApps", "OpenAI.Codex_1.0.0.0_x64__2p2nqsd0c76g0", "app", "resources", "codex.exe");
            var current = FileAt(home, "WindowsApps", "OpenAI.Codex_2.0.0.0_x64__2p2nqsd0c76g0", "app", "resources", "codex.exe");
            var extracted = FileAt(local, "OpenAI", "Codex", "bin", "old", "codex.exe");
            Assert(CodexExecutableLocator.Find(Path.GetDirectoryName(stale), local, roaming, [stale, extracted], [current]) == current);
            var alias = FileAt(local, "Microsoft", "WindowsApps", "codex.exe");
            Assert(CodexExecutableLocator.Find(Path.GetDirectoryName(alias), local, roaming, [stale, extracted], [current]) == current);
            Assert(CodexExecutableLocator.Find("", local, roaming, [stale]) == extracted);
            var native = FileAt(home, "standalone", "bin", "codex.exe");
            Assert(CodexExecutableLocator.Find("", local, roaming, [native], [current]) == native);
        });
        tests.Add(("packaged runtime fallback checks every helper, refreshes changed packages and stages a verified replacement", async () =>
        {
            var home = Path.Combine(root, Guid.NewGuid().ToString("N"));
            var source = FileAt(home, "package", "codex.exe");
            var helper = FileAt(home, "package", "codex-windows-sandbox-service.exe");
            var extracted = FileAt(home, "desktop", "codex.exe");
            var extractedHelper = FileAt(home, "desktop", "codex-windows-sandbox-service.exe");
            var cache = Path.Combine(home, "cache");
            var directory = Path.GetDirectoryName(extracted)!;
            Assert(await CodexDesktopRuntime.ResolveCopyAsync(source, [directory], cache, default) == extracted && !Directory.Exists(cache));
            // Equal length is insufficient even for an auxiliary executable.
            File.WriteAllText(extractedHelper, new string('x', checked((int)new FileInfo(helper).Length)));
            var verified = await CodexDesktopRuntime.ResolveCopyAsync(source, [directory], cache, default);
            Assert(verified != extracted && File.ReadAllText(verified) == File.ReadAllText(source));
            Assert(await CodexDesktopRuntime.ResolveCopyAsync(source, [], cache, default) == verified);
            File.WriteAllText(verified, "damaged");
            var repaired = await CodexDesktopRuntime.ResolveCopyAsync(source, [], cache, default);
            Assert(repaired != verified && File.ReadAllText(repaired) == File.ReadAllText(source));
            File.WriteAllText(helper, "new package helper");
            var upgraded = await CodexDesktopRuntime.ResolveCopyAsync(source, [], cache, default);
            Assert(Path.GetDirectoryName(Path.GetDirectoryName(upgraded)) != Path.GetDirectoryName(Path.GetDirectoryName(repaired)));
            Assert(File.ReadAllText(Path.Combine(Path.GetDirectoryName(upgraded)!, Path.GetFileName(helper))) == "new package helper");
        }));
    }
}
