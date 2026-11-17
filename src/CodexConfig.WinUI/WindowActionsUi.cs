using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private void RefreshWindowActionControls()
    {
        MinimizeWindowButton.IsEnabled = !closing;
        HideWindowButton.IsEnabled = tray?.IsAvailable == true && !closing;
        ExitWindowButton.IsEnabled = !ApplicationBusy && !dialogOpen && !closing && !closePrompt;
    }
    private void MinimizeWindowClicked(object sender, RoutedEventArgs args)
    { if (!closing && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Minimize(); }
    private void HideWindowClicked(object sender, RoutedEventArgs args)
    {
        if (closing) return;
        if (!TryHideToTray()) { backgroundStatus = "托盘暂不可用，请保持窗口运行；可最小化到任务栏。"; RefreshView(); }
    }
    private async void ExitWindowClicked(object sender, RoutedEventArgs args) => await RequestWindowExitAsync();
    private async Task RequestWindowExitAsync()
    {
        if (!CanUseWorkspaceShortcut) return;
        exitRequested = true; await ExitWindowAsync();
    }
}
