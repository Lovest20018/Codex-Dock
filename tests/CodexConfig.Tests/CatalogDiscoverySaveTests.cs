using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class CatalogDiscoverySaveTests
{
    private const string OldModel = "gpt-6.1-sol", NewModel = "gpt-6.2-sol";
    internal static byte[] Catalog(params string[] slugs) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        models = slugs.Select(slug => new { slug, display_name = slug, supported_in_api = true,
            context_window = slug == NewModel ? 777777 : 123456, base_instructions = "Offline capability fixture",
            default_reasoning_level = "low", supported_reasoning_levels = new[] { new { effort = "low" }, new { effort = "max" } },
            service_tiers = new[] { new { id = "ultrafast" } }, future_capability = new { keep = "unchanged" } })
    });
    internal static byte[] Latest => Catalog(OldModel, NewModel, "codex-auto-review");
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add(("catalog discovery " + name, () => { run(); return Task.CompletedTask; }));
        tests.Add(("catalog discovery retains service-declared native capabilities in one bounded model read", async () =>
        {
            var calls = 0;
            using var client = new ModelCatalogClient(new Handler(request =>
            {
                calls++; Assert(request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri == "https://catalog-discovery.invalid/v1/models" &&
                    request.Headers.Authorization?.Parameter == "fake-discovery-key");
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Latest) };
            }));
            var result = await client.ReadModelsAsync(new("https://catalog-discovery.invalid/v1", "fake-discovery-key", OldModel));
            Assert(calls == 1 && result.Models.SequenceEqual(new[] { OldModel, NewModel, "codex-auto-review" }) && result.NativeCatalog is not null);
            var selected = JsonNode.Parse(result.NativeCatalog!)!["models"]![1]!;
            Assert(selected["context_window"]!.GetValue<int>() == 777777 && selected["future_capability"]!["keep"]!.GetValue<string>() == "unchanged" &&
                !Encoding.UTF8.GetString(result.NativeCatalog!).Contains("fake-discovery-key"));
        }));
        Test("does not manufacture native capability from IDs partial entries duplicates or oversized metadata", () =>
        {
            foreach (var json in new[] { "[{\"id\":\"gpt-6.2-sol\"}]", "[{\"slug\":\"main\"}]", "[]",
                "[{\"slug\":\"same\",\"supported_reasoning_levels\":[]},{\"slug\":\"same\",\"supported_reasoning_levels\":[]}]" })
            { using var document = JsonDocument.Parse(json); Assert(NativeModelCatalog.DeclaredCatalog(document.RootElement) is null); }
            var oversized = JsonNode.Parse(Latest)!; oversized["models"]![0]!["extra"] = new string('x', ManagedModelCatalogStore.MaxBytes);
            using var large = JsonDocument.Parse(oversized.ToJsonString()); Assert(NativeModelCatalog.DeclaredCatalog(large.RootElement.GetProperty("models")) is null);
        });
        Test("model discovery does not pin a native automatic reviewer without an explicit dedicated policy", () =>
        {
            using var f = new Fixture(root); var doc = new TomlDocument(f.Before.Config); doc.Remove("model_catalog_json");
            var nativeChoice = f.Before with { Config = doc.Bytes, Catalog = null, CatalogIndex = null };
            File.WriteAllBytes(Path.Combine(f.Home, "models_cache.json"), f.Before.Catalog!.Content!);
            f.Draft.Load(nativeChoice);
            Assert(!f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), f.Draft.Settings()) && f.Draft.Request().ServiceCatalog is null &&
                !f.Draft.SynchronizeCatalog && !f.Draft.HasDraft && f.Draft.Document().String("approvals_reviewer") == "auto_review");
            f.Draft.SetDedicatedReviewer(true);
            Assert(f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), f.Draft.Settings()) && f.Draft.SynchronizeCatalog);
        });
        Test("new main model and approval inheritance are reviewed saved and restored with the fixed reviewer", () =>
        {
            using var f = new Fixture(root); var before = f.Before; f.Draft.Model = NewModel; f.Draft.SetOverride("approval_policy", null);
            Assert(f.Draft.AdoptDiscoveredCatalog(new([OldModel, NewModel, "codex-auto-review"], 1, Latest), f.Draft.Settings()));
            var request = f.Draft.Request(); var plan = f.Service.PreparePlan(request); var review = f.Service.PrepareReview(request);
            Assert(before.SameContents(f.Store.Load(f.Home)) && !File.Exists(plan.CatalogUpdate!.Path) &&
                review.Changes.Any(change => change.Path == "model") && review.Changes.Any(change => change.Path == "approval_policy" || change.Path.StartsWith("approval_policy.")) &&
                review.Targets.Contains(plan.CatalogUpdate.Path) && !review.Description.Contains("fake-discovery-key"));
            var result = f.Save(review); Assert(result.WasWritten && result.Outcome == ConfigurationSaveOutcome.Saved);
            var saved = f.Store.Load(f.Home); var doc = new TomlDocument(saved.Config); var index = ManagedModelCatalogStore.Index(saved.CatalogIndex)!;
            var selected = JsonNode.Parse(saved.Catalog!.Content!)!["models"]![1]!;
            Assert(doc.String("model") == NewModel && doc.String("approvals_reviewer") == "auto_review" && doc.Get("approval_policy") is null &&
                selected["auto_review_model_override"]!.GetValue<string>() == "codex-auto-review" && selected["context_window"]!.GetValue<int>() == 777777 &&
                selected["future_capability"]!["keep"]!.GetValue<string>() == "unchanged" && before.Auth!.SequenceEqual(saved.Auth!) &&
                index.SourcePath == saved.Catalog.Path && index.SourceHash == ManagedModelCatalogStore.Hash(saved.Catalog.Content!) &&
                ManagedModelCatalogStore.Inspect(saved) is { Managed: true, HasUpdate: false });
            f.Store.Restore(saved); Assert(before.SameContents(f.Store.Load(f.Home)));
        });
        Test("ID-only discovery leaves missing-model validation active and never changes review policy", () =>
        {
            using var f = new Fixture(root); f.Draft.Model = NewModel;
            Assert(!f.Draft.AdoptDiscoveredCatalog(new([NewModel], 1), f.Draft.Settings()));
            Throws(() => f.Service.PrepareReview(f.Draft.Request())); Assert(f.Before.SameContents(f.Store.Load(f.Home)));
            f.Draft.AdoptDiscoveredCatalog(new([NewModel], 1, Catalog(OldModel, NewModel)), f.Draft.Settings());
            Throws(() => f.Service.PrepareReview(f.Draft.Request()));
            Assert(f.Draft.Document().String("approvals_reviewer") == "auto_review" && f.Before.SameContents(f.Store.Load(f.Home)));
        });
        Test("changing credentials endpoint protocol mode proxy or HTTP grant discards downloaded provenance", () =>
        {
            using var f = new Fixture(root);
            foreach (var change in new Action[] { () => f.Draft.ApiKey = "fake-replaced-key", () => f.Draft.BaseUrl = "https://changed-discovery.invalid/v1",
                () => f.Draft.Kind = ConnectionKind.CodexRelay, () => f.Draft.Mode = ConnectionMode.OfficialWithApi, () => f.Draft.UseSystemProxy = true })
            {
                f.Draft.Load(f.Before); var source = f.Draft.Settings(); f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), source); change();
                Assert(f.Draft.Request().ServiceCatalog is null && !f.Draft.SynchronizeCatalog);
                Throws(() => f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), source));
            }
            f.Draft.Load(f.Before); f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), f.Draft.Settings()); f.Draft.ApiKey = "fake-other-key"; f.Draft.ApiKey = "fake-discovery-key";
            Assert(f.Draft.Request().ServiceCatalog is null && f.Before.SameContents(f.Store.Load(f.Home)));
            f.Draft.Load(f.Before); f.Draft.BaseUrl = "http://http-discovery.invalid/v1"; f.Draft.HttpAuthorized = true;
            f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), f.Draft.Settings()); f.Draft.HttpAuthorized = false;
            Assert(f.Draft.Request().ServiceCatalog is null && !f.Draft.SynchronizeCatalog);
        });
        Test("service snapshots are copied and a review freezes the accepted bytes", () =>
        {
            using var f = new Fixture(root); var payload = Latest; f.Draft.Model = NewModel;
            f.Draft.AdoptDiscoveredCatalog(new([], 1, payload), f.Draft.Settings()); Array.Fill(payload, (byte)'x');
            var request = f.Draft.Request(); var review = f.Service.PrepareReview(request); Array.Fill(request.ServiceCatalog!, (byte)'x');
            Assert(f.Save(review).Outcome == ConfigurationSaveOutcome.Saved && new TomlDocument(f.Store.Load(f.Home).Config).String("model") == NewModel);
        });
        Test("file import supersedes service metadata and service reads preserve an explicit file source", () =>
        {
            using var f = new Fixture(root); var file = Path.Combine(f.Home, "chosen-native.json"); File.WriteAllBytes(file, Catalog(OldModel, "file-only", "codex-auto-review"));
            f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), f.Draft.Settings()); f.Draft.StageCatalog(file);
            Assert(f.Draft.Request().ServiceCatalog is null && !f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), f.Draft.Settings()));
            var plan = f.Service.PreparePlan(f.Draft.Request()); Assert(plan.CatalogUpdate is { EmbeddedSource: false } && plan.CatalogUpdate.SourcePath == file);
            f.Draft.Load(f.Before); f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), f.Draft.Settings());
            var downloaded = f.Draft.Request(); Throws(() => f.Service.PreparePlan(downloaded with { CatalogSource = file }));
            f.Draft.SetOverride("model_catalog_json", TomlDocument.Quote(file)); Throws(() => f.Service.PreparePlan(f.Draft.Request()));
            Assert(!f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), f.Draft.Settings()) && f.Draft.Request().ServiceCatalog is null);
        });
        Test("service provenance must match the current request and cannot bypass embedded source integrity", () =>
        {
            using var f = new Fixture(root); f.Draft.Model = NewModel; f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), f.Draft.Settings());
            var request = f.Draft.Request(); Throws(() => f.Service.PreparePlan(request with { ServiceCatalogFingerprint = "wrong" }));
            var plan = f.Service.PreparePlan(request);
            Throws(() => f.Store.Apply(plan with { CatalogUpdate = plan.CatalogUpdate! with { SourcePath = Path.Combine(f.Home, "unreviewed.json") } }));
            Throws(() => f.Store.Apply(plan with { CatalogUpdate = plan.CatalogUpdate! with { SourceHash = "wrong" } }));
            Assert(f.Before.SameContents(f.Store.Load(f.Home)) && !File.Exists(plan.CatalogUpdate!.Path) && !f.Store.HasPending(f.Home));
        });
        Test("external changes after review and corrupt immutable targets keep saved files and draft", () =>
        {
            using var f = new Fixture(root); f.Draft.Model = NewModel; f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), f.Draft.Settings());
            var review = f.Service.PrepareReview(f.Draft.Request()); File.AppendAllText(Path.Combine(f.Home, "config.toml"), "model_verbosity='high'\n");
            var external = File.ReadAllBytes(Path.Combine(f.Home, "config.toml")); Assert(f.Save(review).Outcome == ConfigurationSaveOutcome.Conflict);
            Assert(File.ReadAllBytes(Path.Combine(f.Home, "config.toml")).SequenceEqual(external) && f.Draft.HasDraft);
            File.WriteAllBytes(Path.Combine(f.Home, "config.toml"), f.Before.Config!);
            var plan = f.Service.PreparePlan(f.Draft.Request()); Directory.CreateDirectory(Path.GetDirectoryName(plan.CatalogUpdate!.Path)!); File.WriteAllText(plan.CatalogUpdate.Path, "external immutable content");
            Throws(() => f.Store.Apply(plan)); Assert(File.ReadAllText(plan.CatalogUpdate.Path) == "external immutable content" && f.Before.SameContents(f.Store.Load(f.Home)));
        });
        Test("failed confirmed writes roll back embedded catalog and policy together", () =>
        {
            using var f = new Fixture(root); f.Draft.Model = NewModel; f.Draft.SetOverride("approval_policy", null);
            f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), f.Draft.Settings());
            var plan = f.Service.PreparePlan(f.Draft.Request()) with { AuthApiBaseUrl = "https://failed-write.invalid/v1" };
            f.Environment.FailOnce = true; var result = f.Save(f.Service.Review(plan));
            Assert(result.Outcome == ConfigurationSaveOutcome.Failed && f.Before.SameContents(f.Store.Load(f.Home)) && f.Draft.HasDraft && !f.Store.HasPending(f.Home));
        });
        Test("a service refresh owns its sync source even when native capability content is unchanged", () =>
        {
            using var f = new Fixture(root); var external = Path.Combine(f.Home, "external-same-capabilities.json");
            File.WriteAllBytes(external, f.Before.Catalog!.Content!);
            var externalPlan = ManagedModelCatalogStore.Prepare(f.Store.Plan(f.Before, f.Store.ReadSettings(f.Before), updateConnection: false), new(), external);
            f.Store.Apply(externalPlan); var current = f.Store.Load(f.Home);
            var embedded = ManagedModelCatalogStore.Prepare(f.Store.Plan(current, f.Store.ReadSettings(current), updateConnection: false), new(), serviceCatalog: current.Catalog!.Content!);
            Assert(ManagedModelCatalogStore.Index(embedded.CatalogUpdate!.Index)!.SourcePath == embedded.CatalogUpdate.Path && current.SameContents(f.Store.Load(f.Home)));
            f.Store.Apply(embedded); File.AppendAllText(external, " ");
            Assert(ManagedModelCatalogStore.Inspect(f.Store.Load(f.Home)) is { Managed: true, HasUpdate: false });
        });
        Test("a freshly fetched native catalog can accompany a reviewed endpoint change", () =>
        {
            using var f = new Fixture(root); f.Draft.BaseUrl = "https://new-discovery.invalid/v1"; f.Draft.Model = NewModel;
            f.Draft.AdoptDiscoveredCatalog(new([], 1, Latest), f.Draft.Settings()); var review = f.Service.PrepareReview(f.Draft.Request());
            Assert(f.Save(review).Outcome == ConfigurationSaveOutcome.Saved);
            var saved = f.Store.Load(f.Home); var doc = new TomlDocument(saved.Config);
            Assert(doc.String("openai_base_url") == "https://new-discovery.invalid/v1" &&
                ManagedModelCatalogStore.Index(saved.CatalogIndex)!.ConnectionId == ManagedModelCatalogStore.ConnectionId(doc, saved.Bridge) &&
                ConfigSchema.Literal(doc.Get("approval_policy")) == ConfigSchema.Literal(new TomlDocument(f.Before.Config).Get("approval_policy")));
        });
    }
    private static void Assert(bool value) { if (!value) throw new Exception("Catalog discovery save assertion failed"); }
    private static void Throws(Action action)
    { try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { return; } throw new Exception("Expected catalog discovery rejection"); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => Task.FromResult(response(request)); }
    private sealed class EnvironmentFixture : IUserEnvironment
    {
        private string? value; internal bool FailOnce;
        public string? Read() => value;
        public void Write(string? next) { if (FailOnce) { FailOnce = false; throw new IOException("Offline write failure fixture"); } value = next; }
    }
    private sealed class Fixture : IDisposable
    {
        internal string Home { get; }
        internal EnvironmentFixture Environment { get; } = new();
        internal ConfigStore Store { get; }
        internal ConfigSnapshot Before { get; }
        internal ConnectionDraftViewModel Draft { get; }
        internal ConfigurationSaveService Service { get; }
        internal Fixture(string root)
        {
            Home = Path.Combine(root, "catalog-discovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Home);
            File.WriteAllText(Path.Combine(Home, "config.toml"), "# offline fixture\nmodel='" + OldModel + "'\nopenai_base_url='https://catalog-discovery.invalid/v1'\napprovals_reviewer='auto_review'\napproval_policy={ granular={ sandbox_approval=true, rules=true } }\n");
            File.WriteAllText(Path.Combine(Home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-discovery-key\"}");
            File.WriteAllBytes(Path.Combine(Home, "models_cache.json"), Catalog(OldModel, "codex-auto-review"));
            Store = new(Environment, _ => null); var initial = Store.Load(Home);
            Store.Apply(ManagedModelCatalogStore.Prepare(Store.Plan(initial, Store.ReadSettings(initial), updateConnection: false), new()));
            Before = Store.Load(Home); var session = new WorkspaceSession(Home); session.Loaded(Home, Before);
            Service = new(Store, new(session)); Draft = new(Store); Draft.Load(Before);
        }
        internal ConfigurationSaveResult Save(ConfigurationSaveReview review) => Service.Save(review, Before, () => Home, () => { });
        public void Dispose() => Draft.Dispose();
    }
}