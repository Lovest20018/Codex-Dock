using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexConfig.Core;

internal static class ApiKeyMigrationTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
        string Home() { var home = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home); return home; }
        void Fixture(string home)
        {
            File.WriteAllText(Path.Combine(home, "config.toml"), "# keep legacy comment\r\nmodel='migration-model'\r\nmodel_provider='openai'\r\nopenai_base_url='https://relay.invalid/backend-api/codex'\r\nchatgpt_base_url='https://relay.invalid/backend-api/'\r\nweb_search='cached'\r\n");
            File.WriteAllText(Path.Combine(home, "auth.json"), "{\"personal_access_token\":\"fake-original-pat\",\"OPENAI_API_KEY\":\"fake-original-pat\"}", new UTF8Encoding(true));
        }
        Test("legacy PAT is readable without migration side effects", () =>
        {
            var home = Home(); Fixture(home); var environment = new FakeEnvironment { Value = "https://relay.invalid" };
            var store = new ConfigStore(environment, _ => null); var before = store.Load(home); var settings = store.ReadSettings(before);
            Assert(settings.NeedsApiKeyMigration && settings.ApiKey == "fake-original-pat" && settings.Decide().Mode == AuthenticationMode.ApiKey);
            Assert(before.SameContents(store.Load(home)) && store.LatestBackup(home) is null && !store.HasPending(home));
        });
        Test("legacy PAT migration replaces credentials and account routing with a reversible encrypted backup", () =>
        {
            var home = Home(); Fixture(home); var environment = new FakeEnvironment { Value = "https://relay.invalid" };
            var store = new ConfigStore(environment, _ => null); var before = store.Load(home);
            var plan = store.Plan(before, store.ReadSettings(before) with { PreservePreferenceValues = true });
            Assert(before.SameContents(store.Load(home)) && !plan.AuthPreview.Contains("fake-original-pat"));
            var document = new TomlDocument(plan.Config);
            Assert(plan.Mode == AuthenticationMode.ApiKey && document.String("chatgpt_base_url") is null && plan.AuthApiBaseUrl is null);
            Assert(document.String("openai_base_url") == "https://relay.invalid/backend-api/codex" && document.String("web_search") == "cached" && document.Text.Contains("# keep legacy comment\r\n"));
            var backup = store.Apply(plan); var after = store.Load(home);
            using (var auth = JsonDocument.Parse(after.Auth!)) AssertApiKey(auth, "fake-original-pat");
            Assert(environment.Value is null && after.Bridge is null && !store.ReadSettings(after).NeedsApiKeyMigration);
            Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(backup)).Contains("fake-original-pat"));
            store.Restore(after); Assert(before.SameContents(store.Load(home)));
        });
        Test("ordinary settings edits leave legacy authentication untouched until explicit migration", () =>
        {
            var home = Home(); Fixture(home); var environment = new FakeEnvironment { Value = "https://relay.invalid" };
            var store = new ConfigStore(environment, _ => null); var before = store.Load(home);
            var plan = store.Plan(before, store.ReadSettings(before), new Dictionary<string, string?> { ["analytics.enabled"] = "false" }, updateConnection: false);
            Assert(plan.Auth!.SequenceEqual(before.Auth!) && plan.AuthApiBaseUrl == before.AuthApiBaseUrl && new TomlDocument(plan.Config).String("chatgpt_base_url") == new TomlDocument(before.Config).String("chatgpt_base_url"));
            store.Apply(plan); Assert(store.ReadSettings(store.Load(home)).NeedsApiKeyMigration && environment.Value == before.AuthApiBaseUrl);
        });
        Test("explicit API key wins over leftover PAT and tokens then removes all stale fields", () =>
        {
            var home = Home(); Fixture(home);
            File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-current-api-key\",\"personal_access_token\":\"fake-stale-pat\",\"tokens\":{\"access_token\":\"fake-stale-access\"},\"extra\":true}");
            var store = new ConfigStore(new FakeEnvironment(), _ => null); var before = store.Load(home); var settings = store.ReadSettings(before);
            Assert(settings.ApiKey == "fake-current-api-key" && settings.NeedsApiKeyMigration);
            using var auth = JsonDocument.Parse(store.Plan(before, settings).Auth!); AssertApiKey(auth, "fake-current-api-key");
        });
        Test("official login and unrelated custom-provider credentials are not offered as PAT migration", () =>
        {
            var home = Home(); Fixture(home); var store = new ConfigStore(new FakeEnvironment(), _ => null);
            File.WriteAllBytes(Path.Combine(home, "auth.json"), LegacyConnectionTests.Auth());
            Assert(!store.ReadSettings(store.Load(home)).NeedsApiKeyMigration && store.ReadSettings(store.Load(home)).ApiKey.Length == 0);
            Fixture(home); File.WriteAllText(Path.Combine(home, "config.toml"), "model_provider='custom'\n[model_providers.custom]\nbase_url='https://custom.invalid/v1'\nenv_key='MISSING_KEY'\n");
            var custom = store.ReadSettings(store.Load(home)); Assert(!custom.NeedsApiKeyMigration && custom.ApiKey.Length == 0);
        });
        Test("API key connection selects file storage and removes conflicting local ChatGPT-only login", () =>
        {
            var home = Home(); Fixture(home);
            File.AppendAllText(Path.Combine(home, "config.toml"), "cli_auth_credentials_store='keyring'\r\nforced_login_method='chatgpt'\r\n");
            var store = new ConfigStore(new FakeEnvironment(), _ => null); var before = store.Load(home);
            var plan = store.Plan(before, store.ReadSettings(before)); var document = new TomlDocument(plan.Config);
            Assert(document.String("cli_auth_credentials_store") == "file" && document.Get("forced_login_method") is null);
            Assert(plan.Notes.Any(note => note.Contains("组织或其他配置层")) && before.SameContents(store.Load(home)));
        });
        Test("migration rejects changed auth or environment after review without overwriting either", () =>
        {
            foreach (var changeEnvironment in new[] { false, true })
            {
                var home = Home(); Fixture(home); var environment = new FakeEnvironment { Value = "https://relay.invalid" };
                var store = new ConfigStore(environment, _ => null); var before = store.Load(home); var plan = store.Plan(before, store.ReadSettings(before));
                if (changeEnvironment) environment.Value = "https://external.invalid";
                else File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-external-key\"}");
                var changed = store.Load(home);
                try { store.Apply(plan); throw new Exception("Migration must detect external changes."); }
                catch (InvalidOperationException) { }
                Assert(changed.SameContents(store.Load(home)) && store.LatestBackup(home) is null);
            }
        });
        if (Environment.GetEnvironmentVariable("CODEX_CONFIG_TEST_CLI") is { Length: > 0 } cli)
            tests.Add(("native Codex reads migrated auth as API key without device login", async () =>
            {
                var home = Home(); Fixture(home); var store = new ConfigStore(new FakeEnvironment(), _ => null);
                var before = store.Load(home); store.Apply(store.Plan(before, store.ReadSettings(before)));
                var start = new ProcessStartInfo(cli)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = home
                };
                foreach (var argument in new[] { "app-server", "--listen", "stdio://" }) start.ArgumentList.Add(argument);
                start.Environment["CODEX_HOME"] = home;
                foreach (var name in new[] { "CODEX_ACCESS_TOKEN", "CODEX_API_KEY", "OPENAI_API_KEY", "OPENAI_BASE_URL", "CODEX_AUTHAPI_BASE_URL" }) start.Environment.Remove(name);
                start.Environment["HTTP_PROXY"] = start.Environment["HTTPS_PROXY"] = start.Environment["ALL_PROXY"] = "http://127.0.0.1:9";
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Native migration fixture did not start.");
                var errors = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try
                {
                    async Task Send(object value) { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value)); await process.StandardInput.FlushAsync(); }
                    async Task<JsonElement> Read(int id)
                    {
                        while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
                        {
                            using var json = JsonDocument.Parse(line); var response = json.RootElement;
                            if (response.TryGetProperty("id", out var answer) && answer.ValueKind == JsonValueKind.Number && answer.GetInt32() == id)
                            {
                                if (response.TryGetProperty("error", out _)) throw new InvalidOperationException("Native migration account read failed.");
                                return response.GetProperty("result").Clone();
                            }
                        }
                        throw new InvalidOperationException("Native migration fixture exited without its response.");
                    }
                    await Send(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "codex_dock_api_key_migration", version = "0.17.1" } } });
                    await Read(1); await Send(new { method = "initialized" });
                    await Send(new { id = 2, method = "account/read", @params = new { refreshToken = false } });
                    var account = await Read(2); Assert(account.GetProperty("account").GetProperty("type").GetString() == "apiKey");
                    Assert(!store.ReadSettings(store.Load(home)).NeedsApiKeyMigration);
                }
                finally { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await errors; }
            }));
    }

    private static void AssertApiKey(JsonDocument auth, string key) => Assert(auth.RootElement.EnumerateObject().Count() == 2 &&
        auth.RootElement.GetProperty("auth_mode").GetString() == "apikey" && auth.RootElement.GetProperty("OPENAI_API_KEY").GetString() == key);
    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("API key migration assertion failed."); }
}
