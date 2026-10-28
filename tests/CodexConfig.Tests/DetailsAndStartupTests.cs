using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexConfig.Core;
using Microsoft.Win32;

internal static class DetailsAndStartupTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
        string Home() { var path = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
        Test("connection labels save without credentials, stay encrypted and restore with other files", () =>
        {
            var home = Home(); var env = new FakeEnvironment(); var store = new ConfigStore(env);
            File.WriteAllText(Path.Combine(home, "config.toml"), "# retained\r\nmodel='original'\r\n");
            File.WriteAllBytes(Path.Combine(home, "auth.json"), MixedTests.Auth());
            var before = store.Load(home);
            var settings = store.ReadSettings(before) with { Details = new("  我的服务  ", "第一行\r\n第二行 private-note") };
            store.Apply(store.Plan(before, settings, updateConnection: false, updateDetails: true));
            var saved = store.Load(home);
            Assert(saved.Config!.SequenceEqual(before.Config!) && saved.Auth!.SequenceEqual(before.Auth!) && env.Read() is null);
            Assert(!Encoding.UTF8.GetString(saved.Details!).Contains("private-note"));
            Assert(store.ReadSettings(saved).Details == new ConnectionDetails("我的服务", "第一行\n第二行 private-note"));
            var ordinary = store.Plan(saved, store.ReadSettings(saved), new Dictionary<string, string?> { ["analytics.enabled"] = "false" }, updateConnection: false);
            Assert(ordinary.Details!.SequenceEqual(saved.Details!)); store.Apply(ordinary);
            store.Restore(store.Load(home)); Assert(store.ReadSettings(store.Load(home)).Details == store.ReadSettings(saved).Details);
            store.Apply(store.Plan(store.Load(home), settings with { Details = new() }, updateConnection: false, updateDetails: true));
            Assert(store.Load(home).Details is null);
            store.Restore(store.Load(home)); Assert(store.Load(home).Details!.SequenceEqual(saved.Details!));
        });
        Test("connection labels participate in conflicts, transaction rollback and legacy backup restore", () =>
        {
            var home = Home(); var env = new FakeEnvironment(); var store = new ConfigStore(env);
            var settings = new ConnectionSettings("https://relay.example", "fake-pat", "model") { Details = new("First", "Original") };
            store.Apply(store.Plan(store.Load(home), settings, updateDetails: true)); var first = store.Load(home);
            var pending = store.Plan(first, settings with { Details = new("Stale", "") }, updateConnection: false, updateDetails: true);
            File.WriteAllBytes(Path.Combine(home, ConnectionDetails.FileName), new ConnectionDetails("External").Protect()!);
            Throws<InvalidOperationException>(() => store.Apply(pending));
            File.WriteAllBytes(Path.Combine(home, ConnectionDetails.FileName), first.Details!);
            env.ThrowOnce = true;
            Throws<IOException>(() => store.Apply(store.Plan(first, settings with { BaseUrl = "https://other.example", Details = new("After", "") }, updateDetails: true)));
            var rolledBack = store.Load(home);
            Assert(rolledBack.Config!.SequenceEqual(first.Config!) && rolledBack.Auth!.SequenceEqual(first.Auth!) && rolledBack.Details!.SequenceEqual(first.Details!));
            store.Apply(store.Plan(rolledBack, settings with { Details = new("Interrupted", "Pending") }, updateConnection: false, updateDetails: true));
            File.WriteAllText(Path.Combine(home, "codex-config-backups", "pending.json"), Path.GetFileName(store.LatestBackup(home)));
            store.Recover(home);
            Assert(!store.HasPending(home) && store.Load(home).Details!.SequenceEqual(first.Details!));
            var oldBackup = new { BeforeConfig = first.Config, BeforeAuth = first.Auth, BeforeEnv = first.AuthApiBaseUrl,
                AfterConfigHash = Hash(first.Config!), AfterAuthHash = Hash(first.Auth!), AfterEnv = first.AuthApiBaseUrl };
            var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(oldBackup), Encoding.UTF8.GetBytes("CodexConfig.backup.v1"), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path.Combine(home, "codex-config-backups", "zz-legacy.backup"), bytes);
            store.Restore(first); Assert(store.Load(home).Details!.SequenceEqual(first.Details!));
            Throws<InvalidDataException>(() => ConnectionDetails.Read([1, 2, 3]));
            Throws<ArgumentException>(() => (settings.Details with { Name = new('x', 61) }).Protect());
        });
        Test("startup registration uses an isolated registry key and preserves unrelated values", () =>
        {
            var path = @"Software\CodexDockTests\" + Guid.NewGuid().ToString("N");
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(path)) key.SetValue("Unrelated", "retained");
                var registration = new WindowsStartupRegistration(path);
                Assert(registration.Read() is null);
                var command = StartupCommand.Create(@"D:\apps with spaces\CodexDock.exe", @"D:\config with spaces\");
                registration.Write(command); Assert(registration.Read() == command);
                Assert(Parse(command).SequenceEqual(new[] { @"D:\apps with spaces\CodexDock.exe", "--start-in-tray", "--home", @"D:\config with spaces\" }));
                Assert(Parse(StartupCommand.Create(@"D:\app\CodexDock.exe", @"D:\"))[3] == @"D:\");
                Assert(StartupCommand.Targets(command, @"D:\apps with spaces\CodexDock.exe"));
                Assert(!StartupCommand.Targets(command, @"D:\another\CodexDock.exe"));
                registration.Write(null); Assert(registration.Read() is null);
                using var readKey = Registry.CurrentUser.OpenSubKey(path); Assert(readKey?.GetValue("Unrelated") as string == "retained");
                Throws<ArgumentException>(() => StartupCommand.Create(@"D:\app\CodexDock.exe", @"D:\" + new string('a', 260)));
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
        });
    }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Assertion failed"); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static string[] Parse(string command)
    {
        var args = CommandLineToArgvW(command, out var count);
        if (args == IntPtr.Zero) throw new InvalidOperationException("Windows could not parse startup command.");
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(args, i * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(args); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}
