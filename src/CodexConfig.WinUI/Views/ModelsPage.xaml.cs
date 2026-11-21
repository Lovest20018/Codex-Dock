using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class ModelsPage : UserControl
{
    public ModelsPage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    private void BehaviorModelChanged(TextBox sender, TextBoxTextChangingEventArgs args) => Host?.BehaviorModelChanged(sender, args);

    private void CancelDiscoveryClicked(object sender, RoutedEventArgs args) => Host?.CancelDiscoveryClicked(sender, args);

    private void CheckServiceKeyClicked(object sender, RoutedEventArgs args) => Host?.CheckServiceKeyClicked(sender, args);

    private void DedicatedReviewerChanged(object sender, RoutedEventArgs args) => Host?.DedicatedReviewerChanged(sender, args);

    private void DiscoverModelsClicked(object sender, RoutedEventArgs args) => Host?.DiscoverModelsClicked(sender, args);

    private void DiscoveredModelChanged(object sender, SelectionChangedEventArgs args) => Host?.DiscoveredModelChanged(sender, args);

    private void ImportCatalogClicked(object sender, RoutedEventArgs args) => Host?.ImportCatalogClicked(sender, args);

    private void ModelFilterChanged(TextBox sender, TextBoxTextChangingEventArgs args) => Host?.ModelFilterChanged(sender, args);

    private void OpenConnectionClicked(object sender, RoutedEventArgs args) => Host?.OpenConnectionClicked(sender, args);

    private void OpenEnhancementsClicked(object sender, RoutedEventArgs args) => Host?.OpenEnhancementsClicked(sender, args);

    private void ReadClientCatalogClicked(object sender, RoutedEventArgs args) => Host?.ReadClientCatalogClicked(sender, args);

    private void SynchronizeCatalogClicked(object sender, RoutedEventArgs args) => Host?.SynchronizeCatalogClicked(sender, args);
}
