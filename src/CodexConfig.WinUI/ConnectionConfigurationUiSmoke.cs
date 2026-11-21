using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunConnectionConfigurationSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        var previousHome = viewModel.Home;
        var fixture = Path.Combine(output, "connection-editor-home"); Directory.CreateDirectory(fixture);
        var config = Path.Combine(fixture, "config.toml"); var auth = Path.Combine(fixture, "auth.json");
        File.WriteAllText(config, "# connection editor fixture\nmodel='editor-old-model'\nopenai_base_url='https://editor.invalid/v1'\n");
        File.WriteAllText(auth, "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-editor-private-key\"}");
        var original = store.Load(fixture); var runtime = (SmokeDesktopApplyRuntime)applicationRuntime; var launches = runtime.Restarts;
        try
        {
            await SwitchHomeAsync(fixture); NavigateTo(ConnectionNav);
            Check(ConnectionPanel.Visibility == Visibility.Visible && WorkspacePanel.Visibility == Visibility.Collapsed && PageTitle.Text == "连接与模型" &&
                ConnectionPanel.BaseUrlInput.Text == "https://editor.invalid/v1" && ConnectionPanel.ConnectionModelSummary.Text.Contains("editor-old-model") && ConnectionPanel.ChooseConnectionModelButton.IsEnabled && !connectionDraft.HasDraft,
                "WinUI connection page loads the shared saved settings without creating a draft");
            Check(ConnectionPanel.ApiKeyInput.PasswordRevealMode == PasswordRevealMode.Peek && original.SameContents(store.Load(fixture)) && store.LatestBackup(fixture) is null,
                "Opening connection controls masks the key and does not write configuration or backups");
            await CaptureAsync(output, "connection-light.png");
            OpenModelsClicked(ConnectionPanel.ChooseConnectionModelButton, new());
            ModelsPanel.BehaviorModelInput.Text = "editor-new-model"; await Task.Delay(40);
            Check(connectionDraft.HasDraft && viewModel.Session.State.HasDraft && !ApplyButton.IsEnabled && SaveConnectionButton.IsEnabled && original.SameContents(store.Load(fixture)),
                "Editing the default model stages a shared draft and disables desktop application before any write");
            NavigateTo(EnhancementsNav); NavigateTo(ModelsNav);
            await TryAutomaticEnhancementsAsync(); await ApplyDesktopAsync();
            Check(ModelsPanel.BehaviorModelInput.Text == "editor-new-model" && ConnectionDraftActions.Visibility == Visibility.Visible && runtime.Restarts == launches && !automaticEnhancements.IsBusy,
                "Navigation preserves the draft while manual application and automatic reconnect leave desktops untouched (" + currentPage.Tag + ", model " +
                ModelsPanel.BehaviorModelInput.Text + ", draft " + connectionDraft.HasDraft + ", actions " + ConnectionDraftActions.Visibility + ", restarts " + (runtime.Restarts - launches) + ")");
            PageScroll.ChangeView(null, double.MaxValue, null, true);
            await CaptureAsync(output, "connection-draft.png");
            var savePosition = SaveConnectionButton.TransformToVisual(Root).TransformPoint(new());
            Check(savePosition.Y >= 0 && savePosition.Y + SaveConnectionButton.ActualHeight <= Root.ActualHeight && DesktopApplicationActions.Visibility == Visibility.Collapsed,
                "The fixed draft footer keeps save and discard accessible while hiding duplicate desktop actions");
            var dialogChecks = 0;
            smokeDialog = async dialog =>
            {
                dialogChecks++;
                var text = ConfigurationDialogText(dialog);
                Check(text.Contains(config) && text.Contains("editor-new-model") && !text.Contains("fake-editor-private-key") &&
                    !ModelsPanel.BehaviorModelInput.IsEnabled && !ChooseHomeButton.IsEnabled && !ApplyButton.IsEnabled,
                    "Save confirmation names the files and reviewed values hides credentials and locks conflicting actions");
                var shown = dialog.ShowAsync();
                try { await CaptureAsync(output, "connection-review.png", dialog); }
                finally { dialog.Hide(); await shown; }
                return ContentDialogResult.None;
            };
            Check(!await SaveConnectionAsync() && dialogChecks == 1 && connectionDraft.HasDraft && original.SameContents(store.Load(fixture)) && store.LatestBackup(fixture) is null,
                "Returning from the real WinUI review keeps all draft fields and creates no backup");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(await SaveConnectionAsync() && !connectionDraft.HasDraft && !viewModel.Session.State.HasDraft && ApplyButton.IsEnabled &&
                viewModel.Session.State.Phase == WorkspacePhase.NeedsRestart && configurationResult?.BackupPath is { } backup && File.Exists(backup),
                "Confirmed WinUI save writes rereads and backs up once then waits for a separate desktop application");
            var saved = store.Load(fixture);
            Check(new TomlDocument(saved.Config).String("model") == "editor-new-model" && original.Auth!.SequenceEqual(saved.Auth!) &&
                Encoding.UTF8.GetString(saved.Config!).Contains("connection editor fixture") && runtime.Restarts == launches,
                "A model-only save preserves authentication unrelated comments and the running desktop");
            await CaptureAsync(output, "connection-saved.png");

            ModelsPanel.BehaviorModelInput.Text = "keep-draft"; await Task.Delay(40);
            smokeDialog = _ => Task.FromResult(ContentDialogResult.None);
            Check(!await SwitchHomeAsync(Path.Combine(output, "cancelled-editor-home")) && viewModel.Home == fixture && ModelsPanel.BehaviorModelInput.Text == "keep-draft",
                "Cancelling a directory switch preserves the current connection draft and workspace");
            await ReloadWorkspaceAsync();
            Check(ModelsPanel.BehaviorModelInput.Text == "keep-draft" && connectionDraft.HasDraft && saved.SameContents(store.Load(fixture)),
                "Cancelling reload keeps the draft and the saved file unchanged");
            Check(!await StopBeforeCloseAsync() && !closing && connectionDraft.HasDraft, "Cancelling explicit exit retains the unsaved connection draft and window lifetime");
            await RequestEnhancedLaunchAsync(fixture);
            Check(connectionDraft.HasDraft && runtime.Restarts == launches, "An enhanced shortcut cannot bypass the unsaved-draft choice");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary);
            await ReloadWorkspaceAsync();
            Check(!connectionDraft.HasDraft && ModelsPanel.BehaviorModelInput.Text == "editor-new-model", "Explicitly discarding during reload restores the saved connection values");

            ModelsPanel.BehaviorModelInput.Text = "must-not-overwrite"; await Task.Delay(40);
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            File.AppendAllText(config, "service_tier='priority'\n"); var external = store.Load(fixture);
            Check(!await SaveConnectionAsync() && configurationResult?.Outcome == ConfigurationSaveOutcome.Conflict && ErrorBar.IsOpen && connectionDraft.HasDraft && external.SameContents(store.Load(fixture)),
                "A confirmed save rejects outside edits and retains both external bytes and the local draft");
            var reloadReview = 0;
            smokeDialog = dialog => { reloadReview++; return Task.FromResult(reloadReview == 1 ? ContentDialogResult.Secondary : ContentDialogResult.Primary); };
            await ReloadWorkspaceAsync();
            Check(reloadReview == 2 && !connectionDraft.HasDraft && connectionDraft.Model == "editor-new-model" && external.SameContents(store.Load(fixture)),
                "Reload after a failed save separates discarding the draft from reviewing the external difference");

            NavigateTo(ConnectionNav); ConnectionPanel.BaseUrlInput.Text = "invalid-address"; await Task.Delay(40);
            var invalidBefore = store.Load(fixture); var confirmations = 0;
            smokeDialog = _ => { confirmations++; return Task.FromResult(ContentDialogResult.Primary); };
            Check(!await SaveConnectionAsync() && ErrorBar.IsOpen && confirmations == 0 && connectionDraft.HasDraft && invalidBefore.SameContents(store.Load(fixture)),
                "Invalid connection values report a recoverable error before showing a writable confirmation");
            DiscardConnectionClicked(DiscardConnectionButton, new());
            NavigateTo(ModelsNav);
            ModelsPanel.BehaviorModelInput.Text = "stale-reviewed-model"; await Task.Delay(40);
            smokeDialog = _ => { connectionDraft.Model = "changed-during-review"; return Task.FromResult(ContentDialogResult.Primary); };
            Check(!await SaveConnectionAsync() && configurationResult?.Outcome == ConfigurationSaveOutcome.Cancelled && connectionDraft.Model == "changed-during-review" && invalidBefore.SameContents(store.Load(fixture)),
                "Changing draft identity during confirmation invalidates the reviewed write and retains the newer draft");
            DiscardConnectionClicked(DiscardConnectionButton, new());

            NavigateTo(ConnectionNav);
            ConnectionPanel.BaseUrlInput.Text = "http://first-editor.invalid/v1";
            for (var attempt = 0; attempt < 30 && connectionDraft.BaseUrl != ConnectionPanel.BaseUrlInput.Text; attempt++) await Task.Delay(20);
            Check(ConnectionPanel.HttpConsentInput.Visibility == Visibility.Visible && ConnectionPanel.HttpConsentInput.IsChecked != true, "A non-loopback HTTP service exposes the existing site-bound confirmation");
            ConnectionPanel.HttpConsentInput.IsChecked = true; ConnectionPanel.BaseUrlInput.Text = "http://second-editor.invalid/v1";
            for (var attempt = 0; attempt < 30 && connectionDraft.BaseUrl != ConnectionPanel.BaseUrlInput.Text; attempt++) await Task.Delay(20);
            Check(ConnectionPanel.HttpConsentInput.IsChecked == false && ConnectionPanel.HttpConsentText.Text.Contains("second-editor.invalid"), "Changing the HTTP host clears the previous site's consent");
            DiscardConnectionClicked(DiscardConnectionButton, new());

            ConnectionPanel.ConnectionModeInput.SelectedIndex = 1;
            for (var attempt = 0; attempt < 30 && connectionDraft.Mode != ConnectionMode.OfficialWithApi; attempt++) await Task.Delay(20);
            ConnectionPanel.ApiKeyInput.Password = "fake-mixed-editor-key";
            for (var attempt = 0; attempt < 30 && connectionDraft.ApiKey != "fake-mixed-editor-key"; attempt++) await Task.Delay(20);
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(!await SaveConnectionAsync() && ErrorBar.IsOpen && !connectionDraft.HasOfficialLogin && invalidBefore.SameContents(store.Load(fixture)),
                "Mixed mode without official login fails safely and retains the service-key draft");
            DiscardConnectionClicked(DiscardConnectionButton, new());
            File.WriteAllBytes(auth, JsonSerializer.SerializeToUtf8Bytes(new { auth_mode = "chatgpt", tokens = new { access_token = "fake-official-access", refresh_token = "fake-official-refresh", id_token = "fake-official-id" } }));
            LoadWorkspace(fixture);
            var officialAuth = File.ReadAllBytes(auth);
            ConnectionPanel.ConnectionModeInput.SelectedIndex = 1;
            for (var attempt = 0; attempt < 30 && connectionDraft.Mode != ConnectionMode.OfficialWithApi; attempt++) await Task.Delay(20);
            ConnectionPanel.ApiKeyInput.Password = "fake-mixed-editor-key";
            for (var attempt = 0; attempt < 30 && connectionDraft.ApiKey != "fake-mixed-editor-key"; attempt++) await Task.Delay(20);
            Check(connectionDraft.Mode == ConnectionMode.OfficialWithApi && connectionDraft.ApiKey == "fake-mixed-editor-key", "Mixed connection fixture waits for mode and password input events before saving");
            ConnectionPanel.ConnectionProxyInput.IsChecked = true;
            var mixedSaved = await SaveConnectionAsync();
            Check(mixedSaved && store.ReadSettings(store.Load(fixture)).Mode == ConnectionMode.OfficialWithApi && officialAuth.SequenceEqual(File.ReadAllBytes(auth)) &&
                configurationResult?.BackupPath is not null && runtime.Restarts == launches,
                "WinUI saves mixed mode with existing official identity and encrypted model credentials without starting a gateway" +
                (mixedSaved ? "" : "; " + shellError + "; " + configurationResult?.Message));
            await CaptureAsync(output, "connection-mixed.png");

            ConnectionPanel.ConnectionNameInput.Text = "Saved before switching"; await Task.Delay(40);
            var savedOnSwitch = Path.Combine(output, "save-before-switch-home");
            Check(await SwitchHomeAsync(savedOnSwitch) && !connectionDraft.HasDraft && ConnectionDetails.Read(store.Load(fixture).Details).Name == "Saved before switching" &&
                !Directory.Exists(savedOnSwitch), "Choosing save during a directory switch confirms and commits the old draft before opening the new workspace");
            await SwitchHomeAsync(fixture);
            var legacy = Path.Combine(output, "editor-legacy-home"); Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "config.toml"), "model='legacy-editor'\nopenai_base_url='https://legacy-editor.invalid/v1'\n");
            File.WriteAllText(Path.Combine(legacy, "auth.json"), "{\"personal_access_token\":\"fake-legacy-editor-key\"}");
            await SwitchHomeAsync(legacy);
            Check(ConnectionPanel.LegacyKeyInfo.IsOpen && !connectionDraft.HasDraft && ConnectionPanel.MigrateKeyButton.IsEnabled, "Reading legacy PAT exposes an explicit migration action without converting credentials");
            MigrateKeyClicked(ConnectionPanel.MigrateKeyButton, new());
            Check(connectionDraft.HasDraft && connectionDraft.MigrateLegacyPat && ConnectionPanel.LegacyKeyInfo.Message.Contains("草稿"), "PAT migration is staged for review rather than written immediately");
            Check(await SaveConnectionAsync() && !ConnectionPanel.LegacyKeyInfo.IsOpen && !store.ReadSettings(store.Load(legacy)).NeedsApiKeyMigration && runtime.Restarts == launches,
                "Confirming WinUI PAT migration writes API Key authentication with an encrypted backup and no automatic restart");
            await SwitchHomeAsync(fixture);

            Root.RequestedTheme = ElementTheme.Dark;
            AppWindow.Resize(new(900, 760)); await Task.Delay(160); Root.UpdateLayout(); PageScroll.ChangeView(null, 0, null, true);
            await CaptureAsync(output, "connection-compact-dark.png");
            Check(ConnectionPanel.BaseUrlInput.ActualWidth > 250 && ConnectionPanel.BaseUrlInput.TransformToVisual(Root).TransformPoint(new()).X + ConnectionPanel.BaseUrlInput.ActualWidth <= Root.ActualWidth,
                "Connection inputs remain inside the compact WinUI layout while the apply footer stays fixed");
            PageScroll.ChangeView(null, double.MaxValue, null, true);
            await CaptureAsync(output, "connection-fields-lower-dark.png");
            Root.RequestedTheme = ElementTheme.Light;
        }
        finally
        {
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary);
            if (connectionDraft.HasDraft) { LoadConnectionDraft(); viewModel.Session.MarkDraft(false); }
            await SwitchHomeAsync(previousHome); smokeDialog = null; NavigateTo(WorkspaceNav);
        }
    }

    private static string ConfigurationDialogText(ContentDialog dialog)
    {
        string Read(object? element) => element switch
        {
            TextBlock text => text.Text,
            ScrollViewer scroll => Read(scroll.Content),
            Panel panel => string.Join("\n", panel.Children.Select(child => Read(child))),
            _ => ""
        };
        return dialog.Title + "\n" + Read(dialog.Content);
    }
}
