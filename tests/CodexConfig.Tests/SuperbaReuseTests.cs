using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using CodexConfig.Core;

internal static class SuperbaReuseTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        string Home() { var path = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
        Test("updated catalog carries version markers, native constraints and changed lifecycle metadata", () =>
        {
            var schema = ConfigSchema.Instance;
            Assert(schema.Entries.Select(entry => entry.Path).Distinct().Count() == schema.Entries.Count);
            Assert(schema.Entries.Count(entry => entry.VerifiedSince == "0.168.0-alpha.4") == 61);
            Assert(schema.Find("features.multi_agent") is { Stage: "deprecated" });
            Assert(schema.Find("features.unified_image_budget") is { Stage: "stable" } image && image.Default.GetBoolean());
            schema.Validate("tui.notification_method", "'osc777'");
            Assert(schema.Find("tui.notification_method")?.UpdatedIn == "0.168.0-alpha.4");
            schema.Validate("audio.microphone_channel", "[1, 2]");
            Throws<ArgumentException>(() => schema.Validate("audio.microphone_channel", "[0, 1]"));
            schema.Validate("plugins.'one.plugin'.apps.'app.one'.tools.edit.approval_mode", "'writes'");
            Throws<ArgumentException>(() => schema.Validate("plugins.'one.plugin'.apps.'app.one'.tools.edit.approval_mode", "'invalid'"));
            Throws<ArgumentException>(() => schema.Validate("features.guardianv2.sync_elastic_sections.transcript_messages", "{ min_total_section_tokens = 1 }"));
        });
        Test("community default selection preserves analytics and edited template choices", () =>
        {
            var template = BuiltInTemplates.KrillCommunity;
            var before = Doc("[analytics]\nenabled=false\n");
            var choices = template.BuildDiff(before.Model, new Dictionary<string, string?>()).Where(diff => diff.SelectedByDefault).Select(diff => diff.Setting.Path).ToArray();
            Assert(choices.Length == 2 && !choices.Contains("analytics.enabled"));
            var draft = new Dictionary<string, string?>(); template.StageSelected(before.Model, draft, choices);
            Assert(!draft.ContainsKey("analytics.enabled") && before.Get("analytics.enabled") is false);
            var repository = new TemplateRepository(Home());
            repository.Save(template with { Name = "customized", Settings = [new("analytics.enabled", "false", true)] });
            var kept = repository.Load().Single(item => item.Id == template.Id);
            Assert(kept.Name == "customized" && kept.Settings.Single().SelectByDefault);
        });
        Test("legacy shutdown stages explicit false, merges only selected keys and restores backup", () =>
        {
            var home = Home(); var store = new ConfigStore(new FakeEnvironment());
            var original = "model='keep-model'\n[features]\ncontext_management=true # keep comment\nfast_mode=true\n[features.token_budget]\nenabled=true\nuse_history_notes_extension=true\n[projects.demo]\ntrust_level='trusted'\n";
            File.WriteAllText(Path.Combine(home, "config.toml"), original);
            File.WriteAllBytes(Path.Combine(home, "auth.json"), LegacyConnectionTests.Auth());
            var before = store.Load(home); var draft = new Dictionary<string, string?>();
            var template = BuiltInTemplates.DisableLegacyContext;
            var selected = template.BuildDiff(Doc(original).Model, draft).Where(diff => diff.SelectedByDefault).Select(diff => diff.Setting.Path).ToArray();
            Assert(selected.Length == 3);
            template.StageSelected(Doc(original).Model, draft, selected);
            Assert(before.SameContents(store.Load(home)) && draft.Values.All(value => value == "false"));
            var plan = store.Plan(before, store.ReadSettings(before), draft, updateConnection: false);
            store.Apply(plan); var saved = store.Load(home); var after = new TomlDocument(saved.Config);
            Assert(template.Settings.All(setting => after.Get(setting.Path) is false));
            Assert(after.Get("features.fast_mode") is true && after.String("model") == "keep-model" && after.Get("projects.demo.trust_level") as string == "trusted");
            Assert(saved.Auth!.SequenceEqual(before.Auth!) && after.Text.Contains("context_management=false # keep comment"));
            Assert(template.StageSelected(after.Model, new(), selected).Count == 0);
            store.Restore(saved); Assert(before.SameContents(store.Load(home)));
        });
        Test("legacy shutdown draft preserves unselected fields and blocks table replacement atomically", () =>
        {
            var template = BuiltInTemplates.DisableLegacyContext;
            var before = Doc("[features]\ncontext_management=true\n[features.token_budget]\nenabled=true\nuse_history_notes_extension=true\n");
            var draft = new Dictionary<string, string?> { ["web_search"] = "'cached'" };
            var application = template.StageSelected(before.Model, draft, ["features.context_management"]);
            Assert(application.Count == 1 && draft.Count == 2 && draft["features.context_management"] == "false");
            Assert(!draft.ContainsKey("features.token_budget.enabled") && before.Get("features.context_management") is true);
            Assert(application.Undo(draft).Restored == 1 && draft.Count == 1 && draft["web_search"] == "'cached'");
            var table = Doc("[features.context_management]\nexperimental_mode='keep'\n");
            var all = template.Settings.Select(setting => setting.Path).ToArray();
            Throws<ArgumentException>(() => template.StageSelected(table.Model, draft, all));
            Assert(draft.Count == 1 && table.Get("features.context_management.experimental_mode") as string == "keep");
            var copy = TemplateRepository.New("停用方案副本", template.Settings);
            Throws<ArgumentException>(() => copy.StageSelected(table.Model, draft, all));
            var inlineTable = Doc("[features]\ncontext_management={experimental_mode='keep'}\n");
            Throws<ArgumentException>(() => copy.StageSelected(inlineTable.Model, draft, all));
            Throws<ArgumentException>(() => BuiltInTemplates.KrillCommunity.StageSelected(inlineTable.Model, draft, ["web_search", "features.context_management"]));
            Assert(draft.Count == 1 && inlineTable.Get("features.context_management.experimental_mode") as string == "keep");
        });
        Test("Guardian elastic constraints match named sections, aliases, entry limits and default reservations", () =>
        {
            var before = Doc("");
            string Section(string name, long min, long entry = 100, long? max = null) =>
                $"[features.guardianv2.sync_elastic_sections.{name}]\nmin_total_section_tokens={min}\nmax_section_entry_tokens={entry}\n" + (max is null ? "" : $"max_total_section_tokens={max}\n");
            var enabled = "[features.guardianv2]\nsync_elastic_budgeting=true\n";
            var valid = enabled + Section("transcript_messages", 117100);
            Assert(ConfigConstraints.InspectChanges(before, Doc(valid)).All(issue => !issue.IsError));
            foreach (var invalid in new[] { enabled, enabled + Section("unknown", 117100), enabled + Section("transcript_messages", 100),
                enabled + Section("transcript_messages", 117100, 9001), enabled + Section("transcript_messages", 117100, 100, 100),
                enabled + Section("transcript_messages", 117100, 0), enabled + Section("root_messages", 113000) + Section("root_conversation_messages", 113000) })
                Assert(ConfigConstraints.InspectChanges(before, Doc(invalid)).Any(issue => issue.IsError));
            Assert(ConfigConstraints.InspectChanges(before, Doc("[features.guardianv2]\nsync_elastic_budgeting=false\n" + Section("unknown", 0))).All(issue => !issue.IsError));
            var existing = Doc(enabled); var harmless = Doc(enabled + "[analytics]\nenabled=false\n");
            Assert(ConfigConstraints.InspectChanges(existing, harmless).All(issue => !issue.IsError));
        });
        Test("HTTP confirmation binds canonical origin and keeps all plaintext requests off the system proxy", () =>
        {
            Assert(HttpConnectionPolicy.RequiredOrigin("http://EXAMPLE.invalid:80/a") == "http://example.invalid");
            Assert(HttpConnectionPolicy.IsAuthorized("http://example.invalid/second", "http://example.invalid"));
            Assert(!HttpConnectionPolicy.IsAuthorized("http://example.invalid:81/a", "http://example.invalid"));
            Assert(!HttpConnectionPolicy.IsAuthorized("http://other.invalid/a", "http://example.invalid"));
            Assert(HttpConnectionPolicy.IsAuthorized("http://127.0.0.1:9000/v1", "") && HttpConnectionPolicy.IsAuthorized("https://example.invalid", ""));
            Assert(!HttpConnectionPolicy.CanUseProxy("http://example.invalid", true) && HttpConnectionPolicy.CanUseProxy("https://example.invalid", true));
            foreach (var invalid in new[] { "http://example.invalid/path", "http://u:p@example.invalid", "http://example.invalid?x=y", "https://example.invalid" })
                Throws<ArgumentException>(() => new ConnectionDetails(HttpOrigin: invalid).Normalize());
            var details = new ConnectionDetails("fixture", "note", "http://example.invalid");
            Assert(ConnectionDetails.Read(details.Protect()) == details);
        });
        Test("saving changed HTTP targets requires confirmation, and revocation updates only private metadata", () =>
        {
            var home = Home(); File.WriteAllBytes(Path.Combine(home, "auth.json"), LegacyConnectionTests.Auth());
            var store = new ConfigStore(new FakeEnvironment(), _ => null);
            var settings = new ConnectionSettings("http://fixture.invalid/v1", "fake-upstream", "fixture")
            { Kind = ConnectionKind.ResponsesApi, Mode = ConnectionMode.OfficialWithApi };
            var before = store.Load(home);
            Throws<InvalidOperationException>(() => store.Plan(before, settings));
            settings = settings with { Details = new(HttpOrigin: "http://fixture.invalid") };
            var plan = store.Plan(before, settings, updateDetails: true); store.Apply(plan);
            var saved = store.Load(home); var connection = MixedConnection.Read(saved.Bridge!);
            Assert(connection.HttpOrigin == "http://fixture.invalid" && !new TomlDocument(saved.Config).Text.Contains("http://fixture.invalid"));
            Throws<InvalidOperationException>(() => store.Plan(saved, settings with { BaseUrl = "http://other.invalid/v1" }));
            var revoke = store.Plan(saved, store.ReadSettings(saved) with { Details = new() }, updateConnection: false, updateDetails: true);
            store.Apply(revoke); var revoked = store.Load(home);
            Assert(saved.Config!.SequenceEqual(revoked.Config!) && saved.Auth!.SequenceEqual(revoked.Auth!));
            Assert(MixedConnection.Read(revoked.Bridge!).HttpOrigin == "" && ConnectionDetails.Read(revoked.Details).HttpOrigin == "");
            store.Restore(revoked); Assert(saved.SameContents(store.Load(home)));
        });
        tests.Add(("model catalog blocks missing or stale HTTP grants before sending credentials", async () =>
        {
            var calls = 0;
            using var catalog = new ModelCatalogClient(new FakeHandler(request =>
            { calls++; return new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"fixture\"}]}") }; }));
            var settings = new ConnectionSettings("http://fixture.invalid/v1", "fake-key", "fixture") { Kind = ConnectionKind.ResponsesApi };
            await ThrowsAsync<InvalidOperationException>(() => catalog.ReadModelsAsync(settings)); Assert(calls == 0);
            settings = settings with { Details = new(HttpOrigin: "http://fixture.invalid") };
            Assert((await catalog.ReadModelsAsync(settings)).Models.Single() == "fixture" && calls == 1);
            await ThrowsAsync<InvalidOperationException>(() => catalog.ReadModelsAsync(settings with { BaseUrl = "http://fixture.invalid:81/v1" })); Assert(calls == 1);
        }));
        tests.Add(("HTTP and WebSocket bridge requests honor revocation before contacting the upstream", async () =>
        {
            var settings = new ConnectionSettings("http://fixture.invalid/v1", "fake-key", "fixture")
            { Kind = ConnectionKind.ResponsesApi, Mode = ConnectionMode.OfficialWithApi, Details = new(HttpOrigin: "http://fixture.invalid") };
            var current = MixedConnection.Create(settings); var calls = 0;
            using var handler = new FakeHandler(request => { calls++; return new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[]}") }; });
            await using var bridge = new ModelBridge(() => current, () => LegacyConnectionTests.Auth(), handler);
            await bridge.StartAsync();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", current.LocalToken);
            using (var allowed = await client.GetAsync(current.LocalBaseUrl + "/models")) Assert(allowed.IsSuccessStatusCode && calls == 1);
            current = current with { HttpOrigin = "" };
            using (var models = await client.GetAsync(current.LocalBaseUrl + "/models")) Assert(models.StatusCode == HttpStatusCode.Forbidden);
            using (var response = await client.PostAsync(current.LocalBaseUrl + "/responses", new StringContent("{}"))) Assert(response.StatusCode == HttpStatusCode.Forbidden);
            using var socket = new ClientWebSocket(); socket.Options.Proxy = null; socket.Options.CollectHttpResponseDetails = true;
            socket.Options.SetRequestHeader("Authorization", "Bearer " + current.LocalToken);
            await ThrowsAsync<WebSocketException>(() => socket.ConnectAsync(new Uri(current.LocalBaseUrl.Replace("http://", "ws://") + "/responses"), CancellationToken.None));
            Assert(socket.HttpStatusCode == HttpStatusCode.Forbidden && calls == 1);
            Assert(await BridgeProcess.StatusAsync(current) is not null);
        }));
        tests.Add(("saved HTTP revocation stops the worker and prevents an automatic restart", async () =>
        {
            var home = Home(); File.WriteAllBytes(Path.Combine(home, "auth.json"), LegacyConnectionTests.Auth());
            var store = new ConfigStore(new FakeEnvironment(), _ => null);
            var settings = new ConnectionSettings("http://fixture.invalid/v1", "fake-key", "fixture")
            { Kind = ConnectionKind.ResponsesApi, Mode = ConnectionMode.OfficialWithApi, Details = new(HttpOrigin: "http://fixture.invalid") };
            store.Apply(store.Plan(store.Load(home), settings, updateDetails: true)); var saved = store.Load(home);
            var connection = MixedConnection.Read(saved.Bridge!);
            try
            {
                await BridgeProcess.EnsureAsync(home, saved.Bridge!);
                using var process = GatewayWorkerProcess.Capture(connection.Port) ?? throw new Exception("Expected worker owner");
                store.Apply(store.Plan(saved, settings with { Details = new() }, updateConnection: false, updateDetails: true));
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert(GatewayWorkerProcess.PortFree(connection.Port));
                await ThrowsAsync<InvalidOperationException>(() => BridgeProcess.EnsureAsync(home, store.Load(home).Bridge!));
            }
            finally { await BridgeProcess.StopAsync(saved.Bridge!); }
        }));
    }

    private static TomlDocument Doc(string text) => new(Encoding.UTF8.GetBytes(text));
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Superba reuse regression"); }
    private static void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task ThrowsAsync<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
