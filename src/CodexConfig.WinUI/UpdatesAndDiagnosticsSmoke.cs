using System.Security.Cryptography;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    // This runtime is selected by smoke mode at construction, before any button can run.
    private sealed class SmokeUpdateRuntime(string root) : IUpdateDownloadRuntime
    {
        internal bool NewRelease = true, FailCheck, FailDownload;
        internal TaskCompletionSource? Gate;
        internal int Checks, Downloads;
        public async Task<ReleaseUpdate?> CheckAsync(Version current, CancellationToken cancellation)
        {
            Checks++; if (Gate is { } gate) await gate.Task.WaitAsync(cancellation);
            if (FailCheck) throw new IOException("Offline fixture failure.");
            return NewRelease ? Release() : null;
        }
        public async Task<VerifiedInstaller> DownloadAsync(ReleaseUpdate update, string directory, IProgress<int>? progress, CancellationToken cancellation)
        {
            Downloads++; progress?.Report(25); if (Gate is { } gate) await gate.Task.WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested(); if (FailDownload) throw new InvalidDataException("模拟校验失败，未提供安装器。");
            if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture download escaped output.");
            Directory.CreateDirectory(directory); var path = Path.Combine(directory, "never-executed-setup.exe");
            File.WriteAllText(path, "Isolated installer bytes. Never execute."); progress?.Report(100);
            return new(path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }
        private static ReleaseUpdate Release() => new(new(99, 0, 0), new(GitHubUpdates.Repository + "/releases/tag/v99.0.0"),
            new(GitHubUpdates.Repository + "/releases/download/v99.0.0/CodexDock-99.0.0-Setup-win-x64.exe"), 100,
            new(GitHubUpdates.Repository + "/releases/download/v99.0.0/SHA256SUMS-99.0.0.txt"), null);
    }
    private sealed class SmokeDiagnosticRuntime(ISessionDiagnosticRuntime inner) : ISessionDiagnosticRuntime
    {
        internal TaskCompletionSource? Gate;
        internal int Reads;
        public Task<IReadOnlyList<SessionDiagnosticFile>> ListAsync(string home, CancellationToken cancellation) => inner.ListAsync(home, cancellation);
        public async Task<SessionObservation> ReadAsync(string home, string path, CancellationToken cancellation)
        { Reads++; if (Gate is { } gate) await gate.Task.WaitAsync(cancellation); return await inner.ReadAsync(home, path, cancellation); }
    }
    private async Task RunUpdatesAndDiagnosticsSmokeAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        var home = Path.Combine(output, "updates-diagnostics-home"); Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='diagnostic-config-model'\n");
        var sessions = Path.Combine(home, "sessions"); Directory.CreateDirectory(sessions);
        var selected = Path.Combine(sessions, "rollout-selected.jsonl");
        File.WriteAllText(selected, """
            {"type":"event_msg","timestamp":"2026-11-16T03:00:00Z","payload":{"type":"thread_settings_applied","thread_settings":{"model":"recorded-chat-model","reasoning_effort":"medium","service_tier":"fast","approvals_reviewer":"auto_review"}}}
            {"type":"response_item","payload":{"type":"message","content":"fake-private-conversation-never-display"}}
            {"type":"response_item","payload":{"type":"configuration_update","reasoning":{"effort":"high"}}}
            {"type":"event_msg","payload":{"type":"auto_approval_review_completed","status":"approved"}}
            """);
        File.WriteAllText(Path.Combine(sessions, "rollout-other.jsonl"), "{\"type\":\"turn_context\",\"payload\":{\"model\":\"other-recorded-model\"}}\n");
        var previous = viewModel.Home; var originalDiagnostics = DiagnosticsPanel.Service;
        var diagnosticRuntime = new SmokeDiagnosticRuntime(new NativeSessionDiagnosticRuntime()); DiagnosticsPanel.Service = new(diagnosticRuntime);
        try
        {
            await SwitchHomeAsync(home); var before = store.Load(home); NavigateTo(DiagnosticsNav);
            Check(DiagnosticsPanel.Visibility == Visibility.Visible && DiagnosticsPanel.DiagnosticFilesInput.SelectedItem is null && !DiagnosticsPanel.ReadDiagnosticButton.IsEnabled && diagnosticRuntime.Reads == 0,
                "Diagnostic navigation never selects or reads a chat automatically");
            await DiagnosticsPanel.ReadListAsync(); Check(DiagnosticsPanel.Listing?.Files.Count == 2 && DiagnosticsPanel.DiagnosticFilesInput.SelectedItem is null && diagnosticRuntime.Reads == 0,
                "Diagnostic refresh lists only current-home files and still requires explicit selection");
            DiagnosticsPanel.DiagnosticFilesInput.SelectedItem = DiagnosticsPanel.Listing!.Files.Single(file => file.Path == selected); await DiagnosticsPanel.ReadSelectionAsync();
            Check(DiagnosticsPanel.DiagnosticResultText.Text.Contains("recorded-chat-model") && DiagnosticsPanel.DiagnosticResultText.Text.Contains("high") && DiagnosticsPanel.DiagnosticResultText.Text.Contains("approved") &&
                DiagnosticsPanel.DiagnosticResultText.Text.Contains("本记录未提供") && !DiagnosticsPanel.DiagnosticResultText.Text.Contains("fake-private-conversation") && !DiagnosticsPanel.DiagnosticResultText.Text.Contains("diagnostic-config-model"),
                "Selected chat diagnostics show whitelisted observed parameters and review status without prompts or inferred review models");
            await CaptureAsync(output, "chat-parameters-light.png");
            connectionDraft.Model = "unsaved-diagnostic-model"; NavigateTo(UpdatesNav); NavigateTo(DiagnosticsNav);
            Check(connectionDraft.HasDraft && connectionDraft.Model == "unsaved-diagnostic-model" && before.SameContents(store.Load(home)),
                "Diagnostic navigation and reads preserve independent configuration drafts and saved files");
            diagnosticRuntime.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); var read = DiagnosticsPanel.ReadSelectionAsync();
            Check(DiagnosticsPanel.IsReading && !ChooseHomeButton.IsEnabled && !ApplyButton.IsEnabled && DiagnosticsPanel.CancelDiagnosticButton.IsEnabled,
                "Pending diagnostic reads expose cancellation and block conflicting home and apply actions");
            DiagnosticsPanel.CancelDiagnosticClicked(DiagnosticsPanel.CancelDiagnosticButton, new()); await read;
            Check(!DiagnosticsPanel.IsReading && DiagnosticsPanel.DiagnosticResultText.Text.Contains("取消") && !DiagnosticsPanel.DiagnosticResultText.Text.Contains("recorded-chat-model"),
                "Cancelled diagnostic reads remove old observations and restore controls");
            diagnosticRuntime.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); read = DiagnosticsPanel.ReadSelectionAsync();
            DiagnosticsPanel.DiagnosticFilesInput.SelectedItem = DiagnosticsPanel.Listing!.Files.Single(file => file.Path != selected); await read;
            Check(!DiagnosticsPanel.DiagnosticResultText.Text.Contains("recorded-chat-model") && !DiagnosticsPanel.IsReading, "Changing the diagnostic selection rejects a delayed old result");
            diagnosticRuntime.Gate = null; await DiagnosticsPanel.ReadSelectionAsync();
            Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(900, 760));
            await CaptureAsync(output, "chat-parameters-compact-dark.png");
            Check(DiagnosticsPanel.DiagnosticFilesInput.TransformToVisual(Root).TransformPoint(new()).X + DiagnosticsPanel.DiagnosticFilesInput.ActualWidth <= Root.ActualWidth,
                "Diagnostic selector remains within the compact dark window");
            Root.RequestedTheme = ElementTheme.Light; LoadConnectionDraft();
            await SwitchHomeAsync(previous); Check(DiagnosticsPanel.Listing is null && !DiagnosticsPanel.DiagnosticResultText.Text.Contains("other-recorded-model"), "Changing homes clears prior diagnostic files and observations");

            NavigateTo(UpdatesNav);
            var updateRuntime = new SmokeUpdateRuntime(output) { NewRelease = false }; updateDownloads = new(updateRuntime);
            await CheckForUpdatesAsync(); Check(availableUpdate is null && UpdatesPanel.InstallUpdateButton.Visibility == Visibility.Collapsed && UpdatesPanel.CheckUpdateButton.IsEnabled,
                "Software update handles no newer stable release without network access");
            updateRuntime.NewRelease = true; await CheckForUpdatesAsync();
            Check(availableUpdate?.Version == new Version(99, 0, 0) && UpdatesPanel.DownloadUpdateButton.Visibility == Visibility.Visible && downloadedInstaller is null,
                "A newer formal release offers download and keeps preview installation scope explicit");
            updateRuntime.FailCheck = true; await CheckForUpdatesAsync();
            Check(availableUpdate is null && updateMessage.Contains("无法完成") && UpdatesPanel.InstallUpdateButton.Visibility == Visibility.Collapsed,
                "Failed update checks clear an earlier release and never leave a stale install action");
            updateRuntime.FailCheck = false; await CheckForUpdatesAsync();
            updateRuntime.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); var download = DownloadUpdateAsync();
            Check(downloadingUpdate && !UpdatesPanel.CheckUpdateButton.IsEnabled && UpdatesPanel.CancelUpdateButton.IsEnabled && downloadedInstaller is null,
                "Pending installer download has one active request and a working cancel action");
            CancelUpdateClicked(UpdatesPanel.CancelUpdateButton, new()); await download;
            Check(downloadedInstaller is null && !downloadingUpdate && updateMessage.Contains("取消") && UpdatesPanel.CheckUpdateButton.IsEnabled,
                "Cancelled downloads cannot expose an installer and allow a retry");
            updateRuntime.Gate = null; updateRuntime.FailDownload = true; await DownloadUpdateAsync();
            Check(downloadedInstaller is null && UpdatesPanel.InstallUpdateButton.Visibility == Visibility.Collapsed && updateMessage.Contains("校验失败"),
                "Failed installer verification never enables install");
            updateRuntime.FailDownload = false; await DownloadUpdateAsync();
            Check(downloadedInstaller is not null && UpdatesPanel.InstallUpdateButton.Visibility == Visibility.Visible && UpdatesPanel.DownloadUpdateButton.Visibility == Visibility.Collapsed,
                "Completed verified download exposes the explicit install-and-exit action");
            await CaptureAsync(output, "software-update-light.png");
            connectionDraft.Model = "unsaved-update-model"; var launched = 0; smokeInstallerLaunch = _ => launched++;
            smokeDialog = _ => Task.FromResult(ContentDialogResult.None); await InstallUpdateAsync();
            Check(connectionDraft.HasDraft && launched == 0 && !closing, "Continuing to edit an unsaved configuration blocks update shutdown and preserves its draft");
            LoadConnectionDraft(); smokeDialog = async dialog =>
            {
                var text = ConfigurationDialogText(dialog);
                Check(text.Contains(viewModel.Home) && text.Contains("覆盖安装当前 Codex Dock") && text.Contains("手机") && text.Contains("模型") && !ChooseHomeButton.IsEnabled,
                    "Update confirmation identifies formal installer scope current home and both interrupted gateways");
                var shown = dialog.ShowAsync(); try { await CaptureAsync(output, "software-update-confirm.png", dialog); } finally { dialog.Hide(); await shown; }
                return ContentDialogResult.None;
            };
            await InstallUpdateAsync(); Check(!closing && launched == 0 && downloadedInstaller is not null, "Cancelling the concrete update confirmation keeps the preview and installer available");
            var installer = downloadedInstaller!; var bytes = File.ReadAllBytes(installer.Path); File.AppendAllText(installer.Path, "tampered");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary); await InstallUpdateAsync();
            Check(!closing && launched == 0 && updateMessage.Contains("发生变化") && AppWindow.IsVisible,
                "Installer tampering is detected again before any update shutdown or launch");
            File.WriteAllBytes(installer.Path, bytes);
            var gateways = (SmokeGatewayRuntime)gatewayRuntime; var oldConfigured = gateways.Configured.ToDictionary(); var oldRunning = gateways.Running.ToDictionary();
            try
            {
                gateways.Configured[GatewayKind.Model] = gateways.Configured[GatewayKind.Mobile] = true; gateways.Running[GatewayKind.Model] = gateways.Running[GatewayKind.Mobile] = true;
                gateways.FailStop = GatewayKind.Model; await InstallUpdateAsync();
                Check(!closing && launched == 0 && AppWindow.IsVisible && updateMessage.Contains("未能启动"), "Unconfirmed gateway shutdown leaves the preview accessible and never invokes the installer");
            }
            finally { gateways.FailStop = null; foreach (var pair in oldConfigured) gateways.Configured[pair.Key] = pair.Value; foreach (var pair in oldRunning) gateways.Running[pair.Key] = pair.Value; }
            Root.RequestedTheme = ElementTheme.Dark; await CaptureAsync(output, "software-update-compact-dark.png"); Root.RequestedTheme = ElementTheme.Light;
            Check(UpdatesPanel.InstallUpdateButton.TransformToVisual(Root).TransformPoint(new()).X + UpdatesPanel.InstallUpdateButton.ActualWidth <= Root.ActualWidth,
                "Installer action remains within the compact update page");
            await RunUpdateExitSmokeAsync(output, installer, Check);
        }
        finally
        {
            smokeDialog = null; smokeInstallerLaunch = null; DiagnosticsPanel.Service = originalDiagnostics; updateDownloads = new(new SmokeUpdateRuntime(output));
            availableUpdate = null; downloadedInstaller = null; await SwitchHomeAsync(previous); NavigateTo(WorkspaceNav);
        }
    }
    private async Task RunUpdateExitSmokeAsync(string output, VerifiedInstaller installer, Action<bool, string> check)
    {
        var fixture = new MainWindow(new(options.Home) { SmokeOutput = output }); fixture.Root.Loaded -= fixture.RootLoaded;
        fixture.initialized = true; fixture.LoadWorkspace(options.Home); fixture.NavigateTo(UpdatesNav);
        fixture.downloadedInstaller = installer; fixture.smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
        var gateways = (SmokeGatewayRuntime)fixture.gatewayRuntime; gateways.Configured[GatewayKind.Model] = gateways.Configured[GatewayKind.Mobile] = true;
        gateways.Running[GatewayKind.Model] = gateways.Running[GatewayKind.Mobile] = true;
        var launched = false; fixture.smokeInstallerLaunch = _ =>
        {
            check(gateways.Running.Values.All(value => value == false) && !fixture.applicationRuntime.IsEnhancementRunning,
                "WinUI update waits for both gateway shutdowns and enhancement stop before its fake installer launch"); launched = true;
        };
        fixture.Activate(); await Task.Delay(100);
        try
        {
            await fixture.InstallUpdateAsync();
            await Task.Delay(180);
            check(launched && fixture.closing && fixture.allowClose && (fixture.AppWindow is null || !fixture.AppWindow.IsVisible),
                $"A successful isolated install launch exits through the actual WinUI window close path (launched={launched}, closing={fixture.closing}, allowClose={fixture.allowClose}, visible={fixture.AppWindow?.IsVisible})");
        }
        finally { if (!fixture.closing) { fixture.allowClose = true; fixture.Close(); } }
    }
}
