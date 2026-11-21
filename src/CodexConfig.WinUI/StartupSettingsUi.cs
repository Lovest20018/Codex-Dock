using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private readonly IStartupRegistration startupRegistration;
    private readonly StartupRegistrationService startupSettings;
    private StartupRegistrationState? startupState;
    private string startupMessage = "";
    private void ReadStartupSettings()
    {
        try
        {
            startupState = startupSettings.Read(viewModel.Home);
            startupMessage = startupState.Enabled ? "已登记当前程序和配置目录。若 Windows 已禁用此项，请在系统的“启动应用”中启用。" :
                startupState.HasOtherRegistration ? "启动项指向另一程序或目录。开启当前项前会先核对是否更换；关闭当前开关不会移除其他启动项。" : "已关闭开机启动。";
        }
        catch (Exception) { startupState = null; startupMessage = "无法读取当前用户的开机启动项，请检查权限后重新读取。"; }
    }
    private void RefreshStartupControls()
    {
        if (SettingsPanel.StartupToggle is null) return;
        SettingsPanel.StartupToggle.IsOn = startupState?.Enabled == true;
        SettingsPanel.StartupToggle.IsEnabled = startupState is not null && !ManualApplicationBusy && !dialogOpen && !closing;
        SettingsPanel.RefreshStartupButton.IsEnabled = !ManualApplicationBusy && !dialogOpen && !closing;
        SettingsPanel.StartupStatusText.Text = startupMessage; SettingsPanel.StartupHomeText.Text = viewModel.Home;
    }
    internal void RefreshStartupClicked(object sender, RoutedEventArgs args) { ReadStartupSettings(); RefreshView(); }
    internal async void StartupChanged(object sender, RoutedEventArgs args)
    {
        if (!initialized || updatingControls || ManualApplicationBusy || dialogOpen || closing || startupState is null) return;
        await SetStartupAsync(SettingsPanel.StartupToggle.IsOn);
    }
    private async Task SetStartupAsync(bool enabled)
    {
        if (startupState is null || ManualApplicationBusy || dialogOpen || closing) return;
        try
        {
            var review = startupSettings.Prepare(startupState, enabled);
            if (review.ReplacesOther && !await ConfirmDialogAsync("更换开机启动项", "当前启动项属于另一程序或配置目录。改为此程序与下列配置目录，并在登录 Windows 时收起到托盘？\n\n" + review.Home, "改用当前程序与目录")) return;
            startupState = startupSettings.Apply(review, () => viewModel.Home, () => !closing && !ManualApplicationBusy);
            ReadStartupSettings();
        }
        catch (Exception error) { ReadStartupSettings(); startupMessage = error is ArgumentException or IOException or OperationCanceledException or InvalidOperationException ? error.Message : "开机启动设置未能保存，请检查权限并重新读取。"; }
        finally { RefreshView(); }
    }
}
