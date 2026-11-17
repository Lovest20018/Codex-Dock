using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CodexConfig.Core;

internal static class DesktopEnhancementTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        string Home() { var path = Path.Combine(root, "enhancements-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
        Test("automatic enhancements default off and remember only local identity metadata per configuration directory", () =>
        {
            var home = Home(); var storage = Home(); var settings = new DesktopEnhancementSettings(home, storage);
            Assert(!settings.Load().Enabled && settings.Load().Binding is null && !File.Exists(settings.SettingsPath));
            var binding = new DesktopEnhancementBinding(Environment.ProcessPath!, Home(), 42, 100, 12345, new('A', 64), new('B', 64));
            settings.Remember(binding); settings.SetEnabled(true);
            Assert(settings.Load() == new DesktopEnhancementPreference(true, binding));
            settings.SetEnabled(false); Assert(!settings.Load().Enabled && settings.Load().Binding == binding);
            Assert(!new DesktopEnhancementSettings(Home(), storage).Load().Enabled);
            File.WriteAllText(settings.SettingsPath, "{\"Enabled\":\"yes\"}"); Throws<InvalidDataException>(() => settings.Load());
        });
        Test("automatic reconnect snapshot ignores valid runtime pipe rotation and formatting while rejecting connection changes", () =>
        {
            var home = Home(); var before = new ConfigSnapshot(home, Encoding.UTF8.GetBytes("# initial\nmodel='same'\n[mcp_servers.node_repl.env]\nSKY_CUA_NATIVE_PIPE_DIRECTORY='\\\\.\\pipe\\codex-computer-use-" + Guid.NewGuid() + "'\n"),
                Encoding.UTF8.GetBytes("{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"secret-fixture-only\"}"), null);
            var next = new TomlDocument(before.Config); next.Set(CodexApplySnapshot.RuntimePipePath, @"\\.\pipe\codex-computer-use-" + Guid.NewGuid());
            Assert(DesktopEnhancementSettings.SnapshotHash(before) == DesktopEnhancementSettings.SnapshotHash(before with { Config = next.Bytes }));
            next.Set("openai_base_url", "https://changed.invalid/v1");
            Assert(DesktopEnhancementSettings.SnapshotHash(before) != DesktopEnhancementSettings.SnapshotHash(before with { Config = next.Bytes }));
            Assert(DesktopEnhancementSettings.SnapshotHash(before) != DesktopEnhancementSettings.SnapshotHash(before with { Auth = Encoding.UTF8.GetBytes("{\"OPENAI_API_KEY\":\"different-fixture\"}") }));
        });
        Test("automatic reconnect account association permits same identity refresh without persisting token material", () =>
        {
            byte[] Auth(string subject, string token) => JsonSerializer.SerializeToUtf8Bytes(new { auth_mode = "chatgpt", tokens = new {
                account_id = "fixture-account", access_token = token, refresh_token = token,
                id_token = "x." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { iss = "fixture", sub = subject })).TrimEnd('=').Replace('+','-').Replace('/','_') + ".x" } });
            var first = new ConfigSnapshot(Home(), Encoding.UTF8.GetBytes("model='same'"), Auth("same", "old"), null);
            Assert(DesktopEnhancementSettings.SnapshotHash(first) == DesktopEnhancementSettings.SnapshotHash(first with { Auth = Auth("same", "new") }));
            Assert(DesktopEnhancementSettings.SnapshotHash(first) != DesktopEnhancementSettings.SnapshotHash(first with { Auth = Auth("changed", "new") }));
        });
        Test("automatic reconnect requires the remembered process birth executable and profile together", () =>
        {
            var profile = Home(); var exe = Environment.ProcessPath!; var process = new CodexDesktopProcess(42, 1, 100, exe);
            var target = new CodexDesktopTarget(exe, [process]) { UserDataDirectory = profile };
            var binding = new DesktopEnhancementBinding(exe, profile, 42, 100, 12345, new('A', 64), new('B', 64));
            Assert(ReferenceEquals(DesktopEnhancementSettings.Match(binding, [target]), target));
            Assert(DesktopEnhancementSettings.Match(binding with { Started = 101 }, [target]) is null);
            Assert(DesktopEnhancementSettings.Match(binding with { UserDataDirectory = Home() }, [target]) is null);
            Assert(DesktopEnhancementSettings.Match(binding with { Executable = Path.Combine(Home(), "other.exe") }, [target]) is null);
        });
        Test("enhanced Windows shortcut preserves paths and forwards only an explicit launch and configuration directory", () =>
        {
            var home = Home(); var destination = Path.Combine(Home(), "Codex enhanced.lnk"); var executable = Environment.ProcessPath!;
            EnhancedLaunchShortcut.Create(destination, executable, home);
            object? shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                dynamic link = shortcut = ((dynamic)shell).CreateShortcut(destination);
                Assert(File.Exists(destination) && string.Equals((string)link.TargetPath, executable, StringComparison.OrdinalIgnoreCase) && (string)link.Arguments == EnhancedLaunchShortcut.Arguments(home));
            }
            finally { if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
            Throws<ArgumentException>(() => EnhancedLaunchShortcut.Arguments("relative"));
            Throws<ArgumentException>(() => EnhancedLaunchShortcut.Create(destination + ".exe", executable, home));
        });
    }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Automatic enhancement regression"); }
    private static void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
