using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    private void ExitWindowClicked(object sender, RoutedEventArgs args) => Host?.ExitWindowClicked(sender, args);

    private void HideWindowClicked(object sender, RoutedEventArgs args) => Host?.HideWindowClicked(sender, args);

    private void MinimizeWindowClicked(object sender, RoutedEventArgs args) => Host?.MinimizeWindowClicked(sender, args);

    private void PageHelpClicked(object sender, RoutedEventArgs args) => Host?.PageHelpClicked(sender, args);

    private void RefreshStartupClicked(object sender, RoutedEventArgs args) => Host?.RefreshStartupClicked(sender, args);

    private void StartupChanged(object sender, RoutedEventArgs args) => Host?.StartupChanged(sender, args);
}
