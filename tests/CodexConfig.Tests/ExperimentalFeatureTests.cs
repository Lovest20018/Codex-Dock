using System.Diagnostics;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class ExperimentalFeatureTests
{
    internal static bool IsFixture(string[] args) => args is ["app-server", "--listen", "stdio://"] &&
        Environment.GetEnvironmentVariable("CODEX_HOME") is { } home && File.Exists(Path.Combine(home, "feature-reader-fixture.txt"));
    internal static async Task<int> RunFixtureAsync()
    {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME")!; var mode = File.ReadAllText(Path.Combine(home, "feature-reader-fixture.txt"));
        File.WriteAllText(Path.Combine(home, "child.pid"), Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var document = JsonDocument.Parse(line); var request = document.RootElement; var method = request.GetProperty("method").GetString();
            File.AppendAllText(Path.Combine(home, "calls.txt"), method + "\n");
            if (!request.TryGetProperty("id", out var id)) continue;
            object response;
            if (method == "initialize") response = new { id = id.GetInt32(), result = new { userAgent = "Offline fixture execution end" } };
            else if (method == "experimentalFeature/list")
            {
                if (mode == "wait") { await Task.Delay(Timeout.InfiniteTimeSpan); continue; }
                if (mode == "unsupported") response = new { id = id.GetInt32(), error = new { code = -32601, message = "Method not found" } };
                else
                {
                    var cursor = request.GetProperty("params").GetProperty("cursor");
                    response = new { id = id.GetInt32(), result = new { data = new[] { new { name = cursor.ValueKind == JsonValueKind.Null ? "fixture_first" : "fixture_second", stage = "beta", enabled = true, defaultEnabled = false } }, nextCursor = cursor.ValueKind == JsonValueKind.Null || mode == "repeated" ? "next-page" : (string?)null } };
                }
            }
            else response = new { id = id.GetInt32(), error = new { code = -32601, message = "Only read methods allowed in fixture" } };
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response)); await Console.Out.FlushAsync();
        }
        return 0;
    }

    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("experimental feature parsing preserves unknown observations without treating missing flags as false", () =>
        {
            using var document = JsonDocument.Parse("""
                {"data":[{"name":"fixture_beta","stage":"beta","enabled":true,"defaultEnabled":false},
                {"name":"fixture_removed","stage":"removed","enabled":false},{"name":"future_feature","stage":"future-stage","enabled":"true"}],"nextCursor":null}
                """);
            var result = ExperimentalFeatureClient.ParsePage(document.RootElement);
            Assert(result.Features[0] is { CanStage: true, Enabled: true, DefaultEnabled: false } && result.Features[1] is { CanStage: false, DefaultEnabled: null } &&
                result.Features[2] is { Stage: "unknown", Enabled: null, CanStage: false });
            using var dotted = JsonDocument.Parse("{\"data\":[{\"name\":\"guardianv2.thread_context\",\"stage\":\"stable\",\"enabled\":false}]}");
            var observed = ExperimentalFeatureClient.ParsePage(dotted.RootElement).Features.Single();
            Assert(!observed.CanStage && observed.Path == "features.\"guardianv2.thread_context\"");
            foreach (var json in new[] { "{\"data\":[{\"name\":\"\"}]}", "{\"data\":[{\"name\":\"valid\"},{\"name\":\"valid\"}]}", "{\"data\":[],\"nextCursor\":7}" })
            { using var bad = JsonDocument.Parse(json); Throws<InvalidDataException>(() => ExperimentalFeatureClient.ParsePage(bad.RootElement)); }
            return Task.CompletedTask;
        }));
        tests.Add(("native feature reader initializes and pages read-only requests then exits its dedicated process", async () =>
        {
            var home = FixtureHome(root, "normal"); var reader = new ExperimentalFeatureClient();
            var result = await reader.ReadAsync(Environment.ProcessPath!, home, home, null);
            Assert(result.Features.Count == 2 && result.RuntimeInfo == "Offline fixture execution end" && result.Features.All(feature => feature.Enabled == true));
            Assert(File.ReadAllLines(Path.Combine(home, "calls.txt")).SequenceEqual(new[] { "initialize", "initialized", "experimentalFeature/list", "experimentalFeature/list" }) && !File.Exists(Path.Combine(home, "config.toml")));
            Assert(ChildExited(home));
        }));
        tests.Add(("unsupported or incomplete native feature lists remain unconfirmed and cannot leave a reader running", async () =>
        {
            var reader = new ExperimentalFeatureClient(); var unsupported = FixtureHome(root, "unsupported");
            await ThrowsAsync<NotSupportedException>(() => reader.ReadAsync(Environment.ProcessPath!, unsupported, unsupported, null));
            Assert(ChildExited(unsupported));
            var repeated = FixtureHome(root, "repeated");
            await ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(Environment.ProcessPath!, repeated, repeated, null));
            Assert(ChildExited(repeated));
        }));
        tests.Add(("cancelling native experimental feature reading terminates only its dedicated child", async () =>
        {
            var home = FixtureHome(root, "wait"); using var cancellation = new CancellationTokenSource();
            var pending = new ExperimentalFeatureClient().ReadAsync(Environment.ProcessPath!, home, home, null, cancellation.Token);
            for (var attempt = 0; attempt < 500 && (!File.Exists(Path.Combine(home, "calls.txt")) || !File.ReadAllText(Path.Combine(home, "calls.txt")).Contains("experimentalFeature/list")); attempt++) await Task.Delay(10);
            cancellation.Cancel(); await ThrowsAsync<OperationCanceledException>(async () => await pending);
            Assert(ChildExited(home));
        }));
        tests.Add(("experimental feature choices remain independent drafts until the ordinary reviewed save", async () =>
        {
            var f = new Fixture(root); using var draft = new ConnectionDraftViewModel(f.Store); draft.Load(f.Before); draft.Model = "independent-model-draft";
            var read = await f.Service.ReadAsync(f.Request, () => f.Request);
            ExperimentalFeatureService.Stage(read, "fixture_feature", true, draft, f.Request);
            Assert(draft.Model == "independent-model-draft" && draft.HasDraft && draft.Document().Get("features.fixture_feature") is true && f.Before.SameContents(f.Store.Load(f.Home)) && f.Store.LatestBackup(f.Home) is null);
            var session = new WorkspaceSession(f.Home); session.Loaded(f.Home, f.Before); var save = new ConfigurationSaveService(f.Store, new(session));
            var review = save.PrepareReview(draft.Request()); Assert(review.Changes.Any(change => change.Path == "features.fixture_feature"));
            var saved = save.Save(review, f.Before, () => f.Home, () => { });
            Assert(saved.Succeeded && new TomlDocument(saved.Saved!.Config).Get("features.fixture_feature") is true && f.Before.Auth!.AsSpan().SequenceEqual(saved.Saved.Auth!));
            var next = await f.Service.ReadAsync(f.Request, () => f.Request); Assert(next.Snapshot.Features.Single().Enabled == true);
        }));
        tests.Add(("experimental feature reads reject changed source conditions saved files profiles and cancellation", async () =>
        {
            var f = new Fixture(root);
            f.Runtime.BeforeRead = () => File.AppendAllText(Path.Combine(f.Home, "config.toml"), "model_verbosity='low'\n");
            await ThrowsAsync<OperationCanceledException>(() => f.Service.ReadAsync(f.Request, () => f.Request));
            f.Runtime.BeforeRead = null;
            await ThrowsAsync<OperationCanceledException>(() => f.Service.ReadAsync(f.Request, () => f.Request with { Project = Path.Combine(f.Home, "other") }));
            File.WriteAllText(ProfileStore.FilePath(f.Home, "daily"), "model='profile-fixture'\n"); var request = f.Request with { Profile = "daily" };
            f.Runtime.BeforeRead = () => File.AppendAllText(ProfileStore.FilePath(f.Home, "daily"), "model_verbosity='low'\n");
            await ThrowsAsync<OperationCanceledException>(() => f.Service.ReadAsync(request, () => request));
            using var cancellation = new CancellationTokenSource(); f.Runtime.BeforeRead = cancellation.Cancel;
            await ThrowsAsync<OperationCanceledException>(() => f.Service.ReadAsync(f.Request, () => f.Request, cancellation.Token));
        }));
        tests.Add(("stale removed and Profile feature observations cannot stage changes into the main draft", async () =>
        {
            var f = new Fixture(root); using var draft = new ConnectionDraftViewModel(f.Store); draft.Load(f.Before);
            var read = await f.Service.ReadAsync(f.Request, () => f.Request);
            Throws<InvalidOperationException>(() => ExperimentalFeatureService.Stage(read, "fixture_feature", true, draft, f.Request with { Executable = Path.Combine(f.Home, "other.exe") }));
            Throws<ArgumentException>(() => ExperimentalFeatureService.Stage(read with { Snapshot = read.Snapshot with { Features = [new("fixture_feature", "removed", false, false, "", "")] } }, "fixture_feature", true, draft, f.Request));
            var profileRequest = f.Request with { Profile = "daily" }; var profile = await f.Service.ReadAsync(profileRequest, () => profileRequest);
            Throws<InvalidOperationException>(() => ExperimentalFeatureService.Stage(profile, "fixture_feature", true, draft, profileRequest));
            File.AppendAllText(Path.Combine(f.Home, "config.toml"), "model_verbosity='low'\n");
            Throws<InvalidOperationException>(() => ExperimentalFeatureService.Stage(read, "fixture_feature", true, draft, f.Request));
            Assert(!draft.HasDraft && f.Store.LatestBackup(f.Home) is null);
        }));
        tests.Add(("feature shortcuts preserve existing structured draft values until reviewed in the configuration editor", async () =>
        {
            var f = new Fixture(root); using var draft = new ConnectionDraftViewModel(f.Store); draft.Load(f.Before);
            var read = await f.Service.ReadAsync(f.Request, () => f.Request); draft.SetOverride("features.fixture_feature", "{ nested = true }");
            var before = draft.Document().Text;
            foreach (var choice in new bool?[] { null, true, false })
                Throws<InvalidOperationException>(() => ExperimentalFeatureService.Stage(read, "fixture_feature", choice, draft, f.Request));
            Assert(draft.Document().Text == before && f.Before.SameContents(f.Store.Load(f.Home)));
        }));
    }

    internal static async Task ReadNativeAsync(string root, string executable)
    {
        root = Path.GetFullPath(root); if (Directory.Exists(root)) throw new IOException("Native feature acceptance needs a new isolated directory"); Directory.CreateDirectory(root);
        var home = Path.Combine(root, "home"); Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='fixture-native-read'\n[features]\nfast_mode=false\n");
        var before = File.ReadAllBytes(Path.Combine(home, "config.toml"));
        var sources = await new ConfigReadClient().ReadAsync(executable, home, home, null, before);
        Assert(sources.Rows.Any(row => row.Path == "model" && row.EffectiveValue == "\"fixture-native-read\""));
        var result = await new ExperimentalFeatureClient().ReadAsync(executable, home, home, null);
        Assert(before.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(home, "config.toml"))) && !Directory.Exists(Path.Combine(home, "sessions")) && !File.Exists(Path.Combine(home, "auth.json")));
        await File.WriteAllTextAsync(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { status = "PASS", scope = "Fresh native execution end, config and feature reads only; no login, conversation, turn or tool", configRows = sources.Rows.Count, configRuntime = sources.RuntimeInfo, result }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS native read: {sources.Rows.Count} configuration rows, {result.Features.Count} features, no configuration writes or conversation");
    }
    private sealed class Fixture
    {
        internal readonly string Home; internal readonly ConfigStore Store; internal readonly ConfigSnapshot Before; internal readonly Runtime Runtime = new(); internal readonly ExperimentalFeatureService Service;
        internal ConfigurationSourceRequest Request => new(Home, Environment.ProcessPath!, Home);
        internal Fixture(string root)
        {
            Home = Path.Combine(root, "feature-service-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Home);
            File.WriteAllText(Path.Combine(Home, "config.toml"), "model='feature-fixture'\nopenai_base_url='https://fixture.invalid/v1'\n"); File.WriteAllText(Path.Combine(Home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-feature-key-only\"}");
            Store = new(new MemoryEnvironment(), _ => null); Before = Store.Load(Home); Service = new(Runtime);
        }
    }
    private sealed class Runtime : IExperimentalFeatureRuntime
    {
        internal Action? BeforeRead;
        public Task<ExperimentalFeatureSnapshot> ReadAsync(ConfigurationSourceRequest request, CancellationToken cancellation)
        { BeforeRead?.Invoke(); var enabled = new TomlDocument(File.ReadAllBytes(Path.Combine(request.Home, "config.toml"))).Get("features.fixture_feature") is true; return Task.FromResult(new ExperimentalFeatureSnapshot(DateTimeOffset.UtcNow, "Fixture", [new("fixture_feature", "beta", enabled, false, "", "")])); }
    }
    private sealed class MemoryEnvironment : IUserEnvironment { public string? Read() => null; public void Write(string? value) => throw new InvalidOperationException(); }
    private static string FixtureHome(string root, string mode)
    { var home = Path.Combine(root, "feature-reader-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home); File.WriteAllText(Path.Combine(home, "feature-reader-fixture.txt"), mode); return home; }
    private static bool ChildExited(string home)
    { var id = int.Parse(File.ReadAllText(Path.Combine(home, "child.pid")), System.Globalization.CultureInfo.InvariantCulture); try { using var process = Process.GetProcessById(id); return process.HasExited; } catch (ArgumentException) { return true; } }
    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Experimental feature assertion failed"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
