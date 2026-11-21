using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class ProfilesPage : UserControl
{
    public ProfilesPage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    private void OpenProfileClicked(object sender, RoutedEventArgs args) => Host?.OpenProfileClicked(sender, args);

    private void PageHelpClicked(object sender, RoutedEventArgs args) => Host?.PageHelpClicked(sender, args);

    private void ProfileDocumentChanged(TextBox sender, TextBoxTextChangingEventArgs args) => Host?.ProfileDocumentChanged(sender, args);

    private void ProfileSelectionChanged(object sender, SelectionChangedEventArgs args) => Host?.ProfileSelectionChanged(sender, args);

    private void RefreshProfilesClicked(object sender, RoutedEventArgs args) => Host?.RefreshProfilesClicked(sender, args);
}
