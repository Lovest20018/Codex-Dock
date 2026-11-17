using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class WorkspaceReadinessTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        Test("first run reads a missing directory without creating files or permitting desktop application", () =>
        {
            var f = new Fixture(root); f.Shell.Load(f.Home);
            Assert(f.Shell.ConfigurationAvailable && f.Shell.NeedsConnectionSetup && !f.Shell.CanApplyConfiguration && !f.Shell.IsBusy);
            Assert(f.Shell.Snapshot is { Config: null, Auth: null } && f.Shell.SetupGuide.Message.Contains("官方账号") && !Directory.Exists(f.Directory));
            f.Shell.Load(f.Home); Assert(!Directory.Exists(f.Directory));
        });
        Test("first run preserves ordinary settings and requires credentials without inventing authentication", () =>
        {
            var f = new Fixture(root); Directory.CreateDirectory(f.Home); var path = Path.Combine(f.Home, "config.toml");
            const string config = "# existing preferences\nmodel='existing-model'\nmodel_reasoning_effort='high'\n"; File.WriteAllText(path, config);
            f.Shell.Load(f.Home); Assert(f.Shell.NeedsConnectionSetup && f.Shell.ModelLabel == "existing-model" && !f.Shell.CanApplyConfiguration);
            Assert(File.ReadAllText(path) == config && !File.Exists(Path.Combine(f.Home, "auth.json")) && !Directory.Exists(Path.Combine(f.Home, "codex-config-backups")));
        });
        Test("first run retains official login external provider and system credential workflows", () =>
        {
            var f = new Fixture(root); Directory.CreateDirectory(f.Home); var path = Path.Combine(f.Home, "config.toml"); var auth = Path.Combine(f.Home, "auth.json");
            File.WriteAllText(path, "model='official-default'\n"); var official = Encoding.UTF8.GetBytes("{\"auth_mode\":\"chatgpt\",\"tokens\":{\"access_token\":\"fake-access\",\"refresh_token\":\"fake-refresh\",\"id_token\":\"fake-id\"}}"); File.WriteAllBytes(auth, official);
            f.Shell.Load(f.Home); Assert(!f.Shell.NeedsConnectionSetup && f.Shell.CanApplyConfiguration && f.Shell.ConnectionLabel == "官方账号" && f.Shell.SetupGuide.Message.Contains("尚未在线验证"));
            Assert(official.SequenceEqual(File.ReadAllBytes(auth))); File.Delete(auth);
            File.WriteAllText(path, "model_provider='external'\n[model_providers.external]\nbase_url='https://provider.invalid/v1'\n"); f.Shell.Load(f.Home);
            Assert(f.Shell.CanApplyConfiguration && f.Shell.SetupGuide.Message.Contains("未执行外部认证"));
            File.WriteAllText(path, "cli_auth_credentials_store='keyring'\n"); f.Shell.Load(f.Home); Assert(f.Shell.CanApplyConfiguration && !f.Shell.NeedsConnectionSetup);
        });
        Test("first run recognizes inherited process credentials without writing them to disk", () =>
        {
            var f = new Fixture(root, name => name == "OPENAI_API_KEY" ? "fake-inherited-key" : null); f.Shell.Load(f.Home);
            Assert(f.Shell.CanApplyConfiguration && !f.Shell.NeedsConnectionSetup && f.Shell.ConnectionLabel == "API Key 直连" && !Directory.Exists(f.Directory));
            Assert(!JsonSerializer.Serialize(f.Shell.SetupGuide).Contains("fake-inherited-key"));
        });
        Test("failed workspace reads discard earlier snapshots runtime state and busy flags", () =>
        {
            var f = new Fixture(root); f.WriteValid(); f.Shell.Load(f.Home); f.Shell.Session.SetRuntime(new(true, 3)); f.Shell.Session.MarkDraft(true);
            var brokenHome = Path.Combine(f.Directory, "broken"); Directory.CreateDirectory(brokenHome); var path = Path.Combine(brokenHome, "config.toml");
            const string broken = "fake_private_key='do-not-display'\nmodel=[unfinished"; File.WriteAllText(path, broken);
            f.Shell.Load(brokenHome);
            Assert(!f.Shell.IsBusy && !f.Shell.ConfigurationAvailable && !f.Shell.CanApplyConfiguration && f.Shell.Snapshot is null && f.Shell.Session.State.Runtime is null && !f.Shell.Session.State.HasDraft);
            Assert(f.Shell.Home == brokenHome && f.Shell.SetupGuide.Kind == WorkspaceSetupKind.Unreadable && !f.Shell.Error.Contains("do-not-display") && File.ReadAllText(path) == broken);
            File.WriteAllText(path, "model='repaired'\n"); f.Shell.Load(brokenHome); Assert(f.Shell.ConfigurationAvailable && f.Shell.NeedsConnectionSetup && !f.Shell.IsBusy);
        });
        Test("encrypted workspace read failures release loading and can be repaired in the same shell", () =>
        {
            var f = new Fixture(root); Directory.CreateDirectory(f.Home);
            File.WriteAllText(Path.Combine(f.Home, "config.toml"), "model_provider='codex_config_mixed'\n");
            var path = Path.Combine(f.Home, MixedConnection.FileName); byte[] corrupt = [1, 2, 3, 4, 5]; File.WriteAllBytes(path, corrupt);
            f.Shell.Load(f.Home); Assert(!f.Shell.IsBusy && !f.Shell.ConfigurationAvailable && !f.Shell.CanApplyConfiguration && f.Shell.Snapshot is null && f.Shell.Error.Contains("解密"));
            Assert(corrupt.SequenceEqual(File.ReadAllBytes(path))); File.Delete(path); f.WriteValid(); f.Shell.Load(f.Home);
            Assert(f.Shell.CanApplyConfiguration && f.Shell.Error.Length == 0 && !f.Shell.IsBusy);
        });
        Test("malformed saved auth supplied by reload cannot leave a prior snapshot available", () =>
        {
            var f = new Fixture(root); f.WriteValid(); f.Shell.Load(f.Home);
            var path = Path.Combine(f.Home, "auth.json"); File.WriteAllText(path, "{\"fake_private\":\"never-show\", unfinished");
            f.Shell.Load(f.Store.Load(f.Home)); Assert(!f.Shell.ConfigurationAvailable && !f.Shell.IsBusy && f.Shell.Snapshot is null && f.Shell.Error.Contains("auth.json") && !f.Shell.Error.Contains("never-show"));
            f.WriteValid(); f.Shell.Load(f.Store.Load(f.Home)); Assert(f.Shell.CanApplyConfiguration && f.Shell.Error.Length == 0);
        });
        Test("a workspace path naming a file remains unreadable without replacing that file", () =>
        {
            var f = new Fixture(root); Directory.CreateDirectory(f.Directory); File.WriteAllText(f.Home, "preserve file"); f.Shell.Load(f.Home);
            Assert(!f.Shell.ConfigurationAvailable && !f.Shell.IsBusy && f.Shell.Snapshot is null && File.ReadAllText(f.Home) == "preserve file");
        });
        Test("first connection review is read only cancelled confirmation is inert and explicit save creates encrypted backup", () =>
        {
            var f = new Fixture(root); f.Shell.Load(f.Home); var before = f.Shell.Snapshot!; var draft = new ConnectionDraftViewModel(f.Store); draft.Load(before);
            draft.BaseUrl = "https://first-save.invalid/v1"; draft.ApiKey = "fake-first-save-key"; draft.Model = "first-selected-model"; draft.Kind = ConnectionKind.ResponsesApi;
            var service = new ConfigurationSaveService(f.Store, new(f.Shell.Session)); var review = service.PrepareReview(draft.Request());
            Assert(!Directory.Exists(f.Home) && !JsonSerializer.Serialize(review.Changes).Contains("fake-first-save-key"));
            using (var cancelled = new CancellationTokenSource())
            { cancelled.Cancel(); Assert(!service.Save(review, before, () => f.Home, () => { }, cancelled.Token).Succeeded && !Directory.Exists(f.Home)); }
            review = service.PrepareReview(draft.Request()); var result = service.Save(review, before, () => f.Home, () => { });
            Assert(result.Succeeded && result.Saved is not null && result.BackupPath is { } backup && File.Exists(backup));
            f.Shell.ConfigurationSaved(result.Saved!); Assert(f.Shell.CanApplyConfiguration && !f.Shell.NeedsConnectionSetup && f.Shell.ModelLabel == "first-selected-model");
            Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(result.BackupPath!)).Contains("fake-first-save-key"));
        });
        Test("external first-run writes invalidate an earlier connection review", () =>
        {
            var f = new Fixture(root); f.Shell.Load(f.Home); var draft = new ConnectionDraftViewModel(f.Store); draft.Load(f.Shell.Snapshot!);
            draft.BaseUrl = "https://first-conflict.invalid/v1"; draft.ApiKey = "fake-first-conflict"; draft.Model = "first-model";
            var service = new ConfigurationSaveService(f.Store, new(f.Shell.Session)); var review = service.PrepareReview(draft.Request());
            Directory.CreateDirectory(f.Home); File.WriteAllText(Path.Combine(f.Home, "config.toml"), "model='external-first-config'\n"); var external = f.Store.Load(f.Home);
            Assert(!service.Save(review, draft.Snapshot!, () => f.Home, () => { }).Succeeded && external.SameContents(f.Store.Load(f.Home)) && draft.HasDraft && !File.Exists(Path.Combine(f.Home, "auth.json")));
        });
    }
    private sealed class Fixture
    {
        internal readonly string Directory, Home;
        internal readonly ConfigStore Store;
        internal readonly WorkspaceShellViewModel Shell;
        internal Fixture(string root, Func<string, string?>? environment = null)
        {
            Directory = Path.Combine(root, "readiness-" + Guid.NewGuid().ToString("N")); Home = Path.Combine(Directory, "home");
            Store = new(new NoWrites(), environment ?? (_ => null)); Shell = new(Store, Home, Path.Combine(Directory, "connection-prefs"), Path.Combine(Directory, "speed-prefs"), Path.Combine(Directory, "automatic-prefs"));
        }
        internal void WriteValid()
        { System.IO.Directory.CreateDirectory(Home); File.WriteAllText(Path.Combine(Home, "config.toml"), "model='valid-model'\nopenai_base_url='https://readiness.invalid/v1'\n"); File.WriteAllText(Path.Combine(Home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-readiness-key\"}"); }
    }
    private sealed class NoWrites : IUserEnvironment { public string? Read() => null; public void Write(string? value) => throw new InvalidOperationException("No user environment writes."); }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Workspace readiness regression"); }
}
