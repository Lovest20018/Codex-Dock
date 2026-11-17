using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using System.Security.Principal;
using CodexConfig.Application;

namespace CodexConfig.WinUI;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? window;
    private DesktopInstance? desktopInstance;
    private string? forwardedHome;
    private bool restoreRequested;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        WinUiLaunchOptions? options = null;
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (arguments.Contains("--model-bridge"))
        {
            try
            {
                Environment.ExitCode = arguments is ["--model-bridge", var home] && Path.IsPathFullyQualified(home)
                    ? await CodexConfig.Core.BridgeProcess.RunAsync(home) : 2;
            }
            catch (Exception error) { Console.Error.WriteLine("FAIL model gateway worker: " + error.Message); Environment.ExitCode = 2; }
            Exit(); return;
        }
        try
        {
            options = WinUiLaunchOptions.Parse(arguments);
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (options.SmokeOutput is null)
            {
                desktopInstance = new DesktopInstance(() => dispatcher.TryEnqueue(() =>
                { if (window is { } active) active.RestoreWindow(); else restoreRequested = true; }), options.StartInTray || options.EnhancedLaunch,
                    "WinUI." + WindowsIdentity.GetCurrent().User!.Value, home => dispatcher.TryEnqueue(async () =>
                    { if (window is { } active) await active.RequestEnhancedLaunchAsync(home); else forwardedHome = home; }));
                if (!desktopInstance.IsPrimary)
                {
                    if (options.EnhancedLaunch) await desktopInstance.SendEnhancedLaunchAsync(options.Home);
                    desktopInstance.Dispose(); Exit(); return;
                }
            }
            UnhandledException += (_, error) =>
            {
                if (options.SmokeOutput is null) return;
                error.Handled = true;
                FinishSmoke(options.SmokeOutput, error.Exception);
            };
            window ??= new MainWindow(options);
            window.Closed += (_, _) => { desktopInstance?.Dispose(); desktopInstance = null; };
            window.Activate();
            if (restoreRequested) window.RestoreWindow();
            if (forwardedHome is { } forwarded) await window.RequestEnhancedLaunchAsync(forwarded);
        }
        catch (Exception error)
        {
            desktopInstance?.Dispose(); desktopInstance = null;
            if (options?.SmokeOutput is { } output) FinishSmoke(output, error);
            else if (arguments.Contains("--smoke-test"))
            { Console.Error.WriteLine("FAIL WinUI smoke startup\n" + error); Environment.ExitCode = 1; Exit(); }
            else { _ = MessageBox(IntPtr.Zero, "程序无法启动：" + error.Message, "Codex Dock", 0x10); Environment.ExitCode = 1; Exit(); }
        }
    }

    internal static void FinishSmoke(string output, Exception? error = null)
    {
        Environment.ExitCode = error is null ? 0 : 1;
        try
        {
            if (error is not null) File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
        }
        catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
        { Console.Error.WriteLine("FAIL WinUI smoke diagnostic: " + writeError.Message); Environment.ExitCode = 1; }
        finally { Current.Exit(); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);
}
