using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunOfficialLoginUiSmokeAsync(string output)
    {
        Directory.CreateDirectory(output); var checks = new List<string>();
        await RunOfficialLoginChecksAsync(output, checks);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { status = "PASS", tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RunOfficialLoginChecksAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var fixtureHome = Path.Combine(output, "official-login-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixtureHome);
        var store = new ConfigStore(new IsolatedGatewayEnvironment(), _ => null);
        store.Apply(store.Plan(store.Load(fixtureHome), new("https://fixture.invalid/v1", "fake-login-model-key", "login-model") { Kind = ConnectionKind.ResponsesApi }));
        var fixture = new MainWindow(store, fixtureHome, output, new IsolatedGatewayEnvironment()); fixture.Loaded -= fixture.WindowLoaded;
        byte[] Login(string token) => Encoding.UTF8.GetBytes("{\"auth_mode\":\"chatgpt\",\"tokens\":{\"id_token\":\"fake-login-id\",\"access_token\":\"" + token + "\",\"refresh_token\":\"fake-login-refresh\"}}");
        var authPath = Path.Combine(fixtureHome, "auth.json");
        try
        {
            fixture.Show(); fixture.ConnectionModeBox.SelectedIndex = 1; await fixture.Settle();
            Check(fixture.FindName("DeviceLoginButton") is null && fixture.FindName("LoginDeviceCodeText") is null && fixture.FindName("CodexPathBox") is null &&
                fixture.RefreshOfficialLoginButton.IsVisible && fixture.RestoreOfficialLoginButton.IsVisible,
                "Official login offers current-file reading and encrypted backup restoration without device codes or a CLI picker");
            fixture.ModelBox.Text = "unsaved-login-model"; fixture.detailedChanges["analytics.enabled"] = "false"; fixture.MarkDirty();
            var before = store.Load(fixtureHome); await fixture.ReadOfficialLoginAsync(fromBackup: false); fixture.RefreshMixedUi();
            Check(fixture.loginFeedbackIsError && fixture.OfficialStatusText.Text.Contains("Codex 桌面") && fixture.ModelBox.Text == "unsaved-login-model" &&
                before.SameContents(store.Load(fixtureHome)) && fixture.OfficialAuthForDraft is null,
                "Missing official login keeps actionable desktop instructions and preserves files and drafts");
            var live = Login("fake-live-login-access"); File.WriteAllBytes(authPath, live); await fixture.ReadOfficialLoginAsync(fromBackup: false);
            Check(!fixture.loginFeedbackIsError && fixture.snapshot!.Auth!.SequenceEqual(live) && fixture.OfficialAuthForDraft is null && fixture.ModelBox.Text == "unsaved-login-model" && fixture.ApiKeyBox.Password == "fake-login-model-key" &&
                fixture.detailedChanges.GetValueOrDefault("analytics.enabled") == "false" && File.ReadAllBytes(authPath).SequenceEqual(live),
                "Reading a native desktop login adopts only the observed authentication while retaining model and settings drafts");
            Check(!fixture.RestoreOfficialLoginButton.IsEnabled && fixture.SaveButton.IsEnabled, "Current official credentials take priority over restoring an older backup");
            fixture.SaveClicked(fixture.SaveButton, new()); fixture.ConfirmWriteClicked(fixture.ConfirmWriteButton, new()); fixture.CloseOverlay();
            Check(fixture.loadedSettings.Mode == ConnectionMode.OfficialWithApi && store.Load(fixtureHome).Auth!.SequenceEqual(live) && !fixture.Dirty,
                "Confirmed mixed configuration keeps the native official login byte for byte");
            var clean = store.Load(fixtureHome); await fixture.ReadOfficialLoginAsync(fromBackup: false);
            Check(!fixture.Dirty && !fixture.SaveButton.IsEnabled && clean.SameContents(store.Load(fixtureHome)),
                "Re-reading an unchanged current login stays clean and never asks for a redundant save");
            fixture.catalogDraft = fixture.reviewDraft = true; fixture.catalogSourceDraft = "pending-catalog-fixture.json";
            File.AppendAllText(Path.Combine(fixtureHome, "config.toml"), "# native formatting update\n");
            await fixture.ReadOfficialLoginAsync(fromBackup: false);
            Check(!fixture.loginFeedbackIsError && fixture.catalogDraft && fixture.reviewDraft && fixture.catalogSourceDraft == "pending-catalog-fixture.json",
                "Harmless native formatting does not block login refresh or discard a staged catalog and reviewer");
            fixture.catalogDraft = fixture.reviewDraft = false; fixture.catalogSourceDraft = null;
            fixture.ConnectionModeBox.SelectedIndex = 0; fixture.SaveClicked(fixture.SaveButton, new()); fixture.ConfirmWriteClicked(fixture.ConfirmWriteButton, new()); fixture.CloseOverlay();
            var direct = store.Load(fixtureHome); fixture.ConnectionModeBox.SelectedIndex = 1;
            await fixture.ReadOfficialLoginAsync(fromBackup: true);
            Check(fixture.OfficialAuthForDraft!.SequenceEqual(live) && direct.SameContents(store.Load(fixtureHome)) && fixture.OfficialStatusText.Text.Contains("加密备份"),
                "Selecting the saved official login stages only the selected home's encrypted backup until confirmation");
            fixture.Width = 960; fixture.Height = 640; fixture.OfficialPanel.BringIntoView(); await fixture.Settle(); fixture.Capture(output, "official-login-backup-light.png");
            ThemeManager.Apply(true); await fixture.Settle(); fixture.Capture(output, "official-login-backup-dark.png"); ThemeManager.Apply(false);
            var oldStage = fixture.stagedOfficialAuth!; var refreshed = Login("fake-refreshed-login-access"); File.WriteAllBytes(authPath, refreshed);
            await fixture.ReadOfficialLoginAsync(fromBackup: false);
            Check(oldStage.All(value => value == 0) && fixture.OfficialAuthForDraft is null && fixture.snapshot!.Auth!.SequenceEqual(refreshed), "A newly observed native login replaces and clears the older staged backup in memory");
            fixture.SaveClicked(fixture.SaveButton, new()); fixture.ConfirmWriteClicked(fixture.ConfirmWriteButton, new()); fixture.CloseOverlay();
            Check(store.Load(fixtureHome).Auth!.SequenceEqual(refreshed), "Confirmed save preserves the latest refreshed official tokens");
            fixture.ModelBox.Text = "preserved-conflict-draft"; var configPath = Path.Combine(fixtureHome, "config.toml"); File.AppendAllText(configPath, "\nfuture_login_concurrent = true\n");
            var changed = store.Load(fixtureHome); await fixture.ReadOfficialLoginAsync(fromBackup: false);
            Check(fixture.loginFeedbackIsError && fixture.ModelBox.Text == "preserved-conflict-draft" && changed.SameContents(store.Load(fixtureHome)),
                "Concurrent configuration edits block a login re-read without rebasing or discarding the draft");
            fixture.Reload(); File.WriteAllText(authPath, "{}"); fixture.Reload(); fixture.ConnectionModeBox.SelectedIndex = 1;
            Check(fixture.OfficialAuthForDraft is null && !OfficialCredentials.HasLogin(store.Load(fixtureHome).Auth), "Signing out never automatically revives a backed up official login");
        }
        finally { fixture.exitReady = true; fixture.Close(); ThemeManager.Apply(false); }
    }
}
