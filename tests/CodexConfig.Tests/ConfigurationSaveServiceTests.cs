using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class ConfigurationSaveServiceTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        Test("connection draft loading editing and discarding remain in memory until a reviewed save", () =>
        {
            var f = new Fixture(root); var draft = f.Draft(); var revision = draft.Revision;
            Assert(!draft.HasDraft && draft.ApiKey == "fake-save-old-key");
            draft.Model = "changed-model"; draft.Name = "Test connection";
            Assert(draft.HasDraft && !draft.ConnectionChanged && draft.Revision > revision && !draft.Request().UpdateConnection);
            Assert(f.Before.SameContents(f.Store.Load(f.Home)) && f.Store.LatestBackup(f.Home) is null);
            draft.Load(f.Before); Assert(!draft.HasDraft && draft.Model == "old-model" && draft.Name == "");
        });
        Test("shared configuration save commits only the reviewed connection and creates a restorable encrypted backup", () =>
        {
            var f = new Fixture(root); var draft = f.Draft();
            draft.BaseUrl = "https://new-save.invalid/v1/responses"; draft.ApiKey = "fake-save-new-key"; draft.Model = "new-model";
            var review = f.Service.PrepareReview(draft.Request());
            Assert(review.HasChanges && f.Before.SameContents(f.Store.Load(f.Home)) && !review.Description.Contains("fake-save-new-key"));
            f.Session.MarkDraft(true); var result = f.Save(review);
            Assert(result is { Outcome: ConfigurationSaveOutcome.Saved, WasWritten: true } && File.Exists(result.BackupPath));
            var saved = f.Store.ReadSettings(result.Saved!);
            Assert(saved.BaseUrl == "https://new-save.invalid/v1" && saved.ApiKey == "fake-save-new-key" && saved.Model == "new-model");
            Assert(!f.Session.State.HasDraft && f.Session.State.Phase == WorkspacePhase.NeedsRestart && !f.Operations.IsBusy);
            var backupText = Encoding.UTF8.GetString(File.ReadAllBytes(result.BackupPath!));
            Assert(!backupText.Contains("fake-save-old-key") && !backupText.Contains("old-model"));
            f.Store.Restore(result.Saved!); Assert(f.Before.SameContents(f.Store.Load(f.Home)));
        });
        Test("shared configuration review freezes all destination bytes before confirmation", () =>
        {
            var f = new Fixture(root); var draft = f.Draft(); draft.ApiKey = "fake-frozen-key"; draft.Model = "reviewed-model";
            var plan = f.Service.PreparePlan(draft.Request()); var review = f.Service.Review(plan);
            Array.Fill(plan.Config, (byte)'x'); Array.Fill(plan.Auth!, (byte)'x');
            var result = f.Save(review);
            Assert(result.Succeeded && f.Store.ReadSettings(result.Saved!).ApiKey == "fake-frozen-key" && new TomlDocument(result.Saved!.Config).String("model") == "reviewed-model");
        });
        Test("shared configuration save rejects changed settings or a different active directory without overwriting them", () =>
        {
            foreach (var change in new[] { "external", "home", "snapshot", "cancel" })
            {
                var f = new Fixture(root); var draft = f.Draft(); draft.Model = "pending-model"; var review = f.Service.PrepareReview(draft.Request());
                var active = f.Before; var home = f.Home; using var cancellation = new CancellationTokenSource();
                if (change == "external") File.AppendAllText(Path.Combine(f.Home, "config.toml"), "service_tier='priority'\n");
                if (change == "home") home += "-other";
                if (change == "snapshot") active = active with { Config = Encoding.UTF8.GetBytes("model='reloaded'\n") };
                if (change == "cancel") cancellation.Cancel();
                var expected = f.Store.Load(f.Home);
                var result = f.Service.Save(review, active, () => home, () => { }, cancellation.Token);
                Assert(!result.Succeeded && !result.WasWritten && expected.SameContents(f.Store.Load(f.Home)) && f.Store.LatestBackup(f.Home) is null && !f.Operations.IsBusy);
                if (change == "external") Assert(result.Outcome == ConfigurationSaveOutcome.Conflict);
            }
        });
        Test("shared save and desktop application share a serial lease and cannot replay a consumed confirmation", () =>
        {
            var f = new Fixture(root); var draft = f.Draft(); draft.Model = "saved-once"; var review = f.Service.PrepareReview(draft.Request());
            using (var lease = f.Operations.TryBeginDesktopApplication())
            { Assert(lease is not null && f.Save(review).Outcome == ConfigurationSaveOutcome.Busy && f.Before.SameContents(f.Store.Load(f.Home))); }
            var saves = 0;
            f.Session.PropertyChanged += (_, _) =>
            {
                if (f.Session.State.Operation.Operation == WorkspaceOperation.SavingConfiguration && f.Session.State.Operation.IsBusy)
                { Assert(f.Operations.TryBeginDesktopApplication() is null); saves++; }
            };
            var result = f.Save(review); Assert(result.Succeeded && saves > 0);
            var backup = f.Store.LatestBackup(f.Home); var again = f.Save(review);
            Assert(!again.Succeeded && f.Store.LatestBackup(f.Home) == backup && !f.Operations.IsBusy);
        });
        Test("shared save preserves live runtime pipe rotation comments and refreshed same-account login", () =>
        {
            var f = new Fixture(root, official: true);
            File.WriteAllText(Path.Combine(f.Home, "config.toml"), "model='old-model'\n[mcp_servers.node_repl.env]\nSKY_CUA_NATIVE_PIPE_DIRECTORY='\\\\.\\pipe\\codex-computer-use-" + Guid.NewGuid() + "'\n");
            f.Reload(); var draft = f.Draft(); draft.Model = "after-review"; var review = f.Service.PrepareReview(draft.Request());
            var nextPipe = @"\\.\pipe\codex-computer-use-" + Guid.NewGuid();
            var current = new TomlDocument(f.Before.Config); current.Set("mcp_servers.node_repl.env.SKY_CUA_NATIVE_PIPE_DIRECTORY", nextPipe);
            File.WriteAllText(Path.Combine(f.Home, "config.toml"), current.Text + "\n# live retained comment\n");
            var refreshed = Official("same-account", "fresh"); File.WriteAllBytes(Path.Combine(f.Home, "auth.json"), refreshed);
            var result = f.Save(review); Assert(result.Succeeded && refreshed.SequenceEqual(result.Saved!.Auth!));
            var saved = new TomlDocument(result.Saved!.Config);
            Assert(saved.String("model") == "after-review" && saved.Get("mcp_servers.node_repl.env.SKY_CUA_NATIVE_PIPE_DIRECTORY") as string == nextPipe && saved.Text.Contains("live retained comment"));
        });
        Test("model and note edits preserve custom provider environment credentials and absent behavior defaults", () =>
        {
            var f = new Fixture(root);
            File.WriteAllText(Path.Combine(f.Home, "config.toml"), "model='old-model'\nmodel_provider='custom'\n[model_providers.custom]\nbase_url='https://custom.invalid/v1'\nenv_key='CUSTOM_SAVE_KEY'\n# preserve provider\n");
            f.Reload(); var draft = f.Draft(); draft.Model = "custom-new-model"; draft.Note = "my local note";
            var result = f.Save(f.Service.PrepareReview(draft.Request())); Assert(result.Succeeded);
            var doc = new TomlDocument(result.Saved!.Config);
            Assert(doc.String("model_provider") == "custom" && doc.Get("model_providers.custom.env_key") as string == "CUSTOM_SAVE_KEY" && doc.Get("features.fast_mode") is null);
            Assert(f.Before.Auth!.SequenceEqual(result.Saved.Auth!) && ConnectionDetails.Read(result.Saved.Details).Note == "my local note");
        });
        Test("legacy PAT migrates only through an explicit connection edit or migration draft", () =>
        {
            var f = new Fixture(root); File.WriteAllText(Path.Combine(f.Home, "auth.json"), "{\"personal_access_token\":\"fake-legacy-save-key\"}"); f.Reload();
            var draft = f.Draft(); Assert(draft.NeedsApiKeyMigration && !draft.HasDraft);
            draft.Model = "legacy-next"; var result = f.Save(f.Service.PrepareReview(draft.Request())); Assert(result.Succeeded && f.Store.ReadSettings(result.Saved!).NeedsApiKeyMigration);
            f.Reload(); draft = f.Draft(); draft.MigrateLegacyPat = true;
            var migrationReview = f.Service.PrepareReview(draft.Request()); Assert(migrationReview.Description.Contains("旧 PAT → API Key"));
            result = f.Save(migrationReview);
            Assert(result.Succeeded && !f.Store.ReadSettings(result.Saved!).NeedsApiKeyMigration && Encoding.UTF8.GetString(result.Saved!.Auth!).Contains("auth_mode"));
        });
        Test("connection draft binds HTTP consent to the current site and cannot carry it to another host", () =>
        {
            var f = new Fixture(root); var draft = f.Draft(); draft.BaseUrl = "http://one-save.invalid/v1";
            Throws<InvalidOperationException>(() => f.Service.PrepareReview(draft.Request()));
            draft.HttpAuthorized = true; Assert(draft.HttpAuthorized); _ = f.Service.PrepareReview(draft.Request());
            draft.BaseUrl = "http://two-save.invalid/v1"; Assert(!draft.HttpAuthorized);
            Throws<InvalidOperationException>(() => f.Service.PrepareReview(draft.Request()));
            draft.BaseUrl = "http://127.0.0.1:32198/v1"; Assert(draft.HttpRequiredOrigin is null); _ = f.Service.PrepareReview(draft.Request());
            Assert(f.Before.SameContents(f.Store.Load(f.Home)));
        });
        Test("shared mixed connection save preserves official identity and encrypts the separate service key", () =>
        {
            var f = new Fixture(root, official: true); var draft = f.Draft(); draft.Mode = ConnectionMode.OfficialWithApi;
            draft.BaseUrl = "https://mixed-save.invalid/v1"; draft.ApiKey = "fake-separate-model-key"; draft.UseSystemProxy = true;
            var review = f.Service.PrepareReview(draft.Request()); Assert(!review.Description.Contains("fake-separate-model-key"));
            var result = f.Save(review); Assert(result.Succeeded && OfficialCredentials.HasLogin(result.Saved!.Auth));
            var connection = f.Store.ReadSettings(result.Saved!); Assert(connection.Mode == ConnectionMode.OfficialWithApi && connection.ApiKey == "fake-separate-model-key" && connection.UseSystemProxy);
            Assert(!Encoding.UTF8.GetString(result.Saved!.Bridge!).Contains("fake-separate-model-key"));
            var noLogin = new Fixture(root); var noLoginDraft = noLogin.Draft(); noLoginDraft.Mode = ConnectionMode.OfficialWithApi;
            Throws<InvalidOperationException>(() => noLogin.Service.PrepareReview(noLoginDraft.Request()));
        });
        Test("shared save review redacts credentials URL parameters environment secrets and connection notes", () =>
        {
            var f = new Fixture(root); f.Environment.Value = "https://user:private-password@auth.invalid/v1?token=private-query"; f.Reload();
            var draft = f.Draft(); draft.ApiKey = "private-new-key"; draft.Note = "private-connection-note";
            var review = f.Service.PrepareReview(draft.Request()); var visible = review.Description + string.Join("\n", review.Notes);
            Assert(!visible.Contains("private-new-key") && !visible.Contains("private-password") && !visible.Contains("private-query") && !visible.Contains("private-connection-note"));
            Assert(review.Targets.Any(target => target.Contains("auth.json")) && review.Targets.Contains("用户环境变量 CODEX_AUTHAPI_BASE_URL"));
        });
        Test("shared save refuses pending recovery records and no-op drafts create no backup", () =>
        {
            var f = new Fixture(root); var review = f.Service.PrepareReview(f.Draft().Request());
            var result = f.Save(review); Assert(result.Outcome == ConfigurationSaveOutcome.NoChanges && !result.WasWritten && f.Store.LatestBackup(f.Home) is null);
            var pending = Path.Combine(f.Home, "codex-config-backups", "pending.json"); Directory.CreateDirectory(Path.GetDirectoryName(pending)!); File.WriteAllText(pending, "not-a-backup");
            Throws<InvalidOperationException>(() => f.Service.PrepareReview(f.Draft().Request()));
            Assert(File.ReadAllText(pending) == "not-a-backup");
        });
        Test("shared save failure rolls back files and preserves the draft while releasing its operation lease", () =>
        {
            var f = new Fixture(root); f.Environment.Value = "https://legacy-save.invalid/codex/v1"; f.Reload();
            var draft = f.Draft(); draft.ApiKey = "fake-failing-save-key"; f.Session.MarkDraft(true);
            var review = f.Service.PrepareReview(draft.Request()); f.Environment.FailNextWrite = true;
            var result = f.Save(review);
            Assert(result.Outcome == ConfigurationSaveOutcome.Failed && !result.WasWritten && f.Before.SameContents(f.Store.Load(f.Home)) &&
                f.Session.State.HasDraft && f.Session.State.Phase == WorkspacePhase.Failed && !f.Operations.IsBusy && !f.Store.HasPending(f.Home));
        });
    }

    private sealed class Fixture
    {
        internal readonly string Home;
        internal readonly MemoryEnvironment Environment = new();
        internal readonly ConfigStore Store;
        internal readonly WorkspaceSession Session;
        internal readonly WorkspaceApplyCoordinator Operations;
        internal readonly ConfigurationSaveService Service;
        internal ConfigSnapshot Before = null!;
        internal Fixture(string root, bool official = false)
        {
            Home = Path.Combine(root, "configuration-save-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Home);
            File.WriteAllText(Path.Combine(Home, "config.toml"), "model='old-model'\nopenai_base_url='https://old-save.invalid/v1'\n");
            File.WriteAllBytes(Path.Combine(Home, "auth.json"), official ? Official("same-account", "old") : Encoding.UTF8.GetBytes("{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-save-old-key\"}"));
            Store = new(Environment, _ => null); Session = new(Home); Operations = new(Session); Service = new(Store, Operations); Reload();
        }
        internal void Reload() { Before = Store.Load(Home); Session.Loaded(Home, Before); }
        internal ConnectionDraftViewModel Draft() { var draft = new ConnectionDraftViewModel(Store); draft.Load(Before); return draft; }
        internal ConfigurationSaveResult Save(ConfigurationSaveReview review) => Service.Save(review, Before, () => Home, () => { });
    }
    private sealed class MemoryEnvironment : IUserEnvironment
    {
        internal string? Value; internal bool FailNextWrite;
        public string? Read() => Value;
        public void Write(string? value)
        { if (FailNextWrite) { FailNextWrite = false; throw new IOException("fixture environment write failure"); } Value = value; }
    }
    private static byte[] Official(string subject, string token) => JsonSerializer.SerializeToUtf8Bytes(new { auth_mode = "chatgpt", tokens = new {
        account_id = "fake-save-account", access_token = token, refresh_token = token,
        id_token = "x." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { iss = "fixture", sub = subject })).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".x" } });
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Shared configuration save regression"); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
