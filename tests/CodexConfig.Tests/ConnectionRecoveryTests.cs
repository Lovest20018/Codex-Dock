using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexConfig.Core;

internal static class ConnectionRecoveryTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        void Assert(bool value) { if (!value) throw new InvalidOperationException("Connection recovery assertion failed"); }
        void Throws(Action action) { try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { return; } throw new Exception("Expected recovery rejection"); }
        (string Home, string Thread, string State, string Catalog) Fixture()
        {
            var home = Path.Combine(root, "connections-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home); Directory.CreateDirectory(Path.Combine(home, "sqlite")); Directory.CreateDirectory(Path.Combine(home, "sessions"));
            var thread = Guid.NewGuid().ToString(); var state = Path.Combine(home, "state_5.sqlite"); var catalog = Path.Combine(home, "sqlite", "codex.db");
            using (var db = new NativeSqlite(state, true, true))
            {
                db.Execute("CREATE TABLE threads(id TEXT PRIMARY KEY, model_provider TEXT, model TEXT, reasoning_effort TEXT)");
                db.Execute("INSERT INTO threads VALUES(?1,'legacy','selected-model','max')", thread);
            }
            using (var db = new NativeSqlite(catalog, true, true))
            {
                db.Execute("CREATE TABLE local_thread_catalog_hosts(host_id TEXT PRIMARY KEY, host_kind TEXT)");
                db.Execute("INSERT INTO local_thread_catalog_hosts VALUES('this-machine','local'),('remote','ssh')");
                db.Execute("CREATE TABLE local_thread_catalog(host_id TEXT,thread_id TEXT,model_provider TEXT,PRIMARY KEY(host_id,thread_id))");
                db.Execute("INSERT INTO local_thread_catalog VALUES('this-machine',?1,'legacy'),('remote',?1,'remote-provider')", thread);
            }
            File.WriteAllText(Path.Combine(home, "config.toml"), "model='selected-model'\nmodel_provider='openai'\nopenai_base_url='https://fixture.invalid/v1'\n");
            File.WriteAllText(Path.Combine(home, "sessions", "history.jsonl"), "private-fixture-history\nunchanged\n");
            return (home, thread, state, catalog);
        }
        string Provider(string file, string thread, bool catalog = false) { using var db = new NativeSqlite(file); return db.Query(catalog ? "SELECT model_provider FROM local_thread_catalog WHERE host_id='this-machine' AND thread_id=?1" : "SELECT model_provider FROM threads WHERE id=?1", thread).Single()[0]!; }
        Test("local file identity accepts aliases while rejecting copies and replacements", () =>
        {
            var path = Path.Combine(root, "identity-" + Guid.NewGuid().ToString("N")); File.WriteAllText(path, "fixture"); var alias = path + ".hardlink"; var copy = path + ".copy"; File.Copy(path, copy);
            Assert(CreateHardLink(alias, path, IntPtr.Zero));
            using var held = WindowsFileIdentity.Open(path); var identity = WindowsFileIdentity.Read(held);
            Assert(identity is not null && CodexDesktop.SamePath(path, alias) && CodexDesktop.SamePath(path, "\\\\?\\" + Path.GetFullPath(path)) && !CodexDesktop.SamePath(path, copy));
            File.Move(path, path + ".old"); File.WriteAllText(path, "fixture");
            Assert(WindowsFileIdentity.Read(held) == identity && WindowsFileIdentity.Read(path) != identity && !CodexDesktop.SamePath(path, alias));
            Assert(!CodexDesktop.SamePath(path, path + ".missing"));
        });
        Test("thread provider preparation updates both local indexes and preserves history and model fields", () =>
        {
            var (home, thread, state, catalog) = Fixture(); var history = File.ReadAllBytes(Path.Combine(home, "sessions", "history.jsonl")); var config = File.ReadAllBytes(Path.Combine(home, "config.toml")); var validations = 0;
            var result = ThreadProviderStore.Prepare(home, thread, "legacy", "openai", () => validations++);
            Assert(result.ChangedRows == 2 && File.Exists(result.Backup) && validations >= 3 && Provider(state, thread) == "openai" && Provider(catalog, thread, true) == "openai");
            using var db = new NativeSqlite(state); Assert(db.Query("SELECT model,reasoning_effort FROM threads WHERE id=?1", thread).Single().SequenceEqual(["selected-model", "max"]));
            using var catalogDb = new NativeSqlite(catalog); Assert(catalogDb.Query("SELECT model_provider FROM local_thread_catalog WHERE host_id='remote' AND thread_id=?1", thread).Single()[0] == "remote-provider");
            Assert(history.SequenceEqual(File.ReadAllBytes(Path.Combine(home, "sessions", "history.jsonl"))) && config.SequenceEqual(File.ReadAllBytes(Path.Combine(home, "config.toml"))));
            Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(result.Backup!)).Contains(thread));
            Assert(ThreadProviderStore.Prepare(home, thread, "openai", "legacy", () => { }).ChangedRows == 2 && Provider(state, thread) == "legacy");
        });
        Test("provider and database ambiguity reject edits before writing", () =>
        {
            var (home, thread, state, catalog) = Fixture();
            Throws(() => ThreadProviderStore.Prepare(home, thread, "wrong", "openai", () => { })); Assert(Provider(state, thread) == "legacy");
            using (var db = new NativeSqlite(catalog, true)) db.Execute("UPDATE local_thread_catalog SET model_provider='external' WHERE host_id='this-machine'");
            Throws(() => ThreadProviderStore.Prepare(home, thread, "legacy", "openai", () => { })); Assert(Provider(state, thread) == "legacy");
            File.Copy(state, Path.Combine(home, "state_6.sqlite"));
            Throws(() => ThreadProviderStore.Prepare(home, thread, "legacy", "openai", () => { }));
        });
        Test("failed provider preparation rolls back and retains a recoverable encrypted journal", () =>
        {
            var (home, thread, state, catalog) = Fixture(); var validations = 0;
            Throws(() => ThreadProviderStore.Prepare(home, thread, "legacy", "openai", () => { if (++validations == 3) throw new IOException("fixture target changed before commit"); }));
            Assert(Provider(state, thread) == "legacy" && Provider(catalog, thread, true) == "legacy");
            var folder = Path.Combine(home, "codex-dock-thread-backups"); Assert(Directory.GetFiles(folder, "*.pending").Length == 1);
            // Simulate one WAL database committed before a crash; the recovery must reconcile both rows.
            using (var db = new NativeSqlite(state, true)) db.Execute("UPDATE threads SET model_provider='openai' WHERE id=?1", thread);
            var result = ThreadProviderStore.Prepare(home, thread, "legacy", "openai", () => { });
            Assert(result.Recovered && Provider(state, thread) == "openai" && Provider(catalog, thread, true) == "openai" && Directory.GetFiles(folder, "*.pending").Length == 0 && Directory.GetFiles(folder, "*.recovered").Length == 1);
        });
        Test("pending recovery never overwrites an unrelated provider edit", () =>
        {
            var (home, thread, state, _) = Fixture(); var validations = 0;
            Throws(() => ThreadProviderStore.Prepare(home, thread, "legacy", "openai", () => { if (++validations == 3) throw new IOException("fixture interrupt"); }));
            using (var db = new NativeSqlite(state, true)) db.Execute("UPDATE threads SET model_provider='external' WHERE id=?1", thread);
            Throws(() => ThreadProviderStore.Prepare(home, thread, "external", "openai", () => { })); Assert(Provider(state, thread) == "external");
            Assert(Directory.GetFiles(Path.Combine(home, "codex-dock-thread-backups"), "*.pending").Length == 1);
        });
        Test("connection recovery preferences are default off and home scoped", () =>
        {
            var a = new DesktopConnectionSettings(Path.Combine(root, "a"), root); var b = new DesktopConnectionSettings(Path.Combine(root, "b"), root);
            Assert(!a.Load() && !b.Load()); a.Save(true); Assert(a.Load() && !b.Load()); a.Save(false); Assert(!a.Load());
            File.WriteAllText(a.SettingsPath, "{\"enabled\":\"invalid\"}"); Throws(() => a.Load());
        });
        Test("connection generation follows routes and keys while auth and catalog changes require reload", () =>
        {
            var home = Path.Combine(root, "generation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            var store = new ConfigStore(new FixtureEnvironment(), _ => null); store.Apply(store.Plan(store.Load(home), new("https://a.invalid/v1", "fake-key", "model")));
            var baseline = store.Load(home); var backend = new DesktopConnectionBackend(store, baseline, () => true); var first = backend.Inspect();
            var doc = new TomlDocument(baseline.Config); doc.Set("openai_base_url", "https://b.invalid/v1"); File.WriteAllBytes(Path.Combine(home, "config.toml"), doc.Bytes);
            var changed = backend.Inspect(); Assert(first.Generation != changed.Generation && !changed.RestartRequired);
            doc.Set("chatgpt_base_url", "https://accounts.invalid"); File.WriteAllBytes(Path.Combine(home, "config.toml"), doc.Bytes); Assert(backend.Inspect().RestartRequired);
            doc.Remove("chatgpt_base_url"); File.WriteAllBytes(Path.Combine(home, "config.toml"), doc.Bytes);
            File.WriteAllText(Path.Combine(home, "auth.json"), new ConnectionSettings("https://b.invalid/v1", "different-key", "model").RenderAuth());
            Assert(backend.Inspect().RestartRequired);
            File.WriteAllBytes(Path.Combine(home, "auth.json"), baseline.Auth!); doc.Set("model_catalog_json", "catalog.json"); File.WriteAllBytes(Path.Combine(home, "config.toml"), doc.Bytes); File.WriteAllText(Path.Combine(home, "catalog.json"), "{\"models\":[]}");
            Assert(backend.Inspect().RestartRequired);
        });
        Test("native manager hook requires the validated manager shape", () =>
        {
            const string source = "Manager(e,t,n){class M{async sendRequest(e,t,n){this.assertActive();let r=await this.requestClient.sendRequest(e,t,n).catch(n=>{throw n});return r}}}";
            var patched = DesktopConnectionPatch.Rewrite(source);
            Assert(patched.Contains("__codexDockConnection.dispatch(this,e,t,n") && patched.Contains("this.requestClient.sendRequest(__dockMethod,__dockParameters,__dockOptions)") && DesktopConnectionPatch.Rewrite(patched) == patched);
            Throws(() => DesktopConnectionPatch.Rewrite(source.Replace("assertActive", "unknown")));
            Throws(() => DesktopConnectionPatch.Rewrite(source + source));
        });
        Test("native manager hook passes the optional abort signal to both the controller and original request", () =>
        {
            const string source = "async sendRequest(e,t,n,r){this.assertActive();let p=null==r?this.requestClient.sendRequest(e,t,n):this.requestClient.sendRequest(e,t,n,r),a=await p.catch(n=>{throw n});return a}";
            var patched = DesktopConnectionPatch.Rewrite(source);
            Assert(patched.Contains("__codexDockConnection.dispatch(this,e,t,n") && patched.Contains("this.requestClient.sendRequest(__dockMethod,__dockParameters,__dockOptions,r)),r):"));
            Assert(patched.Contains(",a=await p.catch(") && DesktopConnectionPatch.Count(source + source) == 2);
            Throws(() => DesktopConnectionPatch.Rewrite(source.Replace("await p.catch", "await t.catch")));
        });
    }
    private sealed class FixtureEnvironment : IUserEnvironment { public string? Read() => null; public void Write(string? value) { } }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateHardLinkW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string name, string existing, IntPtr security);
}
