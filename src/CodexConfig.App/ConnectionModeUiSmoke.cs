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
    private async Task RunConnectionModeUiSmokeAsync(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        await RunConnectionModeChecksAsync(output, checks);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { status = "PASS", tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RunConnectionModeChecksAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var fixtureHome = Path.Combine(output, "connection-mode-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixtureHome);
        const string address = "https://relay.invalid/backend-api/codex";
        var configPath = Path.Combine(fixtureHome, "config.toml"); var authPath = Path.Combine(fixtureHome, "auth.json");
        File.WriteAllText(configPath, "model='mode-fixture'\nopenai_base_url='" + address + "'\nweb_search='cached'\n[features]\nfast_mode=false # retain preference\n");
        File.WriteAllText(authPath, new ConnectionSettings(address, "fake-mode-model-key", "mode-fixture").RenderAuth());
        var officialAuth = Encoding.UTF8.GetBytes("{\"auth_mode\":\"chatgpt\",\"tokens\":{\"id_token\":\"fake-mode-id\",\"access_token\":\"fake-mode-access\",\"refresh_token\":\"fake-mode-refresh\"}}");
        var fixtureEnvironment = new IsolatedGatewayEnvironment();
        var fixtureStore = new ConfigStore(fixtureEnvironment, _ => null);
        var fixture = new MainWindow(fixtureStore, fixtureHome, output, fixtureEnvironment);
        fixture.Loaded -= fixture.WindowLoaded;
        try
        {
            fixture.Show(); fixture.ShowPage("connection"); await fixture.Settle();
            var before = fixtureStore.Load(fixtureHome);
            Check(!fixture.Dirty && fixture.SavedConnectionModeText.Text.Contains(DirectConnectionLabel), "Saved direct connection is identified separately from the draft selection");
            fixture.ConnectionModeBox.SelectedIndex = 1;
            fixture.BaseUrlBox.Text = "https://other-relay.invalid/backend-api/codex";
            fixture.ApiKeyBox.Password = "fake-edited-model-key"; fixture.ModelBox.Text = "edited-mode-model";
            fixture.GatewayProxyBox.IsChecked = true;
            var staged = officialAuth.ToArray(); fixture.stagedOfficialAuth = staged; fixture.MarkDirty(); fixture.RefreshMixedUi();
            for (var i = 0; i < 5; i++)
            {
                fixture.ConnectionModeBox.SelectedIndex = 0;
                Check(ReferenceEquals(fixture.stagedOfficialAuth, staged) && staged.SequenceEqual(officialAuth) && fixture.OfficialAuthForDraft is null,
                    "Selecting direct retains staged official login without including it in the direct draft (round " + (i + 1) + ")");
                fixture.UpdatePreview();
                using (var directAuth = JsonDocument.Parse(fixture.preview!.Auth!))
                    Check(!directAuth.RootElement.TryGetProperty("tokens", out _) && directAuth.RootElement.GetProperty("OPENAI_API_KEY").GetString() == "fake-edited-model-key",
                        "Direct preview uses only the model service credential (round " + (i + 1) + ")");
                fixture.ConnectionModeBox.SelectedIndex = 1;
            }
            Check(fixture.BaseUrlBox.Text == "https://other-relay.invalid/backend-api/codex" && fixture.ApiKeyBox.Password == "fake-edited-model-key" && fixture.ModelBox.Text == "edited-mode-model" && fixture.GatewayProxyBox.IsChecked == true,
                "Repeated switching preserves service, credential, model and mixed proxy drafts");
            Check(before.SameContents(fixtureStore.Load(fixtureHome)) && fixture.FindName("AuthenticationBox") is null && fixture.CredentialHint.Text.Contains("ChatGPT"),
                "Switching does not write files, and mixed mode explains the separate model credential");
            fixture.SaveClicked(fixture.SaveButton, new());
            Check(fixture.ConfirmWriteButton.IsEnabled && FindVisual<TextBlock>(fixture.ReviewChanges).Any(block => block.Text == "切换连接方式") && before.SameContents(fixtureStore.Load(fixtureHome)),
                "Confirmation describes the mode transition before any write");
            fixture.CloseOverlay();
            Check(ReferenceEquals(fixture.OfficialAuthForDraft, staged) && fixture.Dirty, "Returning from confirmation retains the mixed login draft");

            fixture.ConnectionModeBox.SelectedIndex = 0;
            fixture.BaseUrlBox.Text = fixture.loadedSettings.BaseUrl; fixture.ApiKeyBox.Password = fixture.loadedSettings.ApiKey; fixture.ModelBox.Text = fixture.loadedSettings.Model;
            fixture.UpdatePreview();
            Check(!fixture.Dirty && !fixture.ConnectionChanged && fixture.DraftCount() == 0 && !fixture.SaveButton.IsEnabled && fixture.preview!.Auth!.SequenceEqual(before.Auth!),
                "Returning to the saved direct values is clean despite cached login and hidden proxy drafts");
            Check(fixture.ConnectionModeHint.Text.Contains("本次官方登录已暂存"), "Direct mode explains that the staged login remains available when switching back");
            fixture.ConnectionModeBox.SelectedIndex = 1;
            fixture.SaveClicked(fixture.SaveButton, new()); fixture.ConfirmWriteClicked(fixture.ConfirmWriteButton, new());
            Check(fixture.reviewSaved && !fixture.Dirty && fixture.loadedSettings.Mode == ConnectionMode.OfficialWithApi && File.ReadAllBytes(authPath).SequenceEqual(officialAuth) && staged.All(value => value == 0),
                "Confirmed mixed save writes official login, reloads cleanly and clears its memory draft");
            fixture.CloseOverlay();
            var mixed = fixtureStore.Load(fixtureHome);
            fixture.ConnectionModeBox.SelectedIndex = 0;
            Check(!fixture.OfficialPanel.IsVisible && !fixture.GatewayPanel.IsVisible && !fixture.GatewayProxyBox.IsVisible && fixture.CredentialLabel.Text == "API Key" && fixture.SavedConnectionModeText.Text.Contains(MixedConnectionLabel),
                "Direct draft keeps gateway lifecycle on its own page and labels the saved mixed connection accurately");
            fixture.ShowPage("gateways"); await fixture.RefreshAllGatewaysAsync();
            Check(fixture.GatewayPanel.IsVisible && !fixture.GlobalSaveBar.IsVisible && fixture.RestartGatewayButton.IsEnabled, "Saved mixed gateway remains manageable while the connection draft selects direct mode");
            fixture.ShowPage("connection");
            fixture.ConnectionModeBox.SelectedIndex = 1;
            Check(!fixture.Dirty && !fixture.ConnectionChanged && !fixture.SaveButton.IsEnabled && mixed.SameContents(fixtureStore.Load(fixtureHome)),
                "Returning to saved mixed mode ignores direct-only authentication edits");

            fixture.ConnectionModeBox.SelectedIndex = 0;
            fixture.Width = 960; fixture.Height = 640; fixture.ConnectionPage.ScrollToTop();
            await fixture.Settle(); fixture.Capture(output, "connection-mode-direct-pending.png");
            ThemeManager.Apply(true); await fixture.Settle(); fixture.Capture(output, "connection-mode-direct-dark.png"); ThemeManager.Apply(false);
            fixture.RootPanel.LayoutTransform = new ScaleTransform(1.5, 1.5); await fixture.Settle();
            fixture.Capture(output, "connection-mode-scaled.png");
            foreach (ListBoxItem option in fixture.ConnectionModeBox.Items)
            {
                var bounds = option.TransformToAncestor(fixture.RootPanel).TransformBounds(new Rect(option.RenderSize));
                Check(bounds.Left >= 0 && bounds.Right <= fixture.RootPanel.ActualWidth && option.ActualWidth + option.Margin.Left + option.Margin.Right >= option.DesiredSize.Width - 1,
                    "Mode option fits the minimum window at 150 percent layout scale: " + System.Windows.Automation.AutomationProperties.GetName(option) + $" (bounds {bounds}, actual {option.ActualWidth}, desired {option.DesiredSize.Width})");
            }
            fixture.RootPanel.LayoutTransform = Transform.Identity; await fixture.Settle();
            fixture.SaveClicked(fixture.SaveButton, new());
            Check(FindVisual<TextBlock>(fixture.ReviewChanges).Any(block => block.Text.Contains("原官方登录会纳入加密备份")) && mixed.SameContents(fixtureStore.Load(fixtureHome)),
                "Mixed-to-direct review explains authentication replacement and preserves files until confirmed");
            fixture.Capture(output, "connection-mode-review.png");
            fixture.ConfirmWriteClicked(fixture.ConfirmWriteButton, new()); fixture.CloseOverlay();
            var direct = fixtureStore.Load(fixtureHome);
            Check(!fixture.Dirty && direct.Bridge is null && !OfficialCredentials.HasLogin(direct.Auth) && fixtureStore.LatestBackup(fixtureHome) is not null,
                "Confirmed direct save removes mixed routing and backs up the former official login");
            Check(new TomlDocument(direct.Config).Text.Contains("# retain preference") && new TomlDocument(direct.Config).Get("features.fast_mode") is false,
                "Mode switches preserve unrelated settings and comments");

            File.WriteAllBytes(authPath, officialAuth); File.WriteAllText(configPath, "model='official-only-fixture'\n");
            fixture.Reload();
            Check(!fixture.Dirty && fixture.SavedConnectionModeText.Text.Contains("官方账号（未启用独立模型入口）"),
                "An existing official-only file is described accurately instead of being reported as a PAT connection");
            fixture.ConnectionModeBox.SelectedIndex = 1; await fixture.Settle(); fixture.ConnectionPage.ScrollToTop(); fixture.Capture(output, "connection-mode-official.png");
        }
        finally
        {
            fixture.exitReady = true; fixture.Close(); ThemeManager.Apply(false);
        }
    }
}
