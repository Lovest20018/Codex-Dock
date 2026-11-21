using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class AdvancedPage : UserControl
{
    public AdvancedPage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    private void AdvancedSearchChanged(TextBox sender, TextBoxTextChangingEventArgs args) => Host?.AdvancedSearchChanged(sender, args);

    private void AdvancedToolsSizeChanged(object sender, SizeChangedEventArgs args) => Host?.AdvancedToolsSizeChanged(sender, args);

    private void SearchAdvancedFieldsClicked(object sender, RoutedEventArgs args) => Host?.SearchAdvancedFieldsClicked(sender, args);
}
