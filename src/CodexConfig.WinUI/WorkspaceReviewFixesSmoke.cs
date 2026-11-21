using System.Net;
using System.Net.Sockets;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunWorkspaceReviewFixesSmokeAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        var previous = viewModel.Home; var previousSources = configurationSources;
        var phone = (SmokeMobileRuntime)mobileRuntime; var gateways = (SmokeGatewayRuntime)gatewayRuntime;
        var previousConfigured = gateways.Configured[GatewayKind.Mobile]; var previousRunning = gateways.Running[GatewayKind.Mobile];
        var home = Path.Combine(output, "review-fixes-home"); Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='review-original-model'\nopenai_base_url='https://review.invalid/v1'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-review-only-key\"}");
        Task<GatewayOperationResult?>? pendingOperation = null;
        try
        {
            await SwitchHomeAsync(home); var original = store.Load(home);
            using var phonePort = new TcpListener(IPAddress.Loopback, 0); using var controlPort = new TcpListener(IPAddress.Loopback, 0);
            phonePort.Start(); controlPort.Start(); var port = ((IPEndPoint)phonePort.LocalEndpoint).Port; var control = ((IPEndPoint)controlPort.LocalEndpoint).Port;
            phonePort.Stop(); controlPort.Stop();
            phone.Settings[home] = new(home, true, "https://review-phone.invalid", "fixture", port, control, "review-phone-revision");
            phone.Running = true; gateways.Configured[GatewayKind.Mobile] = true; gateways.Running[GatewayKind.Mobile] = true;
            gateways.WorkerChanged = (kind, running) => { if (kind == GatewayKind.Mobile) { phone.Running = running; if (running) phone.Checked = false; } };
            NavigateTo(MobileSettingsNav); await RefreshMobileAsync();
            var result = await OperateGatewayAsync(GatewayKind.Mobile, GatewayCommand.Stop);
            Check(result?.Succeeded == true && mobileView?.Running == false && gatewayManagement.Overview?.Mobile.Running == false &&
                !MobilePanel.CheckMobileEntryButton.IsEnabled && ContinueMobilePairButton.Visibility == Visibility.Collapsed && MobilePanel.MobileNextStepText.Text.Contains("启动手机服务"),
                "Stopping the phone service immediately updates pairing guidance and disables commands against the stopped worker");
            result = await OperateGatewayAsync(GatewayKind.Mobile, GatewayCommand.Start);
            Check(result?.Succeeded == true && mobileView?.Running == true && gatewayManagement.Overview?.Mobile.Running == true &&
                MobilePanel.CheckMobileEntryButton.IsEnabled && ContinueMobilePairButton.Visibility == Visibility.Visible && MobilePanel.MobileNextStepText.Text.Contains("检测 HTTPS"),
                "Starting the phone service immediately enables its next pairing step without a manual page refresh");
            phone.Checked = true; await RefreshMobileAsync(); mobilePair = new("review-old-pair", [], DateTimeOffset.UtcNow.AddMinutes(5));
            result = await OperateGatewayAsync(GatewayKind.Mobile, GatewayCommand.Restart);
            Check(result?.Succeeded == true && mobileView is { Running: true, EntryChecked: false } && mobilePair is null && MobilePanel.MobileNextStepText.Text.Contains("检测 HTTPS"),
                "Restarting the phone service clears the prior pairing grant and HTTPS result before showing the new worker state");

            await OperateGatewayAsync(GatewayKind.Mobile, GatewayCommand.Stop);
            phone.StatusGate = new(TaskCreationOptions.RunContinuationsAsynchronously); var pendingRead = RefreshMobileAsync();
            pendingOperation = OperateGatewayAsync(GatewayKind.Mobile, GatewayCommand.Start);
            Check(!pendingOperation.IsCompleted && ManualApplicationBusy && !ChooseHomeButton.IsEnabled && !ApplyButton.IsEnabled,
                "A phone command retains its operation guard while an earlier status read is still pending");
            var gate = phone.StatusGate; phone.StatusGate = null; gate.TrySetResult(); await pendingRead; result = await pendingOperation; pendingOperation = null;
            Check(result?.Succeeded == true && mobileView?.Running == true && MobilePanel.CheckMobileEntryButton.IsEnabled && ContinueMobilePairButton.Visibility == Visibility.Visible,
                "An older stopped-state read cannot overwrite the state observed after successful phone startup");

            connectionDraft.Model = "review-unsaved-model"; mobileDraft.PublicUrl = "https://review-unsaved-phone.invalid";
            result = await OperateGatewayAsync(GatewayKind.Mobile, GatewayCommand.Stop);
            Check(result?.Succeeded == true && mobileView?.Running == false && connectionDraft.Model == "review-unsaved-model" && connectionDraft.HasDraft &&
                mobileDraft.PublicUrl == "https://review-unsaved-phone.invalid" && mobileDraft.HasDraft && original.SameContents(store.Load(home)),
                "Phone service refresh preserves independent model and phone drafts without writing their settings");
            DiscardMobileClicked(DiscardMobileButton, new()); LoadConnectionDraft();
            phone.StatusFailure = true; result = await OperateGatewayAsync(GatewayKind.Mobile, GatewayCommand.Start);
            Check(result?.Succeeded == true && mobileView is null && !MobilePanel.CheckMobileEntryButton.IsEnabled && ContinueMobilePairButton.Visibility == Visibility.Collapsed && mobileMessageError,
                "A failed phone status read leaves pairing unavailable and reports an unknown state instead of retaining an old result");
            phone.StatusFailure = false; await RefreshMobileAsync();

            connectionDraft.Model = "review-saved-model"; smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            if (!await SaveConnectionAsync()) throw new InvalidOperationException("Review fixture save failed: " + shellError);
            var requests = 0;
            configurationSources = new(new SmokeSourceRuntime((request, bytes, cancellation) =>
            {
                requests++; var model = new TomlDocument(bytes).String("model")!;
                return Task.FromResult(new ConfigurationReadResult(DateTimeOffset.Now, request.Executable, request.Project, request.Profile,
                    [new("model", model, model, "fixture main file", "fixture")], []));
            }));
            NavigateTo(SourcesNav); await ReadConfigurationSourcesAsync(); var sourceBeforeRestore = sourceResult;
            NavigateTo(RecoveryNav); smokeDialog = _ => Task.FromResult(ContentDialogResult.None);
            Check(!await RestoreConfigurationAsync() && ReferenceEquals(sourceResult, sourceBeforeRestore) && sourceResult?.Rows.Single().FileValue == "review-saved-model" &&
                new TomlDocument(store.Load(home).Config).String("model") == "review-saved-model",
                "Cancelling configuration restore preserves the saved file and its still-valid source snapshot");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            var restored = await RestoreConfigurationAsync();
            NavigateTo(SourcesNav);
            Check(restored && sourceResult is null && SourcesPanel.SourceRows.Children.Count == 0 && SourcesPanel.SourceLayersText.Text.Length == 0 &&
                SourcesPanel.SourceStatusText.Text.Contains("重新读取") && new TomlDocument(store.Load(home).Config).String("model") == "review-original-model" && requests == 1,
                "Successful configuration restore clears stale source rows and requires another explicit read");
            Check(sourceResult is null && requests == 1, "Returning to configuration sources after restore never launches a native read automatically");
            await ReadConfigurationSourcesAsync();
            Check(sourceResult?.Rows.Single().FileValue == "review-original-model" && requests == 2,
                "An explicit source read after restore compares the restored saved file");
        }
        finally
        {
            phone.StatusGate?.TrySetResult(); phone.StatusGate = null; phone.StatusFailure = false;
            if (pendingOperation is not null) await pendingOperation;
            gateways.WorkerChanged = null; gateways.Configured[GatewayKind.Mobile] = previousConfigured; gateways.Running[GatewayKind.Mobile] = previousRunning;
            phone.Running = false; configurationSources = previousSources;
            mobileDraft.Load(mobileDraft.Settings); LoadConnectionDraft();
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); await SwitchHomeAsync(previous); smokeDialog = null;
            NavigateTo(WorkspaceNav);
        }
    }
}
