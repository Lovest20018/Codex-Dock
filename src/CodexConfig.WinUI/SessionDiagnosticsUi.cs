using Microsoft.UI.Xaml;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    internal void OpenSourcesClicked(object sender, RoutedEventArgs args) => NavigateTo(SourcesNav);
}
