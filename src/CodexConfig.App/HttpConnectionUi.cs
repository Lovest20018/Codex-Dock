using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private string draftHttpOrigin = "";

    private void RefreshHttpUi()
    {
        if (HttpConsentPanel is null) return;
        var origin = HttpConnectionPolicy.RequiredOrigin(BaseUrlBox.Text);
        if (origin != draftHttpOrigin) draftHttpOrigin = "";
        var previous = initializing; initializing = true;
        HttpConsentPanel.Visibility = origin is null ? Visibility.Collapsed : Visibility.Visible;
        HttpConsentBox.IsChecked = origin is not null && draftHttpOrigin == origin;
        HttpOriginText.Text = origin is null ? "" : "HTTP 明文连接 · " + origin;
        GatewayProxyBox.IsEnabled = !Uri.TryCreate(BaseUrlBox.Text, UriKind.Absolute, out var uri) || uri.Scheme != "http";
        initializing = previous;
    }

    private void HttpConsentChanged(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        draftHttpOrigin = HttpConsentBox.IsChecked == true ? HttpConnectionPolicy.RequiredOrigin(BaseUrlBox.Text) ?? "" : "";
        MarkDirty();
    }

    private void ViewConfigurationSourcesClicked(object sender, RoutedEventArgs e)
    {
        if (desktopRestartBusy) return;
        CloseOverlay();
        ShowPage("catalog"); configurationTools.IsExpanded = true; configurationTools.BringIntoView();
    }
}
