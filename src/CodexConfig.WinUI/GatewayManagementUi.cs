using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private bool updatingGatewayPreferences;
    private bool gatewayLaunch => busy.IsActive(WindowOperation.StartGateways);
    private bool gatewayAction => busy.IsActive(WindowOperation.OperateGateway);
    private int gatewayTicks;
    private string? gatewayHome, gatewayPreferencesError;
    private GatewayStartupSettings gatewayPreferences = new();
    private readonly Dictionary<GatewayKind, GatewayOperationResult> gatewayResults = [];
    private GatewayKind? activeGateway;
    private string gatewayPreferencesStatus = "自动启动默认关闭，按配置目录保存。";

    private void LoadGatewayPreferences()
    {
        if (gatewayHome != viewModel.Home)
        { gatewayHome = viewModel.Home; gatewayResults.Clear(); gatewayManagement.ClearOverview(); GatewaysPanel.ModelGatewayCopyStatus.Visibility = Visibility.Collapsed; gatewayPreferencesStatus = "自动启动默认关闭，按配置目录保存。"; }
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
            void PaintPreferences(ToggleSwitch toggle, bool automatic, InfoBar error, TextBlock status)
            {
                toggle.IsOn = automatic;
                toggle.IsEnabled = editable && gatewayPreferencesError is null;
                error.IsOpen = gatewayPreferencesError is not null;
                error.Message = gatewayPreferencesError ?? "";
                status.Text = gatewayPreferencesStatus;
            }
            var overview = gatewayManagement.Overview?.Home == viewModel.Home ? gatewayManagement.Overview : null;
            if (currentPage == MobileSettingsNav)
            {
                PaintPreferences(MobilePanel.MobileGatewayAutomaticInput, gatewayPreferences.MobileOnDockStart, MobilePanel.MobileGatewayPreferencesError, MobilePanel.MobileGatewayPreferencesStatus);
                Paint(overview?.Mobile, MobilePanel.MobileGatewayStateText, MobilePanel.MobileGatewayDescription, MobilePanel.MobileGatewayDetail, MobilePanel.MobileGatewayStartButton, MobilePanel.MobileGatewayRestartButton, MobilePanel.MobileGatewayStopButton, MobilePanel.MobileGatewayOperation, GatewayKind.Mobile);
                return;
            }

            PaintPreferences(GatewaysPanel.ModelGatewayAutomaticInput, gatewayPreferences.ModelOnDockStart, GatewaysPanel.GatewayPreferencesError, GatewaysPanel.GatewayPreferencesStatus);
            Paint(overview?.Model, GatewaysPanel.ModelGatewayStateText, GatewaysPanel.ModelGatewayDescription, GatewaysPanel.ModelGatewayDetail, GatewaysPanel.ModelGatewayStartButton, GatewaysPanel.ModelGatewayRestartButton, GatewaysPanel.ModelGatewayStopButton, GatewaysPanel.ModelGatewayOperation, GatewayKind.Model);
            var diagnostics = overview?.Model.Diagnostics;
            GatewaysPanel.ModelGatewayDiagnosticsStatus.Text = diagnostics is not null ? "当前运行网关的进程内统计" : overview?.Model.Error is not null ? "读取失败，当前统计未确认。" :
                overview?.Model.Running == false ? "当前模型网关未运行，没有运行中的进程统计。" : "尚未确认当前网关统计，请刷新状态。";
            GatewaysPanel.ModelGatewayCompactionConfiguration.Text = diagnostics?.Configuration ?? "";
            GatewaysPanel.ModelGatewayCompactionMetrics.Text = diagnostics?.Compaction ?? "";
            GatewaysPanel.ModelGatewayLatencyMetrics.Text = diagnostics?.Latency ?? "";
            GatewaysPanel.ModelGatewayUpstreamMetrics.Text = diagnostics?.Upstream ?? "";
            GatewaysPanel.ModelGatewayTransferText.Text = diagnostics?.TransferSummary ?? (diagnostics is null ? "" : "尚无可关联的响应记录。");
            GatewaysPanel.CopyModelTransferButton.IsEnabled = editable && diagnostics?.TransferSummary is not null;
            if (!GatewaysPanel.CopyModelTransferButton.IsEnabled) GatewaysPanel.ModelGatewayCopyStatus.Visibility = Visibility.Collapsed;
            GatewaysPanel.ModelGatewayMetricsScope.Text = ModelGatewayDiagnostics.Scope;
            GatewaysPanel.RefreshGatewaysButton.IsEnabled = editable;
            GatewaysPanel.GatewayExitButton.IsEnabled = CanRequestExit;
        }
        finally { updatingGatewayPreferences = false; }
    }

    internal void GatewayAutomaticChanged(object sender, RoutedEventArgs args)
    {
        if (updatingGatewayPreferences || !initialized || ManualApplicationBusy || dialogOpen || closing) return;
        try
        {
            var model = ReferenceEquals(sender, GatewaysPanel.ModelGatewayAutomaticInput);
            gatewayManagement.SetAutomaticStart(viewModel.Home, model ? GatewayKind.Model : GatewayKind.Mobile,
                model ? GatewaysPanel.ModelGatewayAutomaticInput.IsOn : MobilePanel.MobileGatewayAutomaticInput.IsOn);
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
    internal async void RefreshGatewaysClicked(object sender, RoutedEventArgs args) { LoadGatewayPreferences(); await RefreshGatewaysAsync(); }
    internal void CopyModelTransferClicked(object sender, RoutedEventArgs args)
    {
        if (!GatewaysPanel.CopyModelTransferButton.IsEnabled || gatewayManagement.Overview is not { } overview || overview.Home != viewModel.Home ||
            overview.Model.Diagnostics?.TransferSummary is not { } summary) return;
        try { CopyWorkspaceText(summary); GatewaysPanel.ModelGatewayCopyStatus.Text = "已复制诊断。"; }
        catch (Exception) { GatewaysPanel.ModelGatewayCopyStatus.Text = "暂时无法复制，请选择上方文字。"; }
        GatewaysPanel.ModelGatewayCopyStatus.Visibility = Visibility.Visible;
    }
    private void OpenGatewaysClicked(object sender, RoutedEventArgs args) => NavigateTo(GatewaysNav);
    internal async void GatewayActionClicked(object sender, RoutedEventArgs args)
    {
        var parts = ((Button)sender).Tag?.ToString()?.Split(':');
        if (parts is [var kind, var command] && Enum.TryParse<GatewayKind>(kind, out var selected) && Enum.TryParse<GatewayCommand>(command, out var operation))
            await OperateGatewayAsync(selected, operation);
    }

    private async Task<GatewayOperationResult?> OperateGatewayAsync(GatewayKind kind, GatewayCommand command, bool exitBatch = false)
    {
        if ((ManualApplicationBusy && !(exitBatch && busy.IsOnlyActive(WindowOperation.StopGatewaysForExit))) || dialogOpen || closing) return null;
        var home = viewModel.Home; using var operation = busy.Begin(WindowOperation.OperateGateway, "正在执行网关操作…"); activeGateway = kind; gatewayResults.Remove(kind); RefreshView();
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
                operation.Dispose(); activeGateway = null;
                if (!closing && home == viewModel.Home) { await RefreshGatewaysAsync(); RefreshView(); }
            }
        }
    }

    private async Task StartGatewaysOnLaunchAsync()
    {
        if (gatewayLaunch || closing) return;
        var home = viewModel.Home; using var operation = busy.Begin(WindowOperation.StartGateways, "正在检查网关启动设置…"); RefreshView();
        try
        {
            var result = await gatewayManagement.StartOnLaunchAsync(home, () => !closing && home == viewModel.Home, lifetime.Token);
            if (!closing && home == viewModel.Home)
            {
                foreach (var outcome in result.Results) gatewayResults[outcome.Kind] = outcome;
                if (result.Error is { } error) gatewayPreferencesError = error;
            }
        }
        finally { operation.Dispose(); if (!closing) RefreshView(); }
    }

    private async Task<bool> StopGatewaysForExitAsync()
    {
        using var operation = busy.Begin(WindowOperation.StopGatewaysForExit, "正在停止网关…"); RefreshView();
        GatewayOperationResult? model, mobile;
        try
        {
            model = await OperateGatewayAsync(GatewayKind.Model, GatewayCommand.Stop, exitBatch: true);
            mobile = await OperateGatewayAsync(GatewayKind.Mobile, GatewayCommand.Stop, exitBatch: true);
        }
        finally { operation.Dispose(); RefreshView(); }
        if (model?.Succeeded != true || mobile?.Succeeded != true)
        { NavigateTo(model?.Succeeded != true ? GatewaysNav : MobileSettingsNav); RestoreWindow(); return false; }
        return true;
    }
}
