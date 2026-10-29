using System.Text;
using CodexConfig.Core;

internal static class LocalStateTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
        byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
        Test("read existing quoted custom provider and preserve its routing on ordinary saves", () =>
        {
            var home = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            var original = Bytes("# existing connection\r\nmodel='original-model'\r\nmodel_provider='team.gateway'\r\nopenai_base_url='https://unused.example/v1'\r\n[model_providers.'team.gateway']\r\nname='Team gateway'\r\nbase_url='https://team.example/v1'\r\nenv_key='TEST_PROVIDER_KEY'\r\nwire_api='responses'\r\n");
            File.WriteAllBytes(Path.Combine(home, "config.toml"), original);
            var environment = new FakeEnvironment();
            var store = new ConfigStore(environment, key => key == "TEST_PROVIDER_KEY" ? "fake-provider-key" : "fake-unrelated-key");
            var before = store.Load(home); var settings = store.ReadSettings(before);
            Assert(settings.ProviderId == "team.gateway" && settings.ProviderName == "Team gateway");
            Assert(settings.BaseUrl == "https://team.example/v1" && settings.Model == "original-model" && settings.ApiKey == "fake-provider-key");
            Assert(settings.Kind == ConnectionKind.ResponsesApi && settings.Authentication == AuthenticationChoice.ApiKey && settings.CredentialSource.Contains("TEST_PROVIDER_KEY"));
            Assert(before.SameContents(store.Load(home)) && before.Auth is null && environment.Read() is null && !Directory.Exists(Path.Combine(home, "codex-config-backups")));
            var plan = store.Plan(before, settings, new Dictionary<string, string?> { ["web_search"] = "'cached'" }, updateConnection: false);
            store.Apply(plan);
            var saved = store.Load(home); var doc = new TomlDocument(saved.Config);
            Assert(doc.String("model_provider") == "team.gateway" && doc.String("openai_base_url") == "https://unused.example/v1");
            Assert(store.ReadSettings(saved).BaseUrl == "https://team.example/v1" && saved.Auth is null && environment.Read() is null);
            Assert(doc.Text.Contains("# existing connection\r\n") && doc.Get("model_providers.'team.gateway'.env_key") as string == "TEST_PROVIDER_KEY");
        });
        Test("custom provider credentials never fall back to unrelated account or environment keys", () =>
        {
            var store = new ConfigStore(new FakeEnvironment(), _ => null);
            var auth = Bytes("{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-account-key\"}");
            var config = "model_provider='custom'\n[model_providers.custom]\nbase_url='https://custom.example/v1'\n";
            var snapshot = new ConfigSnapshot(root, Bytes(config + "env_key='MISSING_PROVIDER_KEY'\n"), auth, null);
            Assert(store.ReadSettings(snapshot).ApiKey == "" && store.ReadSettings(snapshot).CredentialSource.Contains("未设置"));
            var unrelatedEnvironment = new ConfigStore(new FakeEnvironment(), name => name == "OPENAI_API_KEY" ? "fake-global-key" : null);
            Assert(unrelatedEnvironment.ReadSettings(snapshot).ApiKey == "");
            Assert(unrelatedEnvironment.ReadSettings(snapshot with { Config = Bytes(config) }).ApiKey == "");
            var bearer = store.ReadSettings(snapshot with { Config = Bytes(config + "experimental_bearer_token='fake-provider-token'\n") });
            Assert(bearer.ApiKey == "fake-provider-token" && bearer.CredentialSource.Contains("Bearer"));
            Assert(store.ReadSettings(snapshot with { Config = Bytes(config + "requires_openai_auth=true\n") }).ApiKey == "fake-account-key");
            var command = unrelatedEnvironment.ReadSettings(snapshot with { Config = Bytes(config + "[model_providers.custom.auth]\ncommand='never-run-this-command'\n") });
            Assert(command.ApiKey == "" && command.CredentialSource.Contains("未执行"));
        });
        Test("explicit official authentication ignores stale PAT and API key fields", () =>
        {
            var auth = "{\"auth_mode\":\"chatgpt\",\"personal_access_token\":\"fake-stale-pat\",\"OPENAI_API_KEY\":\"fake-stale-key\",\"tokens\":{\"access_token\":\"fake-access\",\"refresh_token\":\"fake-refresh\",\"id_token\":\"fake-id\"}}";
            var store = new ConfigStore(new FakeEnvironment(), _ => null);
            var settings = store.ReadSettings(new(root, Bytes("model='existing-model'\n"), Bytes(auth), null));
            Assert(settings.ApiKey.Length == 0 && settings.CredentialSource.Contains("官方账号"));
        });
        Test("snapshot content comparison detects deletions, empty files, credentials and environment changes", () =>
        {
            var snapshot = new ConfigSnapshot(root, Bytes("model='test'"), Bytes("{}"), null) { Bridge = [1, 2], Details = [3, 4] };
            Assert(snapshot.SameContents(snapshot with { Config = snapshot.Config!.ToArray(), Auth = snapshot.Auth!.ToArray(), Bridge = [1, 2], Details = [3, 4] }));
            Assert(!snapshot.SameContents(snapshot with { Config = null }) && !snapshot.SameContents(snapshot with { Auth = Bytes("{ }") }));
            Assert(!snapshot.SameContents(snapshot with { Bridge = null }) && !snapshot.SameContents(snapshot with { Details = [3, 5] }));
            Assert(!snapshot.SameContents(snapshot with { AuthApiBaseUrl = "https://changed.example" }));
            Assert(!new ConfigSnapshot(root, null, null, null).SameContents(new(root, [], null, null)));
            Assert(!snapshot.SameContents(snapshot with { Home = Path.Combine(root, "other") }));
        });
    }

    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Local Codex state assertion failed."); }
}
