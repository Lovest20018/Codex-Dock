using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexConfig.Core;

internal static class ConfigWriteConcurrencyTests
{
    private const string Pipe = "mcp_servers.node_repl.env.SKY_CUA_NATIVE_PIPE_DIRECTORY";
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        (ConfigStore Store, string Home, EnvironmentFixture Env) Setup()
        {
            var home = Path.Combine(root, "write-concurrency-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "config.toml"), "# original\nmodel='main'\nopenai_base_url='https://fixture.invalid/v1'\n[mcp_servers.node_repl.env]\nSKY_CUA_NATIVE_PIPE_DIRECTORY='" + Endpoint(1) + "'\n");
            File.WriteAllBytes(Path.Combine(home, "auth.json"), Login(1));
            var env = new EnvironmentFixture(); return (new(env, _ => null), home, env);
        }
        void Refresh(string home, int version)
        {
            var doc = new TomlDocument(File.ReadAllBytes(Path.Combine(home, "config.toml"))); doc.Set(Pipe, Endpoint(version));
            File.WriteAllText(Path.Combine(home, "config.toml"), doc.Text + "# retained runtime comment " + version + "\n");
            File.WriteAllBytes(Path.Combine(home, "auth.json"), Login(version));
        }
        ConfigPlan Edit(ConfigStore store, string home) => store.Plan(store.Load(home), store.ReadSettings(store.Load(home)),
            new Dictionary<string, string?> { ["model_reasoning_effort"] = "'high'" }, updateConnection: false);

        Test("configuration save rebases a reviewed edit while preserving current runtime pipe, comments and refreshed login", () =>
        {
            var (store, home, _) = Setup(); var plan = Edit(store, home); Refresh(home, 2);
            var fresh = store.Load(home); var backup = store.Apply(plan); var saved = store.Load(home);
            Assert(new TomlDocument(saved.Config).String("model_reasoning_effort") == "high" && new TomlDocument(saved.Config).Get(Pipe) as string == Endpoint(2));
            Assert(saved.Auth!.SequenceEqual(fresh.Auth!) && new TomlDocument(saved.Config).Text.Contains("# retained runtime comment 2"));
            var record = ReadBackup(backup);
            Assert(Convert.FromBase64String(record["BeforeConfig"]!.GetValue<string>()).SequenceEqual(fresh.Config!) &&
                Convert.FromBase64String(record["BeforeAuth"]!.GetValue<string>()).SequenceEqual(fresh.Auth!));
            Assert(record["AfterSnapshotVersion"]!.GetValue<int>() == 1 && !store.HasPending(home));
        });
        Test("mixed connection save preserves a verified fresh login while applying the reviewed credential cleanup", () =>
        {
            var (store, home, _) = Setup();
            var settings = new ConnectionSettings("https://model.invalid/v1", "fake-mixed-concurrency", "main") { Mode = ConnectionMode.OfficialWithApi, Kind = ConnectionKind.ResponsesApi };
            var plan = store.Plan(store.Load(home), settings); Refresh(home, 2); var fresh = store.Load(home);
            store.Apply(plan);
            Assert(store.Load(home).Auth!.SequenceEqual(OfficialCredentials.ForMixed(fresh.Auth)) && store.ReadSettings(store.Load(home)).Mode == ConnectionMode.OfficialWithApi);
        });
        Test("backup restore and interrupted recovery preserve runtime updates and current same-account tokens", () =>
        {
            foreach (var interrupted in new[] { false, true })
            {
                var (store, home, _) = Setup(); var backup = store.Apply(Edit(store, home)); Refresh(home, 3);
                var fresh = store.Load(home);
                if (interrupted)
                {
                    File.WriteAllText(Path.Combine(home, "codex-config-backups", "pending.json"), Path.GetFileName(backup)); store.Recover(home);
                }
                else store.Restore(fresh);
                var restored = store.Load(home); var doc = new TomlDocument(restored.Config);
                Assert(doc.String("model") == "main" && doc.Get("model_reasoning_effort") is null && doc.Get(Pipe) as string == Endpoint(3));
                Assert(doc.Text.Contains("# retained runtime comment 3") && restored.Auth!.SequenceEqual(fresh.Auth!) && !store.HasPending(home));
            }
        });
        Test("configuration writes still reject changed settings, another account and edited runtime endpoints", () =>
        {
            foreach (var kind in new[] { "model", "account", "pipe" })
            {
                var (store, home, _) = Setup(); var plan = Edit(store, home);
                if (kind == "account") File.WriteAllBytes(Path.Combine(home, "auth.json"), Login(2, "other-account"));
                else
                {
                    var doc = new TomlDocument(store.Load(home).Config);
                    doc.Set(kind == "model" ? "model" : Pipe, kind == "model" ? "external-model" : @"C:\external-directory");
                    File.WriteAllBytes(Path.Combine(home, "config.toml"), doc.Bytes);
                }
                var external = store.Load(home); Throws<InvalidOperationException>(() => store.Apply(plan));
                Assert(external.SameContents(store.Load(home)) && store.LatestBackup(home) is null);
            }
            var (recovery, directory, _) = Setup(); var backup = recovery.Apply(Edit(recovery, directory));
            var changed = new TomlDocument(recovery.Load(directory).Config); changed.Set("service_tier", "ultrafast");
            File.WriteAllBytes(Path.Combine(directory, "config.toml"), changed.Bytes);
            Throws<InvalidOperationException>(() => recovery.Restore(recovery.Load(directory)));
            File.WriteAllText(Path.Combine(directory, "codex-config-backups", "pending.json"), Path.GetFileName(backup));
            Throws<IOException>(() => recovery.Recover(directory)); Assert(recovery.HasPending(directory));
        });
        Test("legacy backup hashes accept only an exactly reconstructed runtime pipe rotation", () =>
        {
            var (store, home, _) = Setup(); var backup = store.Apply(Edit(store, home)); var record = ReadBackup(backup);
            foreach (var field in new[] { "AfterSnapshotVersion", "AfterConfig", "AfterAuth" }) record.Remove(field);
            WriteBackup(backup, record);
            var doc = new TomlDocument(store.Load(home).Config); doc.Set(Pipe, Endpoint(2));
            File.WriteAllBytes(Path.Combine(home, "config.toml"), doc.Bytes); File.WriteAllBytes(Path.Combine(home, "auth.json"), Login(2));
            store.Restore(store.Load(home));
            Assert(new TomlDocument(store.Load(home).Config).Get(Pipe) as string == Endpoint(2) && store.Load(home).Auth!.SequenceEqual(Login(2)));
        });
        Test("transaction validation retries runtime rotation before writing and accepts refresh after writing", () =>
        {
            var (store, home, env) = Setup(); var plan = Edit(store, home);
            env.OnRead = () => { env.OnRead = null; Refresh(home, 2); };
            store.Apply(plan);
            Assert(new TomlDocument(store.Load(home).Config).Get(Pipe) as string == Endpoint(2) && store.Load(home).Auth!.SequenceEqual(Login(2)));
            var next = Edit(store, home) with { AuthApiBaseUrl = "https://auth.fixture.invalid" };
            env.OnWrite = () => { env.OnWrite = null; Refresh(home, 3); };
            store.Apply(next);
            Assert(new TomlDocument(store.Load(home).Config).Get(Pipe) as string == Endpoint(3) && store.Load(home).Auth!.SequenceEqual(Login(3)) && !store.HasPending(home));
        });
    }

    private static string Endpoint(int version) => @"\\.\pipe\codex-computer-use-" + version.ToString("x8") + "-2222-3333-4444-555555555555";
    private static byte[] Login(int version, string account = "same-account")
    {
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { iss = "https://issuer.invalid", sub = account })).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return JsonSerializer.SerializeToUtf8Bytes(new { auth_mode = "chatgpt", last_refresh = version.ToString(), tokens = new
        { account_id = account, id_token = "fixture." + payload + ".signature", access_token = "fake-access-" + version, refresh_token = "fake-refresh-" + version } });
    }
    private static JsonObject ReadBackup(string path)
    {
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), Encoding.UTF8.GetBytes("CodexConfig.backup.v1"), DataProtectionScope.CurrentUser);
        try { return JsonNode.Parse(bytes)!.AsObject(); } finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static void WriteBackup(string path, JsonObject record)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
        try { File.WriteAllBytes(path, ProtectedData.Protect(bytes, Encoding.UTF8.GetBytes("CodexConfig.backup.v1"), DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private sealed class EnvironmentFixture : IUserEnvironment
    {
        private string? value;
        internal Action? OnRead, OnWrite;
        public string? Read() { OnRead?.Invoke(); return value; }
        public void Write(string? desired) { value = desired; OnWrite?.Invoke(); }
    }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Configuration concurrency regression"); }
    private static void Throws<T>(Action run) where T : Exception
    { try { run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
