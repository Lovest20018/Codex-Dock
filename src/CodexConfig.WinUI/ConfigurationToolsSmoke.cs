using System.Text;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunConfigurationToolsSmokeAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        void Filter(string scope, string search = "")
        {
            CatalogPanel.CatalogScopeInput.SelectedItem = ((IReadOnlyList<CatalogScope>)CatalogPanel.CatalogScopeInput.ItemsSource).Single(item => item.Id == scope);
            CatalogPanel.CatalogSearchInput.Text = search; catalogSearchTimer.Stop(); catalogPageIndex = 0; RenderConfigurationCatalog(); RefreshView();
        }
        void SetText(string path, string text) => ((TextBox)catalogInputs[path]).Text = text;
        var previousHome = viewModel.Home; var home = Path.Combine(output, "configuration-tools-home"); Directory.CreateDirectory(home);
        var config = Path.Combine(home, "config.toml"); var auth = Path.Combine(home, "auth.json");
        File.WriteAllText(config, "# configuration tools fixture\nmodel='main'\nopenai_base_url='https://tools-ui.invalid/v1'\nmodel_verbosity='low'\nmodel_reasoning_effort='low'\nfuture_setting=17\nfuture_parent={child=0}\n[mcp_servers.fixture]\ncommand='fixture-command'\n[mcp_servers.fixture.env]\nPRIVATE_KEY='fake-secret-tools-ui'\n");
        File.WriteAllText(auth, "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-tools-ui-key\"}");
        var libraryDirectory = Path.Combine(output, "template-library"); var libraryPath = Path.Combine(libraryDirectory, "templates.json");
        var runtime = (SmokeDesktopApplyRuntime)applicationRuntime; var restarts = runtime.Restarts;
        try
        {
            await SwitchHomeAsync(home); NavigateTo(CatalogNav); var original = store.Load(home);
            Check(CatalogPanel.Visibility == Visibility.Visible && PageTitle.Text == CatalogNav.Title && catalogInputs.ContainsKey("future_setting") && !connectionDraft.HasDraft && original.SameContents(store.Load(home)),
                "Full configuration opens configured and unknown fields without writing files or creating a draft");
            Filter("all"); Check(catalogPage is { Pages: > 1 } && CatalogPanel.CatalogRows.Children.Count <= ConfigurationCatalogService.PageSize,
                "Full schema rendering is bounded to a paginated set of configuration controls");
            var firstPagePaths = catalogPage!.Entries.Select(entry => entry.Path).ToArray(); NextCatalogPageClicked(CatalogPanel.NextCatalogPageButton, new());
            Check(catalogPage?.Page == 1 && CatalogPanel.CatalogRows.Children.Count <= ConfigurationCatalogService.PageSize && firstPagePaths.All(path => catalogPage.Entries.All(entry => entry.Path != path)),
                "Configuration paging replaces visible rows without altering drafts");
            Filter("all", "model_reasoning_effort"); SetText("model_reasoning_effort", "[unfinished");
            Check(connectionDraft.FieldErrors.Count == 1 && connectionDraft.HasDraft && CatalogPanel.CatalogErrors.Visibility == Visibility.Visible && SaveConnectionButton.IsEnabled,
                "Invalid raw field text stays visible in the shared draft with a row error");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(!await SaveConnectionAsync() && ErrorBar.IsOpen && original.SameContents(store.Load(home)) && store.LatestBackup(home) is null,
                "Invalid configuration text prevents every save before confirmation or backup creation");
            Filter("configured"); NavigateTo(ModelsNav); NavigateTo(CatalogNav); Filter("changed");
            Check(((TextBox)catalogInputs["model_reasoning_effort"]).Text == "[unfinished", "Filtering paging and navigation preserve invalid configuration input for correction");
            SetText("model_reasoning_effort", "'high'");
            NavigateTo(ModelsNav);
            Check(connectionDraft.FieldErrors.Count == 0 && behaviorChoices["model_reasoning_effort"].SelectedItem is BehaviorChoice { Value: "\"high\"" },
                "Correcting a raw configuration field synchronizes the common model page through the same draft");
            NavigateTo(CatalogNav);
            CatalogPanel.NewSettingPathInput.Text = "mcp_servers.'with.dot'.command"; CatalogPanel.NewSettingValueInput.Password = "'fixture-second-command'"; AddSettingClicked(CatalogPanel.AddSettingButton, new()); catalogSearchTimer.Stop();
            Check(connectionDraft.Document().Get("mcp_servers.'with.dot'.command") is "fixture-second-command" && original.SameContents(store.Load(home)),
                "Adding a quoted dynamic configuration path stages a schema-validated value without running its command");
            connectionDraft.EditField("future_parent.child", "42"); RemoveCatalogOverride("future_parent");
            Check(ErrorBar.IsOpen && connectionDraft.Document().Get("future_parent.child") is 42L && original.SameContents(store.Load(home)),
                "Removing a parent with a conflicting child draft reports a recoverable row action error and preserves the child");
            connectionDraft.UndoField("future_parent.child");
            Filter("configured", "PRIVATE_KEY");
            Check(catalogInputs.Values.Single() is PasswordBox { PasswordRevealMode: PasswordRevealMode.Peek }, "Sensitive nested environment values remain masked in the full configuration editor");
            Filter("changed"); await CaptureAsync(output, "configuration-catalog-light.png"); Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(900, 760)); Root.UpdateLayout();
            var rowsOffset = CatalogPanel.CatalogRows.TransformToVisual(PageScroll).TransformPoint(new()).Y + PageScroll.VerticalOffset;
            PageScroll.ChangeView(null, Math.Max(0, rowsOffset - 16), null, true);
            await CaptureAsync(output, "configuration-fields-dark.png");
            Check(CatalogPanel.CatalogRows.ActualWidth > 260 && CatalogPanel.CatalogRows.TransformToVisual(Root).TransformPoint(new()).X + CatalogPanel.CatalogRows.ActualWidth <= Root.ActualWidth,
                "Full configuration fields fit the compact desktop viewport and use the fixed save footer");
            Root.RequestedTheme = ElementTheme.Light;
            smokeDialog = async dialog =>
            {
                var text = ConfigurationDialogText(dialog);
                Check(text.Contains("model_reasoning_effort") && text.Contains("with.dot") && !text.Contains("fake-secret-tools-ui") && !text.Contains("fake-tools-ui-key"),
                    "The shared save review includes advanced fields while hiding nested secrets and authentication");
                var shown = dialog.ShowAsync(); try { await CaptureAsync(output, "configuration-fields-review.png", dialog); } finally { dialog.Hide(); await shown; }
                return ContentDialogResult.None;
            };
            Check(!await SaveConnectionAsync() && connectionDraft.HasDraft && original.SameContents(store.Load(home)), "Cancelling an advanced-field save preserves the complete draft and original files");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(await SaveConnectionAsync() && !connectionDraft.HasDraft && new TomlDocument(store.Load(home).Config).Get("mcp_servers.'with.dot'.command") is "fixture-second-command" && runtime.Restarts == restarts,
                "Confirmed advanced configuration save creates an encrypted backup without executing commands or restarting Codex");
            NavigateTo(RecoveryNav);
            var advancedSaved = store.Load(home); var backupPath = store.LatestBackup(home);
            smokeDialog = async dialog =>
            {
                var text = ConfigurationDialogText(dialog);
                Check(text.Contains("确认恢复") && text.Contains(backupPath!) && !text.Contains("fake-secret-tools-ui") && !text.Contains("fake-tools-ui-key") && !ChooseHomeButton.IsEnabled && !CanEditConnection,
                    "Recovery review identifies its encrypted source and exact targets hides credentials and locks editing");
                var shown = dialog.ShowAsync(); try { await CaptureAsync(output, "configuration-recovery-review.png", dialog); } finally { dialog.Hide(); await shown; }
                return ContentDialogResult.None;
            };
            Check(!await RestoreConfigurationAsync() && advancedSaved.SameContents(store.Load(home)) && store.LatestBackup(home) == backupPath,
                "Cancelling the rendered recovery review leaves all configuration files and backup records untouched");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(await RestoreConfigurationAsync() && original.SameContents(store.Load(home)) && store.LatestBackup(home) != backupPath && runtime.Restarts == restarts && ApplyButton.IsEnabled,
                "Confirmed restore returns the prior configuration backs up the current state and waits for separate desktop application");

            NavigateTo(TemplatesNav);
            Check(TemplatesPanel.Visibility == Visibility.Visible && templateLibrarySnapshot?.Templates.Count == BuiltInTemplates.Defaults.Count && !Directory.Exists(libraryDirectory) && original.SameContents(store.Load(home)),
                "Opening the shared template library loads defaults without creating a library file or changing configuration");
            connectionDraft.SetOverride("model_reasoning_effort", "'max'"); RenderTemplateDetails(BuiltInTemplates.Daily);
            Check(templateChoices["model_reasoning_effort"].IsChecked == false, "Template differences preserve an existing manual draft by default");
            templateChoices["model_reasoning_effort"].IsChecked = true; StageSelectedTemplate();
            Check(connectionDraft.Document().String("model_reasoning_effort") == "high" && original.SameContents(store.Load(home)), "Explicitly selecting a template difference changes only the memory draft");
            connectionDraft.SetOverride("model_reasoning_effort", "'ultra'"); UndoTemplateClicked(TemplatesPanel.UndoTemplateButton, new());
            Check(connectionDraft.Document().String("model_reasoning_effort") == "ultra" && TemplatesPanel.TemplateStatusText.Text.Contains("保留"), "Undoing a staged template preserves later manual field changes");
            DiscardConnectionClicked(DiscardConnectionButton, new()); RenderTemplateDetails(BuiltInTemplates.Daily); StageSelectedTemplate();
            Check(await SaveConnectionAsync() && new TomlDocument(store.Load(home).Config).String("model_reasoning_effort") == "high" && !Directory.Exists(libraryDirectory),
                "A chosen built-in recipe uses the same configuration review and save without writing the template library");
            var beforeLibrary = store.Load(home); RenderTemplateList(); NewTemplateClicked(TemplatesPanel.NewTemplateButton, new()); TemplatesPanel.TemplateNameInput.Text = "UI custom recipe";
            templateRows[0].Path.Text = "model_verbosity"; templateRows[0].Value.Text = "[broken";
            Check(!SaveTemplateEdit() && TemplatesPanel.TemplateEditError.Text.Length > 0 && HasTemplateDraft && !File.Exists(libraryPath) && beforeLibrary.SameContents(store.Load(home)),
                "Invalid template values remain in the template editor and cannot create a library or write Codex configuration");
            templateRows[0].Value.Text = "'high'"; Check(SaveTemplateEdit() && File.Exists(libraryPath) && beforeLibrary.SameContents(store.Load(home)),
                "Saving a custom template updates only the revision-checked local library");
            var custom = selectedTemplate!; var libraryBytes = File.ReadAllBytes(libraryPath);
            Check(!Encoding.UTF8.GetString(libraryBytes).Contains("fake-secret-tools-ui") && !Encoding.UTF8.GetString(libraryBytes).Contains("fake-tools-ui-key"), "The persisted template library contains no connection credentials");
            await CaptureAsync(output, "templates-differences-light.png");
            BeginTemplateEdit(custom, false); TemplatesPanel.TemplateNameInput.Text = "Unsaved edited template";
            NavigateTo(CatalogNav); NavigateTo(TemplatesNav);
            Check(editingTemplate?.Id == custom.Id && TemplatesPanel.TemplateNameInput.Text == "Unsaved edited template" && HasTemplateDraft,
                "Navigating away from template editing retains its separate unsaved library draft");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.None);
            Check(!await SwitchHomeAsync(Path.Combine(output, "cancel-template-home")) && viewModel.Home == home && HasTemplateDraft && !await StopBeforeCloseAsync(),
                "Cancelling home switch or explicit exit cannot discard an unsaved template edit");
            await RequestEnhancedLaunchAsync(home); Check(HasTemplateDraft && runtime.Restarts == restarts && libraryBytes.SequenceEqual(File.ReadAllBytes(libraryPath)),
                "Enhanced launch also respects unsaved template changes without touching the library or desktop");
            Root.RequestedTheme = ElementTheme.Dark; PageScroll.ChangeView(null, 0, null, true); await CaptureAsync(output, "templates-edit-compact-dark.png");
            var templateSavePosition = SaveTemplateButton.TransformToVisual(Root).TransformPoint(new());
            Check(TemplateEditFooter.Visibility == Visibility.Visible && ApplicationFooter.Visibility == Visibility.Collapsed && templateSavePosition.Y >= 0 &&
                templateSavePosition.Y + SaveTemplateButton.ActualHeight <= Root.ActualHeight, "Template editing keeps its own save action fixed and clearly separate from desktop application"); Root.RequestedTheme = ElementTheme.Light;
            var externalRepository = new TemplateRepository(libraryDirectory);
            externalRepository.Save(TemplateRepository.New("External fixture recipe", [new("analytics.enabled", "false")]), true); var externalBytes = File.ReadAllBytes(libraryPath);
            Check(!SaveTemplateEdit() && TemplatesPanel.TemplateEditError.Text.Contains("其他窗口") && HasTemplateDraft && externalBytes.SequenceEqual(File.ReadAllBytes(libraryPath)),
                "A newer library written by another window blocks a stale template save and preserves both versions for review");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); Check(await ResolveTemplateEditAsync(), "Explicitly discarding the stale editor allows the current library to be reloaded"); ReadTemplateLibrary();
            var count = templateLibrarySnapshot!.Templates.Count;
            BeginTemplateEdit(TemplateRepository.New("Copied fixture recipe", custom.Settings), true); Check(SaveTemplateEdit() && templateLibrarySnapshot!.Templates.Count == count + 1,
                "Copying a template saves a separate identity while retaining all other library entries");
            var copy = selectedTemplate!; RenderTemplateList();
            smokeDialog = _ => Task.FromResult(ContentDialogResult.None); var beforeDelete = File.ReadAllBytes(libraryPath); await DeleteTemplateAsync(copy);
            Check(beforeDelete.SequenceEqual(File.ReadAllBytes(libraryPath)), "Cancelling template deletion leaves the complete library unchanged");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary); await DeleteTemplateAsync(copy);
            Check(templateLibrarySnapshot!.Templates.All(item => item.Id != copy.Id) && templateLibrarySnapshot.Templates.Any(item => item.Id == custom.Id), "Confirmed template deletion preserves every other custom recipe");
            connectionDraft.SetOverride("analytics.enabled", "false"); CaptureTemplateClicked(TemplatesPanel.CaptureTemplateButton, new());
            Check(templateRows.All(row => TemplateRepository.CanInclude(row.Path.Text) && !row.Value.Text.Contains("fake-secret") && row.Path.Text != "model") &&
                templateRows.Any(row => row.Path.Text == "analytics.enabled"), "Capturing current settings includes ordinary drafts and filters model identity and nested private data");
            TemplatesPanel.TemplateNameInput.Text = "Captured UI fixture"; Check(SaveTemplateEdit() && beforeLibrary.SameContents(store.Load(home)), "Saving a captured template preserves the independent unsaved Codex configuration draft");
            DiscardConnectionClicked(DiscardConnectionButton, new());
            RenderTemplateList(); libraryBytes = File.ReadAllBytes(libraryPath); File.WriteAllText(libraryPath, "{broken"); ReadTemplateLibrary();
            Check(!TemplatesPanel.NewTemplateButton.IsEnabled && TemplatesPanel.TemplateStatusText.Text.Contains("无法读取") && File.ReadAllText(libraryPath) == "{broken", "A corrupt template library disables writes and preserves the original damaged file");
            File.WriteAllBytes(libraryPath, libraryBytes); ReadTemplateLibrary(); Check(TemplatesPanel.NewTemplateButton.IsEnabled, "Repairing and rereading the library restores template management controls");

            var pendingHome = Path.Combine(output, "pending-recovery-home"); Directory.CreateDirectory(pendingHome);
            File.WriteAllText(Path.Combine(pendingHome, "config.toml"), "model='before-pending'\nopenai_base_url='https://pending.invalid/v1'\n");
            File.WriteAllText(Path.Combine(pendingHome, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-pending-key\"}");
            var beforePending = store.Load(pendingHome); store.Apply(store.Plan(beforePending, store.ReadSettings(beforePending) with { Model = "after-pending" }, updateConnection: false, updateModel: true));
            var pendingSource = store.LatestBackup(pendingHome)!; File.WriteAllText(Path.Combine(pendingHome, "codex-config-backups", "pending.json"), Path.GetFileName(pendingSource));
            await SwitchHomeAsync(pendingHome); NavigateTo(RecoveryNav);
            Check(PendingRecoveryBar.IsOpen && RecoveryPanel.RestoreConfigurationButton.IsEnabled && !CanEditConnection && !ApplyButton.IsEnabled,
                "An interrupted save exposes recovery while disabling configuration editing and desktop application");
            await ApplyDesktopAsync(); Check(runtime.Restarts == restarts, "Pending transaction state also blocks direct desktop application commands");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.None); var pendingSaved = store.Load(pendingHome);
            Check(!await RestoreConfigurationAsync() && store.HasPending(pendingHome) && pendingSaved.SameContents(store.Load(pendingHome)), "Cancelling interrupted-save recovery keeps the transaction record and partial state intact");
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            Check(await RestoreConfigurationAsync() && !store.HasPending(pendingHome) && beforePending.SameContents(store.Load(pendingHome)) && File.Exists(pendingSource + ".recovered") && CanEditConnection,
                "Confirmed interrupted-save recovery restores the prior state archives its source and unlocks editing without restarting Codex");
            await CaptureAsync(output, "configuration-recovery-complete.png");
        }
        finally
        {
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); editingTemplate = selectedTemplate = null; templateApplication = null;
            LoadConnectionDraft(); viewModel.Session.MarkDraft(false); await SwitchHomeAsync(previousHome);
            smokeDialog = null; Root.RequestedTheme = ElementTheme.Light; NavigateTo(WorkspaceNav);
        }
    }
}
