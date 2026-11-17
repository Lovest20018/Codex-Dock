using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class ConfigurationToolsTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
        Test("configuration catalog queries are paginated read-only and retain unknown configured fields", () =>
        {
            var f = new Fixture(root); File.AppendAllText(f.ConfigPath, "future_setting='untouched'\n[mcp_servers.'demo.with.dot']\ncommand='fixture-node'\n"); f.Reload();
            using var draft = f.Draft(); var catalog = new ConfigurationCatalogService();
            var configured = catalog.Query(draft, new()); Assert(configured.Entries.Any(entry => entry.Path == "future_setting" && entry.Category == "unlisted"));
            var search = catalog.Query(draft, new("all", "推理")); Assert(search.Count > 0);
            var all = catalog.Query(draft, new("all", Advanced: true)); var next = catalog.Query(draft, new("all", Advanced: true, Page: 1));
            Assert(all.Entries.Count <= ConfigurationCatalogService.PageSize && all.Pages > 1 && next.Page == 1 && all.Entries.All(entry => next.Entries.All(other => other.Path != entry.Path)));
            Assert(configured.Entries.Any(entry => entry.Path.Contains("demo.with.dot")) && !draft.HasDraft && f.Before.SameContents(f.Store.Load(f.Home)) && f.Store.LatestBackup(f.Home) is null);
            draft.EditField("mcp_servers.'invalid.new'.command", "[unfinished");
            Assert(catalog.Query(draft, new("all", "invalid.new")).Entries.Any(entry => entry.Path.Contains("invalid.new")) && draft.FieldErrors.Count == 1);
        });
        Test("invalid field input stays in the shared draft prevents save and survives catalog navigation", () =>
        {
            var f = new Fixture(root); using var draft = f.Draft();
            draft.EditField("model_verbosity", "'low'"); Assert(draft.Document().String("model_verbosity") == "low");
            draft.EditField("model_verbosity", "[broken");
            Assert(draft.HasDraft && draft.FieldErrors.Count == 1 && draft.FieldInput("model_verbosity") == "[broken" && draft.Document().String("model_verbosity") == "low");
            Throws<InvalidOperationException>(() => draft.Request());
            var page = new ConfigurationCatalogService().Query(draft, new("changed")); Assert(page.Entries.Single().Path == "model_verbosity");
            draft.EditField("model_verbosity", "'high'"); Assert(draft.FieldErrors.Count == 0 && draft.Document().String("model_verbosity") == "high");
            draft.UndoField("model_verbosity"); Assert(!draft.HasDraft && draft.FieldErrors.Count == 0 && f.Before.SameContents(f.Store.Load(f.Home)));
        });
        Test("field normalization removes equivalent drafts and quoted dynamic paths remain editable", () =>
        {
            var f = new Fixture(root); File.AppendAllText(f.ConfigPath, "model_verbosity='low'\n"); f.Reload(); using var draft = f.Draft();
            draft.EditField("model_verbosity", "  'low'  "); Assert(!draft.HasDraft);
            draft.SetOverride("model_verbosity", null); Assert(draft.Document().Get("model_verbosity") is null && draft.HasDraft);
            draft.UndoField("model_verbosity");
            draft.EditField("mcp_servers.'with.dot'.command", "'fixture-command'"); draft.EditField("mcp_servers.'with.dot'.args", "['--fixture']");
            var review = f.Save.PrepareReview(draft.Request()); Assert(review.Changes.Count >= 2 && f.Before.SameContents(f.Store.Load(f.Home)));
            var result = f.Save.Save(review, f.Before, () => f.Home, () => { });
            Assert(result.Succeeded && new TomlDocument(result.Saved!.Config).Get("mcp_servers.'with.dot'.command") is "fixture-command");
        });
        Test("overlapping raw fields fail in the draft without breaking projected configuration", () =>
        {
            var f = new Fixture(root); using var draft = f.Draft(); draft.EditField("future_table.child", "42");
            draft.EditField("future_table", "'cannot-replace-a-table'");
            Assert(draft.FieldErrors.ContainsKey("future_table") && draft.Document().Get("future_table.child") is 42L && draft.HasDraft);
            Throws<InvalidOperationException>(() => draft.Request()); draft.UndoField("future_table");
            Assert(draft.FieldErrors.Count == 0 && draft.Document().Get("future_table.child") is 42L);
            draft.EditField("mcp_servers.demo.url", "'https://mcp.invalid'"); draft.EditField("mcp_servers.demo.command", "'fixture-command'");
            Throws<ArgumentException>(() => f.Save.PrepareReview(draft.Request())); Assert(f.Before.SameContents(f.Store.Load(f.Home)));
        });
        Test("configuration field summaries mask nested secrets and unsafe address parameters", () =>
        {
            var value = TomlDocument.ParseModel("value={ env={CUSTOM_API_KEY='fake-private-value'}, url='https://user:private@source.invalid/path?token=private-query#private-fragment' }")["value"];
            Assert(ConfigurationCatalogService.Sensitive("future_table", value)); var display = ConfigurationCatalogService.Display("future_table", value);
            Assert(!display.Contains("fake-private-value") && !display.Contains("private-query") && !display.Contains("private-fragment") && !display.Contains("user:private"));
            var f = new Fixture(root); using var draft = f.Draft(); draft.EditField("future_table.api_key", "bad-private-input[");
            Assert(!draft.FieldErrors["future_table.api_key"].Contains("bad-private-input"));
        });
        Test("template staging preserves existing drafts by default and undo keeps later user edits", () =>
        {
            var f = new Fixture(root); using var draft = f.Draft(); draft.SetOverride("model_reasoning_effort", "'low'");
            var recipe = TemplateRepository.New("two fields", [new("model_reasoning_effort", "'high'"), new("model_verbosity", "'low'")]);
            var diff = recipe.BuildDiff(new TomlDocument(f.Before.Config).Model, draft.PendingFields);
            Assert(!diff.Single(item => item.Setting.Path == "model_reasoning_effort").SelectedByDefault);
            var application = draft.StageTemplate(recipe, diff.Where(item => item.SelectedByDefault).Select(item => item.Setting.Path));
            Assert(application.Count == 1 && draft.Document().String("model_reasoning_effort") == "low" && draft.Document().String("model_verbosity") == "low");
            draft.EditField("model_verbosity", "'high'"); var undo = draft.UndoTemplate(application);
            Assert(undo.Preserved == 1 && draft.Document().String("model_verbosity") == "high" && f.Before.SameContents(f.Store.Load(f.Home)));
        });
        Test("template cancellation restores invalid preceding input and rejects stale workspace undo", () =>
        {
            var f = new Fixture(root); using var draft = f.Draft(); draft.EditField("model_verbosity", "[unfinished");
            var recipe = TemplateRepository.New("replace one draft", [new("model_verbosity", "'low'")]);
            var application = draft.StageTemplate(recipe, ["model_verbosity"]); Assert(draft.FieldErrors.Count == 0);
            var undo = draft.UndoTemplate(application); Assert(undo.Restored == 1 && draft.FieldInput("model_verbosity") == "[unfinished" && draft.FieldErrors.Count == 1);
            draft.Load(f.Before); Throws<InvalidOperationException>(() => draft.UndoTemplate(application));
        });
        Test("template validation is atomic and structured context configuration cannot be replaced silently", () =>
        {
            var f = new Fixture(root); File.AppendAllText(f.ConfigPath, "[features.context_management]\nenabled=true\n"); f.Reload(); using var draft = f.Draft();
            draft.EditField("model_verbosity", "'high'"); var previous = draft.PendingFields.ToArray();
            Throws<ArgumentException>(() => draft.StageTemplate(BuiltInTemplates.DisableLegacyContext, ["features.context_management"]));
            Throws<ArgumentException>(() => draft.StageTemplate(BuiltInTemplates.Daily, ["not-in-this-template"]));
            Assert(previous.SequenceEqual(draft.PendingFields) && draft.Document().Get("features.context_management") is Tomlyn.Model.TomlTable && f.Before.SameContents(f.Store.Load(f.Home)));
        });
        Test("template capture excludes models connections authentication and invalid drafts", () =>
        {
            var f = new Fixture(root); File.AppendAllText(f.ConfigPath, "model_verbosity='low'\n[mcp_servers.test.env]\nTOKEN='fake-private-token'\n"); f.Reload(); using var draft = f.Draft();
            draft.Model = "draft-model"; draft.ApiKey = "fake-draft-key"; draft.EditField("analytics.enabled", "false");
            var captured = TemplateLibraryService.Capture(draft);
            Assert(captured.Any(setting => setting.Path == "analytics.enabled") && captured.Any(setting => setting.Path == "model_verbosity") &&
                captured.All(setting => setting.Path != "model" && !setting.Path.Contains("openai_base_url") && !setting.Path.Contains("TOKEN") && !setting.Value.Contains("fake-private")));
            draft.EditField("model_verbosity", "[bad"); Throws<InvalidOperationException>(() => TemplateLibraryService.Capture(draft));
        });
        Test("shared template library reads do not create files and revision-checked CRUD preserves custom recipes", () =>
        {
            var directory = Path.Combine(root, "template-library-" + Guid.NewGuid().ToString("N")); var service = new TemplateLibraryService(new(directory));
            var initial = service.Read(); Assert(initial.Templates.Count == BuiltInTemplates.Defaults.Count && !Directory.Exists(directory));
            var custom = service.Save(TemplateRepository.New("mine", [new("model_verbosity", "'low'")]), true);
            service.Delete(BuiltInTemplates.Daily.Id); Assert(service.Read().Templates.All(item => item.Id != BuiltInTemplates.Daily.Id));
            service.RestoreBuiltIns(); Assert(service.Read().Templates.Any(item => item.Id == custom.Id) && service.Read().Templates.Any(item => item.Id == BuiltInTemplates.Daily.Id));
            service.Delete(custom.Id); Assert(service.Read().Templates.All(item => item.Id != custom.Id));
        });
        Test("template library concurrent writes and corrupt files never replace the newer library", () =>
        {
            var directory = Path.Combine(root, "template-conflict-" + Guid.NewGuid().ToString("N")); var repository = new TemplateRepository(directory);
            var first = new TemplateLibraryService(repository); var second = new TemplateLibraryService(new(directory)); first.Read(); second.Read();
            var custom = first.Save(TemplateRepository.New("first", [new("model_verbosity", "'low'")]), true); var path = Path.Combine(directory, "templates.json"); var bytes = File.ReadAllBytes(path);
            Throws<IOException>(() => second.Save(TemplateRepository.New("stale", [new("model_verbosity", "'high'")]), true)); Assert(File.ReadAllBytes(path).SequenceEqual(bytes));
            second.Read(); second.Delete(custom.Id); File.WriteAllText(path, "{broken");
            Throws<InvalidDataException>(() => first.Read()); Assert(File.ReadAllText(path) == "{broken");
        });
        Test("recovery review is read-only redacts credentials and restores only after a single confirmation", () =>
        {
            var f = new Fixture(root); f.Apply("new-model"); var after = f.Store.Load(f.Home); var latest = f.Store.LatestBackup(f.Home);
            var review = f.Recovery.PrepareReview(f.Home); var visible = string.Join("\n", review.Changes.Select(change => change.Previous + change.Next));
            Assert(after.SameContents(f.Store.Load(f.Home)) && f.Store.LatestBackup(f.Home) == latest && !visible.Contains("fake-tools-key") && review.Targets.Contains(f.ConfigPath));
            var result = f.Recovery.Restore(review, () => f.Home, () => { });
            Assert(result.Succeeded && result.WasWritten && f.Before.SameContents(result.Saved!) && result.BackupPath != latest && !f.Operations.IsBusy);
            var repeated = f.Recovery.Restore(review, () => f.Home, () => { }); Assert(!repeated.Succeeded && f.Before.SameContents(f.Store.Load(f.Home)));
        });
        Test("recovery rejects external changes expired source context cancellation and concurrent operations", () =>
        {
            var f = new Fixture(root); f.Apply("new-model"); var review = f.Recovery.PrepareReview(f.Home); var after = f.Store.Load(f.Home);
            using (var lease = f.Operations.TryBeginDesktopApplication()) Assert(f.Recovery.Restore(review, () => f.Home, () => { }).Outcome == ConfigurationSaveOutcome.Busy);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert(f.Recovery.Restore(review, () => f.Home, () => { }, cancellation.Token).Outcome == ConfigurationSaveOutcome.Cancelled && after.SameContents(f.Store.Load(f.Home)));
            Assert(f.Recovery.Restore(review, () => Path.Combine(f.Home, "other"), () => { }).Outcome == ConfigurationSaveOutcome.Cancelled);
            File.AppendAllText(f.ConfigPath, "model_verbosity='high'\n"); var changed = f.Store.Load(f.Home);
            Assert(f.Recovery.Restore(review, () => f.Home, () => { }).Outcome == ConfigurationSaveOutcome.Conflict && changed.SameContents(f.Store.Load(f.Home)) && !f.Operations.IsBusy);
        });
        Test("recovery refuses a replaced encrypted backup or a newly created latest backup", () =>
        {
            var f = new Fixture(root); f.Apply("new-model"); var review = f.Recovery.PrepareReview(f.Home); var after = f.Store.Load(f.Home);
            var source = File.ReadAllBytes(review.SourcePath); File.WriteAllBytes(review.SourcePath, source.Concat(new byte[] { 0 }).ToArray());
            Assert(!f.Recovery.Restore(review, () => f.Home, () => { }).Succeeded && after.SameContents(f.Store.Load(f.Home)));
            File.WriteAllBytes(review.SourcePath, source); review = f.Recovery.PrepareReview(f.Home);
            var current = f.Store.Load(f.Home); f.Store.Apply(f.Store.Plan(current, f.Store.ReadSettings(current) with { Model = "even-newer" }, updateConnection: false, updateModel: true));
            var newer = f.Store.Load(f.Home); Assert(!f.Recovery.Restore(review, () => f.Home, () => { }).Succeeded && newer.SameContents(f.Store.Load(f.Home)));
        });
        Test("interrupted-save review stays read-only and recovers partial file state before archiving", () =>
        {
            var f = new Fixture(root); f.Apply("new-model", key: "fake-new-key"); var after = f.Store.Load(f.Home); var source = f.Store.LatestBackup(f.Home)!;
            File.WriteAllBytes(Path.Combine(f.Home, "auth.json"), f.Before.Auth!); f.Pending(Path.GetFileName(source)); var partial = f.Store.Load(f.Home);
            var review = f.Recovery.PrepareReview(f.Home); Assert(review.Interrupted && f.Store.HasPending(f.Home) && partial.SameContents(f.Store.Load(f.Home)));
            var result = f.Recovery.Restore(review, () => f.Home, () => { });
            Assert(result.Succeeded && result.BackupPath is null && f.Before.SameContents(result.Saved!) && !f.Store.HasPending(f.Home) && File.Exists(source + ".recovered") && !after.SameContents(result.Saved!));
        });
        Test("interrupted-save confirmation rejects altered pending records and external edits", () =>
        {
            var f = new Fixture(root); f.Apply("new-model"); var source = f.Store.LatestBackup(f.Home)!; f.Pending(Path.GetFileName(source));
            var review = f.Recovery.PrepareReview(f.Home); f.Pending("../outside.backup"); var after = f.Store.Load(f.Home);
            Assert(!f.Recovery.Restore(review, () => f.Home, () => { }).Succeeded && after.SameContents(f.Store.Load(f.Home)));
            Throws<InvalidDataException>(() => f.Recovery.PrepareReview(f.Home)); f.Pending(Path.GetFileName(source));
            File.AppendAllText(f.ConfigPath, "model_verbosity='high'\n"); var external = f.Store.Load(f.Home);
            Throws<IOException>(() => f.Recovery.PrepareReview(f.Home)); Assert(external.SameContents(f.Store.Load(f.Home)) && f.Store.HasPending(f.Home));
        });
        Test("a fully rolled back pending save can be reviewed and archived without changing configuration", () =>
        {
            var f = new Fixture(root); f.Apply("new-model"); var source = f.Store.LatestBackup(f.Home)!;
            File.WriteAllBytes(f.ConfigPath, f.Before.Config!); f.Pending(Path.GetFileName(source));
            var review = f.Recovery.PrepareReview(f.Home); Assert(review.Changes.Count == 1 && review.Changes[0].Path == "pending.json");
            var result = f.Recovery.Restore(review, () => f.Home, () => { }); Assert(result.Succeeded && f.Before.SameContents(result.Saved!) && !f.Store.HasPending(f.Home));
        });
        Test("recovery recreates a missing model dependency inside the home and preserves external files", () =>
        {
            var f = new Fixture(root); var external = Path.Combine(f.Home, "external-models.json"); File.WriteAllText(external, ManagedCatalogTests.Fixture);
            File.AppendAllText(f.ConfigPath, "model_catalog_json='external-models.json'\n");
            var document = new TomlDocument(File.ReadAllBytes(f.ConfigPath)); document.Set("model", "main"); File.WriteAllBytes(f.ConfigPath, document.Bytes); f.Reload();
            var plan = f.Store.Plan(f.Before, f.Store.ReadSettings(f.Before), new Dictionary<string, string?> { ["approvals_reviewer"] = "\"auto_review\"" }, updateConnection: false);
            f.Store.Apply(ManagedModelCatalogStore.Prepare(plan, new())); File.Delete(external);
            var review = f.Recovery.PrepareReview(f.Home); var managed = ManagedModelCatalogStore.CatalogPath(f.Home, Encoding.UTF8.GetBytes(ManagedCatalogTests.Fixture));
            Assert(!File.Exists(managed) && !File.Exists(external) && review.Targets.Contains(managed));
            var result = f.Recovery.Restore(review, () => f.Home, () => { });
            Assert(result.Succeeded && File.Exists(managed) && !File.Exists(external) && result.Saved!.Catalog!.Path == managed);
        });
        foreach (var interrupted in new[] { false, true })
            Test((interrupted ? "interrupted" : "ordinary") + " reviewed recovery preserves later verified account refresh runtime pipe and comments", () =>
            {
                var f = new Fixture(root);
                File.WriteAllBytes(Path.Combine(f.Home, "auth.json"), Official("same-account", "old"));
                var document = new TomlDocument(File.ReadAllBytes(f.ConfigPath));
                document.Set("mcp_servers.node_repl.env.SKY_CUA_NATIVE_PIPE_DIRECTORY", @"\\.\pipe\codex-computer-use-" + Guid.NewGuid()); File.WriteAllBytes(f.ConfigPath, document.Bytes);
                f.Reload(); f.Apply("after-review-model");
                if (interrupted) f.Pending(Path.GetFileName(f.Store.LatestBackup(f.Home)!));
                var review = f.Recovery.PrepareReview(f.Home);
                var fresh = Official("same-account", "fresh"); File.WriteAllBytes(Path.Combine(f.Home, "auth.json"), fresh);
                document = new TomlDocument(File.ReadAllBytes(f.ConfigPath)); var nextPipe = @"\\.\pipe\codex-computer-use-" + Guid.NewGuid();
                document.Set("mcp_servers.node_repl.env.SKY_CUA_NATIVE_PIPE_DIRECTORY", nextPipe); File.WriteAllText(f.ConfigPath, document.Text + "\n# runtime comment retained\n");
                var result = f.Recovery.Restore(review, () => f.Home, () => { }); var saved = new TomlDocument(result.Saved?.Config);
                Assert(result.Succeeded && result.Saved!.Auth!.SequenceEqual(fresh) && saved.String("model") == "old-model" &&
                    saved.Get("mcp_servers.node_repl.env.SKY_CUA_NATIVE_PIPE_DIRECTORY") is string pipe && pipe == nextPipe && saved.Text.Contains("runtime comment retained"));
            });
    }
    private sealed class Fixture
    {
        internal readonly string Home;
        internal string ConfigPath => Path.Combine(Home, "config.toml");
        internal readonly ConfigStore Store = new(new MemoryEnvironment(), _ => null);
        internal readonly WorkspaceSession Session;
        internal readonly WorkspaceApplyCoordinator Operations;
        internal readonly ConfigurationSaveService Save;
        internal readonly ConfigurationRecoveryService Recovery;
        internal ConfigSnapshot Before = null!;
        internal Fixture(string root)
        {
            Home = Path.Combine(root, "configuration-tools-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Home);
            File.WriteAllText(ConfigPath, "# tools fixture\nmodel='old-model'\nopenai_base_url='https://tools.invalid/v1'\n");
            File.WriteAllText(Path.Combine(Home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-tools-key\"}");
            Session = new(Home); Operations = new(Session); Save = new(Store, Operations); Recovery = new(Store, Operations); Reload();
        }
        internal void Reload() { Before = Store.Load(Home); Session.Loaded(Home, Before); }
        internal ConnectionDraftViewModel Draft() { var draft = new ConnectionDraftViewModel(Store); draft.Load(Before); return draft; }
        internal void Apply(string model, string? key = null)
        { Store.Apply(Store.Plan(Before, Store.ReadSettings(Before) with { Model = model, ApiKey = key ?? Store.ReadSettings(Before).ApiKey }, updateConnection: key is not null, updateModel: true)); }
        internal void Pending(string name) { var path = Path.Combine(Home, "codex-config-backups", "pending.json"); File.WriteAllText(path, name); }
    }
    private sealed class MemoryEnvironment : IUserEnvironment { public string? Read() => null; public void Write(string? value) { if (value is not null) throw new InvalidOperationException("Fixture cannot change environment"); } }
    private static byte[] Official(string subject, string token) => JsonSerializer.SerializeToUtf8Bytes(new { auth_mode = "chatgpt", tokens = new {
        account_id = "fake-tools-account", access_token = token, refresh_token = token,
        id_token = "x." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { iss = "fixture", sub = subject })).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".x" } });
    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Configuration tools regression"); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
