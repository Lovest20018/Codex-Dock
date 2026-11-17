using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private sealed class SmokeSourceRuntime(Func<ConfigurationSourceRequest, byte[]?, CancellationToken, Task<ConfigurationReadResult>> read) : IConfigurationSourceRuntime
    { public Task<ConfigurationReadResult> ReadAsync(ConfigurationSourceRequest request, byte[]? config, CancellationToken cancellation) => read(request, config, cancellation); }
    private async Task RunWindowsToolsSmokeAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        ConfigurationReadResult Source(ConfigurationSourceRequest request, byte[]? bytes)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { config = new { model = "fixture-effective-model", future_items = new[] { new { token = "fake-private-source-response" } },
                fixture = Enumerable.Range(0, 25).ToDictionary(index => "value" + index, index => index) }, layers = new[] { new { name = new { type = "project", dotCodexFolder = "fixture-project/.codex" }, disabledReason = "untrusted fixture" } } }));
            return ConfigReadClient.Parse(json.RootElement, bytes, request.Executable, request.Project, request.Profile) with { RuntimeInfo = "Offline fixture kernel" };
        }
        var previousHome = viewModel.Home; var previousSources = configurationSources;
        var home = Path.Combine(output, "windows-tools-home"); Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "# windows tools fixture\nmodel='saved-main-model'\nopenai_base_url='https://tools-ui.invalid/v1'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-tools-ui-key\"}");
        var original = store.Load(home); var nativeStartup = new WindowsStartupRegistration().Read();
        try
        {
            var shortHome = Path.Combine(Path.GetPathRoot(home)!, "Dock startup fixture"); await SwitchHomeAsync(shortHome); NavigateTo(SettingsNav);
            Check(SettingsPanel.Visibility == Visibility.Visible && !StartupToggle.IsOn && startupRegistration.Read() is null,
                "Windows settings reads startup registration without creating a Run entry");
            await SetStartupAsync(true);
            Check(StartupToggle.IsOn && startupRegistration.Read() == StartupCommand.Create(Environment.ProcessPath!, shortHome) && !Directory.Exists(shortHome),
                "Startup enable binds the current program and selected home without creating model configuration");
            await SetStartupAsync(false); Check(startupRegistration.Read() is null && !StartupToggle.IsOn, "Startup disable removes only the currently matched in-memory registration");
            startupRegistration.Write("foreign fixture registration"); ReadStartupSettings(); RefreshView();
            smokeDialog = _ => Task.FromResult(ContentDialogResult.None); await SetStartupAsync(true);
            Check(startupRegistration.Read() == "foreign fixture registration" && !StartupToggle.IsOn, "Cancelling startup replacement preserves another program or directory registration");
            smokeDialog = _ => { startupRegistration.Write("newer external fixture registration"); return Task.FromResult(ContentDialogResult.Primary); };
            await SetStartupAsync(true);
            Check(startupRegistration.Read() == "newer external fixture registration" && StartupStatusText.Text.Contains("其他窗口"),
                "Startup replacement rechecks its observed registration after confirmation and rejects concurrent writes");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary); await SetStartupAsync(true); await SetStartupAsync(false);
            Check(startupRegistration.Read() is null && new WindowsStartupRegistration().Read() == nativeStartup,
                "Confirmed startup replacement and removal leave the user's actual Windows Run entry untouched");
            await CaptureAsync(output, "windows-startup-light.png");
            await SwitchHomeAsync(home); connectionDraft.Model = "unsaved-main-model";
            var profilePath = ProfileStore.FilePath(home, "daily"); File.WriteAllText(profilePath, "# profile fixture retained\nmodel='saved-profile-model'\nmodel_reasoning_effort='low'\nfuture_items=[{token='fake-private-profile'}]\nsandbox_mode='read-only'\n");
            var profileBefore = File.ReadAllBytes(profilePath);
            NavigateTo(ProfilesNav); Check(await LoadProfileAsync("daily"), "Independent profile can be read without applying or discarding the main configuration draft");
            Check(ProfilesPanel.Visibility == Visibility.Visible && ProfileFooter.Visibility == Visibility.Visible && ApplicationFooter.Visibility == Visibility.Collapsed &&
                !ProfileDocumentInput.Text.Contains("fake-private-profile") && !ProfileDocumentInput.Text.Contains("sandbox_mode") && profileDraft.PreservedFields == 2 &&
                connectionDraft.Model == "unsaved-main-model" && original.SameContents(store.Load(home)), "Profile editor exposes only ordinary values and preserves private fields and the separate main draft");
            ProfileDocumentInput.Text = "model=[broken";
            Check(!await SaveProfileAsync(false) && profileDraft.HasDraft && ProfileStatusText.Text.Contains("校验") && profileBefore.SequenceEqual(File.ReadAllBytes(profilePath)),
                "Invalid Profile TOML remains editable and cannot create a backup or replace its file");
            ProfileDocumentInput.Text = "model='chosen-profile-model'\nmodel_reasoning_effort='high'\n";
            smokeDialog = async dialog =>
            {
                var text = ConfigurationDialogText(dialog);
                Check(text.Contains(profilePath) && text.Contains("model_reasoning_effort") && !text.Contains("fake-private-profile") && !text.Contains("fake-tools-ui-key") && !BaseUrlInput.IsEnabled && !ChooseHomeButton.IsEnabled,
                    "Profile review names only its own target hides private fields and locks competing changes");
                var shown = dialog.ShowAsync(); try { await CaptureAsync(output, "profile-save-review.png", dialog); } finally { dialog.Hide(); await shown; }
                return ContentDialogResult.None;
            };
            Check(!await SaveProfileAsync(false) && profileDraft.HasDraft && profileBefore.SequenceEqual(File.ReadAllBytes(profilePath)), "Cancelling Profile review preserves its raw draft and original file");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(await SaveProfileAsync(false) && !profileDraft.HasDraft && connectionDraft.Model == "unsaved-main-model" && connectionDraft.HasDraft &&
                File.ReadAllText(profilePath).Contains("fake-private-profile") && File.ReadAllText(profilePath).Contains("# profile fixture retained") &&
                original.SameContents(store.Load(home)), "Confirmed Profile save backs up and changes only its own file while retaining the main draft and protected content");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.None);
            var profileSaved = File.ReadAllBytes(profilePath); Check(!await SaveProfileAsync(true) && profileSaved.SequenceEqual(File.ReadAllBytes(profilePath)), "Cancelling Profile recovery leaves the saved file intact");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(await SaveProfileAsync(true) && profileBefore.SequenceEqual(File.ReadAllBytes(profilePath)) && connectionDraft.Model == "unsaved-main-model", "Confirmed Profile recovery restores its encrypted backup without changing main configuration");
            ProfileDocumentInput.Text = "model='keep-my-profile-draft'\n"; NavigateTo(SourcesNav); NavigateTo(ProfilesNav);
            Check(profileDraft.HasDraft && ProfileDocumentInput.Text.Contains("keep-my-profile-draft"), "Navigation retains the independent Profile draft");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.None);
            Check(!await SwitchHomeAsync(Path.Combine(output, "cancel-profile-home")) && viewModel.Home == home && !await StopBeforeCloseAsync(), "Cancelling home switch or explicit exit preserves an unsaved Profile");
            await RequestEnhancedLaunchAsync(home); Check(profileDraft.HasDraft && original.SameContents(store.Load(home)), "Enhanced launch also respects unsaved Profile changes");
            smokeDialog = _ => { profileDraft.Text = "model='changed-during-review'\n"; return Task.FromResult(ContentDialogResult.Primary); };
            Check(!await SaveProfileAsync(false) && profileBefore.SequenceEqual(File.ReadAllBytes(profilePath)) && profileDraft.Text.Contains("changed-during-review"), "A Profile draft changed during confirmation invalidates the reviewed write");
            DiscardProfileClicked(DiscardProfileButton, new());
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary); ProfileDocumentInput.Text = "model='conflicting-profile'\n";
            File.AppendAllText(profilePath, "# native profile edit\n"); var externalProfile = File.ReadAllBytes(profilePath);
            Check(!await SaveProfileAsync(false) && externalProfile.SequenceEqual(File.ReadAllBytes(profilePath)) && profileDraft.HasDraft, "External Profile file changes block a reviewed overwrite and retain the local draft");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); await LoadProfileAsync("daily");
            Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(900, 760)); Root.UpdateLayout();
            var profileOffset = ProfileDocumentInput.TransformToVisual(PageScroll).TransformPoint(new()).Y + PageScroll.VerticalOffset;
            PageScroll.ChangeView(null, Math.Max(0, profileOffset - 120), null, true); await CaptureAsync(output, "profile-compact-dark.png");
            var savePosition = SaveProfileButton.TransformToVisual(Root).TransformPoint(new());
            Check(savePosition.Y >= 0 && savePosition.Y + SaveProfileButton.ActualHeight <= Root.ActualHeight && ProfileDocumentInput.ActualWidth > 250,
                "Independent Profile save and recovery actions remain accessible in the compact Windows layout"); Root.RequestedTheme = ElementTheme.Light;

            NavigateTo(SourcesNav); var requests = 0;
            configurationSources = new(new SmokeSourceRuntime((request, bytes, _) => { requests++; return Task.FromResult(Source(request, bytes)); }));
            SourceExecutableInput.Text = Path.Combine(output, "fixture-codex.exe"); SourceProjectInput.Text = home; SourceProfileInput.Text = "";
            await ReadConfigurationSourcesAsync();
            Check(requests == 1 && sourceResult is not null && sourceResult.Rows.Single(row => row.Path == "model").FileValue.Contains("saved-main-model") &&
                connectionDraft.Model == "unsaved-main-model" && original.SameContents(store.Load(home)), "Configuration sources compare saved main settings without committing or reading the memory draft");
            Check(!JsonSerializer.Serialize(sourceResult).Contains("fake-private-source-response") && SourceLayersText.Text.Contains("未采用") && SourceRows.Children.Count <= 20,
                "Source results redact nested credentials identify inactive project layers and bound visible rows");
            NextSourceClicked(NextSourceButton, new()); Check(sourcePage == 1 && SourceRows.Children.Count > 0 && SourceRows.Children.Count <= 20, "Source results support bounded paging");
            SourceSearchInput.Text = "model"; Check(SourceRows.Children.Count == 1, "Source filtering narrows the snapshot without another native read");
            Root.UpdateLayout(); PageScroll.ChangeView(null, double.MaxValue, null, true); await CaptureAsync(output, "configuration-sources-light.png");
            SourceProfileInput.Text = "daily"; Check(sourceResult is null && SourceRows.Children.Count == 0, "Changing source read conditions immediately clears the older snapshot");
            await ReadConfigurationSourcesAsync(); Check(sourceResult?.Rows.Single(row => row.Path == "model").FileValue.Contains("saved-profile-model") == true && profileDraft.Snapshot?.Name == "daily",
                "Reading Profile sources compares its saved file without changing the selected main model");
            var started = new TaskCompletionSource(); configurationSources = new(new SmokeSourceRuntime(async (request, bytes, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return Source(request, bytes); }));
            var reading = ReadConfigurationSourcesAsync(); await started.Task;
            Check(!ChooseHomeButton.IsEnabled && !SaveProfileButton.IsEnabled && !StartupToggle.IsEnabled && CancelSourceButton.IsEnabled, "A pending local source read serializes conflicting UI actions and keeps cancellation available");
            CancelSourceClicked(CancelSourceButton, new()); await reading;
            Check(sourceResult is null && SourceStatusText.Text.Contains("取消") && ReadSourceButton.IsEnabled && original.SameContents(store.Load(home)), "Cancelling local source reading restores controls without changing either configuration draft");
            configurationSources = new(new SmokeSourceRuntime((_, _, _) => throw new NotSupportedException("Fixture profile source unsupported")));
            await ReadConfigurationSourcesAsync(); Check(sourceResult is null && SourceStatusText.Text.Contains("unsupported"), "Unsupported native Profile source reading remains an explicit unknown state");
            configurationSources = new(new SmokeSourceRuntime((request, bytes, _) => { File.AppendAllText(profilePath, "# changed during source read\n"); return Task.FromResult(Source(request, bytes)); }));
            await ReadConfigurationSourcesAsync(); Check(sourceResult is null && SourceStatusText.Text.Contains("比较文件"), "A source result arriving after its comparison file changed is discarded");
            SourceProfileInput.Text = ""; configurationSources = new(new SmokeSourceRuntime((request, bytes, _) => Task.FromResult(Source(request, bytes))));
            await ReadConfigurationSourcesAsync();
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); Check(await SwitchHomeAsync(Path.Combine(output, "new-tools-home")) && sourceResult is null && profileDraft.Snapshot is null && !profileDraft.HasDraft,
                "Changing home clears old Profile and configuration-source state after resolving its drafts");
            Check(new WindowsStartupRegistration().Read() == nativeStartup, "All Windows settings acceptance checks leave the actual startup registration unchanged");
        }
        finally
        {
            configurationSources = previousSources; sourceCancellation?.Cancel(); profileDraft.Load(null);
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); LoadConnectionDraft(); viewModel.Session.MarkDraft(false); await SwitchHomeAsync(previousHome);
            smokeDialog = null; Root.RequestedTheme = ElementTheme.Light; NavigateTo(WorkspaceNav);
        }
    }
}
