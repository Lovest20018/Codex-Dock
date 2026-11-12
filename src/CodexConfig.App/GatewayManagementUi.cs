using System.IO;
using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private bool updatingGatewayStartup, gatewayLaunchAttempted, gatewayLaunchInProgress;
    private bool gatewayStartupReadError;
    private string? gatewayPreferencesHome;
    private Task? gatewayRefreshTask;

    private GatewayStartupStore GatewayPreferences() => new(home, smokeOutput is null ? null : Path.Combine(smokeOutput, "gateway-preferences"));

    private void ReloadGatewayStartupSettings()
    {
        if (gatewayPreferencesHome != home)
        {
            gatewayPreferencesHome = home;
            GatewayStartupFeedback.Visibility = ModelGatewayOperationText.Visibility = MobileGatewayOperationText.Visibility = Visibility.Collapsed;
            StartGatewayButton.IsEnabled = RestartGatewayButton.IsEnabled = StopGatewayButton.IsEnabled = false;
            StartMobileGatewayButton.IsEnabled = RestartMobileGatewayButton.IsEnabled = StopMobileGatewayButton.IsEnabled = false;
        }
        updatingGatewayStartup = true;
        try
        {
            var settings = GatewayPreferences().Load();
            if (gatewayStartupReadError) { GatewayStartupFeedback.Visibility = Visibility.Collapsed; gatewayStartupReadError = false; }
            ModelGatewayAutoStartBox.IsChecked = settings.ModelOnDockStart;
            MobileGatewayAutoStartBox.IsChecked = settings.MobileOnDockStart;
            ModelGatewayAutoStartBox.IsEnabled = MobileGatewayAutoStartBox.IsEnabled = !closing;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ModelGatewayAutoStartBox.IsChecked = MobileGatewayAutoStartBox.IsChecked = false;
            ModelGatewayAutoStartBox.IsEnabled = MobileGatewayAutoStartBox.IsEnabled = false;
            gatewayStartupReadError = true;
            SetGatewayFeedback(GatewayStartupFeedback, error.Message, true);
        }
        finally { updatingGatewayStartup = false; }
    }

    private void GatewayAutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || updatingGatewayStartup || closing) return;
        try
        {
            var preferences = GatewayPreferences(); var settings = preferences.Load();
            settings = ReferenceEquals(sender, ModelGatewayAutoStartBox)
                ? settings with { ModelOnDockStart = ModelGatewayAutoStartBox.IsChecked == true }
                : settings with { MobileOnDockStart = MobileGatewayAutoStartBox.IsChecked == true };
            preferences.Save(settings);
            ReloadGatewayStartupSettings();
            SetGatewayFeedback(GatewayStartupFeedback, "已保存，下一次打开 Dock 时按此设置启动。需要立即运行或停止时，请使用下方按钮。");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ReloadGatewayStartupSettings(); SetGatewayFeedback(GatewayStartupFeedback, "自动启动设置未保存：" + error.Message, true);
        }
    }

    private static void SetGatewayFeedback(TextBlock text, string message, bool error = false)
    {
        text.Text = message; text.Visibility = Visibility.Visible;
        text.SetResourceReference(TextBlock.ForegroundProperty, error ? "Error" : "Muted");
    }

    private void PaintGatewayManagement(TrayGatewayState state)
    {
        var modelReady = false; var modelAuthorized = false; var mobileReady = false;
        try
        {
            var saved = store.Load(home); var settings = store.ReadSettings(saved);
            modelReady = settings.Mode == ConnectionMode.OfficialWithApi && saved.Bridge is not null;
            modelAuthorized = modelReady && HttpConnectionPolicy.IsAuthorized(settings.BaseUrl, settings.Details.HttpOrigin);
            ModelGatewaySetupHint.Text = modelReady
                ? "本机地址：" + MixedConnection.Read(saved.Bridge!).NativeBaseUrl + (modelAuthorized ? " · 已保存模型连接" : " · 请先确认 HTTP 站点并保存")
                : "当前连接无需启动本机模型网关。若使用官方账号加独立模型入口，再到连接页配置。";
        }
        catch (Exception) { ModelGatewaySetupHint.Text = "无法读取已保存的模型连接。请到“连接与账号”检查配置。"; }
        try
        {
            var mobile = MobileManager().Load(); mobileReady = mobile is not null;
            MobileGatewaySetupHint.Text = mobile is null ? "先到“手机连接”设置 HTTPS 域名、账号和密码；保存后可在这里启停。"
                : $"HTTP 端口：{mobile.Port} · HTTPS 入口：{mobile.PublicUrl} · 入口检测在“手机连接”完成";
        }
        catch (Exception) { MobileGatewaySetupHint.Text = "无法读取已保存的手机设置。请到“手机连接”检查配置。"; }

        void Status(TextBlock text, bool? running, bool operating, bool configured)
        {
            text.Text = operating ? "正在处理…" : running is true ? "运行中" : running is null ? "暂时无法确认，请刷新状态" : configured ? "已停止" : "尚未配置";
            text.SetResourceReference(TextBlock.ForegroundProperty, running is true && !operating ? "Success" : running is null && !operating ? "Warning" : "Muted");
        }
        Status(ModelGatewayStateText, state.ModelRunning, gatewayPolling, modelReady);
        if (!modelReady && state.ModelRunning is not true && !gatewayPolling) ModelGatewayStateText.Text = "当前连接无需启动";
        Status(MobileGatewayStateText, state.MobileRunning, mobileBusy, mobileReady);
        StartGatewayButton.IsEnabled = modelAuthorized && !gatewayPolling && !closing && state.ModelRunning is not true;
        RestartGatewayButton.IsEnabled = modelAuthorized && !gatewayPolling && !closing;
        StopGatewayButton.IsEnabled = state.ModelRunning is true && !gatewayPolling && !closing;
        StartMobileGatewayButton.IsEnabled = mobileReady && !mobileBusy && !closing && state.MobileRunning is not true;
        RestartMobileGatewayButton.IsEnabled = mobileReady && !mobileBusy && !closing;
        StopMobileGatewayButton.IsEnabled = state.MobileRunning is true && !mobileBusy && !closing;
    }

    private void ManageGatewaysClicked(object sender, RoutedEventArgs e) { ShowPage("gateways"); GatewayPage.ScrollToTop(); }
    private void SavedGatewaysClicked(object sender, RoutedEventArgs e) { CloseOverlay(); ShowPage("gateways"); GatewayPanel.BringIntoView(); }
    private void GatewayConnectionClicked(object sender, RoutedEventArgs e) => ShowPage("connection");
    private void GatewayMobileSettingsClicked(object sender, RoutedEventArgs e) => ShowPage("mobile");

    private Task RefreshAllGatewaysAsync() => gatewayRefreshTask is { IsCompleted: false } running ? running
        : gatewayRefreshTask = RefreshAllGatewaysCoreAsync();

    private async Task RefreshAllGatewaysCoreAsync()
    {
        var expectedHome = home;
        await RefreshGatewayAsync(); await RefreshGatewaySummaryAsync();
        if (!closing && home != expectedHome) await RefreshAllGatewaysCoreAsync();
    }

    private async Task ModelGatewayActionAsync(Func<Task> action)
    {
        var expectedHome = home; ModelGatewayOperationText.Visibility = Visibility.Collapsed;
        try { await action(); }
        catch (OperationCanceledException) { if (!closing && home == expectedHome) SetGatewayFeedback(ModelGatewayOperationText, "操作已取消，请刷新状态。"); }
        catch (Exception error) { if (!closing && home == expectedHome) SetGatewayFeedback(ModelGatewayOperationText, error.Message, true); }
        finally { if (!closing && home == expectedHome) await RefreshGatewaySummaryAsync(); }
    }

    private async Task MobileGatewayActionAsync(Func<Task> action)
    {
        var expectedHome = home; MobileGatewayOperationText.Visibility = Visibility.Collapsed;
        try { await RunMobileActionAsync(action); }
        catch (OperationCanceledException) { if (!closing && home == expectedHome) SetGatewayFeedback(MobileGatewayOperationText, "操作已取消，请刷新状态。"); }
        catch (Exception error) { if (!closing && home == expectedHome) SetGatewayFeedback(MobileGatewayOperationText, error.Message, true); }
    }

    private async void StartMobileGatewayClicked(object sender, RoutedEventArgs e) => await MobileGatewayActionAsync(StartMobileGatewayAsync);
    private async void RestartMobileGatewayClicked(object sender, RoutedEventArgs e) => await MobileGatewayActionAsync(RestartMobileGatewayAsync);
    private async void StopMobileGatewayClicked(object sender, RoutedEventArgs e) => await MobileGatewayActionAsync(StopMobileGatewayAsync);

    private async Task StartMobileGatewayAsync()
    {
        var expectedHome = home;
        await MobileManager().StartAsync(lifetime.Token);
        if (!closing && home == expectedHome) await RefreshMobilePageAsync();
    }

    private async Task StartGatewaysOnDockLaunchAsync()
    {
        if (gatewayLaunchAttempted || closing || smokeOutput is not null && !allowSmokeGateway) return;
        gatewayLaunchAttempted = true; gatewayLaunchInProgress = true;
        var expectedHome = home;
        try
        {
            var preferences = GatewayPreferences().Load();
            if (preferences.ModelOnDockStart) await ModelGatewayActionAsync(StartModelGatewayAsync);
            if (!closing && home == expectedHome && preferences.MobileOnDockStart) await MobileGatewayActionAsync(StartMobileGatewayAsync);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { if (!closing && home == expectedHome) SetGatewayFeedback(GatewayStartupFeedback, error.Message, true); }
        finally { gatewayLaunchInProgress = false; }
    }
}
