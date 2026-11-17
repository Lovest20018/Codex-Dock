using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Net;
using System.Net.Sockets;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunGatewayManagementSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        var runtime = (SmokeGatewayRuntime)gatewayRuntime;
        var original = store.Load(viewModel.Home); var home = viewModel.Home;
        NavigateTo(GatewaysNav); await RefreshGatewaysAsync();
        Check(GatewaysPanel.Visibility == Visibility.Visible && ConnectionPanel.Visibility == Visibility.Collapsed && PageTitle.Text == "运行与增强" &&
            !ModelGatewayStartButton.IsEnabled && !MobileGatewayStartButton.IsEnabled && !ModelGatewayAutomaticInput.IsOn && !MobileGatewayAutomaticInput.IsOn,
            "WinUI gateway page defaults both auto-start choices off and does not start unconfigured workers");
        Check(runtime.Trace.Count == 0 && original.SameContents(store.Load(home)), "Gateway refresh is read-only and never applies a connection draft");
        runtime.Configured[GatewayKind.Model] = runtime.Configured[GatewayKind.Mobile] = true;
        await RefreshGatewaysAsync();
        Check(ModelGatewayStartButton.IsEnabled && MobileGatewayStartButton.IsEnabled && !ModelGatewayStopButton.IsEnabled,
            "Saved stopped gateway definitions enable start without claiming an existing running process");
        ModelGatewayAutomaticInput.IsOn = true; MobileGatewayAutomaticInput.IsOn = true;
        Check(gatewayManagement.LoadPreferences(home) == new GatewayStartupSettings(true, true) && runtime.Trace.Count == 0 && !connectionDraft.HasDraft,
            "Gateway auto-start toggles persist per directory without starting workers or changing the connection draft");
        BehaviorModelInput.Text = "gateway-keeps-draft"; await Task.Delay(40);
        Check(connectionDraft.HasDraft, "Gateway fixture stages a connection draft for preservation checks");
        await StartGatewaysOnLaunchAsync();
        Check(runtime.Running.Values.All(value => value == true) && connectionDraft.Model == "gateway-keeps-draft" &&
            original.SameContents(store.Load(home)) && runtime.Trace.SequenceEqual(new[] { "start:Model", "start:Mobile" }),
            "Dock launch starts only opted-in saved gateways once while preserving the unsaved connection fields");
        var result = await OperateGatewayAsync(GatewayKind.Model, GatewayCommand.Stop);
        var count = runtime.Trace.Count;
        await RefreshGatewaysAsync(); await StartGatewaysOnLaunchAsync();
        Check(result?.Succeeded == true && runtime.Trace.Count == count && runtime.Running[GatewayKind.Model] == false && runtime.Running[GatewayKind.Mobile] == true,
            "A manual model stop survives polling and repeated launch callbacks while the phone gateway continues");
        ModelGatewayAutomaticInput.IsOn = false; MobileGatewayAutomaticInput.IsOn = false;
        Check(runtime.Running[GatewayKind.Mobile] == true && gatewayManagement.LoadPreferences(home) == new GatewayStartupSettings(),
            "Disabling gateway auto-start leaves an already running phone worker untouched");
        await OperateGatewayAsync(GatewayKind.Mobile, GatewayCommand.Restart);
        Check(runtime.Trace.TakeLast(2).SequenceEqual(new[] { "stop:Mobile", "start:Mobile" }) && connectionDraft.Model == "gateway-keeps-draft",
            "Gateway restart stops before starting and preserves the connection draft");
        runtime.Running[GatewayKind.Model] = null; await RefreshGatewaysAsync();
        Check(ModelGatewayStateText.Text == "暂未确认" && !ModelGatewayStopButton.IsEnabled,
            "An unconfirmed model listener remains unknown instead of being shown as stopped");
        runtime.Running[GatewayKind.Model] = false;
        runtime.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var starting = OperateGatewayAsync(GatewayKind.Model, GatewayCommand.Start);
        Check(ManualApplicationBusy && !ApplyButton.IsEnabled && !ChooseHomeButton.IsEnabled && !BaseUrlInput.IsEnabled && !MobileGatewayStartButton.IsEnabled,
            "An in-progress gateway action locks save application directory and competing gateway actions");
        var duplicate = await OperateGatewayAsync(GatewayKind.Model, GatewayCommand.Start);
        Check(duplicate is null && !await SwitchHomeAsync(Path.Combine(output, "blocked-gateway-home")), "Duplicate gateway actions and directory switches cannot race the held operation lease");
        runtime.Gate.TrySetResult(); await starting; runtime.Gate = null;
        runtime.FailStart = GatewayKind.Model;
        result = await OperateGatewayAsync(GatewayKind.Model, GatewayCommand.Restart);
        Check(result?.Outcome == GatewayOperationOutcome.Failed && ModelGatewayOperation.IsOpen && ModelGatewayOperation.Message.Contains("失败") &&
            runtime.Running[GatewayKind.Mobile] == true && connectionDraft.Model == "gateway-keeps-draft",
            "A model gateway failure remains visible without stopping the phone worker or losing draft fields");
        runtime.FailStart = null;
        DiscardConnectionClicked(DiscardConnectionButton, new());
        var preferences = gatewayManagement.Preferences(home); var valid = File.ReadAllBytes(preferences.SettingsPath);
        File.WriteAllText(preferences.SettingsPath, "{\"ModelOnDockStart\":\"invalid\"}"); LoadGatewayPreferences();
        Check(GatewayPreferencesError.IsOpen && !ModelGatewayAutomaticInput.IsEnabled && !MobileGatewayAutomaticInput.IsEnabled &&
            File.ReadAllText(preferences.SettingsPath).Contains("invalid"), "Corrupt gateway auto-start storage disables writes and preserves the original damaged file");
        File.WriteAllBytes(preferences.SettingsPath, valid); LoadGatewayPreferences();
        Check(!GatewayPreferencesError.IsOpen && ModelGatewayAutomaticInput.IsEnabled, "Repair and refresh restore gateway auto-start controls without starting a worker");
        runtime.FailStop = GatewayKind.Model; smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
        await StopGatewaysAndExitAsync();
        Check(!closing && ModelGatewayOperation.IsOpen && runtime.Running[GatewayKind.Mobile] == false && AppWindow.IsVisible,
            "Stop-and-exit attempts both gateways and keeps the window accessible when one shutdown cannot be confirmed");
        await CaptureAsync(output, "gateways-stop-error.png");
        runtime.FailStop = null; smokeDialog = _ => Task.FromResult(ContentDialogResult.None);
        count = runtime.Trace.Count; await StopGatewaysAndExitAsync();
        Check(runtime.Trace.Count == count && !closing, "Cancelling stop-and-exit leaves both gateway states untouched");
        runtime.Authorized[GatewayKind.Model] = false; await RefreshGatewaysAsync();
        Check(!ModelGatewayStartButton.IsEnabled && ModelGatewayDescription.Text.Contains("HTTP"), "Unconfirmed HTTP routing is visible and cannot start a model gateway");
        runtime.Authorized[GatewayKind.Model] = true; gatewayResults.Clear(); await RefreshGatewaysAsync();
        PageScroll.ChangeView(null, 0, null, true); await CaptureAsync(output, "gateways-light.png");
        Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(900, 760)); await Task.Delay(120); Root.UpdateLayout();
        PageScroll.ChangeView(null, double.MaxValue, null, true); await CaptureAsync(output, "gateways-compact-dark.png");
        Check(ModelGatewayStartButton.ActualWidth > 0 && ModelGatewayStartButton.TransformToVisual(Root).TransformPoint(new()).X + ModelGatewayStartButton.ActualWidth <= Root.ActualWidth,
            "Gateway action buttons fit the compact dark page and remain reachable by scrolling");
        Root.RequestedTheme = ElementTheme.Light;
        Check(original.SameContents(store.Load(home)) && !connectionDraft.HasDraft, "All gateway controls preserve Codex configuration authentication and connection drafts unless explicitly discarded");
        smokeDialog = null; NavigateTo(WorkspaceNav);
        await RunNativeGatewayManagementSmokeAsync(output, Check);
    }

    private async Task RunNativeGatewayManagementSmokeAsync(string output, Action<bool, string> check)
    {
        var home = Path.Combine(output, "native-gateway-management"); Directory.CreateDirectory(home);
        var connection = MixedConnection.Create(new("https://gateway-never-called.invalid/v1", "fake-management-model-key", "fixture")
            { Kind = ConnectionKind.ResponsesApi, Mode = ConnectionMode.OfficialWithApi });
        File.WriteAllBytes(Path.Combine(home, MixedConnection.FileName), connection.Protect());
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='fixture'\nopenai_base_url='" + connection.NativeBaseUrl + "'\n");
        var mobileRoot = Path.Combine(output, "native-mobile-state"); var mobile = new MobileGatewayManager(home, mobileRoot);
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); var number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        var savedMobile = await mobile.SaveAsync("https://phone-fixture.invalid", "fixture", "fake-gateway-ui-password", number);
        var before = store.Load(home);
        var runtime = new NativeGatewayManagementRuntime(store, mobileRoot);
        var service = new GatewayManagementService(runtime, new(new WorkspaceSession(home)), Path.Combine(output, "native-gateway-preferences"));
        try
        {
            foreach (var kind in new[] { GatewayKind.Model, GatewayKind.Mobile })
            {
                var start = await service.ExecuteAsync(kind, GatewayCommand.Start, home, () => true);
                check(start.Succeeded, "Delivered WinUI starts its own isolated " + kind + " worker through the shared gateway service" + (start.Succeeded ? "" : ": " + start.Message));
                var restart = await service.ExecuteAsync(kind, GatewayCommand.Restart, home, () => true);
                check(restart.Succeeded, "Delivered WinUI restarts its isolated " + kind + " worker only after the old process exits");
            }
            var state = await service.RefreshAsync(home, () => true);
            check(state is { Model.Running: true, Mobile.Running: true } && (await BridgeProcess.StatusAsync(connection))?.Requests == 0,
                "Both delivered gateway workers answer authenticated local health without any remote model request");
            check(state?.Model.Diagnostics?.Compaction.Contains("确认完成 0 次") == true && state.Model.Diagnostics.Latency.Contains("未测得"),
                "Delivered WinUI reads actual running model worker counters with unknown latency until requests are observed");
            foreach (var kind in new[] { GatewayKind.Model, GatewayKind.Mobile })
                check((await service.ExecuteAsync(kind, GatewayCommand.Stop, home, () => true)).Succeeded,
                    "Delivered WinUI confirms its isolated " + kind + " worker and listening ports are released");
            check(before.SameContents(store.Load(home)) && mobile.Load() == savedMobile && await mobile.StatusAsync() is null && await BridgeProcess.StatusAsync(connection) is null,
                "Actual gateway control preserves saved routing and phone credentials and leaves neither test worker running");
        }
        finally { await mobile.StopAsync(); await BridgeProcess.StopAsync(before.Bridge!); }
    }

    private sealed class SmokeGatewayRuntime : IGatewayManagementRuntime
    {
        internal readonly Dictionary<GatewayKind, bool?> Running = new() { [GatewayKind.Model] = false, [GatewayKind.Mobile] = false };
        internal readonly Dictionary<GatewayKind, bool> Configured = new() { [GatewayKind.Model] = false, [GatewayKind.Mobile] = false };
        internal readonly Dictionary<GatewayKind, bool> Authorized = new() { [GatewayKind.Model] = true, [GatewayKind.Mobile] = true };
        internal readonly Dictionary<GatewayKind, string> Revisions = new() { [GatewayKind.Model] = "fixture-model", [GatewayKind.Mobile] = "fixture-mobile" };
        internal readonly List<string> Trace = [];
        internal TaskCompletionSource? Gate;
        internal GatewayKind? FailStart, FailStop;
        internal ModelGatewayDiagnostics? ModelDiagnostics;
        internal bool FailModelProbe;
        internal Action<GatewayKind, bool>? WorkerChanged;
        public Task<GatewayDefinition> LoadAsync(GatewayKind kind, string home, CancellationToken cancellation)
            => Task.FromResult(new GatewayDefinition(kind, home, Configured[kind], Configured[kind] && Authorized[kind],
                kind == GatewayKind.Model ? Authorized[kind] ? "本机地址：http://127.0.0.1:32190/v1 · 隔离配置" : "HTTP 站点未确认，请先保存确认。" : "HTTPS 入口：https://phone-fixture.invalid · 隔离配置", Revisions[kind]));
        public Task<GatewayProbe> ProbeAsync(GatewayDefinition definition, CancellationToken cancellation)
        {
            if (definition.Kind == GatewayKind.Model && FailModelProbe) throw new IOException("模拟网关状态读取失败。");
            return Task.FromResult(new GatewayProbe(Running[definition.Kind])
            { Diagnostics = definition.Kind == GatewayKind.Model && Running[definition.Kind] == true ? ModelDiagnostics : null });
        }
        public async Task StartAsync(GatewayDefinition definition, CancellationToken cancellation)
        {
            Trace.Add("start:" + definition.Kind); if (Gate is { } gate) await gate.Task.WaitAsync(cancellation);
            if (FailStart == definition.Kind) throw new IOException("模拟网关启动失败。");
            Running[definition.Kind] = true; WorkerChanged?.Invoke(definition.Kind, true);
        }
        public Task StopAsync(GatewayDefinition definition, CancellationToken cancellation)
        { Trace.Add("stop:" + definition.Kind); if (FailStop == definition.Kind) throw new IOException("模拟网关尚未停止。"); Running[definition.Kind] = false; WorkerChanged?.Invoke(definition.Kind, false); return Task.CompletedTask; }
    }
}
