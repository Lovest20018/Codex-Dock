using System.IO;
using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunSuperbaReuseSmokeAsync(string output, List<string> checks)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var previousHome = home; var width = Width; var height = Height;
        var fixture = Path.Combine(output, "http-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "config.toml"), "model='fixture-http'\nopenai_base_url='https://fixture.invalid/v1'\n");
        File.WriteAllText(Path.Combine(fixture, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-http-key\"}");
        try
        {
            home = fixture; Check(Reload(), "HTTP fixture reloads"); ShowPage("connection");
            KindBox.SelectedIndex = (int)ConnectionKind.ResponsesApi;
            BaseUrlBox.Text = "http://fixture.invalid:8080/v1"; await Settle();
            Check(HttpConsentPanel.IsVisible && HttpConsentBox.IsChecked == false && !GatewayProxyBox.IsEnabled, "HTTP shows an unchecked origin grant and disables the proxy option");
            Check(!SaveCurrent(), "Unconfirmed new HTTP connection cannot be saved");
            HttpConsentBox.IsChecked = true; BaseUrlBox.Text = "http://fixture.invalid:8080/other";
            Check(HttpConsentBox.IsChecked == true && draftHttpOrigin == "http://fixture.invalid:8080", "HTTP path changes preserve the same origin grant");
            BaseUrlBox.Text = "http://fixture.invalid:8081/v1";
            Check(HttpConsentBox.IsChecked == false && draftHttpOrigin == "", "HTTP port changes invalidate the grant");
            BaseUrlBox.Text = "http://other.invalid:8081/v1"; HttpConsentBox.IsChecked = true;
            BaseUrlBox.Text = "http://fixture.invalid:8080/v1";
            Check(HttpConsentBox.IsChecked == false, "Changing the HTTP host invalidates its grant");
            Width = 960; Height = 640; await Settle(); Capture(output, "http-confirmation.png");
            HttpConsentBox.IsChecked = true; SaveClicked(SaveButton, new()); await Settle();
            Capture(output, "http-write-review.png");
            Check(ConfirmWriteButton.IsEnabled && FindVisual<TextBlock>(ReviewChanges).Any(text => text.Text.Contains("http://fixture.invalid:8080")) && FindVisual<TextBlock>(ReviewChanges).Any(text => text.Text == "HTTP 明文连接确认"), "Write review identifies the exact HTTP origin: " + ReviewError.Text);
            ConfirmWriteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
            Check(reviewSaved && loadedSettings.Details.HttpOrigin == "http://fixture.invalid:8080" && ReviewSuccessMessage.Text.Contains("新建聊天"), "Saved HTTP grant and configuration activation instructions survive reload");
            await RunDesktopRestartReviewSmokeAsync(output, checks);
            ViewConfigurationSourcesClicked(this, new()); await Settle();
            Check(currentPage == "catalog" && configurationTools.IsExpanded && !PreviewOverlay.IsVisible, "Successful save links directly to configuration sources");
            ShowPage("connection"); HttpConsentBox.IsChecked = false;
            Check(!ConnectionChanged && DetailsChanged && SaveCurrent() && loadedSettings.Details.HttpOrigin == "", "Saved HTTP revocation changes private metadata without rewriting the direct connection");
            checks.Add("HTTP confirmation binds to one origin, survives reload, appears in review and can be revoked");
            checks.Add("Successful saves explain activation limits and link to configuration sources");
        }
        finally
        {
            smokeDesktopTarget = null; smokeDesktopConfirm = null; smokeDesktopRestart = null;
            CloseOverlay(); home = previousHome; Reload(); Width = width; Height = height; ShowPage("connection");
        }
    }
}
