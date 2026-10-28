using System.IO;
using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args is ["--model-bridge", var bridgeHome])
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            RunBridge(bridgeHome);
            return;
        }
        try
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (_, args) => { WriteStartupError(args.Exception); args.Handled = true; Shutdown(1); };
            ThemeManager.ApplySystemTheme();
            string? home = null, smoke = null;
            for (var i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--home" && i + 1 < e.Args.Length) home = e.Args[++i];
                else if (e.Args[i] == "--smoke-test" && i + 1 < e.Args.Length) smoke = Path.GetFullPath(e.Args[++i]);
            }
            IUserEnvironment environment = new WindowsUserEnvironment();
            if (smoke is not null)
            {
                // Smoke runs can never load the user's actual Codex home or registry.
                home = Path.Combine(smoke, "isolated-home-" + Guid.NewGuid().ToString("N"));
                environment = new MemoryUserEnvironment();
            }
            var window = new MainWindow(new ConfigStore(environment), home ?? ConfigStore.DefaultHome, smoke, environment);
            MainWindow = window;
            window.Show();
        }
        catch (Exception error)
        {
            WriteStartupError(error); Shutdown(1);
        }
    }

    private async void RunBridge(string home)
    {
        try { await BridgeProcess.RunAsync(home); Shutdown(0); }
        catch (Exception) { Shutdown(1); }
    }

    private static void WriteStartupError(Exception error)
    {
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "CodexConfig-startup.txt"), error.ToString()); } catch { }
    }

    private sealed class MemoryUserEnvironment : IUserEnvironment
    {
        private string? value;
        public string? Read() => value;
        public void Write(string? newValue) => value = newValue;
    }
}
