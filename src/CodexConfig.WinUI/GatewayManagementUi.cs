using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private bool gatewayAction, gatewayLaunch, gatewayExit, updatingGatewayPreferences;
    private int gatewayTicks;
    private string? gatewayHome, gatewayPreferencesError;
    private GatewayStartupSettings gatewayPreferences = new();
    private readonly Dictionary<GatewayKind, GatewayOperationResult> gatewayResults = [];
    private GatewayKind? activeGateway;
    private string gatewayPreferencesStatus = "自动启动默认关闭，按配置目录保存。";

    private void LoadGatewayPreferences()
    {
        if (gatewayHome != viewModel.Home)
        { gatewayHome = viewModel.Home; gatewayResults.Clear(); gatewayManagement.ClearOverview(); gatewayPreferencesStatus = "自动启动默认关闭，按配置目录保存。"; }
        try { gatewayPreferences = gatewayManagement.LoadPreferences(viewModel.Home); gatewayPreferencesError = null; }
        catch (Exception error) { gatewayPreferences = new(); gatewayPreferencesError = error.Message; }
        RefreshView();
    }

    private void RefreshGatewayControls()
    {
        if (GatewaysPanel is null) return;
        updatingGatewayPreferences = true;
        try
        {
            var editable = !ManualApplicationBusy && !dialogOpen && !closing;
            ModelGatewayAutomaticInput.IsOn = gatewayPreferences.ModelOnDockStart;
            MobileGatewayAutomaticInput.IsOn = gatewayPreferences.MobileOnDockStart;
            ModelGatewayAutomaticInput.IsEnabled = MobileGatewayAutomaticInput.IsEnabled = editable && gatewayPreferencesError is null;
            GatewayPreferencesError.IsOpen = gatewayPreferencesError is not null; GatewayPreferencesError.Message = gatewayPreferencesError ?? "";
            GatewayPreferencesStatus.Text = MobileGatewayPreferencesStatus.Text = gatewayPreferencesStatus;
            MobileGatewayPreferencesError.IsOpen = gatewayPreferencesError is not null; MobileGatewayPreferencesError.Message = gatewayPreferencesError ?? "";
            void Paint(GatewayState? state, TextBlock status, TextBlock description, TextBlock detail, Button start, Button restart, Button stop, InfoBar result, GatewayKind kind)
            {
                status.Text = gatewayAction && activeGateway == kind ? "正在处理…" : state?.Status ?? "尚未检查";
                description.Text = state?.Description ?? "刷新后读取此目录已保存的网关设置。";
                detail.Text = state?.Error ?? state?.Detail ?? "";
                start.IsEnabled = editable && state is { CanStart: true } && state.Running != true;
                restart.IsEnabled = editable && state is { CanStart: true };
                stop.IsEnabled = editable && state is { Configured: true, Running: true };
                result.IsOpen = gatewayResults.TryGetValue(kind, out var outcome);
                if (outcome is not null) { result.Message = outcome.Message; result.Severity = outcome.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error; }
            }
            var overview = gatewayManagement.Overview?.Home == viewModel.Home ? gatewayManagement.Overview : null;
            Paint(overview?.Model, ModelGatewayStateText, ModelGatewayDescription, ModelGatewayDetail, ModelGatewayStartButton, ModelGatewayRestartButton, ModelGatewayStopButton, ModelGatewayOperation, GatewayKind.Model);
            Paint(overview?.Mobile, MobileGatewayStateText, MobileGatewayDescription, MobileGatewayDetail, MobileGatewayStartButton, MobileGatewayRestartButton, MobileGatewayStopButton, MobileGatewayOperation, GatewayKind.Mobile);
            var diagnostics = overview?.Model.Diagnostics;
            ModelGatewayDiagnosticsStatus.Text = diagnostics is not null ? "当前运行网关的进程内统计" : overview?.Model.Error is not null ? "读取失败，当前统计未确认。" :
                overview?.Model.Running == false ? "当前模型网关未运行，没有运行中的进程统计。" : "尚未确认当前网关统计，请刷新状态。";
            ModelGatewayCompactionConfiguration.Text = diagnostics?.Configuration ?? "";
            ModelGatewayCompactionMetrics.Text = diagnostics?.Compaction ?? "";
            ModelGatewayLatencyMetrics.Text = diagnostics?.Latency ?? "";
            ModelGatewayUpstreamMetrics.Text = diagnostics?.Upstream ?? "";
            ModelGatewayMetricsScope.Text = ModelGatewayDiagnostics.Scope;
            RefreshGatewaysButton.IsEnabled = StopGatewaysExitButton.IsEnabled = editable;
        }
        finally { updatingGatewayPreferences = false; }
    }

    private void GatewayAutomaticChanged(object sender, RoutedEventArgs args)
    {
        if (updatingGatewayPreferences || !initialized || ManualApplicationBusy || dialogOpen || closing) return;
        try
        {
            var model = ReferenceEquals(sender, ModelGatewayAutomaticInput);
            gatewayManagement.SetAutomaticStart(viewModel.Home, model ? GatewayKind.Model : GatewayKind.Mobile,
                model ? ModelGatewayAutomaticInput.IsOn : MobileGatewayAutomaticInput.IsOn);
            gatewayPreferencesStatus = "已保存，下次打开 Dock 时生效。需要立即启停时，请使用对应按钮。";
        }
        catch (Exception error) { gatewayPreferencesStatus = "自动启动设置未保存：" + error.Message; }
        LoadGatewayPreferences();
    }

    private async Task RefreshGatewaysAsync()
    {
        if (closing || gatewayAction) return;
        var home = viewModel.Home;
        try { await gatewayManagement.RefreshAsync(home, () => !closing && viewModel.Home == home, lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!closing && viewModel.Home == home) gatewayPreferencesStatus = error.Message; }
        if (!closing && viewModel.Home == home) RefreshView();
    }
    private async void RefreshGatewaysClicked(object sender, RoutedEventArgs args) { LoadGatewayPreferences(); await RefreshGatewaysAsync(); }
    private void OpenGatewaysClicked(object sender, RoutedEventArgs args) => NavigateTo(GatewaysNav);
    private async void GatewayActionClicked(object sender, RoutedEventArgs args)
    {
        var parts = ((Button)sender).Tag?.ToString()?.Split(':');
        if (parts is [var kind, var command] && Enum.TryParse<GatewayKind>(kind, out var selected) && Enum.TryParse<GatewayCommand>(command, out var operation))
            await OperateGatewayAsync(selected, operation);
    }

    private async Task<GatewayOperationResult?> OperateGatewayAsync(GatewayKind kind, GatewayCommand command, bool exitBatch = false)
    {
        if ((ManualApplicationBusy && !(exitBatch && gatewayExit && !gatewayAction && !desktopApplication.IsBusy)) || dialogOpen || closing) return null;
        var home = viewModel.Home; gatewayAction = true; activeGateway = kind; gatewayResults.Remove(kind); RefreshView();
        if (kind == GatewayKind.Mobile && command is GatewayCommand.Stop or GatewayCommand.Restart) ClearMobilePair();
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            var result = await gatewayManagement.ExecuteAsync(kind, command, home, () => !closing && home == viewModel.Home, lifetime.Token);
            if (!closing && home == viewModel.Home) gatewayResults[kind] = result;
            return result;
        }
        finally
        {
            try
            {
                if (kind == GatewayKind.Mobile && !closing && home == viewModel.Home) await RefreshMobileAfterGatewayAsync(home);
            }
            finally
            {
                gatewayAction = false; activeGateway = null;
                if (!closing && home == viewModel.Home) { await RefreshGatewaysAsync(); RefreshView(); }
            }
        }
    }

    private async Task StartGatewaysOnLaunchAsync()
    {
        if (gatewayLaunch || closing) return;
        var home = viewModel.Home; gatewayLaunch = true; RefreshView();
        try
        {
            var result = await gatewayManagement.StartOnLaunchAsync(home, () => !closing && home == viewModel.Home, lifetime.Token);
            if (!closing && home == viewModel.Home)
            {
                foreach (var outcome in result.Results) gatewayResults[outcome.Kind] = outcome;
                if (result.Error is { } error) gatewayPreferencesError = error;
            }
        }
        finally { gatewayLaunch = false; if (!closing) RefreshView(); }
    }

    private async void StopGatewaysExitClicked(object sender, RoutedEventArgs args) => await StopGatewaysAndExitAsync();
    private async Task StopGatewaysAndExitAsync()
    {
        if (ManualApplicationBusy || dialogOpen || closing || !await ResolveProfileDraftAsync() || !await ResolveTemplateEditAsync() || !await ResolveConnectionDraftAsync() || !await ResolveMobileDraftAsync()) return;
        if (!await ConfirmDialogAsync("停止网关并退出", "将停止当前目录已保存的模型网关和手机网关，手机连接及经模型网关的请求会中断。确认两个网关都停止后，再退出程序。", "停止并退出")) return;
        gatewayExit = true; RefreshView();
        GatewayOperationResult? model, mobile;
        try
        {
            model = await OperateGatewayAsync(GatewayKind.Model, GatewayCommand.Stop, exitBatch: true);
            mobile = await OperateGatewayAsync(GatewayKind.Mobile, GatewayCommand.Stop, exitBatch: true);
        }
        finally { gatewayExit = false; RefreshView(); }
        if (model?.Succeeded != true || mobile?.Succeeded != true)
        { NavigateTo(model?.Succeeded != true ? GatewaysNav : MobileSettingsNav); RestoreWindow(); return; }
        exitRequested = true; await ExitWindowAsync();
    }
}
