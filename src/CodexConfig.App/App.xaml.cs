using System.IO;
using System.Windows;
using CodexConfig.Core;
using CodexConfig.Application;

namespace CodexConfig.App;

public partial class App : System.Windows.Application
{
    private DesktopInstance? desktopInstance;
    private string? forwardedEnhancedHome;
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
            string? home = null, smoke = null; var startInTray = false; var enhancedLaunch = false;
            for (var i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--home" && i + 1 < e.Args.Length) home = e.Args[++i];
                else if (e.Args[i] == "--smoke-test" && i + 1 < e.Args.Length) smoke = Path.GetFullPath(e.Args[++i]);
                else if (e.Args[i] == "--start-in-tray") startInTray = true;
                else if (e.Args[i] == "--enhanced-launch") enhancedLaunch = true;
            }
            home = Path.GetFullPath(home ?? ConfigStore.DefaultHome);
            IUserEnvironment environment = new WindowsUserEnvironment();
            if (smoke is null)
            {
                desktopInstance = new DesktopInstance(() => Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.RestoreWindow()), startInTray || enhancedLaunch,
                    enhancedLaunch: directory => Dispatcher.BeginInvoke(async () =>
                    {
                        if (MainWindow is MainWindow active) await active.RequestEnhancedLaunchAsync(directory);
                        else forwardedEnhancedHome = directory;
                    }));
                if (!desktopInstance.IsPrimary)
                {
                    if (enhancedLaunch) await desktopInstance.SendEnhancedLaunchAsync(home);
                    Shutdown(); return;
                }
            }
            if (smoke is not null)
            {
                // Fixture windows close while the asynchronous regression is still running.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                // Smoke runs can never load the user's actual Codex home or registry.
                home = Path.Combine(smoke, "isolated-home-" + Guid.NewGuid().ToString("N"));
                environment = new MemoryUserEnvironment();
            }
            var window = new MainWindow(new ConfigStore(environment, smoke is null ? null : _ => null), home, smoke, environment)
                { StartInTray = startInTray || enhancedLaunch, EnhancedLaunchRequested = enhancedLaunch };
            MainWindow = window;
            if (forwardedEnhancedHome is { } forwarded) await window.RequestEnhancedLaunchAsync(forwarded);
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
