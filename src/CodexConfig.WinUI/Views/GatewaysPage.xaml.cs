using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class GatewaysPage : UserControl
{
    public GatewaysPage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    private void CopyModelTransferClicked(object sender, RoutedEventArgs args) => Host?.CopyModelTransferClicked(sender, args);

    private void ExitWindowClicked(object sender, RoutedEventArgs args) => Host?.ExitWindowClicked(sender, args);

    private void GatewayActionClicked(object sender, RoutedEventArgs args) => Host?.GatewayActionClicked(sender, args);

    private void GatewayAutomaticChanged(object sender, RoutedEventArgs args) => Host?.GatewayAutomaticChanged(sender, args);

    private void RefreshGatewaysClicked(object sender, RoutedEventArgs args) => Host?.RefreshGatewaysClicked(sender, args);
}
