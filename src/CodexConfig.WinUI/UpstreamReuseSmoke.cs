using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private sealed class SmokeFeatureRuntime : IExperimentalFeatureRuntime
    {
        internal TaskCompletionSource? Gate;
        internal bool Unsupported;
        internal int Reads;
        public async Task<ExperimentalFeatureSnapshot> ReadAsync(ConfigurationSourceRequest request, CancellationToken cancellation)
        {
            Reads++; if (Gate is { } gate) await gate.Task.WaitAsync(cancellation);
            if (Unsupported) throw new NotSupportedException("隔离执行端不支持实验功能，状态未确认。");
            var document = new TomlDocument(File.Exists(Path.Combine(request.Home, "config.toml")) ? File.ReadAllBytes(Path.Combine(request.Home, "config.toml")) : null);
            var features = Enumerable.Range(0, 23).Select(index => new ExperimentalFeature("reuse_fixture_" + index, "beta", document.Get("features.reuse_fixture_" + index) as bool? ?? false, false, "隔离功能 " + index, "仅用于功能读取与草稿检查。"))
                .Append(new("reuse_removed", "removed", null, null, "", "" )).ToArray();
            return new(DateTimeOffset.UtcNow, "Offline feature fixture", features);
        }
    }
    private async Task RunUpstreamReuseSmokeAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        var previous = viewModel.Home; var previousFeatures = experimentalFeatures; var size = AppWindow.Size;
        var home = Path.Combine(output, "upstream-reuse-home"); Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='reuse-fixture-model'\nopenai_base_url='https://reuse-fixture.invalid/v1'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"chatgpt\",\"tokens\":{\"access_token\":\"fake-reuse-access\",\"id_token\":\"fake-reuse-id\",\"refresh_token\":\"fake-reuse-refresh\"}}");
        var runtime = new SmokeFeatureRuntime(); experimentalFeatures = new(runtime);
        try
        {
            await SwitchHomeAsync(home); var original = store.Load(home); NavigateTo(ConnectionNav);
            ConnectionPanel.ConnectionModeInput.SelectedIndex = 1; ConnectionPanel.ApiKeyInput.Password = "fake-reuse-upstream-key";
            Check(ConnectionPanel.GatewayWaitFields.Visibility == Visibility.Visible && ConnectionPanel.GatewayHeaderWaitInput.Text == "120", "Mixed connection exposes a bounded HTTP header wait with the default 120 seconds");
            ConnectionPanel.GatewayHeaderWaitInput.Text = "59";
            Check(!await SaveConnectionAsync() && connectionDraft.HasDraft && original.SameContents(store.Load(home)) && ErrorBar.Message.Contains("60"),
                "Invalid gateway header wait remains a draft and fails before a file write or desktop operation");
            ConnectionPanel.ConnectionModeInput.SelectedIndex = 0;
            Check(ConnectionPanel.GatewayWaitFields.Visibility == Visibility.Collapsed && connectionDraft.Request().Settings.Details.GatewayHeaderTimeoutSeconds == 120,
                "Switching to direct connection hides and ignores an invalid gateway wait draft");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(await SaveConnectionAsync() && store.ReadSettings(store.Load(home)).Mode == ConnectionMode.Direct &&
                ConnectionDetails.Read(store.Load(home).Details).GatewayHeaderTimeoutSeconds == 120,
                "Direct connection can be confirmed and saved while the obsolete gateway input is hidden");
            store.Restore(store.Load(home)); LoadWorkspace(home); original = store.Load(home);
            ConnectionPanel.ConnectionModeInput.SelectedIndex = 1; ConnectionPanel.ApiKeyInput.Password = "fake-reuse-upstream-key";
            ConnectionPanel.GatewayHeaderWaitInput.Text = "600"; smokeDialog = dialog =>
            {
                var text = ConfigurationDialogText(dialog); Check(text.Contains("600 秒") && text.Contains("HTTP") && !text.Contains("fake-reuse-upstream-key"),
                    "Gateway wait appears explicitly in the ordinary redacted save confirmation"); return Task.FromResult(ContentDialogResult.None);
            };
            Check(!await SaveConnectionAsync() && connectionDraft.GatewayHeaderTimeout == "600" && original.SameContents(store.Load(home)),
                "Cancelling wait changes preserves all connection draft fields and the original files");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(await SaveConnectionAsync() && MixedConnection.Read(store.Load(home).Bridge!).HeaderTimeoutSeconds == 600 && !connectionDraft.HasDraft,
                "Confirmed wait preference reaches the encrypted runtime connection without starting its gateway");
            var saved = store.Load(home); NavigateTo(SourcesNav); SourcesPanel.ExperimentalFeaturesExpander.IsExpanded = true; Root.UpdateLayout();
            Check(runtime.Reads == 0 && featureRead is null && SourcesPanel.ReadFeaturesButton.IsEnabled, "Navigating to feature support never launches a native read automatically");
            await ReadFeaturesAsync();
            Check(runtime.Reads == 1 && featureRead?.Snapshot.Features.Count == 24 && SourcesPanel.FeatureRows.Children.Count == 20 && SourcesPanel.NextFeatureButton.IsEnabled &&
                SourcesPanel.FeatureStatusText.Text.Contains("独立执行端") && SourcesPanel.FeatureStatusText.Text.Contains("不是当前桌面") && saved.SameContents(store.Load(home)),
                "Feature reading displays a bounded independent execution-end snapshot without writing saved configuration");
            featureDraftInputs["reuse_fixture_0"].SelectedItem = FeatureChoices.Single(choice => choice.Enabled == true);
            Check(connectionDraft.Document().Get("features.reuse_fixture_0") is true && connectionDraft.HasDraft && SourcesPanel.FeatureDraftButton.Visibility == Visibility.Visible &&
                saved.SameContents(store.Load(home)) && ApplicationFooter.Visibility == Visibility.Collapsed,
                "A feature choice stages the main draft and retains the source page's explicit save boundary");
            smokeDialog = _ => throw new InvalidOperationException("Source shortcut must not save a draft");
            await ExecuteWorkspaceShortcutAsync(Windows.System.VirtualKey.S, Windows.System.VirtualKeyModifiers.Control);
            Check(saved.SameContents(store.Load(home)) && connectionDraft.HasDraft, "Ctrl S on feature observations cannot save an unrelated main configuration draft");
            NextFeatureClicked(SourcesPanel.NextFeatureButton, new());
            Check(SourcesPanel.FeatureRows.Children.Count == 4 && SourcesPanel.PreviousFeatureButton.IsEnabled && !featureDraftInputs["reuse_removed"].IsEnabled,
                "Feature pagination retains drafts and disables shortcuts for removed or unconfirmed support stages");
            SourcesPanel.FeatureSearchInput.Text = "reuse_fixture_0"; RefreshView();
            Check(SourcesPanel.FeatureRows.Children.Count == 1 && featureDraftInputs["reuse_fixture_0"].SelectedItem is FeatureChoice { Enabled: true },
                "Feature filtering reflects the same unsaved override without resetting it");
            SourcesPanel.SourceProfileInput.Text = "daily"; await ReadFeaturesAsync();
            Check(featureRead?.Request.Profile == "daily" && featureDraftInputs.Values.All(input => !input.IsEnabled) && connectionDraft.HasDraft,
                "A Profile feature snapshot is read only and cannot silently stage changes into the main configuration");
            SourcesPanel.SourceProfileInput.Text = ""; SourcesPanel.FeatureSearchInput.Text = ""; await ReadFeaturesAsync();
            runtime.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); var pending = ReadFeaturesAsync();
            Check(readingFeatures && SourcesPanel.CancelFeaturesButton.IsEnabled && !ChooseHomeButton.IsEnabled && !SaveConnectionButton.IsEnabled && SourcesPanel.FeatureRows.Children.Count == 0,
                "A pending feature read clears its old rows exposes cancel and locks conflicting workspace actions");
            CancelFeaturesClicked(SourcesPanel.CancelFeaturesButton, new()); await pending; runtime.Gate = null;
            Check(featureRead is null && !readingFeatures && SourcesPanel.FeatureStatusText.Text.Contains("取消") && connectionDraft.HasDraft,
                "Cancelling feature reading rejects late observations and keeps the independent main draft");
            runtime.Unsupported = true; await ReadFeaturesAsync(); runtime.Unsupported = false;
            Check(featureRead is null && SourcesPanel.FeatureStatusText.Text.Contains("不支持") && SourcesPanel.FeatureRows.Children.Count == 0,
                "An unsupported feature endpoint remains explicitly unconfirmed instead of displaying an old success");
            await ReadFeaturesAsync(); NavigateTo(CatalogNav); smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(await SaveConnectionAsync() && featureRead is null && SourcesPanel.FeatureRows.Children.Count == 0,
                "Saving a staged feature uses the existing backup flow and invalidates prior feature observations");
            NavigateTo(SourcesNav); SourcesPanel.FeatureSearchInput.Text = "reuse_fixture_0"; await ReadFeaturesAsync();
            Check(featureRead?.Snapshot.Features.Single(feature => feature.Name == "reuse_fixture_0").Enabled == true && SourcesPanel.FeatureStatusText.Text.Contains("不是当前桌面"),
                "An explicit read after saving confirms the fresh execution-end value without claiming desktop adoption");
            Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(720, 540)); await Task.Delay(120); Root.UpdateLayout();
            SourcesPanel.FeatureSearchInput.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            await CaptureAsync(output, "upstream-features-compact-dark.png");
            Check(SourcesPanel.FeatureRows.ActualWidth > 240 && SourcesPanel.FeatureRows.TransformToVisual(Root).TransformPoint(new()).X + SourcesPanel.FeatureRows.ActualWidth <= Root.ActualWidth + 1,
                "Feature observation and draft controls fit the compact dark task page");
        }
        finally
        {
            runtime.Gate?.TrySetResult(); featureCancellation?.Cancel(); experimentalFeatures = previousFeatures;
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); LoadConnectionDraft(); await SwitchHomeAsync(previous); smokeDialog = null;
            Root.RequestedTheme = ElementTheme.Light; AppWindow.Resize(size); NavigateTo(WorkspaceNav);
        }
    }
}
