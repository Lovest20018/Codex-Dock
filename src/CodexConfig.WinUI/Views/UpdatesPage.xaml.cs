using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class UpdatesPage : UserControl
{
    public UpdatesPage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    internal void Render(Version version, string message, bool canCheck, bool hasUpdate, bool hasInstaller,
        bool canInstall, bool canCancel, bool downloading, int percent)
    {
        UpdateVersionText.Text = "当前程序版本 " + version.ToString(3) + " · WinUI 3";
        UpdateStatusText.Text = message;
        CheckUpdateButton.IsEnabled = canCheck;
        DownloadUpdateButton.Visibility = hasUpdate && !hasInstaller ? Visibility.Visible : Visibility.Collapsed;
        DownloadUpdateButton.IsEnabled = canCheck;
        InstallUpdateButton.Visibility = hasInstaller ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.IsEnabled = canInstall;
        CancelUpdateButton.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        CancelUpdateButton.IsEnabled = canCancel;
        UpdateProgress.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.Value = percent;
    }

    private void CancelUpdateClicked(object sender, RoutedEventArgs args) => Host?.CancelUpdateClicked(sender, args);

    private void CheckUpdateClicked(object sender, RoutedEventArgs args) => Host?.CheckUpdateClicked(sender, args);

    private void DownloadUpdateClicked(object sender, RoutedEventArgs args) => Host?.DownloadUpdateClicked(sender, args);

    private void InstallUpdateClicked(object sender, RoutedEventArgs args) => Host?.InstallUpdateClicked(sender, args);

    private void PageHelpClicked(object sender, RoutedEventArgs args) => Host?.PageHelpClicked(sender, args);

    private void ReleasePageClicked(object sender, RoutedEventArgs args) => Host?.ReleasePageClicked(sender, args);
}
