using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class CatalogPage : UserControl
{
    public CatalogPage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    private void AddSettingClicked(object sender, RoutedEventArgs args) => Host?.AddSettingClicked(sender, args);

    private void CatalogFilterChanged(object sender, RoutedEventArgs args) => Host?.CatalogFilterChanged(sender, args);

    private void CatalogSearchChanged(TextBox sender, TextBoxTextChangingEventArgs args) => Host?.CatalogSearchChanged(sender, args);

    private void CatalogSelectionChanged(object sender, SelectionChangedEventArgs args) => Host?.CatalogSelectionChanged(sender, args);

    private void NextCatalogPageClicked(object sender, RoutedEventArgs args) => Host?.NextCatalogPageClicked(sender, args);

    private void OpenSourcesClicked(object sender, RoutedEventArgs args) => Host?.OpenSourcesClicked(sender, args);

    private void PreviousCatalogPageClicked(object sender, RoutedEventArgs args) => Host?.PreviousCatalogPageClicked(sender, args);
}
