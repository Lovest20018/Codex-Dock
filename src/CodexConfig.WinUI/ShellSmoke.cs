using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunSmokeAsync(string output)
    {
        var checks = new List<string>();
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks.Add(message);
            File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine);
        }
        var store = new ConfigStore(new IsolatedEnvironment(), _ => null);
        var original = store.Load(options.Home);
        var preferences = new ClientEnhancementPreferencesService(options.Home, Path.Combine(output, "connection-preferences"),
            Path.Combine(output, "speed-preferences"), Path.Combine(output, "automatic-preferences"));
        var connectionPath = preferences.ConnectionSettings.SettingsPath;
        Check(viewModel.Error.Length == 0 && viewModel.ConnectionLabel == "API Key 直连" && viewModel.ModelLabel == "preview-model", "Workspace loads through the shared application layer");
        Check(ReferenceEquals(currentPage, WorkspaceNav) && WorkspacePanel.Visibility == Visibility.Visible, "Workspace is the initial navigation target");
        Check(original.SameContents(store.Load(options.Home)) && !File.Exists(connectionPath), "Opening the shell leaves config authentication and enhancement preferences untouched");
        await CaptureAsync(output, "workspace-light.png");

        NavigateTo(EnhancementsNav);
        Check(EnhancementsPanel.Visibility == Visibility.Visible && WorkspacePanel.Visibility == Visibility.Collapsed && PageTitle.Text == "运行与增强", "Navigation presents one page with a matching heading");
        Check(!ConnectionRecoveryToggle.IsOn && !SpeedToggle.IsOn && !AutomaticToggle.IsOn && viewModel.PreferencesAvailable, "All enhancement choices default off");
        ConnectionRecoveryToggle.IsOn = true;
        AutomaticToggle.IsOn = true;
        Check(preferences.Load() is { ConnectionRecoveryEnabled: true, SpeedCompatibilityEnabled: false, AutomaticReconnectEnabled: true }, "User toggle actions persist the selected preferences through the shared service");
        Check(PendingBar.IsOpen && original.SameContents(store.Load(options.Home)), "Preference saves show pending application without claiming desktop attachment or writing Codex configuration");
        var recoveryBytes = File.ReadAllBytes(connectionPath);
        var automaticBytes = File.ReadAllBytes(preferences.AutomaticSettings.SettingsPath);
        LoadWorkspace(viewModel.Home); RefreshView(); RefreshView();
        Check(recoveryBytes.SequenceEqual(File.ReadAllBytes(connectionPath)) && automaticBytes.SequenceEqual(File.ReadAllBytes(preferences.AutomaticSettings.SettingsPath)) &&
            !File.Exists(preferences.SpeedSettings.SettingsPath), "Reload and repeated visual updates never save toggles or create additional preference files");
        Check(ConnectionRecoveryToggle.IsOn && AutomaticToggle.IsOn && !SpeedToggle.IsOn, "Saved preferences reappear without recursive toggle writes");
        await CaptureAsync(output, "enhancements-light.png");

        Root.RequestedTheme = ElementTheme.Dark;
        await CaptureAsync(output, "enhancements-dark.png");
        AppWindow.Resize(new(900, 760));
        await CaptureAsync(output, "enhancements-compact-dark.png");
        Check(EnhancementsPanel.ActualWidth > 260 && ConnectionRecoveryToggle.ActualWidth > 0 &&
            ConnectionRecoveryToggle.TransformToVisual(Root).TransformPoint(new()).X + ConnectionRecoveryToggle.ActualWidth <= Root.ActualWidth,
            "Compact navigation and enhancement toggles remain within the window");
        Root.RequestedTheme = ElementTheme.Light;

        var other = Path.Combine(output, "other-home"); await SwitchHomeAsync(other);
        Check(viewModel.Home == other && !ConnectionRecoveryToggle.IsOn && !SpeedToggle.IsOn && !AutomaticToggle.IsOn && !PendingBar.IsOpen && !Directory.Exists(other),
            "Switching directories resets current preferences without creating a configuration directory");
        await SwitchHomeAsync(options.Home);
        Check(ConnectionRecoveryToggle.IsOn && AutomaticToggle.IsOn && recoveryBytes.SequenceEqual(File.ReadAllBytes(connectionPath)), "Returning to a directory restores only its own choices without writes");

        File.WriteAllText(connectionPath, "{}"); LoadWorkspace(options.Home);
        Check(ErrorBar.IsOpen && !ConnectionRecoveryToggle.IsEnabled && !SpeedToggle.IsEnabled && !AutomaticToggle.IsEnabled && File.ReadAllText(connectionPath) == "{}",
            "Damaged preferences show an error and disable all writes while preserving the file");
        await CaptureAsync(output, "enhancements-error.png");
        File.WriteAllBytes(connectionPath, recoveryBytes); LoadWorkspace(options.Home);
        Check(!ErrorBar.IsOpen && ConnectionRecoveryToggle.IsEnabled && ConnectionRecoveryToggle.IsOn, "Repair and reload restore the preference controls");

        File.SetAttributes(connectionPath, FileAttributes.ReadOnly);
        try
        {
            ConnectionRecoveryToggle.IsOn = false;
            Check(ErrorBar.IsOpen && !ConnectionRecoveryToggle.IsEnabled && ConnectionRecoveryToggle.IsOn && recoveryBytes.SequenceEqual(File.ReadAllBytes(connectionPath)),
                "A preference write failure preserves prior bytes restores the toggle and disables further writes");
        }
        finally { File.SetAttributes(connectionPath, FileAttributes.Normal); }
        LoadWorkspace(options.Home);
        File.WriteAllText(Path.Combine(options.Home, "config.toml"), "model=[broken");
        LoadWorkspace(options.Home);
        Check(ErrorBar.IsOpen && !ConnectionRecoveryToggle.IsEnabled && ConnectionText.Text == "尚未读取", "Invalid TOML is shown as a recoverable configuration error without stale connection details");
        File.WriteAllBytes(Path.Combine(options.Home, "config.toml"), original.Config!); LoadWorkspace(options.Home);
        Check(!ErrorBar.IsOpen && ConnectionRecoveryToggle.IsEnabled, "Reloading repaired configuration restores normal workspace state");
        NavigateTo(AboutNav);
        Check(AboutPanel.Visibility == Visibility.Visible && WorkspacePanel.Visibility == Visibility.Collapsed && EnhancementsPanel.Visibility == Visibility.Collapsed,
            "About navigation has its own content");
        NavigateTo(WorkspaceNav);
        Check(original.SameContents(store.Load(options.Home)), "All preview interactions preserve the original Codex config auth environment and catalog");
        await RunDesktopApplicationSmokeAsync(output, checks);
        await RunDesktopLifecycleSmokeAsync(output, checks);
        await RunConnectionConfigurationSmokeAsync(output, checks);
        await RunOfficialLoginAndModelSmokeAsync(output, checks);
        await RunConfigurationToolsSmokeAsync(output, checks);
        await RunWindowsToolsSmokeAsync(output, checks);
        await RunGatewayManagementSmokeAsync(output, checks);
        await RunMobileConnectionSmokeAsync(output, checks);
        await RunUpdatesAndDiagnosticsSmokeAsync(output, checks);
        await RunWorkspaceReadinessSmokeAsync(output, checks);
        await RunGatewayDiagnosticsSmokeAsync(output, checks);
        await RunWorkspaceInformationSmokeAsync(output, checks);
        await RunWorkspaceUsabilitySmokeAsync(output, checks);
        await RunWorkspaceConsolidationSmokeAsync(output, checks);
        await RunWorkspaceTaskFlowSmokeAsync(output, checks);
        await RunQuickModelSmokeAsync(output, checks);
        await RunWorkspaceReviewFixesSmokeAsync(output, checks);
        await RunUpstreamReuseSmokeAsync(output, checks);
        await CaptureAsync(output, "workspace-compact.png");
        await WriteSmokeReportAsync(output, checks);
    }
    private async Task WriteSmokeReportAsync(string output, List<string> checks, string? scope = null)
    {
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
        {
            status = "PASS", scope = scope ?? "isolated Windows preferences, connection and model save review, official login reading/staging, model discovery, and mobile setup/pair/device UI with fake desktop controls and model responses; real encrypted test settings and local model/mobile worker lifecycle; no real Codex public entry checks or remote model calls",
            uiFramework = "WinUI 3",
            applicationVersion = typeof(App).Assembly.GetName().Version!.ToString(3),
            executableName = Path.GetFileName(Environment.ProcessPath),
            executableSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Environment.ProcessPath!))).ToLowerInvariant(),
            assemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),
            applicationAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))).ToLowerInvariant(),
            sharedApplicationAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(WorkspaceShellViewModel).Assembly.Location))),
            rasterizationScale = Root.XamlRoot.RasterizationScale, tests = checks
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task CaptureAsync(string output, string name, UIElement? element = null)
    {
        await Task.Delay(220);
        Root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element ?? Root);
        using var pixels = DataReader.FromBuffer(await bitmap.GetPixelsAsync());
        var bytes = new byte[pixels.UnconsumedBufferLength]; pixels.ReadBytes(bytes);
        using var memory = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync(); memory.Seek(0);
        using var reader = new DataReader(memory.GetInputStreamAt(0));
        await reader.LoadAsync((uint)memory.Size);
        var png = new byte[memory.Size]; reader.ReadBytes(png);
        await File.WriteAllBytesAsync(Path.Combine(output, name), png);
    }
}
