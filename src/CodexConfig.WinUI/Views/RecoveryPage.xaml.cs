using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class RecoveryPage : UserControl
{
    public RecoveryPage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    private void PageHelpClicked(object sender, RoutedEventArgs args) => Host?.PageHelpClicked(sender, args);

    private void RefreshRecoveryClicked(object sender, RoutedEventArgs args) => Host?.RefreshRecoveryClicked(sender, args);

    private void RestoreConfigurationClicked(object sender, RoutedEventArgs args) => Host?.RestoreConfigurationClicked(sender, args);
}
