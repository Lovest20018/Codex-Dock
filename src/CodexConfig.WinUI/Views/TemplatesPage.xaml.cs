using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class TemplatesPage : UserControl
{
    public TemplatesPage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    private void AddTemplateRowClicked(object sender, RoutedEventArgs args) => Host?.AddTemplateRowClicked(sender, args);

    private void CaptureTemplateClicked(object sender, RoutedEventArgs args) => Host?.CaptureTemplateClicked(sender, args);

    private void NewTemplateClicked(object sender, RoutedEventArgs args) => Host?.NewTemplateClicked(sender, args);

    private void RefreshTemplatesClicked(object sender, RoutedEventArgs args) => Host?.RefreshTemplatesClicked(sender, args);

    private void RestoreBuiltInsClicked(object sender, RoutedEventArgs args) => Host?.RestoreBuiltInsClicked(sender, args);

    private void TemplateEditTextChanged(TextBox sender, TextBoxTextChangingEventArgs args) => Host?.TemplateEditTextChanged(sender, args);

    private void UndoTemplateClicked(object sender, RoutedEventArgs args) => Host?.UndoTemplateClicked(sender, args);
}
