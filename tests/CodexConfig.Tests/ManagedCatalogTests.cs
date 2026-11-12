using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexConfig.Core;

internal static class ManagedCatalogTests
{
    internal static string Fixture => JsonSerializer.Serialize(new { models = new[] { "main", "other", "codex-auto-review" }.Select(slug => new {
        slug, display_name = slug, base_instructions = "Offline fixture", supported_in_api = true, default_reasoning_level = "low",
        supported_reasoning_levels = new[] { new { effort = "low", description = "Low" }, new { effort = "max", description = "Max" } },
        supports_reasoning_effort_updates = true, service_tiers = new[] { new { id = "ultrafast", name = "Ultrafast" } }, future_data = new { keep = 42 }
    }) });
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        static void Assert(bool value) { if (!value) throw new Exception("Managed catalog assertion failed"); }
        static void Throws(Action run) { try { run(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { return; } throw new Exception("Expected catalog rejection"); }
        (ConfigStore Store, string Home, EnvironmentFixture Env) Setup(bool external = false)
        {
            var home = Path.Combine(root, "catalog-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, external ? "external.json" : "models_cache.json"), Fixture);
            File.WriteAllText(Path.Combine(home, "config.toml"), "# untouched\nmodel='main'\nopenai_base_url='https://fixture.invalid/v1'\napprovals_reviewer='auto_review'\napproval_policy={ granular={ sandbox_approval=true, rules=true, mcp_elicitations=true, request_permissions=true, skill_approval=true } }\n" +
                (external ? "model_catalog_json='external.json'\n" : "") + "[features]\nfast_mode=true\n");
            var env = new EnvironmentFixture(); return (new ConfigStore(env), home, env);
        }
        static ConfigPlan Pin(ConfigStore store, ConfigSnapshot before, string? source = null) => ManagedModelCatalogStore.Prepare(
            store.Plan(before, store.ReadSettings(before), updateConnection: false), new(), source);
        Test("managed reviewer planning retains metadata and granular permissions without writes", () =>
        {
            var (store, home, _) = Setup(); var before = store.Load(home); var plan = Pin(store, before);
            Assert(before.SameContents(store.Load(home)) && !File.Exists(plan.CatalogUpdate!.Path));
            var models = JsonNode.Parse(plan.CatalogUpdate!.Content)!["models"]!.AsArray();
            var original = JsonNode.Parse(Fixture)!["models"]!.AsArray();
            for (var i = 0; i < models.Count; i++)
            {
                if (i < 2) original[i]!["auto_review_model_override"] = "codex-auto-review";
                Assert(JsonNode.DeepEquals(models[i], original[i]));
            }
            Assert(new TomlDocument(plan.Config).Text.Contains("approval_policy={ granular=") && plan.Auth is null);
            var manual = ManagedModelCatalogStore.Prepare(store.Plan(before, store.ReadSettings(before), new Dictionary<string, string?> { ["approvals_reviewer"] = "'user'" }, updateConnection: false), new());
            Assert(new TomlDocument(manual.Config).String("approvals_reviewer") == "user" && manual.CatalogUpdate is not null);
        });
        Test("catalog save, source update and restore retain the referenced immutable versions", () =>
        {
            var (store, home, _) = Setup(); var before = store.Load(home); var first = Pin(store, before); store.Apply(first);
            var saved = store.Load(home); Assert(ManagedModelCatalogStore.Inspect(saved) is { Managed: true, ReviewModel: "codex-auto-review", HasUpdate: false });
            var updated = JsonNode.Parse(Fixture)!; var extra = updated["models"]![0]!.DeepClone(); extra["slug"] = "new-model"; updated["models"]!.AsArray().Add(extra);
            File.WriteAllText(Path.Combine(home, "models_cache.json"), updated.ToJsonString());
            Assert(ManagedModelCatalogStore.Inspect(saved).HasUpdate);
            var second = Pin(store, saved); store.Apply(second); Assert(ManagedModelCatalogStore.Inspect(store.Load(home)).ModelCount == 4);
            File.Delete(first.CatalogUpdate!.Path); store.Restore(store.Load(home));
            Assert(saved.SameContents(store.Load(home)) && File.Exists(first.CatalogUpdate.Path) && File.Exists(second.CatalogUpdate!.Path));
            Assert(File.ReadAllText(Path.Combine(home, "config.toml")).StartsWith("# untouched\n"));
        });
        Test("missing external dependency restores as a managed copy and external edits are preserved", () =>
        {
            var (store, home, _) = Setup(true); var plan = Pin(store, store.Load(home)); store.Apply(plan);
            File.Delete(Path.Combine(home, "external.json")); store.Restore(store.Load(home));
            var restored = store.Load(home); Assert(restored.Catalog!.Content!.SequenceEqual(Encoding.UTF8.GetBytes(Fixture)) && !File.Exists(Path.Combine(home, "external.json")));
            Assert(restored.Catalog.Path.StartsWith(Path.Combine(home, "codex-dock-models"), StringComparison.OrdinalIgnoreCase));
        });
        Test("preview conflicts and corrupt immutable files cannot overwrite user changes", () =>
        {
            var (store, home, _) = Setup(); var plan = Pin(store, store.Load(home));
            File.AppendAllText(Path.Combine(home, "models_cache.json"), " "); Throws(() => store.Apply(plan)); Assert(!File.Exists(plan.CatalogUpdate!.Path));
            plan = Pin(store, store.Load(home)); Directory.CreateDirectory(Path.GetDirectoryName(plan.CatalogUpdate!.Path)!); File.WriteAllText(plan.CatalogUpdate.Path, "changed");
            Throws(() => store.Apply(plan)); Assert(File.ReadAllText(plan.CatalogUpdate.Path) == "changed" && !store.HasPending(home));
        });
        Test("failed writes roll back catalog policy and config while preserving immutable files", () =>
        {
            var (store, home, env) = Setup(); var before = store.Load(home); var plan = Pin(store, before) with { AuthApiBaseUrl = "https://fixture.invalid" };
            env.FailOnce = true; Throws(() => store.Apply(plan)); Assert(before.SameContents(store.Load(home)) && !store.HasPending(home));
        });
        Test("fixed reviewer rejects unknown models, missing reviewers and unconfirmed connection changes", () =>
        {
            var (store, home, _) = Setup(); var before = store.Load(home);
            var plan = store.Plan(before, store.ReadSettings(before) with { Model = "unknown" }, updateConnection: false, updateModel: true);
            Throws(() => ManagedModelCatalogStore.Prepare(plan, new()));
            store.Apply(Pin(store, before)); before = store.Load(home);
            plan = store.Plan(before, store.ReadSettings(before), updateConnection: false);
            var changed = new TomlDocument(plan.Config); changed.Set("openai_base_url", "https://other.invalid/v1"); plan = plan with { Config = changed.Bytes };
            Throws(() => ManagedModelCatalogStore.Prepare(plan, new()));
            var broken = JsonNode.Parse(Fixture)!; broken["models"]!.AsArray().RemoveAt(2); File.WriteAllText(Path.Combine(home, "models_cache.json"), broken.ToJsonString());
            Throws(() => Pin(store, before));
            var connection = MixedConnection.Create(new ConnectionSettings("https://first.invalid/v1", "fake-only", "main") { Mode = ConnectionMode.OfficialWithApi, Kind = ConnectionKind.ResponsesApi });
            var route = new TomlDocument(null); route.Set("openai_base_url", connection.NativeBaseUrl);
            Assert(ManagedModelCatalogStore.ConnectionId(route, connection.Protect()) != ManagedModelCatalogStore.ConnectionId(route, (connection with { BaseUrl = "https://second.invalid/v1" }).Protect()));
        });
        Test("selected-session diagnostics retain only metadata and reject other homes", () =>
        {
            var home = Path.Combine(root, "session-" + Guid.NewGuid().ToString("N")); var sessions = Path.Combine(home, "sessions", "2026", "11", "12"); Directory.CreateDirectory(sessions);
            var path = Path.Combine(sessions, "rollout-fixture.jsonl");
            File.WriteAllLines(path, [
                JsonSerializer.Serialize(new { type = "event_msg", timestamp = "2026-11-12T08:29:00Z", payload = new { type = "thread_settings_applied", thread_settings = new { model = "main", reasoning_effort = "xhigh", service_tier = "ultrafast", approvals_reviewer = "auto_review", secret = "private-must-not-return" } } }),
                JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "message", content = "private-must-not-return" } }),
                JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "configuration_update", reasoning = new { effort = "max" } } }),
                "{partial" ]);
            var observed = SessionDiagnostics.Read(home, path);
            Assert(observed is { Model: "main", Reasoning: "max", ServiceTier: "ultrafast", ContainsConfigurationUpdate: true } && !JsonSerializer.Serialize(observed).Contains("private-must-not-return"));
            Assert(SessionDiagnostics.List(home).Single().Path == path);
            Throws(() => SessionDiagnostics.Read(Path.Combine(root, "different-home"), path));
        });
    }
    private sealed class EnvironmentFixture : IUserEnvironment
    {
        public bool FailOnce;
        private string? value;
        public string? Read() => value;
        public void Write(string? next) { if (FailOnce) { FailOnce = false; throw new IOException("Fixture write failed"); } value = next; }
    }
}
