using CodexConfig.Application;
using CodexConfig.Core;

internal static class ClientEnhancementStateTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("client enhancement preferences share one service boundary and remain home scoped", () =>
        {
            var isolated = Path.Combine(root, "run-" + Guid.NewGuid().ToString("N")); var first = Path.Combine(isolated, "enhancement-home-a"); var second = Path.Combine(isolated, "enhancement-home-b");
            Directory.CreateDirectory(first); Directory.CreateDirectory(second);
            var service = new ClientEnhancementPreferencesService(first, Path.Combine(isolated, "connections"), Path.Combine(isolated, "speed"), Path.Combine(isolated, "automatic"));
            var viewModel = new ClientEnhancementsViewModel(service); var changed = 0; viewModel.PropertyChanged += (_, _) => changed++;
            Assert(!viewModel.Reload().ConnectionRecoveryEnabled && !viewModel.Preferences.SpeedCompatibilityEnabled && !viewModel.Preferences.AutomaticReconnectEnabled);
            viewModel.SetConnectionRecovery(true); viewModel.SetAutomaticReconnect(true);
            Assert(viewModel.ConnectionRecoveryEnabled && viewModel.AutomaticReconnectEnabled && !viewModel.SpeedCompatibilityEnabled && changed > 0);
            viewModel.SetHome(second); Assert(!viewModel.Reload().ConnectionRecoveryEnabled && !viewModel.AutomaticReconnectEnabled);
            viewModel.SetHome(first); Assert(viewModel.Reload().ConnectionRecoveryEnabled && viewModel.Reload().AutomaticReconnectEnabled);
            return Task.CompletedTask;
        }));
        tests.Add(("client enhancement service preserves an automatic binding while changing only the opt-in", () =>
        {
            var isolated = Path.Combine(root, "run-" + Guid.NewGuid().ToString("N")); var home = Path.Combine(isolated, "enhancement-binding"); Directory.CreateDirectory(home);
            var automatic = new DesktopEnhancementSettings(home, Path.Combine(isolated, "binding-root"));
            var binding = new DesktopEnhancementBinding(Environment.ProcessPath!, home, 7, 10, 12345, new('A', 64), new('B', 64)); automatic.Remember(binding);
            var service = new ClientEnhancementPreferencesService(home, automaticRoot: Path.Combine(isolated, "binding-root"));
            service.SetAutomaticReconnect(true); Assert(service.Load() is { AutomaticReconnectEnabled: true, HasRememberedInstance: true });
            service.SetAutomaticReconnect(false); Assert(service.Load() is { AutomaticReconnectEnabled: false, HasRememberedInstance: true });
            return Task.CompletedTask;
        }));
    }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Client enhancement state regression"); }
}
