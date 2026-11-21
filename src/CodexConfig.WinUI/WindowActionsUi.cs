using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private void RefreshWindowActionControls()
    {
        SettingsPanel.MinimizeWindowButton.IsEnabled = !closing;
        SettingsPanel.HideWindowButton.IsEnabled = tray?.IsAvailable == true && !closing;
        SettingsPanel.ExitWindowButton.IsEnabled = CanRequestExit;
    }
    internal void MinimizeWindowClicked(object sender, RoutedEventArgs args)
    { if (!closing && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Minimize(); }
    internal void HideWindowClicked(object sender, RoutedEventArgs args)
    {
        if (closing) return;
        if (!TryHideToTray()) { backgroundStatus = "托盘暂不可用，请保持窗口运行；可最小化到任务栏。"; RefreshView(); }
    }
    internal async void ExitWindowClicked(object sender, RoutedEventArgs args) => await RequestWindowExitAsync();
    private bool CanRequestExit => CanUseWorkspaceShortcut && !enhancedLaunchBusy;

    private async Task RequestWindowExitAsync()
    {
        if (!CanRequestExit) return;
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = "两种方式都会停止 Dock 提供的客户端增强。Codex 会继续运行。",
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = "仅退出 Dock：已启动的模型网关和手机网关继续运行。",
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = "停止网关并退出：停止当前配置目录的两个网关，手机连接和经模型网关的请求会中断。",
            TextWrapping = TextWrapping.Wrap
        });
        var dialog = new ContentDialog
        {
            Title = "退出 Codex Dock", Content = body, XamlRoot = Root.XamlRoot,
            PrimaryButtonText = "仅退出 Dock", SecondaryButtonText = "停止网关并退出",
            CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        };
        var choice = await ShowDialogAsync(dialog);
        if (choice is ContentDialogResult.Primary or ContentDialogResult.Secondary)
            await ExitWindowAsync(confirmed: true, stopGateways: choice == ContentDialogResult.Secondary);
    }
}
