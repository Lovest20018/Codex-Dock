using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunApiKeyMigrationUiSmokeAsync(string output)
    {
        Directory.CreateDirectory(output); var checks = new List<string>();
        await RunApiKeyMigrationChecksAsync(output, checks);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { status = "PASS", tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RunApiKeyMigrationChecksAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var fixtureHome = Path.Combine(output, "api-key-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixtureHome);
        var authPath = Path.Combine(fixtureHome, "auth.json");
        File.WriteAllText(authPath, "{\"personal_access_token\":\"fake-migration-key\",\"OPENAI_API_KEY\":\"fake-migration-key\"}", new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(fixtureHome, "config.toml"), "# keep migration comment\r\nmodel='migration-fixture'\r\nopenai_base_url='https://relay.invalid/backend-api/codex'\r\nchatgpt_base_url='https://relay.invalid/backend-api/'\r\n");
        var fixtureEnvironment = new IsolatedGatewayEnvironment(); fixtureEnvironment.Write("https://relay.invalid");
        var fixtureStore = new ConfigStore(fixtureEnvironment, _ => null);
        var fixture = new MainWindow(fixtureStore, fixtureHome, output, fixtureEnvironment); fixture.Loaded -= fixture.WindowLoaded;
        try
        {
            fixture.Show(); await fixture.Settle(); var before = fixtureStore.Load(fixtureHome);
            Check(fixture.LegacyPatPanel.IsVisible && !fixture.Dirty && !fixture.SaveButton.IsEnabled && fixture.SavedConnectionModeText.Text.Contains("旧 PAT"),
                "Legacy PAT opens read-only with a migration action and an accurate saved label");
            Check(fixture.FindName("AuthenticationBox") is null && fixture.CredentialLabel.Text == "API Key" && before.SameContents(fixtureStore.Load(fixtureHome)),
                "API key is the only key authentication choice and opening the window never rewrites credentials");
            Check(fixture.DirectFastCompatibilityHint.IsVisible && fixture.DirectFastCompatibilityHint.Text.Contains("26.1106") && fixture.DirectFastCompatibilityHint.Text.Contains("Fast／闪电"),
                "Direct connection explains the verified desktop Fast icon limitation before migration");
            fixture.MigratePatClicked(fixture.MigratePatButton, new());
            using (var auth = JsonDocument.Parse(fixture.preview!.Auth!))
                Check(auth.RootElement.GetProperty("auth_mode").GetString() == "apikey" && auth.RootElement.EnumerateObject().Count() == 2,
                    "Migration preview replaces the legacy PAT with the two-field API key form");
            Check(fixture.ConfirmWriteButton.IsEnabled && fixture.ReviewTargets.Text.Contains("auth.json") && fixture.ReviewTargets.Text.Contains("CODEX_AUTHAPI_BASE_URL") &&
                FindVisual<TextBlock>(fixture.ReviewChanges).Any(text => text.Text.Contains("旧 PAT → API Key")) && !fixture.RenderChanges().Contains("fake-migration-key") &&
                !fixture.preview.AuthPreview.Contains("fake-migration-key") && before.SameContents(fixtureStore.Load(fixtureHome)),
                "Migration confirmation shows authentication and old-address cleanup without writing or exposing the key");
            await fixture.Settle(); fixture.Capture(output, "api-key-migration-review.png");
            Check(FindVisual<TextBlock>(fixture.ReviewChanges).Any(text => text.Text == "Codex 桌面 Fast 入口") && before.SameContents(fixtureStore.Load(fixtureHome)),
                "PAT migration review discloses the desktop Fast limitation without changing credentials");
            fixture.CloseOverlay(); fixture.Reload();
            Check(!fixture.Dirty && fixture.LegacyPatPanel.IsVisible && before.SameContents(fixtureStore.Load(fixtureHome)), "Cancelling and reloading leaves the original PAT and addresses intact");
            fixture.MigratePatClicked(fixture.MigratePatButton, new()); fixture.ConfirmWriteClicked(fixture.ConfirmWriteButton, new());
            var saved = fixtureStore.Load(fixtureHome);
            Check(fixture.reviewSaved && !fixture.Dirty && !fixture.LegacyPatPanel.IsVisible && fixtureEnvironment.Read() is null &&
                new TomlDocument(saved.Config).Get("chatgpt_base_url") is null && saved.Bridge is null && fixtureStore.LatestBackup(fixtureHome) is not null,
                "Confirmed migration writes API key credentials, removes old account routing, backs up and reloads cleanly without a gateway");
            using (var auth = JsonDocument.Parse(saved.Auth!))
                Check(auth.RootElement.GetProperty("OPENAI_API_KEY").GetString() == "fake-migration-key" && auth.RootElement.GetProperty("auth_mode").GetString() == "apikey" && auth.RootElement.EnumerateObject().Count() == 2,
                    "The saved API key is the original key and contains neither PAT nor official tokens");
            fixture.CloseOverlay(); fixture.Width = 960; fixture.Height = 640; fixture.ConnectionPage.ScrollToTop(); await fixture.Settle(); fixture.Capture(output, "api-key-direct-compact.png");
            fixtureStore.Restore(saved); fixture.Reload();
            Check(before.SameContents(fixtureStore.Load(fixtureHome)) && fixture.LegacyPatPanel.IsVisible && !fixture.Dirty, "Encrypted restore recovers the exact original PAT, config and environment");
            fixture.RootPanel.LayoutTransform = new ScaleTransform(1.5, 1.5); await fixture.Settle(); fixture.MigratePatButton.BringIntoView(); await fixture.Settle();
            var actionBounds = fixture.MigratePatButton.TransformToAncestor(fixture.ConnectionPage).TransformBounds(new Rect(fixture.MigratePatButton.RenderSize));
            Check(fixture.ConnectionPage.ScrollableHeight > 0 && actionBounds.Top >= 0 && actionBounds.Bottom <= fixture.ConnectionPage.ActualHeight,
                "Migration action scrolls fully into view at minimum size and 150 percent layout scale");
            fixture.Capture(output, "api-key-migration-scaled.png"); fixture.RootPanel.LayoutTransform = Transform.Identity;
            fixture.MigratePatClicked(fixture.MigratePatButton, new());
            const string externalAuth = "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-external-migration-key\"}";
            File.WriteAllText(authPath, externalAuth); fixture.ConfirmWriteClicked(fixture.ConfirmWriteButton, new());
            Check(fixture.reviewWriteFailed && File.ReadAllText(authPath) == externalAuth && fixtureEnvironment.Read() == before.AuthApiBaseUrl,
                "A credential change after migration review blocks the write and preserves external authentication");
            fixture.CloseOverlay(); fixture.Reload(); fixture.ConnectionModeBox.SelectedIndex = 1;
            Check(!fixture.LegacyPatPanel.IsVisible && !fixture.DirectFastCompatibilityHint.IsVisible && fixture.OfficialPanel.IsVisible, "Official login remains a separate mode without a PAT migration action or an API-key-only limitation notice");
        }
        finally { fixture.exitReady = true; fixture.Close(); }
    }
}
