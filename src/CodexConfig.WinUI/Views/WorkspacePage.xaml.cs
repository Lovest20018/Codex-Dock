using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class WorkspacePage : UserControl
{
    public WorkspacePage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    private void CancelQuickModelReadClicked(object sender, RoutedEventArgs args) => Host?.CancelQuickModelReadClicked(sender, args);

    private void OpenConnectionClicked(object sender, RoutedEventArgs args) => Host?.OpenConnectionClicked(sender, args);

    private void OpenEnhancementsClicked(object sender, RoutedEventArgs args) => Host?.OpenEnhancementsClicked(sender, args);

    private void OpenMobileClicked(object sender, RoutedEventArgs args) => Host?.OpenMobileClicked(sender, args);

    private void QuickModelClicked(object sender, RoutedEventArgs args) => Host?.QuickModelClicked(sender, args);

    private void ReloadClicked(object sender, RoutedEventArgs args) => Host?.ReloadClicked(sender, args);

    private void WorkspaceNextClicked(object sender, RoutedEventArgs args) => Host?.WorkspaceNextClicked(sender, args);
}
