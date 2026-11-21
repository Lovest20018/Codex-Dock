using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class EnhancementsPage : UserControl
{
    public EnhancementsPage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    private void AutomaticChanged(object sender, RoutedEventArgs args) => Host?.AutomaticChanged(sender, args);

    private void ConnectionRecoveryChanged(object sender, RoutedEventArgs args) => Host?.ConnectionRecoveryChanged(sender, args);

    private void CreateEnhancedShortcutClicked(object sender, RoutedEventArgs args) => Host?.CreateEnhancedShortcutClicked(sender, args);

    private void SpeedChanged(object sender, RoutedEventArgs args) => Host?.SpeedChanged(sender, args);
}
