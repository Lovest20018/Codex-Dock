using System.Text;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunWorkspaceInformationSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        async Task WaitReadAsync()
        { for (var i = 0; i < 250 && readingSavedPreview; i++) await Task.Delay(20); if (readingSavedPreview) throw new TimeoutException("Saved preview read did not finish."); }
        var previous = viewModel.Home; var previewService = savedConfigurationPreview;
        var desktop = (SmokeDesktopApplyRuntime)applicationRuntime; var restarts = desktop.Restarts;
        var copied = smokeCopiedTexts.Count; var opened = smokeOpenedDirectories.Count;
        var home = Path.Combine(output, "information-ui-home"); Directory.CreateDirectory(home);
        var configPath = Path.Combine(home, "config.toml"); var authPath = Path.Combine(home, "auth.json");
        var config = Encoding.UTF8.GetBytes("# fake-preview-comment\nmodel='saved-preview-model'\nopenai_base_url='https://preview.invalid/v1'\n[future]\nflag=false\nurl='https://fake-user:fake-pass@preview.invalid/v1?key=fake-query#fake-fragment'\n[mcp_servers.fixture]\nenv={PRIVATE='fake-preview-env'}\n");
        var auth = Encoding.UTF8.GetBytes("{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-preview-key\",\"fake-private-name\":\"fake-private-person\"}");
        File.WriteAllBytes(configPath, config); File.WriteAllBytes(authPath, auth);
        try
        {
            NavigateTo(WorkspaceNav); await SwitchHomeAsync(home);
            connectionDraft.Model = "unsaved-preview-model"; var revision = connectionDraft.Revision;
            NavigateTo(FilePreviewNav); await WaitReadAsync();
            Check(FilePreviewPanel.Visibility == Visibility.Visible && savedPreview is not null && SavedPreviewText.IsReadOnly && SavedPreviewStatus.Text.Contains("未保存草稿未参与"),
                "File preview opens a read only saved snapshot with its directory time and draft boundary");
            Check(SavedPreviewText.Text.Contains("saved-preview-model") && !SavedPreviewText.Text.Contains("unsaved-preview-model") && connectionDraft.Revision == revision && connectionDraft.HasDraft,
                "Saved file preview excludes and preserves an unsaved main configuration draft");
            Check(!SavedPreviewText.Text.Contains("fake-") && SavedPreviewText.Text.Contains("preview.invalid/v1") && SavedPreviewText.Text.Contains("false") && CopySavedPreviewButton.IsEnabled,
                "Saved TOML preview removes comments and nested credentials while retaining ordinary fields");
            Check(smokeCopiedTexts.Count == copied && smokeOpenedDirectories.Count == opened && config.SequenceEqual(File.ReadAllBytes(configPath)) && auth.SequenceEqual(File.ReadAllBytes(authPath)) && store.LatestBackup(home) is null && desktop.Restarts == restarts,
                "Opening saved preview performs no copy directory launch backup save or desktop restart");
            AppWindow.Resize(new(1180, 850)); PageScroll.ChangeView(null, 120, null, true); await CaptureAsync(output, "saved-files-light.png");
            SavedPreviewSectionInput.SelectedIndex = 1;
            Check(SavedPreviewText.Text.Contains("other_fields_hidden") && SavedPreviewText.Text.Contains("OPENAI_API_KEY") && !SavedPreviewText.Text.Contains("fake-"),
                "Authentication tab displays only the recognized mode presence markers and hidden field count");
            CopySavedPreviewClicked(CopySavedPreviewButton, new());
            Check(smokeCopiedTexts.Count == copied + 1 && smokeCopiedTexts.Last() == savedPreview!.Auth.Text && !smokeCopiedTexts.Last().Contains("fake-"),
                "Explicit authentication copy uses the sanitized section through the isolated clipboard fixture");
            SavedPreviewSectionInput.SelectedIndex = 2;
            Check(SavedPreviewText.Text.Contains("CODEX_AUTHAPI_BASE_URL") && SavedPreviewText.Text.Contains("未设置") && !CopySavedPreviewButton.IsEnabled,
                "Environment tab names its single user variable and disables copying absent values");
            OpenWorkspaceFolderClicked(OpenWorkspaceFolderButton, new()); CopyWorkspacePathClicked(CopyWorkspacePathButton, new());
            Check(smokeOpenedDirectories.Count == opened + 1 && smokeOpenedDirectories.Last() == home && smokeCopiedTexts.Last() == home,
                "Explicit directory actions use the current path through isolated open and copy fixtures");
            NavigateTo(HelpNav); HelpSearchInput.Text = "Profile";
            Check(HelpPanel.Visibility == Visibility.Visible && ((IEnumerable<DockHelpTopic>)HelpTopicsInput.ItemsSource).Any(topic => topic.Id == "profiles"),
                "Central help filters both topic titles and bodies without discarding configuration edits");
            HelpTopicsInput.SelectedItem = ((IEnumerable<DockHelpTopic>)HelpTopicsInput.ItemsSource).Single(topic => topic.Id == "profiles");
            HelpTopicActionClicked(HelpTopicActionButton, new());
            Check(ProfilesPanel.Visibility == Visibility.Visible && connectionDraft.HasDraft && connectionDraft.Revision == revision && config.SequenceEqual(File.ReadAllBytes(configPath)),
                "Help topic action navigates to Profiles while leaving the main draft and saved files intact");
            if (!await LoadProfileAsync("preview-only")) throw new InvalidOperationException("Preview Profile fixture could not load.");
            ProfileDocumentInput.Text = "model='unsaved-profile-preview-model'\n"; var profileRevision = profileDraft.Revision;
            NavigateTo(FilePreviewNav); await WaitReadAsync(); SavedPreviewSectionInput.SelectedIndex = 0;
            Check(profileDraft.HasDraft && profileDraft.Revision == profileRevision && connectionDraft.Revision == revision && !SavedPreviewText.Text.Contains("unsaved-profile-preview-model") && !File.Exists(ProfileStore.FilePath(home, "preview-only")),
                "Saved preview excludes and preserves an independent unsaved Profile without creating its file");
            profileDraft.Load(null);
            NavigateTo(HelpNav); HelpSearchInput.Text = "no-topic-123";
            Check(HelpEmptyText.Visibility == Visibility.Visible && HelpTopicCard.Visibility == Visibility.Collapsed,
                "Help search gives an empty result state instead of retaining the previous topic");
            HelpSearchInput.Text = ""; HelpTopicsInput.SelectedIndex = 0; Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(900, 760));
            await Task.Delay(80); Root.UpdateLayout(); PageScroll.ChangeView(null, PageScroll.ScrollableHeight, null, true);
            await CaptureAsync(output, "help-compact-dark.png");
            var helpAction = HelpTopicActionButton.TransformToVisual(Root).TransformPoint(new());
            Check(HelpTopicActionButton.ActualWidth > 0 && helpAction.X + HelpTopicActionButton.ActualWidth <= Root.ActualWidth && helpAction.Y >= 0 && helpAction.Y + HelpTopicActionButton.ActualHeight < Root.ActualHeight - 70,
                "Central help topic text and navigation action fit the compact dark window");
            NavigateTo(FilePreviewNav); await WaitReadAsync(); SavedPreviewSectionInput.SelectedIndex = 0;
            PageScroll.ChangeView(null, 350, null, true); await CaptureAsync(output, "saved-files-compact-dark.png");
            Check(SavedPreviewText.ActualWidth > 240 && SavedPreviewText.TransformToVisual(Root).TransformPoint(new()).X + SavedPreviewText.ActualWidth <= Root.ActualWidth && SaveConnectionButton.IsEnabled,
                "Compact saved preview wraps inside the window while the unsaved main draft retains its save action");

            LoadConnectionDraft(); await ReadSavedConfigurationPreviewAsync();
            connectionDraft.Model = "confirmed-preview-model"; smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(await SaveConnectionAsync() && savedPreview is null && SavedPreviewText.Text.Length == 0, "Confirmed main save invalidates the previous saved file preview");
            await ReadSavedConfigurationPreviewAsync();
            Check(savedPreview!.Config.Text.Contains("confirmed-preview-model"), "Rereading saved preview displays the newly confirmed file contents");
            Check(await RestoreConfigurationAsync() && savedPreview is null && SavedPreviewText.Text.Length == 0 && desktop.Restarts == restarts,
                "Confirmed backup restoration clears the earlier preview and does not apply or restart Codex");
            smokeDialog = null;

            var gate = new GatePreviewRuntime(); savedConfigurationPreview = new(gate);
            var pending = ReadSavedConfigurationPreviewAsync(); await gate.Entered.Task;
            Check(CancelSavedPreviewButton.IsEnabled && !ReadSavedPreviewButton.IsEnabled && !OpenWorkspaceFolderButton.IsEnabled && !CopySavedPreviewButton.IsEnabled,
                "An active preview read exposes cancellation and blocks conflicting file actions");
            CancelSavedPreviewClicked(CancelSavedPreviewButton, new()); gate.Release.SetResult(new(new(config), new(auth), null)); await pending;
            Check(savedPreview is null && !readingSavedPreview && !CopySavedPreviewButton.IsEnabled && SavedPreviewStatus.Text.Contains("已取消"),
                "Cancelling an uncooperative preview read rejects its late result and restores the controls");
            savedConfigurationPreview = previewService;
            File.WriteAllText(configPath, "private='fake-malformed-preview'\nmodel=[broken");
            await ReloadWorkspaceAsync(); await ReadSavedConfigurationPreviewAsync();
            Check(savedPreview is { Config.State: ConfigurationPreviewState.Unreadable, Auth.State: ConfigurationPreviewState.Ready } && !SavedPreviewText.Text.Contains("fake-") && !CopySavedPreviewButton.IsEnabled,
                "A damaged workspace can preview readable authentication without exposing malformed TOML or enabling writes");
            SavedPreviewSectionInput.SelectedIndex = 1;
            Check(CopySavedPreviewButton.IsEnabled && !SavedPreviewText.Text.Contains("fake-"), "A failed configuration section does not disable another readable sanitized section");
            File.WriteAllBytes(configPath, config); await ReloadWorkspaceAsync();
            Check(savedPreview is null && SavedPreviewText.Text.Length == 0, "Workspace reload clears prior file preview content before the next read");
            await ReadSavedConfigurationPreviewAsync(); var missing = Path.Combine(output, "information-ui-missing");
            await SwitchHomeAsync(missing);
            Check(savedPreview is null && SavedPreviewText.Text.Length == 0 && !Directory.Exists(missing), "Changing directories clears the old preview without creating the new home");
            await ReadSavedConfigurationPreviewAsync(); OpenWorkspaceFolderClicked(OpenWorkspaceFolderButton, new()); CopyWorkspacePathClicked(CopyWorkspacePathButton, new());
            Check(savedPreview is { Config.State: ConfigurationPreviewState.Missing, Auth.State: ConfigurationPreviewState.Missing } && !CopySavedPreviewButton.IsEnabled && !Directory.Exists(missing) && smokeOpenedDirectories.Count == opened + 1 && smokeCopiedTexts.Last() == missing,
                "Missing home preview and explicit directory actions report absence and can copy its path without creating files");
        }
        finally
        {
            savedConfigurationPreview = previewService; smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary);
            LoadConnectionDraft(); await SwitchHomeAsync(previous); smokeDialog = null;
            HelpSearchInput.Text = ""; SavedPreviewSectionInput.SelectedIndex = 0; Root.RequestedTheme = ElementTheme.Light; NavigateTo(WorkspaceNav);
        }
    }
    private sealed class GatePreviewRuntime : IConfigurationPreviewRuntime
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<ConfigurationPreviewFiles> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ConfigurationPreviewFiles> ReadAsync(string home, CancellationToken cancellation) { Entered.TrySetResult(); return Release.Task; }
    }
}
