using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class OfficialLoginAndModelTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("official login refresh rebases authentication while preserving the complete connection and behavior draft", async () =>
        {
            var f = new Fixture(root); using var draft = f.Draft();
            draft.Model = "draft-model"; draft.Name = "draft name"; draft.ApiKey = "fake-draft-key";
            draft.Mode = ConnectionMode.OfficialWithApi; draft.SetOverride("model_verbosity", "\"low\"");
            draft.StageCatalog("import.json");
            File.WriteAllBytes(f.AuthPath, Official("new-login")); var latest = f.Store.Load(f.Home);
            using var read = await f.Login.ReadAsync(f.Before, false, () => true);
            draft.AdoptOfficialLogin(read);
            Assert(read.FromCurrentFile && !read.HasStagedAuthentication && latest.SameContents(draft.Snapshot!) &&
                draft.Model == "draft-model" && draft.Name == "draft name" && draft.ApiKey == "fake-draft-key" &&
                draft.Edits["model_verbosity"] == "\"low\"" && draft.SynchronizeCatalog && draft.CatalogSource == Path.Combine(f.Home, "import.json"));
            Assert(latest.SameContents(f.Store.Load(f.Home)) && f.Store.LatestBackup(f.Home) is null);
        }));
        tests.Add(("official backup requires explicit selection and remains a disposable in-memory draft", async () =>
        {
            var f = new Fixture(root); var runtime = f.Runtime(); runtime.Backup = Official("backup-login");
            var reader = new OfficialLoginService(f.Store, runtime);
            await Throws<InvalidOperationException>(() => reader.ReadAsync(f.Before, false, () => true));
            Assert(runtime.BackupReads == 0);
            using (var read = await reader.ReadAsync(f.Before, true, () => true))
                Assert(read.HasStagedAuthentication && !read.FromCurrentFile && f.Before.SameContents(f.Store.Load(f.Home)));
            Assert(runtime.Backup!.All(value => value == 0) && f.Store.LatestBackup(f.Home) is null);
        }));
        tests.Add(("a current official login takes precedence over an older backup without staging or writes", async () =>
        {
            var f = new Fixture(root, official: true); var runtime = f.Runtime(); runtime.Backup = Official("older-backup");
            using var read = await new OfficialLoginService(f.Store, runtime).ReadAsync(f.Before, true, () => true);
            Assert(read.FromCurrentFile && !read.HasStagedAuthentication && runtime.BackupReads == 0 && f.Before.SameContents(f.Store.Load(f.Home)));
        }));
        tests.Add(("non-file credential configuration stages official file identity for explicit mixed-mode save", async () =>
        {
            var f = new Fixture(root, official: true); File.AppendAllText(f.ConfigPath, "cli_auth_credentials_store='keyring'\n"); f.Reload();
            using var read = await f.Login.ReadAsync(f.Before, false, () => true);
            Assert(read.HasStagedAuthentication && read.FromCurrentFile && f.Before.SameContents(f.Store.Load(f.Home)));
        }));
        tests.Add(("official login reads reject external configuration and environment changes", async () =>
        {
            var f = new Fixture(root, official: true); File.AppendAllText(f.ConfigPath, "model_verbosity='low'\n");
            await Throws<IOException>(() => f.Login.ReadAsync(f.Before, false, () => true));
            f.Reload(); f.Environment.Value = "https://external-login.invalid/v1";
            await Throws<IOException>(() => f.Login.ReadAsync(f.Before, false, () => true));
            Assert(File.ReadAllText(f.ConfigPath).Contains("model_verbosity='low'") && f.Store.LatestBackup(f.Home) is null);
        }));
        tests.Add(("pending save recovery prevents login reads before touching credentials", async () =>
        {
            var f = new Fixture(root); var runtime = f.Runtime();
            var pending = Path.Combine(f.Home, "codex-config-backups", "pending.json"); Directory.CreateDirectory(Path.GetDirectoryName(pending)!);
            File.WriteAllText(pending, "fixture pending record");
            await Throws<IOException>(() => new OfficialLoginService(f.Store, runtime).ReadAsync(f.Before, true, () => true));
            Assert(runtime.Loads == 0 && runtime.BackupReads == 0 && File.ReadAllText(pending) == "fixture pending record");
        }));
        tests.Add(("a workspace change during backup reading discards and zeroes the older credentials", async () =>
        {
            var f = new Fixture(root); var current = true; var runtime = f.Runtime(); runtime.Backup = Official("old-workspace");
            runtime.OnBackup = () => current = false;
            await Throws<OperationCanceledException>(() => new OfficialLoginService(f.Store, runtime).ReadAsync(f.Before, true, () => current));
            Assert(runtime.Backup.All(value => value == 0) && f.Before.SameContents(f.Store.Load(f.Home)));
        }));
        tests.Add(("a native login arriving during backup reading cannot be replaced by an older backup", async () =>
        {
            var f = new Fixture(root); var runtime = f.Runtime(); runtime.Backup = Official("older-backup");
            var newer = Official("newer-native-login"); runtime.OnBackup = () => File.WriteAllBytes(f.AuthPath, newer);
            await Throws<IOException>(() => new OfficialLoginService(f.Store, runtime).ReadAsync(f.Before, true, () => true));
            Assert(runtime.Backup.All(value => value == 0) && File.ReadAllBytes(f.AuthPath).SequenceEqual(newer));
        }));
        tests.Add(("cancelling a backup read zeroes returned authentication and preserves disk contents", async () =>
        {
            var f = new Fixture(root); using var cancellation = new CancellationTokenSource(); var runtime = f.Runtime();
            runtime.Backup = Official("cancelled-login"); runtime.OnBackup = cancellation.Cancel;
            await Throws<OperationCanceledException>(() => new OfficialLoginService(f.Store, runtime).ReadAsync(f.Before, true, () => true, cancellation.Token));
            Assert(runtime.Backup.All(value => value == 0) && f.Before.SameContents(f.Store.Load(f.Home)));
        }));
        tests.Add(("staged login is omitted from direct requests and erased on discard or directory reload", async () =>
        {
            var f = new Fixture(root); var runtime = f.Runtime(); runtime.Backup = Official("staged-only");
            using var draft = f.Draft(); draft.Mode = ConnectionMode.OfficialWithApi;
            using (var read = await new OfficialLoginService(f.Store, runtime).ReadAsync(f.Before, true, () => true)) draft.AdoptOfficialLogin(read);
            Assert(draft.HasStagedOfficialLogin && draft.HasDraft && draft.Request().OfficialAuth is not null);
            draft.Mode = ConnectionMode.Direct; Assert(draft.Request().OfficialAuth is null && !draft.HasDraft);
            draft.Load(f.Before);
            Assert(!draft.HasStagedOfficialLogin && !draft.HasOfficialLogin && runtime.Backup.All(value => value == 0));
            var other = new Fixture(root); draft.Load(other.Before); Assert(draft.Snapshot!.Home == other.Home && draft.Request().OfficialAuth is null);
        }));
        tests.Add(("native encrypted login backup is adopted only by the reviewed save", async () =>
        {
            var f = new Fixture(root, official: true); var originalAuth = f.Before.Auth!;
            f.Store.Apply(f.Store.Plan(f.Before, f.Store.ReadSettings(f.Before) with { ApiKey = "fake-direct-key" })); f.Reload();
            using var draft = f.Draft(); draft.Mode = ConnectionMode.OfficialWithApi; draft.ApiKey = "fake-separate-model-key";
            using (var read = await f.Login.ReadAsync(f.Before, true, () => true)) draft.AdoptOfficialLogin(read);
            var review = f.Save.PrepareReview(draft.Request());
            Assert(draft.HasStagedOfficialLogin && f.Before.SameContents(f.Store.Load(f.Home)) && !review.Description.Contains("fake-separate-model-key") && !review.Description.Contains("fake-login"));
            var saved = f.Save.Save(review, draft.Snapshot!, () => f.Home, () => { });
            Assert(saved.Succeeded && saved.WasWritten && OfficialCredentials.HasLogin(saved.Saved!.Auth) && saved.Saved.Auth!.SequenceEqual(originalAuth) &&
                f.Store.ReadSettings(saved.Saved).Mode == ConnectionMode.OfficialWithApi);
            draft.Load(saved.Saved); Assert(!draft.HasStagedOfficialLogin && !draft.HasDraft);
        }));
        tests.Add(("inherited behavior is a no-op and reverting an override removes the draft", async () =>
        {
            var f = new Fixture(root); using var draft = f.Draft();
            draft.SetOverride("model_verbosity", null); Assert(!draft.HasDraft && draft.Edits.Count == 0);
            draft.SetOverride("model_verbosity", "\"low\""); Assert(draft.Document().String("model_verbosity") == "low" && draft.HasDraft);
            draft.SetOverride("model_verbosity", null); Assert(!draft.HasDraft && draft.Edits.Count == 0);
            await Throws<InvalidDataException>(() => { draft.SetOverride("model_verbosity", "[broken"); return Task.CompletedTask; });
            Assert(!draft.HasDraft && f.Before.SameContents(f.Store.Load(f.Home)));
        }));
        tests.Add(("dedicated reviewer preserves structured approval rules and sandbox and freezes the chosen catalog", () =>
        {
            var f = new Fixture(root); File.AppendAllText(f.ConfigPath, "sandbox_mode='read-only'\napproval_policy={ granular={ rules=true, sandbox_approval=true } }\n"); f.Reload();
            File.WriteAllText(Path.Combine(f.Home, "models_cache.json"), ManagedCatalogTests.Fixture);
            using var draft = f.Draft(); draft.Model = "main"; var policy = ConfigSchema.Literal(draft.Document().Get("approval_policy"));
            draft.SetDedicatedReviewer(true); draft.StageCatalog();
            Assert(ConfigSchema.Literal(draft.Document().Get("approval_policy")) == policy && draft.Document().String("sandbox_mode") == "read-only");
            var review = f.Save.PrepareReview(draft.Request());
            Assert(f.Before.SameContents(f.Store.Load(f.Home)) && !Directory.Exists(Path.Combine(f.Home, "codex-dock-models")));
            draft.SetDedicatedReviewer(false); Assert(draft.Document().String("approvals_reviewer") == "user" && ConfigSchema.Literal(draft.Document().Get("approval_policy")) == policy);
            var result = f.Save.Save(review, f.Before, () => f.Home, () => { });
            Assert(result.Succeeded && ManagedModelCatalogStore.Inspect(result.Saved!) is { Managed: true, ReviewModel: "codex-auto-review" } &&
                ConfigSchema.Literal(new TomlDocument(result.Saved!.Config).Get("approval_policy")) == policy);
            return Task.CompletedTask;
        }));
        tests.Add(("dedicated reviewer requires a native catalog covering the selected main model", async () =>
        {
            var f = new Fixture(root); File.WriteAllText(Path.Combine(f.Home, "models_cache.json"), ManagedCatalogTests.Fixture);
            using var draft = f.Draft(); draft.SetDedicatedReviewer(true);
            await Throws<InvalidDataException>(() => { _ = f.Save.PrepareReview(draft.Request()); return Task.CompletedTask; });
            Assert(f.Before.SameContents(f.Store.Load(f.Home)) && f.Store.LatestBackup(f.Home) is null);
        }));
        tests.Add(("model discovery reads only the selected endpoint and filters unusable IDs without altering the model", async () =>
        {
            var f = new Fixture(root); using var draft = f.Draft(); draft.Model = "keep-my-model"; var calls = 0;
            var service = new ModelDiscoveryService((settings, _) =>
            {
                calls++; Assert(settings.BaseUrl == "https://login-model.invalid/v1" && settings.ApiKey == "fake-api-key");
                return Task.FromResult(new ModelCatalogResult(["one", "one", "two", "", " ", "bad\nvalue", new string('x', 513)], 7));
            });
            var read = await service.ReadAsync(f.Home, draft.Request().Settings, () => (f.Home, draft.Request().Settings));
            Assert(calls == 1 && read.Models.SequenceEqual(new[] { "one", "two" }) && draft.Model == "keep-my-model" && f.Before.SameContents(f.Store.Load(f.Home)));
        }));
        tests.Add(("model discovery accepts model-only drafts but rejects changed endpoint key mode or workspace", async () =>
        {
            var f = new Fixture(root); using var draft = f.Draft();
            foreach (var mutation in new Action[] { () => draft.BaseUrl = "https://changed-model.invalid/v1", () => draft.ApiKey = "fake-changed-key", () => draft.Mode = ConnectionMode.OfficialWithApi })
            {
                draft.Load(f.Before); var settings = draft.Request().Settings;
                var service = new ModelDiscoveryService((_, _) => { mutation(); return Task.FromResult(new ModelCatalogResult(["result"], 1)); });
                await Throws<OperationCanceledException>(() => service.ReadAsync(f.Home, settings, () => (f.Home, draft.Request().Settings)));
            }
            draft.Load(f.Before);
            var readOnly = new ModelDiscoveryService((_, _) => { draft.Model = "new-draft-model"; return Task.FromResult(new ModelCatalogResult([], 1)); });
            var read = await readOnly.ReadAsync(f.Home, draft.Request().Settings, () => (f.Home, draft.Request().Settings)); Assert(read.Models.Count == 0 && draft.Model == "new-draft-model");
            await Throws<OperationCanceledException>(() => readOnly.ReadAsync(f.Home, draft.Request().Settings, () => (Path.Combine(f.Home, "other"), draft.Request().Settings)));
            Assert(f.Before.SameContents(f.Store.Load(f.Home)));
        }));
        tests.Add(("cancelled or failed model discovery returns no replacement selection or disk writes", async () =>
        {
            var f = new Fixture(root); using var draft = f.Draft(); using var cancellation = new CancellationTokenSource();
            var cancelled = new ModelDiscoveryService((_, _) => { cancellation.Cancel(); return Task.FromResult(new ModelCatalogResult(["ignored"], 0)); });
            await Throws<OperationCanceledException>(() => cancelled.ReadAsync(f.Home, draft.Request().Settings, () => (f.Home, draft.Request().Settings), cancellation.Token));
            var failed = new ModelDiscoveryService((_, _) => throw new HttpRequestException("fixture failure"));
            await Throws<HttpRequestException>(() => failed.ReadAsync(f.Home, draft.Request().Settings, () => (f.Home, draft.Request().Settings)));
            Assert(draft.Model == "old-model" && !draft.HasDraft && f.Before.SameContents(f.Store.Load(f.Home)));
        }));
    }

    private sealed class Fixture
    {
        internal readonly string Home;
        internal string ConfigPath => Path.Combine(Home, "config.toml");
        internal string AuthPath => Path.Combine(Home, "auth.json");
        internal readonly MemoryEnvironment Environment = new();
        internal readonly ConfigStore Store;
        internal readonly ConfigurationSaveService Save;
        internal readonly OfficialLoginService Login;
        internal ConfigSnapshot Before = null!;
        internal Fixture(string root, bool official = false)
        {
            Home = Path.Combine(root, "login-model-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Home);
            File.WriteAllText(ConfigPath, "# login model fixture\nmodel='old-model'\nopenai_base_url='https://login-model.invalid/v1'\n");
            File.WriteAllBytes(AuthPath, official ? Official("fake-login") : Encoding.UTF8.GetBytes("{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-api-key\"}"));
            Store = new(Environment, _ => null); var session = new WorkspaceSession(Home); var operations = new WorkspaceApplyCoordinator(session);
            Save = new(Store, operations); Login = new(Store); Reload(); session.Loaded(Home, Before);
        }
        internal void Reload() => Before = Store.Load(Home);
        internal ConnectionDraftViewModel Draft() { var draft = new ConnectionDraftViewModel(Store); draft.Load(Before); return draft; }
        internal FakeLoginRuntime Runtime() => new(() => Store.Load(Home));
    }
    private sealed class MemoryEnvironment : IUserEnvironment
    { internal string? Value; public string? Read() => Value; public void Write(string? value) => Value = value; }
    private sealed class FakeLoginRuntime(Func<ConfigSnapshot> load) : IOfficialLoginRuntime
    {
        internal byte[]? Backup; internal int Loads, BackupReads; internal Action? OnBackup;
        public Task<ConfigSnapshot> LoadAsync(string home, CancellationToken cancellation) { Loads++; return Task.FromResult(load()); }
        public Task<byte[]?> ReadBackupAsync(string home, CancellationToken cancellation) { BackupReads++; OnBackup?.Invoke(); return Task.FromResult(Backup); }
    }
    private static byte[] Official(string value) => JsonSerializer.SerializeToUtf8Bytes(new { auth_mode = "chatgpt", tokens = new {
        account_id = "fake-account", access_token = value, refresh_token = value, id_token = value } });
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Official login or model behavior regression"); }
    private static async Task Throws<T>(Func<Task> run) where T : Exception
    { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
