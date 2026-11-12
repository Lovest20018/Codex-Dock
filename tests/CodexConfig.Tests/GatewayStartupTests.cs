using CodexConfig.Core;

internal static class GatewayStartupTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
        void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Gateway startup preference assertion failed."); }
        Test("gateway auto start defaults off, persists independently per home and does not write Codex files", () =>
        {
            var state = Path.Combine(root, "gateway-preferences-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "gateway-home-" + Guid.NewGuid().ToString("N"));
            var store = new GatewayStartupStore(home, state);
            Assert(store.Load() == new GatewayStartupSettings() && !Directory.Exists(state) && !Directory.Exists(home));
            store.Save(new(true, false));
            Assert(new GatewayStartupStore(home.ToUpperInvariant() + Path.DirectorySeparatorChar, state).Load() == new GatewayStartupSettings(true, false));
            Assert(new GatewayStartupStore(home + "-other", state).Load() == new GatewayStartupSettings());
            store.Save(store.Load() with { MobileOnDockStart = true });
            Assert(store.Load() == new GatewayStartupSettings(true, true));
            store.Save(store.Load() with { ModelOnDockStart = false });
            Assert(store.Load() == new GatewayStartupSettings(false, true) && !Directory.Exists(home));
        });
        Test("invalid gateway auto start data cannot silently enable services or overwrite itself", () =>
        {
            var store = new GatewayStartupStore(root, Path.Combine(root, "gateway-invalid-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsPath)!);
            foreach (var invalid in new[] { "[]", "null", "{\"ModelOnDockStart\":\"true\"}", "{\"MobileOnDockStart\":1}", new string('x', 4097) })
            {
                File.WriteAllText(store.SettingsPath, invalid);
                var rejected = false;
                try { _ = store.Load(); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected && File.ReadAllText(store.SettingsPath) == invalid);
            }
        });
        Test("failed gateway preference replacement preserves previous flags and cleans temporary files", () =>
        {
            var store = new GatewayStartupStore(root, Path.Combine(root, "gateway-write-failure-" + Guid.NewGuid().ToString("N")));
            store.Save(new(true, false));
            File.SetAttributes(store.SettingsPath, FileAttributes.ReadOnly);
            try
            {
                var rejected = false;
                try { store.Save(new(false, true)); } catch (Exception error) when (error is UnauthorizedAccessException or IOException) { rejected = true; }
                Assert(rejected && store.Load() == new GatewayStartupSettings(true, false));
                Assert(Directory.GetFiles(Path.GetDirectoryName(store.SettingsPath)!, "*.tmp").Length == 0);
            }
            finally { File.SetAttributes(store.SettingsPath, FileAttributes.Normal); }
        });
        Test("Windows startup registration distinguishes the configured home without changing another home", () =>
        {
            var startupTestRoot = Path.GetPathRoot(root)!;
            var executable = Path.Combine(startupTestRoot, "Dock startup fixture", "Dock.exe"); var home = Path.Combine(startupTestRoot, "home with spaces");
            var registration = new MemoryStartupRegistration(); registration.Write(StartupCommand.Create(executable, home));
            Assert(StartupCommand.Targets(registration.Read(), executable, home));
            Assert(!StartupCommand.Targets(registration.Read(), executable, home + "-other"));
            Assert(!StartupCommand.Targets(registration.Read(), executable, Path.Combine(root, new string('x', 240))));
            Assert(registration.Read() == StartupCommand.Create(executable, home));
        });
    }
}
