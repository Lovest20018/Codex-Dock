using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunOfficialLoginAndModelSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine);
        }
        void Choose(string path, string? literal) => behaviorChoices[path].SelectedItem =
            ((IReadOnlyList<BehaviorChoice>)behaviorChoices[path].ItemsSource).Single(option => option.Value == literal);
        var previousHome = viewModel.Home; var previousDiscovery = modelDiscovery;
        var home = Path.Combine(output, "login-model-home"); Directory.CreateDirectory(home);
        var config = Path.Combine(home, "config.toml"); var auth = Path.Combine(home, "auth.json");
        File.WriteAllText(config, "# login and model fixture\nmodel='main'\nopenai_base_url='https://model-ui.invalid/v1'\nsandbox_mode='read-only'\napproval_policy={ granular={ rules=true, sandbox_approval=true } }\n");
        File.WriteAllText(auth, "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-login-ui-key\"}");
        byte[] Login(string value) => JsonSerializer.SerializeToUtf8Bytes(new { auth_mode = "chatgpt", tokens = new { access_token = value, refresh_token = value, id_token = value } });
        try
        {
            await SwitchHomeAsync(home); NavigateTo(ModelsNav);
            var original = store.Load(home); var policy = ConfigSchema.Literal(connectionDraft.Document().Get("approval_policy"));
            Check(ModelsPanel.Visibility == Visibility.Visible && PageTitle.Text == "Codex 配置" && BehaviorModelInput.Text == "main" &&
                behaviorChoices["model_verbosity"].SelectedItem is BehaviorChoice { Value: null } && !connectionDraft.HasDraft,
                "Model and behavior page displays inherited defaults without creating a draft");
            Check(behaviorChoices["approval_policy"].SelectedItem is BehaviorChoice { Label: var label } && label.Contains("结构化规则") &&
                original.SameContents(store.Load(home)), "Structured approval rules are preserved as the current option without rewriting their fields");
            Choose("model_verbosity", "\"low\""); Choose("model_reasoning_effort", "\"high\"");
            BehaviorModelInput.Text = "other";
            Check(connectionDraft.Model == "other" && ConnectionModelSummary.Text.Contains("other") && connectionDraft.Edits.Count == 2 && original.SameContents(store.Load(home)),
                "Main model and behavior changes share one memory draft across connection and model pages");
            NavigateTo(ConnectionNav); NavigateTo(ModelsNav);
            Check(BehaviorModelInput.Text == "other" && behaviorChoices["model_verbosity"].SelectedItem is BehaviorChoice { Value: "\"low\"" },
                "Navigation retains behavior edits and the selected main model");
            await CaptureAsync(output, "models-light.png");
            smokeDialog = async dialog =>
            {
                var text = ConfigurationDialogText(dialog);
                Check(text.Contains("model_verbosity") && text.Contains("model_reasoning_effort") && !text.Contains("fake-login-ui-key") &&
                    !BehaviorModelInput.IsEnabled && !behaviorChoices["sandbox_mode"].IsEnabled, "Behavior save review lists changed settings masks credentials and locks editing");
                var shown = dialog.ShowAsync();
                try { await CaptureAsync(output, "models-save-review.png", dialog); }
                finally { dialog.Hide(); await shown; }
                return ContentDialogResult.None;
            };
            Check(!await SaveConnectionAsync() && connectionDraft.HasDraft && original.SameContents(store.Load(home)),
                "Returning from the rendered behavior review preserves all edits and original files");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(await SaveConnectionAsync() && !connectionDraft.HasDraft && new TomlDocument(store.Load(home).Config).String("model_verbosity") == "low" &&
                ConfigSchema.Literal(new TomlDocument(store.Load(home).Config).Get("approval_policy")) == policy,
                "Confirmed behavior save writes defaults while preserving structured approval rules and login");
            Choose("model_verbosity", null);
            Check(await SaveConnectionAsync() && new TomlDocument(store.Load(home).Config).Get("model_verbosity") is null &&
                behaviorChoices["model_verbosity"].SelectedItem is BehaviorChoice { Value: null }, "Choosing inheritance removes the saved local override and returns to a clean draft");

            DedicatedReviewerInput.IsChecked = true;
            var beforeCatalog = store.Load(home);
            Check(!await SaveConnectionAsync() && ErrorBar.IsOpen && connectionDraft.HasDraft && beforeCatalog.SameContents(store.Load(home)),
                "A dedicated reviewer cannot save without a valid native capability catalog");
            var catalog = Path.Combine(home, "imported-models.json");
            File.WriteAllText(catalog, "{\"data\":[{\"id\":\"main\"}]}"); StageModelCatalog(catalog);
            Check(ErrorBar.IsOpen && connectionDraft.SynchronizeCatalog && beforeCatalog.SameContents(store.Load(home)),
                "An ID-only catalog remains a visible draft error and cannot change saved capabilities");
            File.WriteAllText(catalog, JsonSerializer.Serialize(new { models = new[] { "main", "other", "codex-auto-review" }.Select(slug => new {
                slug, display_name = slug, supported_in_api = true, default_reasoning_level = "low", context_window = 128000,
                supported_reasoning_levels = new[] { new { effort = "low" }, new { effort = "max" } },
                service_tiers = new[] { new { id = "priority" } }, base_instructions = "Offline WinUI fixture"
            }) }));
            StageModelCatalog(catalog);
            Check(await SaveConnectionAsync() && ManagedModelCatalogStore.Inspect(store.Load(home)) is { Managed: true, ReviewModel: "codex-auto-review" } &&
                ConfigSchema.Literal(connectionDraft.Document().Get("approval_policy")) == policy && connectionDraft.Document().String("sandbox_mode") == "read-only",
                "Reviewed native catalog import pins the dedicated reviewer and preserves independent execution rules");
            var levels = (IReadOnlyList<BehaviorChoice>)behaviorChoices["model_reasoning_effort"].ItemsSource;
            Check(levels.Any(option => option.Value == "\"max\"") && levels.Any(option => option.Value == "\"high\"" && option.Label.Contains("保留")) &&
                ModelCapabilityStatus.Text.Contains("128,000"), "Native capabilities limit offered effort levels while retaining an existing unlisted value");
            Choose("model_reasoning_effort", "\"max\"");
            Check(await SaveConnectionAsync(), "Capability-backed behavior changes use the same reviewed save path");
            Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(900, 760)); PageScroll.ChangeView(null, 0, null, true);
            await CaptureAsync(output, "models-compact-dark.png");
            Check(BehaviorModelInput.ActualWidth > 250 && BehaviorModelInput.TransformToVisual(Root).TransformPoint(new()).X + BehaviorModelInput.ActualWidth <= Root.ActualWidth &&
                behaviorChoices.Values.All(field => field.ActualWidth > 200), "Model and approval controls fit the compact desktop layout with scrolling");
            PageScroll.ChangeView(null, double.MaxValue, null, true); await CaptureAsync(output, "models-reviewer-dark.png"); Root.RequestedTheme = ElementTheme.Light;

            home = Path.Combine(output, "official-login-home"); Directory.CreateDirectory(home);
            config = Path.Combine(home, "config.toml"); auth = Path.Combine(home, "auth.json");
            File.WriteAllText(config, "# official login fixture\nmodel='main'\nopenai_base_url='https://model-ui.invalid/v1'\n");
            File.WriteAllText(auth, "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-login-ui-key\"}");
            await SwitchHomeAsync(home); NavigateTo(ConnectionNav); ConnectionModeInput.SelectedIndex = 1;
            BehaviorModelInput.Text = "other"; ConnectionNameInput.Text = "keep-login-draft";
            await ReadOfficialLoginAsync(false);
            Check(ErrorBar.IsOpen && !connectionDraft.HasOfficialLogin && ConnectionNameInput.Text == "keep-login-draft" && beforeCatalog.Auth!.SequenceEqual(File.ReadAllBytes(auth)),
                "Missing official login shows native desktop instructions and preserves the full connection draft");
            var official = Login("fake-current-login"); File.WriteAllBytes(auth, official);
            await ReadOfficialLoginAsync(false);
            Check(connectionDraft.HasCurrentOfficialLogin && !connectionDraft.HasStagedOfficialLogin && !RestoreOfficialLoginButton.IsEnabled &&
                connectionDraft.ApiKey == "fake-login-ui-key" && ConnectionNameInput.Text == "keep-login-draft" && viewModel.Snapshot!.Auth!.SequenceEqual(official),
                "Reading a native login rebases observed authentication while preserving the service key model and name draft");
            Check(await SaveConnectionAsync() && OfficialCredentials.HasLogin(store.Load(home).Auth), "Mixed-mode save adopts the observed official identity only after review");
            var clean = store.Load(home); await ReadOfficialLoginAsync(false);
            Check(!connectionDraft.HasDraft && clean.SameContents(store.Load(home)), "Re-reading an unchanged native login stays clean without an extra save");
            ConnectionModeInput.SelectedIndex = 0; Check(await SaveConnectionAsync(), "Explicit direct-mode save retains the previous login in encrypted backup");
            var direct = store.Load(home); ConnectionModeInput.SelectedIndex = 1; await ReadOfficialLoginAsync(true);
            Check(connectionDraft.HasStagedOfficialLogin && OfficialLoginStatus.Text.Contains("加密备份") && direct.SameContents(store.Load(home)),
                "Explicit official backup selection stages credentials without restoring them to disk");
            PageScroll.ChangeView(null, 0, null, true); await CaptureAsync(output, "official-login-backup.png");
            ConnectionModeInput.SelectedIndex = 0; Check(connectionDraft.Request().OfficialAuth is null, "Returning to direct mode omits the staged login from every save request");
            LoadConnectionDraft(); viewModel.Session.MarkDraft(false);
            Check(!connectionDraft.HasStagedOfficialLogin && direct.SameContents(store.Load(home)), "Discarding the login draft clears its staged credentials without writing files");

            var selectedModel = connectionDraft.Model; var calls = 0;
            modelDiscovery = new((settings, _) => { calls++; return Task.FromResult(new ModelCatalogResult(["main", "other", "main"], 2)); });
            await DiscoverModelsAsync();
            Check(calls == 1 && discoveredModels.SequenceEqual(new[] { "main", "other" }) && connectionDraft.Model == selectedModel && !connectionDraft.HasDraft && direct.SameContents(store.Load(home)),
                "Service model discovery makes one read and retains the saved model without writes or automatic selection");
            DiscoveredModelsInput.SelectedItem = selectedModel == "main" ? "other" : "main";
            Check(connectionDraft.HasDraft && BehaviorModelInput.Text == connectionDraft.Model && direct.SameContents(store.Load(home)),
                "Choosing a discovered model stages the shared model draft without writing configuration");
            BaseUrlInput.Text = "https://new-model-ui.invalid/v1";
            Check(discoveredModels.Count == 0 && DiscoveredModelsInput.Visibility == Visibility.Collapsed, "Changing service connection clears results from the previous endpoint");
            DiscardConnectionClicked(DiscardConnectionButton, new());
            var started = new TaskCompletionSource();
            modelDiscovery = new(async (_, cancellation) => { started.SetResult(); await Task.Delay(Timeout.Infinite, cancellation); return new([], 0); });
            var pending = DiscoverModelsAsync(); await started.Task;
            Check(!BaseUrlInput.IsEnabled && !ChooseHomeButton.IsEnabled && CancelDiscoveryButton.IsEnabled && !BehaviorModelInput.IsEnabled,
                "A pending model read locks connection workspace and behavior changes while keeping cancellation available");
            CancelDiscoveryClicked(CancelDiscoveryButton, new()); await pending;
            Check(!discoveringModels && BaseUrlInput.IsEnabled && ModelDiscoveryStatus.Text.Contains("取消") && !connectionDraft.HasDraft && direct.SameContents(store.Load(home)),
                "Cancelling discovery restores controls and retains the saved selection and files");
            modelDiscovery = new((_, _) => throw new HttpRequestException("Offline model fixture error")); await DiscoverModelsAsync();
            Check(ErrorBar.IsOpen && BaseUrlInput.IsEnabled && connectionDraft.Model == selectedModel && direct.SameContents(store.Load(home)),
                "Model discovery failure is recoverable and preserves the current model and files");
            modelDiscovery = new((_, _) => { connectionDraft.ApiKey = "fake-key-changed-during-read"; return Task.FromResult(new ModelCatalogResult(["stale-result"], 1)); });
            await DiscoverModelsAsync();
            Check(discoveredModels.Count == 0 && ModelDiscoveryStatus.Text.Contains("连接设置") && direct.SameContents(store.Load(home)),
                "A result for superseded credentials is discarded and cannot populate the model selector");
            DiscardConnectionClicked(DiscardConnectionButton, new());
        }
        finally
        {
            modelDiscovery = previousDiscovery; modelDiscoveryCancellation?.Cancel();
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary);
            LoadConnectionDraft(); viewModel.Session.MarkDraft(false); await SwitchHomeAsync(previousHome);
            smokeDialog = null; Root.RequestedTheme = ElementTheme.Light; NavigateTo(WorkspaceNav);
        }
    }
}
