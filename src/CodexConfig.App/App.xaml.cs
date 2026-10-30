using System.IO;
using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class App : Application
{
    private DesktopInstance? desktopInstance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--model-bridge"))
        {
            base.OnStartup(e);
            if (e.Args.Length != 2 || e.Args[0] != "--model-bridge" || !Path.IsPathFullyQualified(e.Args[1])) { Shutdown(2); return; }
            try { Shutdown(await BridgeProcess.RunAsync(e.Args[1])); } catch { Shutdown(2); }
            return;
        }
        try
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (_, args) => { WriteStartupError(args.Exception); args.Handled = true; Shutdown(1); };
            ThemeManager.ApplySystemTheme();
            string? home = null, smoke = null; var startInTray = false;
            for (var i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--home" && i + 1 < e.Args.Length) home = e.Args[++i];
                else if (e.Args[i] == "--smoke-test" && i + 1 < e.Args.Length) smoke = Path.GetFullPath(e.Args[++i]);
                else if (e.Args[i] == "--start-in-tray") startInTray = true;
            }
            IUserEnvironment environment = new WindowsUserEnvironment();
            if (smoke is null)
            {
                desktopInstance = new DesktopInstance(() => Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.RestoreWindow()), startInTray);
                if (!desktopInstance.IsPrimary) { Shutdown(); return; }
            }
            if (smoke is not null)
            {
                // Smoke runs can never load the user's actual Codex home or registry.
                home = Path.Combine(smoke, "isolated-home-" + Guid.NewGuid().ToString("N"));
                environment = new MemoryUserEnvironment();
            }
            var window = new MainWindow(new ConfigStore(environment, smoke is null ? null : _ => null), home ?? ConfigStore.DefaultHome, smoke, environment) { StartInTray = startInTray };
            MainWindow = window;
            window.Show();
        }
        catch (Exception error)
        {
            WriteStartupError(error); Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        desktopInstance?.Dispose();
        base.OnExit(e);
    }

    private static void WriteStartupError(Exception error)
    {
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "CodexDock-startup.txt"), error.ToString()); } catch { }
    }

    private sealed class MemoryUserEnvironment : IUserEnvironment
    {
        private string? value;
        public string? Read() => value;
        public void Write(string? newValue) => value = newValue;
    }
}
