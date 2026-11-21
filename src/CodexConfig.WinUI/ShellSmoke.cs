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
    private async Task RunSmokeHostAsync(string output)
    {
        try
        {
            var filter = Environment.GetEnvironmentVariable("CODEX_DOCK_WINUI_TEST_FILTER");
            if (string.IsNullOrEmpty(filter)) await RunSmokeAsync(output);
            else if (filter == "workspace-usability")
            {
                var checks = new List<string>(); await RunWorkspaceUsabilitySmokeAsync(output, checks);
                await WriteSmokeReportAsync(output, checks);
            }
            else if (filter == "ui-consolidation")
            {
                var checks = new List<string>(); await RunWorkspaceConsolidationSmokeAsync(output, checks);
                await WriteSmokeReportAsync(output, checks);
            }
            else if (filter == "task-flow")
            {
                var checks = new List<string>(); await RunWorkspaceTaskFlowSmokeAsync(output, checks);
                await WriteSmokeReportAsync(output, checks);
            }
            else if (filter == "quick-model")
            {
                var checks = new List<string>(); await RunQuickModelSmokeAsync(output, checks);
                await WriteSmokeReportAsync(output, checks);
            }
            else if (filter == "connection-setup")
            {
                var checks = new List<string>(); await RunWorkspaceReadinessSmokeAsync(output, checks);
                await WriteSmokeReportAsync(output, checks);
            }
            else if (filter == "review-fixes")
            {
                var checks = new List<string>(); await RunWorkspaceReviewFixesSmokeAsync(output, checks);
                await WriteSmokeReportAsync(output, checks);
            }
            else if (filter == "upstream-reuse")
            {
                var checks = new List<string>(); await RunUpstreamReuseSmokeAsync(output, checks);
                await WriteSmokeReportAsync(output, checks);
            }
            else if (filter == "native-acceptance") await RunNativeAcceptanceHostAsync(output);
            else if (filter == "installer-host")
            {
                InitializeTray();
                if (!TryHideToTray()) throw new InvalidOperationException("The installer fixture must run in the tray.");
                File.WriteAllText(Path.Combine(output, "installer-host.ready"), options.Home);
                return;
            }
            else if (filter == "environment-native")
            {
                var checks = new List<string>(); await RunNativeEnvironmentSmokeAsync(output, checks);
                await WriteSmokeReportAsync(output, checks, "Connected monitors with actual native DPI and work areas; isolated window movement; clipboard eligibility explicitly reported and non-fixture contents skipped; requested opening of the isolated fixture folder; no real model or desktop actions");
            }
            else throw new ArgumentException("Unknown CODEX_DOCK_WINUI_TEST_FILTER.");
            App.FinishSmoke(output);
        }
        catch (Exception error) { App.FinishSmoke(output, error); }
    }

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
        Check(EnhancementsPanel.Visibility == Visibility.Visible && WorkspacePanel.Visibility == Visibility.Collapsed && PageTitle.Text == EnhancementsNav.Title, "Navigation presents one page with a matching heading");
        Check(!EnhancementsPanel.ConnectionRecoveryToggle.IsOn && !EnhancementsPanel.SpeedToggle.IsOn && !EnhancementsPanel.AutomaticToggle.IsOn && viewModel.PreferencesAvailable, "All enhancement choices default off");
        EnhancementsPanel.ConnectionRecoveryToggle.IsOn = true;
        EnhancementsPanel.AutomaticToggle.IsOn = true;
        Check(preferences.Load() is { ConnectionRecoveryEnabled: true, SpeedCompatibilityEnabled: false, AutomaticReconnectEnabled: true }, "User toggle actions persist the selected preferences through the shared service");
        Check(EnhancementsPanel.PendingBar.IsOpen && original.SameContents(store.Load(options.Home)), "Preference saves show pending application without claiming desktop attachment or writing Codex configuration");
        var recoveryBytes = File.ReadAllBytes(connectionPath);
        var automaticBytes = File.ReadAllBytes(preferences.AutomaticSettings.SettingsPath);
        LoadWorkspace(viewModel.Home); RefreshView(); RefreshView();
        Check(recoveryBytes.SequenceEqual(File.ReadAllBytes(connectionPath)) && automaticBytes.SequenceEqual(File.ReadAllBytes(preferences.AutomaticSettings.SettingsPath)) &&
            !File.Exists(preferences.SpeedSettings.SettingsPath), "Reload and repeated visual updates never save toggles or create additional preference files");
        Check(EnhancementsPanel.ConnectionRecoveryToggle.IsOn && EnhancementsPanel.AutomaticToggle.IsOn && !EnhancementsPanel.SpeedToggle.IsOn, "Saved preferences reappear without recursive toggle writes");
        await CaptureAsync(output, "enhancements-light.png");

        Root.RequestedTheme = ElementTheme.Dark;
        await CaptureAsync(output, "enhancements-dark.png");
        AppWindow.Resize(new(900, 760));
        await CaptureAsync(output, "enhancements-compact-dark.png");
        Check(EnhancementsPanel.ActualWidth > 260 && EnhancementsPanel.ConnectionRecoveryToggle.ActualWidth > 0 &&
            EnhancementsPanel.ConnectionRecoveryToggle.TransformToVisual(Root).TransformPoint(new()).X + EnhancementsPanel.ConnectionRecoveryToggle.ActualWidth <= Root.ActualWidth,
            "Compact navigation and enhancement toggles remain within the window");
        Root.RequestedTheme = ElementTheme.Light;

        var other = Path.Combine(output, "other-home"); await SwitchHomeAsync(other);
        Check(viewModel.Home == other && !EnhancementsPanel.ConnectionRecoveryToggle.IsOn && !EnhancementsPanel.SpeedToggle.IsOn && !EnhancementsPanel.AutomaticToggle.IsOn && !EnhancementsPanel.PendingBar.IsOpen && !Directory.Exists(other),
            "Switching directories resets current preferences without creating a configuration directory");
        await SwitchHomeAsync(options.Home);
        Check(EnhancementsPanel.ConnectionRecoveryToggle.IsOn && EnhancementsPanel.AutomaticToggle.IsOn && recoveryBytes.SequenceEqual(File.ReadAllBytes(connectionPath)), "Returning to a directory restores only its own choices without writes");

        File.WriteAllText(connectionPath, "{}"); LoadWorkspace(options.Home);
        Check(ErrorBar.IsOpen && !EnhancementsPanel.ConnectionRecoveryToggle.IsEnabled && !EnhancementsPanel.SpeedToggle.IsEnabled && !EnhancementsPanel.AutomaticToggle.IsEnabled && File.ReadAllText(connectionPath) == "{}",
            "Damaged preferences show an error and disable all writes while preserving the file");
        await CaptureAsync(output, "enhancements-error.png");
        connectionDraft.Model = "keep-on-preference-reload";
        NavigateTo(WorkspaceNav);
        await RunWorkspaceNextAsync();
        Check(currentPage == EnhancementsNav && ErrorReloadButton.Content.ToString() == "重新读取增强偏好",
            "Home routes damaged enhancement preferences to their own retry action");
        File.WriteAllBytes(connectionPath, recoveryBytes);
        ErrorReloadClicked(ErrorReloadButton, new());
        Check(!ErrorBar.IsOpen && EnhancementsPanel.ConnectionRecoveryToggle.IsEnabled && EnhancementsPanel.ConnectionRecoveryToggle.IsOn &&
            connectionDraft.HasDraft && connectionDraft.Model == "keep-on-preference-reload" && original.SameContents(store.Load(options.Home)),
            "Retrying enhancement preferences restores toggles and preserves unsaved configuration edits");
        DiscardConnectionClicked(DiscardConnectionButton, new());

        File.SetAttributes(connectionPath, FileAttributes.ReadOnly);
        try
        {
            EnhancementsPanel.ConnectionRecoveryToggle.IsOn = false;
            await WaitForQueuedRefreshAsync();
            Check(ErrorBar.IsOpen && !EnhancementsPanel.ConnectionRecoveryToggle.IsEnabled && EnhancementsPanel.ConnectionRecoveryToggle.IsOn && recoveryBytes.SequenceEqual(File.ReadAllBytes(connectionPath)),
                "A preference write failure preserves prior bytes restores the toggle and disables further writes");
        }
        finally { File.SetAttributes(connectionPath, FileAttributes.Normal); }
        LoadWorkspace(options.Home);
        File.WriteAllText(Path.Combine(options.Home, "config.toml"), "model=[broken");
        LoadWorkspace(options.Home);
        Check(ErrorBar.IsOpen && !EnhancementsPanel.ConnectionRecoveryToggle.IsEnabled, "Invalid TOML disables preference changes");
        NavigateTo(WorkspaceNav);
        Check(WorkspacePanel.ConnectionText.Text == "尚未读取" && WorkspaceNextStep().Page == RecoveryNav, "Invalid TOML offers recovery without stale connection details");
        File.WriteAllBytes(Path.Combine(options.Home, "config.toml"), original.Config!); LoadWorkspace(options.Home);
        NavigateTo(EnhancementsNav);
        Check(!ErrorBar.IsOpen && EnhancementsPanel.ConnectionRecoveryToggle.IsEnabled, "Reloading repaired configuration restores normal workspace state");
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

    private Task WaitForQueuedRefreshAsync()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => completed.SetResult()))
            throw new InvalidOperationException("Could not wait for the queued UI refresh.");
        return completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
