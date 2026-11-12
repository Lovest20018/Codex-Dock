using System.Text;
using System.Text.Json;
using CodexConfig.Core;

internal static class CodexApplyTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
        ConfigSnapshot Snapshot() => new(root, Bytes("model='same-model'\nservice_tier='ultrafast'\n[features]\nfast_mode=true\n"),
            Bytes("{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-apply-key\"}"), null);
        string Identity(string subject, string issuer = "https://issuer.invalid") => "fixture." + Convert.ToBase64String(
            JsonSerializer.SerializeToUtf8Bytes(new { iss = issuer, sub = subject })).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".fixture";
        byte[] Official(string subject = "same-subject", string account = "same-account", string version = "old", string issuer = "https://issuer.invalid") =>
            JsonSerializer.SerializeToUtf8Bytes(new { auth_mode = "chatgpt", last_refresh = version,
                tokens = new { account_id = account, access_token = "fake-access-" + version, refresh_token = "fake-refresh-" + version, id_token = Identity(subject, issuer) } });

        Test("apply preflight and save preserve formatting without losing reviewed edits", () =>
        {
            var saved = Snapshot();
            var formatting = saved with { Config = Bytes("# client comment\r\nmodel = \"same-model\"\r\nservice_tier=\"ultrafast\"\r\n[features]\r\nfast_mode = true\r\n"),
                Auth = Bytes("{\"OPENAI_API_KEY\":\"fake-apply-key\", \"auth_mode\":\"apikey\"}") };
            Assert(!saved.SameContents(formatting) && CodexApplySnapshot.Compare(saved, formatting).CanContinue);
            var directory = Path.Combine(root, "apply-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "config.toml"), saved.Config!);
            File.WriteAllBytes(Path.Combine(directory, "auth.json"), saved.Auth!);
            var store = new ConfigStore(new FakeEnvironment(), _ => null); var baseline = store.Load(directory);
            var plan = store.Plan(baseline, store.ReadSettings(baseline), new Dictionary<string, string?> { ["model_reasoning_effort"] = "'high'" }, updateConnection: false);
            File.WriteAllBytes(Path.Combine(directory, "config.toml"), formatting.Config!);
            store.Apply(plan);
            var savedDocument = new TomlDocument(store.Load(directory).Config);
            Assert(savedDocument.String("model_reasoning_effort") == "high" && savedDocument.Text.Contains("# client comment"));
            Assert(savedDocument.String("service_tier") == "ultrafast");
        });
        Test("apply preflight lists real field changes without exposing credentials", () =>
        {
            var saved = Snapshot();
            var current = saved with { Config = Bytes("model='same-model'\nservice_tier='priority'\n[features]\nfast_mode=false\n"),
                Auth = Bytes("{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-other-secret\"}") };
            var comparison = CodexApplySnapshot.Compare(saved, current);
            Assert(!comparison.CanContinue && comparison.Changes.SequenceEqual(["config.toml · features.fast_mode", "config.toml · service_tier", "auth.json · 登录或凭据"]));
            var error = Throws<CodexApplyConflictException>(() => CodexApplySnapshot.RequireCompatible(saved, current));
            Assert(!error.Message.Contains("fake-other-secret") && !error.Message.Contains("fake-apply-key"));
            Throws<InvalidDataException>(() => CodexApplySnapshot.Compare(saved, saved with { Config = Bytes("model=[invalid") }));
        });
        Test("apply preflight permits only the desktop-owned named-pipe rotation and preserves latest runtime state", () =>
        {
            ConfigSnapshot WithPipe(string pipe, string extra = "") => Snapshot() with { Config = Bytes(
                "model='same-model'\n[mcp_servers.node_repl.env]\nSKY_CUA_NATIVE_PIPE_DIRECTORY='" + pipe + "'\n" + extra) };
            var saved = WithPipe(@"\\.\pipe\codex-computer-use-11111111-2222-3333-4444-555555555555");
            var rotated = WithPipe(@"\\.\pipe\codex-computer-use-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var comparison = CodexApplySnapshot.Compare(saved, rotated);
            Assert(!saved.SameContents(rotated) && comparison.CanContinue && comparison.RuntimeChanges.SequenceEqual(["mcp_servers.node_repl.env.SKY_CUA_NATIVE_PIPE_DIRECTORY"]));
            Assert(!CodexApplySnapshot.Compare(saved, WithPipe(@"C:\arbitrary\directory")).CanContinue);
            Assert(!CodexApplySnapshot.Compare(saved, WithPipe(@"\\.\pipe\other-application-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")).CanContinue);
            Assert(!CodexApplySnapshot.Compare(saved, WithPipe(@"\\.\pipe\codex-computer-use-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "API_TOKEN='fake-new-key'\n")).CanContinue);
        });
        Test("apply preflight permits verified same-account token refresh but rejects account changes and unprovable identities", () =>
        {
            var saved = Snapshot() with { Auth = Official() };
            Assert(CodexApplySnapshot.Compare(saved, saved with { Auth = Official(version: "new") }).CanContinue);
            Assert(!CodexApplySnapshot.Compare(saved, saved with { Auth = Official(subject: "other", version: "new") }).CanContinue);
            Assert(!CodexApplySnapshot.Compare(saved, saved with { Auth = Official(account: "other", version: "new") }).CanContinue);
            Assert(!CodexApplySnapshot.Compare(saved, saved with { Auth = Official(issuer: "https://other.invalid", version: "new") }).CanContinue);
            Assert(!CodexApplySnapshot.Compare(saved, saved with { Auth = Snapshot().Auth }).CanContinue);
            var unknown = saved with { Auth = Bytes("{\"tokens\":{\"id_token\":\"fake-id\",\"access_token\":\"fake-access\",\"refresh_token\":\"fake-refresh\"}}") };
            Assert(!CodexApplySnapshot.Compare(unknown, unknown with { Auth = Bytes("{\"tokens\":{\"id_token\":\"fake-new-id\",\"access_token\":\"fake-new-access\",\"refresh_token\":\"fake-refresh\"}}") }).CanContinue);
        });
        Test("apply preflight protects catalog dependencies, routing, metadata and launch environment", () =>
        {
            var saved = Snapshot() with { Bridge = [1], Details = [2], CatalogIndex = [3], Catalog = new(Path.Combine(root, "models.json"), [4]) };
            var mutations = new[] { saved with { Bridge = [9] }, saved with { Details = [9] }, saved with { CatalogIndex = [9] },
                saved with { Catalog = saved.Catalog with { Content = null } }, saved with { AuthApiBaseUrl = "https://other.invalid" }, saved with { Home = root + "-other" } };
            foreach (var current in mutations) Assert(!CodexApplySnapshot.Compare(saved, current).CanContinue);
        });
    }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Apply preflight regression"); }
    private static T Throws<T>(Action run) where T : Exception
    { try { run(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
