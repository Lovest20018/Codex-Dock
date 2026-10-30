using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexConfig.Core;

internal static class LegacyConnectionTests
{
    internal static byte[] Auth() => Encoding.UTF8.GetBytes("{\"auth_mode\":\"chatgpt\",\"tokens\":{\"id_token\":\"fake-id-token\",\"access_token\":\"fake-access-token\",\"refresh_token\":\"fake-refresh-token\"}}");

    internal static void WriteFixture(string home)
    {
        Directory.CreateDirectory(home);
        var token = new string('A', 64);
        File.WriteAllText(Path.Combine(home, "config.toml"), "# retained preference\nmodel='legacy-model'\nmodel_provider='codex_config_mixed'\nopenai_base_url='http://127.0.0.1:48125/v1'\ncli_auth_credentials_store='file'\nweb_search='cached'\n[model_providers.codex_config_mixed]\nname='Codex Config connection'\nbase_url='http://127.0.0.1:48125/v1'\nwire_api='responses'\nrequires_openai_auth=true\nexperimental_bearer_token='" + token + "'\nsupports_websockets=false\n");
        File.WriteAllBytes(Path.Combine(home, "auth.json"), Auth());
        var plain = JsonSerializer.SerializeToUtf8Bytes(new { BaseUrl = "https://legacy.example/backend-api/codex", ApiKey = "fake-legacy-pat", Kind = 0, LocalToken = token, Port = 48125, UseSystemProxy = true });
        try { File.WriteAllBytes(Path.Combine(home, MixedConnection.FileName), ProtectedData.Protect(plain, Encoding.UTF8.GetBytes("CodexConfig.mixed.v1"), DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
        string Home() => Path.Combine(root, Guid.NewGuid().ToString("N"));
        Test("legacy connection reads its saved PAT without changing files", () =>
        {
            var home = Home(); WriteFixture(home);
            var store = new ConfigStore(new FakeEnvironment(), _ => null);
            var before = store.Load(home); var settings = store.ReadSettings(before);
            Assert(settings.Mode == ConnectionMode.OfficialWithApi && settings.BaseUrl == "https://legacy.example/backend-api/codex" && settings.ApiKey == "fake-legacy-pat");
            Assert(before.SameContents(store.Load(home)) && !Directory.Exists(Path.Combine(home, "codex-config-backups")));
        });
        Test("legacy connection converts to PAT with an encrypted reversible backup", () =>
        {
            var home = Home(); WriteFixture(home);
            var environment = new FakeEnvironment(); var store = new ConfigStore(environment, _ => null);
            var before = store.Load(home);
            var settings = store.ReadSettings(before) with { Mode = ConnectionMode.Direct, Authentication = AuthenticationChoice.Pat, PreservePreferenceValues = true };
            store.Apply(store.Plan(before, settings));
            var saved = store.Load(home); var document = new TomlDocument(saved.Config);
            Assert(saved.Bridge is null && document.String("model_provider") == "openai" && document.String("openai_base_url") == "https://legacy.example/backend-api/codex");
            Assert(document.String("web_search") == "cached" && document.Text.Contains("# retained preference") && !document.Text.Contains("48125") && !document.Text.Contains(new string('A', 64)));
            using var auth = JsonDocument.Parse(saved.Auth!);
            Assert(auth.RootElement.GetProperty("personal_access_token").GetString() == "fake-legacy-pat" && !auth.RootElement.TryGetProperty("tokens", out _));
            Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(store.LatestBackup(home)!)).Contains("fake-legacy-pat"));
            store.Restore(saved);
            Assert(before.SameContents(store.Load(home)));
        });
        Test("new connections cannot create an account login or gateway", () =>
        {
            var store = new ConfigStore(new FakeEnvironment(), _ => null);
            var settings = new ConnectionSettings("https://pat.example/backend-api/codex", "fake-pat", "model") { Mode = ConnectionMode.OfficialWithApi };
            try { store.Plan(new ConfigSnapshot(Home(), null, Auth(), null), settings); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("The PAT editor must reject creation of removed account and gateway connections.");
        });
        Test("PAT saves and reloads without reaching a model service", () =>
        {
            var home = Home(); var store = new ConfigStore(new FakeEnvironment(), _ => null);
            var settings = new ConnectionSettings("https://offline.invalid/backend-api/codex", "fake-offline-pat", "offline-model");
            store.Apply(store.Plan(store.Load(home), settings));
            var saved = store.Load(home); var read = store.ReadSettings(saved);
            Assert(read.ApiKey == "fake-offline-pat" && read.Model == "offline-model" && saved.Bridge is null);
            store.Apply(store.Plan(saved, read with { ApiKey = "fake-updated-pat" }));
            Assert(store.ReadSettings(store.Load(home)).ApiKey == "fake-updated-pat");
        });
    }

    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Legacy PAT migration assertion failed."); }
}
