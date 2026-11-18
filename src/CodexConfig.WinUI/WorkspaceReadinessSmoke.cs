using System.Text;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunWorkspaceReadinessSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        var previous = viewModel.Home; var runtime = (SmokeDesktopApplyRuntime)applicationRuntime; var restarts = runtime.Restarts;
        var previousTargets = runtime.Targets;
        var home = Path.Combine(output, "first-run-home"); var bad = Path.Combine(output, "first-run-broken-home");
        try
        {
            NavigateTo(WorkspaceNav); await SwitchHomeAsync(home);
            Check(WorkspaceNextButton.Content.ToString() == "设置连接" && WorkspaceConnectionCard.Visibility == Visibility.Collapsed && !ApplyButton.IsEnabled && connectionDraft.Available &&
                !connectionDraft.HasDraft && !Directory.Exists(home) && !viewModel.IsBusy, "First-run workspace explains the next step keeps editing available and creates no home or settings");
            await ApplyDesktopAsync(); await TryAutomaticEnhancementsAsync();
            Check(runtime.Restarts == restarts && !Directory.Exists(home), "Empty first-run configuration cannot trigger manual desktop application or automatic reconnect");
            await CaptureAsync(output, "first-run-workspace-light.png");
            await RunWorkspaceNextAsync();
            Check(ConnectionPanel.Visibility == Visibility.Visible && SetupAuthenticationChoices.Visibility == Visibility.Visible && BaseUrlInput.IsEnabled && ApiKeyInput.IsEnabled && !ApplyButton.IsEnabled,
                "First-run setup action opens the existing editable connection form without starting Codex");
            BaseUrlInput.Text = "https://first-ui.invalid/v1"; ConnectionKindInput.SelectedIndex = (int)ConnectionKind.ResponsesApi;
            ApiKeyInput.Password = "fake-first-ui-secret";
            for (var i = 0; i < 30 && connectionDraft.ApiKey != "fake-first-ui-secret"; i++) await Task.Delay(20);
            SelectSetupAuthentication(native: true); await ReadOfficialLoginAsync(backup: false, native: true);
            Check(SetupNativeCard.Visibility == Visibility.Visible && SetupNativeStatus.Text.Contains("未") && !Directory.Exists(home) &&
                connectionDraft.ApiKey == "fake-first-ui-secret", "Checking native login without credentials leaves the alternate API draft intact");
            SelectSetupAuthentication(native: false); await DiscoverModelsAsync();
            Check(DiscoveredModelsInput.Visibility == Visibility.Visible && connectionDraft.Model.Length == 0 && !Directory.Exists(home), "Reading available models is explicit and renders a choice on the first connection page");
            DiscoveredModelsInput.SelectedIndex = 0;
            Check(connectionDraft.Model == DiscoveredModelsInput.SelectedItem as string, "Selecting the listed model updates the same connection draft");
            BehaviorModelInput.Text = "first-selected-model";
            for (var i = 0; i < 30 && (connectionDraft.ApiKey != "fake-first-ui-secret" || connectionDraft.Model != "first-selected-model"); i++) await Task.Delay(20);
            Check(connectionDraft.ApiKey == "fake-first-ui-secret" && connectionDraft.Model == "first-selected-model", "Immediate password and adjacent text edits retain the first-run key in the shared draft");
            runtime.Targets = [new(Path.Combine(output, "first-run-Codex.exe"), [])];
            await PrepareConnectionReviewAsync();
            Check(ConnectionReviewCard.Visibility == Visibility.Visible && connectionReview!.Review.Targets.Contains(Path.Combine(home, "config.toml")) &&
                !ConnectionReviewSummary.Text.Contains("fake-first-ui-secret") && SaveConnectionButton.Content.ToString() == "保存并打开 Codex" && !Directory.Exists(home),
                "First connection review shows the scope and default model without writing files or displaying the key");
            await CaptureAsync(output, "first-run-save-review.png");
            SetupEditReviewClicked(SetupEditReviewButton, new());
            Check(connectionReview is null && connectionDraft.HasDraft && !Directory.Exists(home), "Returning from first connection review preserves its draft without creating configuration or backup files");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.None);
            Check(!await SwitchHomeAsync(Path.Combine(output, "cancel-first-run-switch")) && viewModel.Home == home && connectionDraft.HasDraft && !Directory.Exists(home),
                "Continuing to edit first-run settings cancels a directory switch without discarding the draft");
            smokeDialog = _ => throw new InvalidOperationException("Inline connection review should be the only confirmation.");
            await PrepareConnectionReviewAsync();
            runtime.Targets = [new(Path.Combine(output, "different-Codex.exe"), [])];
            await ConfirmConnectionReviewAsync(apply: true);
            Check(connectionReview is null && connectionDraft.HasDraft && !Directory.Exists(home) && runtime.Restarts == restarts,
                "A changed launch target invalidates the inline review before any save or restart");
            await PrepareConnectionReviewAsync(); await ConfirmConnectionReviewAsync(apply: false);
            Check(configurationResult?.BackupPath is { } backup && File.Exists(backup) && !connectionDraft.HasDraft &&
                viewModel.CanApplyConfiguration && ApplyButton.IsEnabled && WorkspaceSetupCard.Visibility == Visibility.Collapsed && runtime.Restarts == restarts,
                "Confirmed first connection save creates its encrypted backup clears setup guidance and waits for separate application");
            var saved = store.Load(home); var firstConfig = saved.Config!.ToArray(); var firstAuth = saved.Auth!.ToArray();
            Check(new TomlDocument(saved.Config).String("model") == "first-selected-model" && store.ReadSettings(saved).ApiKey == "fake-first-ui-secret",
                "First connection write uses exactly the reviewed model and credential");

            Directory.CreateDirectory(bad); var configPath = Path.Combine(bad, "config.toml");
            const string malformed = "private_field='fake-never-show-config-content'\nmodel=[unfinished"; File.WriteAllText(configPath, malformed);
            await SwitchHomeAsync(bad); NavigateTo(WorkspaceNav);
            Check(!viewModel.IsBusy && !viewModel.ConfigurationAvailable && viewModel.Snapshot is null && WorkspaceSetupCard.Visibility == Visibility.Visible && SetupRecoveryButton.Visibility == Visibility.Visible &&
                !BaseUrlInput.IsEnabled && !ApplyButton.IsEnabled && !ErrorBar.Message.Contains("fake-never-show") && File.ReadAllText(configPath) == malformed,
                "Malformed first-run TOML clears stale workspace data disables writes and offers safe repair guidance");
            Root.RequestedTheme = ElementTheme.Dark; await CaptureAsync(output, "first-run-broken-dark.png"); Root.RequestedTheme = ElementTheme.Light;
            OpenRecoveryClicked(SetupRecoveryButton, new());
            Check(RecoveryPanel.Visibility == Visibility.Visible && !RestoreConfigurationButton.IsEnabled && File.ReadAllText(configPath) == malformed,
                "Damaged workspace can inspect missing recovery records without overwriting its file");
            File.WriteAllBytes(configPath, firstConfig); File.WriteAllBytes(Path.Combine(bad, "auth.json"), firstAuth); await ReloadWorkspaceAsync(); NavigateTo(WorkspaceNav);
            Check(viewModel.CanApplyConfiguration && viewModel.Error.Length == 0 && ApplyButton.IsEnabled && WorkspaceSetupCard.Visibility == Visibility.Collapsed,
                "Repair and reload unlock the same window after a malformed configuration");

            var encrypted = Path.Combine(bad, MixedConnection.FileName); File.WriteAllBytes(encrypted, [1, 2, 3, 4, 5]);
            File.WriteAllText(configPath, "model_provider='codex_config_mixed'\n"); await ReloadWorkspaceAsync();
            Check(!viewModel.IsBusy && viewModel.Snapshot is null && ErrorBar.IsOpen && ErrorBar.Message.Contains("解密") && WorkspaceSetupCard.Visibility == Visibility.Visible && !ApplyButton.IsEnabled,
                "Encrypted connection read errors close the loading state and retain a usable repair and directory switch flow");
            File.Delete(encrypted); File.WriteAllBytes(configPath, firstConfig); await ReloadWorkspaceAsync();
            File.WriteAllText(Path.Combine(bad, "auth.json"), "{\"fake_private\":\"never-display-auth\", unfinished"); await ReloadWorkspaceAsync();
            Check(!viewModel.IsBusy && viewModel.Snapshot is null && ErrorBar.Message.Contains("auth.json") && !ErrorBar.Message.Contains("never-display-auth") && !ApiKeyInput.IsEnabled,
                "Malformed authentication on reload does not expose bytes or retain a previously loaded credential");
            File.WriteAllBytes(Path.Combine(bad, "auth.json"), firstAuth); await ReloadWorkspaceAsync();
            Check(viewModel.CanApplyConfiguration && !ErrorBar.IsOpen && ApiKeyInput.IsEnabled, "Repairing authentication restores the same workspace without restarting the shell");

            var official = Path.Combine(output, "first-run-official-home"); Directory.CreateDirectory(official);
            File.WriteAllText(Path.Combine(official, "config.toml"), "model='native-official-model'\n");
            var auth = Encoding.UTF8.GetBytes("{\"auth_mode\":\"chatgpt\",\"tokens\":{\"access_token\":\"fake-first-official-access\",\"refresh_token\":\"fake-first-official-refresh\",\"id_token\":\"fake-first-official-id\"}}");
            File.WriteAllBytes(Path.Combine(official, "auth.json"), auth); await SwitchHomeAsync(official);
            Check(viewModel.ConnectionLabel == "官方账号" && viewModel.CanApplyConfiguration && ApplyButton.IsEnabled && WorkspaceSetupCard.Visibility == Visibility.Collapsed &&
                auth.SequenceEqual(File.ReadAllBytes(Path.Combine(official, "auth.json"))) && runtime.Restarts == restarts,
                "Existing official login bypasses API setup while preserving native credentials and requiring explicit application");

            var nativeHome = Path.Combine(output, "native-login-home"); await SwitchHomeAsync(nativeHome); NavigateTo(ConnectionNav);
            SelectSetupAuthentication(native: true);
            await OpenWorkspaceDesktopAsync(forLogin: true);
            Check(runtime.Restarts == restarts + 1 && runtime.LastTarget?.IsRunning == false && !runtime.LastForce && !Directory.Exists(nativeHome),
                "The native login action opens the isolated target with no restart, force exit or Dock file save");
            Directory.CreateDirectory(nativeHome);
            File.WriteAllText(Path.Combine(nativeHome, "config.toml"), "model='native-default'\n");
            File.WriteAllBytes(Path.Combine(nativeHome, "auth.json"), auth);
            await ReadOfficialLoginAsync(backup: false, native: true);
            Check(viewModel.CanApplyConfiguration && viewModel.ModelLabel == "native-default" && SetupNativeStatus.Text.Contains("已读到登录") && store.LatestBackup(nativeHome) is null,
                "Re-reading native login adopts credentials and defaults written by Codex without a Dock transaction");
            NavigateTo(WorkspaceNav); var activations = runtime.Activations;
            await RunWorkspaceNextAsync();
            Check(WorkspaceNextButton.Content.ToString() == "回到 Codex" && runtime.Activations == activations + 1 && runtime.Restarts == restarts + 1,
                "Home returns to the identified running window in one action without restarting it");

            var combinedHome = Path.Combine(output, "first-run-apply-home"); await SwitchHomeAsync(combinedHome); NavigateTo(ConnectionNav);
            runtime.Targets = [new(Path.Combine(output, "apply-Codex.exe"), [])];
            connectionDraft.BaseUrl = "https://first-apply.invalid/v1"; connectionDraft.ApiKey = "fake-first-apply-key"; connectionDraft.Model = "apply-model";
            await PrepareConnectionReviewAsync(); await ConfirmConnectionReviewAsync(apply: true);
            Check(runtime.Restarts == restarts + 2 && lastApplication?.DesktopOpened == true && store.ReadSettings(store.Load(combinedHome)).Model == "apply-model" &&
                store.LatestBackup(combinedHome) is not null && !connectionDraft.HasDraft && currentPage == ConnectionNav,
                "One inline confirmation backs up, saves the reviewed connection and opens the identified desktop target");
        }
        finally
        {
            runtime.Targets = previousTargets; openedDesktop = null; runningDesktopCount = null;
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); await SwitchHomeAsync(previous); smokeDialog = null; Root.RequestedTheme = ElementTheme.Light; NavigateTo(WorkspaceNav);
        }
    }
    private async Task RunGatewayDiagnosticsSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        var runtime = (SmokeGatewayRuntime)gatewayRuntime; var home = viewModel.Home; var before = store.Load(home);
        var configured = runtime.Configured.ToDictionary(); var running = runtime.Running.ToDictionary(); var trace = runtime.Trace.Count;
        var metrics = new BridgeMetrics(3, 2, 4, 1, 0, 2, DateTimeOffset.UtcNow, null, null, null, 1, 2, 7, 21, 34, 55, "completed")
        { LastTriggerProtocol = "v2", LastTriggerTransport = "sse", LastTransfer = new(DateTimeOffset.UtcNow, "incomplete", "sse", "req_fixture", null) };
        try
        {
            runtime.Configured[GatewayKind.Model] = true; runtime.Running[GatewayKind.Model] = true;
            runtime.ModelDiagnostics = ModelGatewayDiagnostics.FromStatus(new(8, 2, 1, ModelBridge.ProtocolVersion, LastUpstreamStatus: 200, Metrics: metrics, UltrafastRequests: 1), "v2");
            NavigateTo(GatewaysNav); await RefreshGatewaysAsync(); ModelGatewayDiagnosticsExpander.IsExpanded = true;
            Check(ModelGatewayCompactionConfiguration.Text.Contains("文件允许 v2") && ModelGatewayCompactionMetrics.Text.Contains("trigger 4 个") &&
                ModelGatewayCompactionMetrics.Text.Contains("压缩条目 1 个 · 确认完成 0 次") && ModelGatewayCompactionMetrics.Text.Contains("HTTP 200 不能") && ModelGatewayUpstreamMetrics.Text.Contains("200"),
                "WinUI gateway diagnostics distinguish allowed configuration requested compaction returned items and confirmed success");
            Check(ModelGatewayCompactionMetrics.Text.Contains("v2 / SSE") && ModelGatewayCompactionMetrics.Text.Contains("计数可能不全") && ModelGatewayLatencyMetrics.Text.Contains("7 ms") &&
                ModelGatewayLatencyMetrics.Text.Contains("55 ms") && ModelGatewayMetricsScope.Text.Contains("重启后重新累计"),
                "Gateway details retain trigger transport incomplete inspection latency and the per-process measurement boundary");
            CopyModelTransferClicked(CopyModelTransferButton, new());
            Check(CopyModelTransferButton.IsEnabled && smokeCopiedTexts.LastOrDefault() == ModelGatewayTransferText.Text &&
                ModelGatewayTransferText.Text.Contains("请求 ID req_fixture") && ModelGatewayTransferText.Text.Contains("最终响应 ID 未收到"),
                "Copying a truncated gateway response retains the request ID and marks the final response ID as absent");
            PageScroll.ChangeView(null, 210, null, true); await CaptureAsync(output, "gateway-diagnostics-light.png");
            connectionDraft.Model = "unsaved-while-reading-metrics"; var revision = connectionDraft.Revision; await RefreshGatewaysAsync();
            Check(connectionDraft.HasDraft && connectionDraft.Revision == revision && before.SameContents(store.Load(home)) && runtime.Trace.Count == trace,
                "Refreshing diagnostic counters preserves the configuration draft and never starts or stops a gateway");
            Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(900, 760)); PageScroll.ChangeView(null, 260, null, true);
            await CaptureAsync(output, "gateway-diagnostics-compact-dark.png");
            Check(ModelGatewayCompactionMetrics.ActualWidth > 250 && ModelGatewayCompactionMetrics.TransformToVisual(Root).TransformPoint(new()).X + ModelGatewayCompactionMetrics.ActualWidth <= Root.ActualWidth,
                "Expanded gateway diagnostics wrap inside the compact dark window while the draft actions remain available");
            runtime.Running[GatewayKind.Model] = false; await RefreshGatewaysAsync();
            Check(ModelGatewayCompactionMetrics.Text.Length == 0 && ModelGatewayLatencyMetrics.Text.Length == 0 && ModelGatewayDiagnosticsStatus.Text.Contains("未运行"),
                "Stopped gateway clears prior counters instead of presenting an old process snapshot");
            runtime.Running[GatewayKind.Model] = null; await RefreshGatewaysAsync();
            Check(ModelGatewayCompactionMetrics.Text.Length == 0 && ModelGatewayDiagnosticsStatus.Text.Contains("尚未确认"), "Unknown gateway listener state cannot reuse prior compaction metrics");
            runtime.Running[GatewayKind.Model] = true; runtime.FailModelProbe = true; await RefreshGatewaysAsync();
            Check(ModelGatewayCompactionMetrics.Text.Length == 0 && ModelGatewayDiagnosticsStatus.Text.Contains("读取失败"), "Failed gateway refresh clears old measurements and explains that statistics are unconfirmed");
            runtime.FailModelProbe = false; runtime.ModelDiagnostics = ModelGatewayDiagnostics.FromStatus(new(2, 0, 0), null); await RefreshGatewaysAsync();
            Check(ModelGatewayCompactionMetrics.Text.Contains("未返回压缩指标") && !ModelGatewayCompactionMetrics.Text.Contains("确认完成 0"),
                "An older gateway payload displays missing metrics without implying zero successful compactions");
            LoadConnectionDraft(); Root.RequestedTheme = ElementTheme.Light;
            await SwitchHomeAsync(Path.Combine(output, "diagnostic-empty-other-home"));
            Check(gatewayManagement.Overview is null && ModelGatewayCompactionMetrics.Text.Length == 0 && !Directory.Exists(viewModel.Home),
                "Changing directories removes another home's diagnostic snapshot without creating the empty destination");
        }
        finally
        {
            runtime.ModelDiagnostics = null; runtime.FailModelProbe = false;
            foreach (var pair in configured) runtime.Configured[pair.Key] = pair.Value;
            foreach (var pair in running) runtime.Running[pair.Key] = pair.Value;
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); await SwitchHomeAsync(home); smokeDialog = null;
            ModelGatewayDiagnosticsExpander.IsExpanded = false; Root.RequestedTheme = ElementTheme.Light; NavigateTo(WorkspaceNav);
        }
    }
}
