using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class SourcesPage : UserControl
{
    public SourcesPage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    private void CancelFeaturesClicked(object sender, RoutedEventArgs args) => Host?.CancelFeaturesClicked(sender, args);

    private void CancelSourceClicked(object sender, RoutedEventArgs args) => Host?.CancelSourceClicked(sender, args);

    private void ChooseSourceExecutableClicked(object sender, RoutedEventArgs args) => Host?.ChooseSourceExecutableClicked(sender, args);

    private void ChooseSourceProjectClicked(object sender, RoutedEventArgs args) => Host?.ChooseSourceProjectClicked(sender, args);

    private void FeatureSearchChanged(TextBox sender, TextBoxTextChangingEventArgs args) => Host?.FeatureSearchChanged(sender, args);

    private void FindSourceExecutableClicked(object sender, RoutedEventArgs args) => Host?.FindSourceExecutableClicked(sender, args);

    private void NextFeatureClicked(object sender, RoutedEventArgs args) => Host?.NextFeatureClicked(sender, args);

    private void NextSourceClicked(object sender, RoutedEventArgs args) => Host?.NextSourceClicked(sender, args);

    private void OpenCatalogClicked(object sender, RoutedEventArgs args) => Host?.OpenCatalogClicked(sender, args);

    private void PageHelpClicked(object sender, RoutedEventArgs args) => Host?.PageHelpClicked(sender, args);

    private void PreviousFeatureClicked(object sender, RoutedEventArgs args) => Host?.PreviousFeatureClicked(sender, args);

    private void PreviousSourceClicked(object sender, RoutedEventArgs args) => Host?.PreviousSourceClicked(sender, args);

    private void ReadFeaturesClicked(object sender, RoutedEventArgs args) => Host?.ReadFeaturesClicked(sender, args);

    private void ReadSourceClicked(object sender, RoutedEventArgs args) => Host?.ReadSourceClicked(sender, args);

    private void SourceConditionChanged(TextBox sender, TextBoxTextChangingEventArgs args) => Host?.SourceConditionChanged(sender, args);

    private void SourceSearchChanged(TextBox sender, TextBoxTextChangingEventArgs args) => Host?.SourceSearchChanged(sender, args);
}
