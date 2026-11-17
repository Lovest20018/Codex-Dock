using CodexConfig.Application;
using CodexConfig.Core;

internal static class WorkspaceShellTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        (WorkspaceShellViewModel Shell, string Home, string Connections, string Speed, string Automatic) Fixture()
        {
            var directory = Path.Combine(root, "workspace-shell-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(directory, "home"); Directory.CreateDirectory(home);
            var connections = Path.Combine(directory, "connections"); var speed = Path.Combine(directory, "speed"); var automatic = Path.Combine(directory, "automatic");
            var shell = new WorkspaceShellViewModel(new ConfigStore(new NoWrites(), _ => null), home, connections, speed, automatic);
            return (shell, home, connections, speed, automatic);
        }
        tests.Add(("shell loading and rendering preserve configuration and hide address credentials", () =>
        {
            var (shell, home, connections, speed, automatic) = Fixture();
            const string config = "model='fixture-model'\nopenai_base_url='https://user:private-password@service.invalid/v1?token=private-query#private-fragment'\n";
            File.WriteAllText(Path.Combine(home, "config.toml"), config);
            File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-shell-private-key\"}");
            shell.Load(home);
            Assert(shell.ConnectionLabel == "API Key 直连" && shell.ServiceOrigin == "https://service.invalid" && shell.ModelLabel == "fixture-model");
            Assert(!Directory.Exists(connections) && !Directory.Exists(speed) && !Directory.Exists(automatic));
            Assert(File.ReadAllText(Path.Combine(home, "config.toml")) == config && !shell.PreferencesChanged && shell.Error.Length == 0);
            shell.SetConnectionRecovery(true); shell.SetAutomaticReconnect(true);
            var bytes = File.ReadAllBytes(new DesktopConnectionSettings(home, connections).SettingsPath);
            shell.Load(home);
            Assert(shell.ConnectionRecoveryEnabled && shell.AutomaticReconnectEnabled && shell.PreferencesChanged && !shell.SpeedCompatibilityEnabled);
            Assert(bytes.SequenceEqual(File.ReadAllBytes(new DesktopConnectionSettings(home, connections).SettingsPath)));
            var other = Path.Combine(home, "other"); shell.Load(other);
            Assert(!shell.ConnectionRecoveryEnabled && !shell.AutomaticReconnectEnabled && !shell.PreferencesChanged && !Directory.Exists(other));
            shell.Load(home); Assert(shell.ConnectionRecoveryEnabled && shell.AutomaticReconnectEnabled);
            return Task.CompletedTask;
        }));
        tests.Add(("shell corrupt preferences disable changes and remain untouched until repaired", () =>
        {
            var (shell, home, connections, _, _) = Fixture();
            var settings = new DesktopConnectionSettings(home, connections); settings.Save(true);
            File.WriteAllText(settings.SettingsPath, "{}"); shell.Load(home);
            Assert(!shell.PreferencesAvailable && shell.Error.Length > 0 && !shell.SetConnectionRecovery(false));
            Assert(File.ReadAllText(settings.SettingsPath) == "{}");
            settings.Save(true); shell.Load(home);
            Assert(shell.PreferencesAvailable && shell.Error.Length == 0 && shell.ConnectionRecoveryEnabled);
            File.WriteAllText(Path.Combine(home, "config.toml"), "model=[broken"); shell.Load(home);
            Assert(!shell.PreferencesAvailable && shell.ConnectionLabel == "尚未读取" && shell.Error.Length > 0);
            return Task.CompletedTask;
        }));
        tests.Add(("shell save failure preserves prior preference and blocks further writes", () =>
        {
            var (shell, home, connections, _, _) = Fixture(); var settings = new DesktopConnectionSettings(home, connections);
            settings.Save(false); shell.Load(home); var bytes = File.ReadAllBytes(settings.SettingsPath);
            File.SetAttributes(settings.SettingsPath, FileAttributes.ReadOnly);
            try
            {
                Assert(!shell.SetConnectionRecovery(true) && !shell.PreferencesAvailable && shell.Error.Length > 0 && !shell.ConnectionRecoveryEnabled);
                Assert(bytes.SequenceEqual(File.ReadAllBytes(settings.SettingsPath)));
            }
            finally { File.SetAttributes(settings.SettingsPath, FileAttributes.Normal); }
            shell.Load(home); Assert(shell.PreferencesAvailable && shell.Error.Length == 0);
            return Task.CompletedTask;
        }));
    }

    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Workspace shell regression"); }
    private sealed class NoWrites : IUserEnvironment
    { public string? Read() => null; public void Write(string? value) => throw new InvalidOperationException("Shell must not write the user environment"); }
}
