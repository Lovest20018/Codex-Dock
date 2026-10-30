using System.Net;
using System.Text;
using System.Text.Json;
using CodexConfig.Core;
using Tomlyn.Model;

if (args is ["--model-bridge", var workerHome]) return await BridgeProcess.RunAsync(workerHome);
if (args is ["--gateway-launch-once", var launcherHome])
{
    var bytes = File.ReadAllBytes(Path.Combine(launcherHome, MixedConnection.FileName));
    await BridgeProcess.EnsureAsync(launcherHome, bytes); return 0;
}

var root = Path.GetFullPath(args.FirstOrDefault() ?? ".test-home/core-tests");
Directory.CreateDirectory(root);
var runRoot = Path.Combine(root, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(runRoot);
var tests = new List<(string Name, Func<Task> Run)>();
var utf8 = new UTF8Encoding(false);
byte[] Bytes(string text) => utf8.GetBytes(text);
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}
string Home() { var path = Path.Combine(runRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
ConnectionSettings Settings(string address = "https://gateway.example.com", string key = "ah-test-key") => new(address, key, "gpt-6.1-sol");
void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));

Test("normalize PAT relay root and full Responses API paths", () =>
{
    Assert(Settings().Normalize().BaseUrl == "https://gateway.example.com/backend-api/codex");
    Assert(Settings("https://gateway.example.com/proxy/codex/responses/").Normalize().BaseUrl == "https://gateway.example.com/proxy/codex");
    Assert(Settings("http://127.0.0.1:9999/codex/").Normalize().BaseUrl == "http://127.0.0.1:9999/codex");
});
Test("reject unsafe or non-compatibility addresses", () =>
{
    foreach (var value in new[] { "file:///tmp", "https://user:secret@gateway.example.com", "https://gateway.example.com?key=x", "ftp://gateway.example.com/v1", "example.com", "https://gateway.example.com/#fragment" })
        Throws<ArgumentException>(() => Settings(value).Normalize());
    Throws<ArgumentException>(() => Settings(key: "a\nb").Normalize());
    Throws<ArgumentException>(() => Settings(key: "").Normalize());
});
Test("write PAT routing with three distinct endpoints", () =>
{
    var store = new ConfigStore(new FakeEnvironment());
    var plan = store.Plan(store.Load(Home()), Settings());
    var doc = new TomlDocument(plan.Config);
    Assert(doc.String("model_provider") == "openai");
    Assert(doc.String("openai_base_url") == "https://gateway.example.com/backend-api/codex");
    Assert(doc.String("chatgpt_base_url") == "https://gateway.example.com/backend-api/");
    Assert(plan.AuthApiBaseUrl == "https://gateway.example.com");
    Assert(!doc.Text.Contains("experimental_bearer_token"));
    using var auth = JsonDocument.Parse(plan.Auth);
    Assert(auth.RootElement.GetProperty("personal_access_token").GetString() == "ah-test-key");
    Assert(auth.RootElement.GetProperty("OPENAI_API_KEY").GetString() == "ah-test-key");
    Assert(auth.RootElement.EnumerateObject().Count() == 2);
});
Test("preserve BOM, CRLF, comments and unrelated sections", () =>
{
    var original = "# 中文\r\nmodel = 'old' # keep comment\r\n[projects.'D:\\work']\r\ntrust_level = 'trusted'\r\n[features]\r\nfast_mode = false # keep fast\r\n[mcp_servers.demo]\r\ncommand = 'hello'\r\n";
    var bytes = Encoding.UTF8.Preamble.ToArray().Concat(Bytes(original)).ToArray();
    var home = Home(); File.WriteAllBytes(Path.Combine(home, "config.toml"), bytes);
    var store = new ConfigStore(new FakeEnvironment());
    var plan = store.Plan(store.Load(home), Settings());
    Assert(plan.Config.AsSpan().StartsWith(Encoding.UTF8.Preamble));
    var output = new TomlDocument(plan.Config).Text;
    Assert(output.Contains("# keep comment\r\n"));
    Assert(output.Contains("[projects.'D:\\work']\r\ntrust_level = 'trusted'\r\n"));
    Assert(output.Contains("[mcp_servers.demo]\r\ncommand = 'hello'\r\n"));
    Assert(!output.Replace("\r\n", "").Contains('\n'));
    Assert(output.Contains("fast_mode = true # keep fast"));
});
Test("quoted/dotted/inline TOML keys and multiline values", () =>
{
    foreach (var original in new[] {
        "\"model\" = '''old\nmodel''' # comment\nfeatures = { fast_mode = false }\n",
        "features.fast_mode = false\nmodel = 'old'\n[features.other]\nflag = true\n",
        "[\"features\"]\n\"fast_mode\" = false\ncontext_management = false\n"
    })
    {
        var home = Home(); File.WriteAllText(Path.Combine(home, "config.toml"), original, utf8);
        var store = new ConfigStore(new FakeEnvironment());
        var doc = new TomlDocument(store.Plan(store.Load(home), Settings()).Config);
        Assert(doc.String("model") == "gpt-6.1-sol");
        var features = (TomlTable)doc.Model["features"];
        Assert((bool)features["fast_mode"] && (bool)features["context_management"]);
    }
});
Test("do not mistake multiline string content for assignments", () =>
{
    var sample = "developer_instructions = '''\n[features]\nmodel = 'literal content'\n'''\nmodel = 'old'\n";
    var doc = new TomlDocument(Bytes(sample));
    doc.Set("model", "new"); doc.Set("features.fast_mode", true);
    Assert(doc.Text.Contains("[features]\nmodel = 'literal content'\n'''"));
    Assert(doc.String("model") == "new");
});
Test("preserve active profile and login restrictions with notes", () =>
{
    var home = Home();
    File.WriteAllText(Path.Combine(home, "config.toml"), "profile='work'\nforced_login_method='chatgpt'\nforced_chatgpt_workspace_id='original'\n[profiles.work]\nmodel='old'\n", utf8);
    var store = new ConfigStore(new FakeEnvironment()); var plan = store.Plan(store.Load(home), Settings());
    var doc = new TomlDocument(plan.Config);
    Assert(doc.String("profile") == "work" && doc.String("forced_login_method") == "chatgpt" && doc.String("forced_chatgpt_workspace_id") == "original");
    Assert(doc.Model.ContainsKey("profiles")); Assert(plan.Notes.Count >= 3);
});
Test("reject invalid TOML and invalid auth without rewriting", () =>
{
    Throws<InvalidDataException>(() => new TomlDocument(Bytes("model = 'a'\nmodel='b'\n")));
    Throws<InvalidDataException>(() => new TomlDocument([255, 254, 100]));
    var home = Home(); File.WriteAllText(Path.Combine(home, "auth.json"), "{invalid", utf8);
    var store = new ConfigStore(new FakeEnvironment());
    Throws<InvalidDataException>(() => store.Plan(store.Load(home), Settings()));
    Assert(File.ReadAllText(Path.Combine(home, "auth.json")) == "{invalid");
});
Test("masked previews hide current and old credentials", () =>
{
    var home = Home();
    File.WriteAllText(Path.Combine(home, "config.toml"), "[model_providers.legacy]\nexperimental_bearer_token='old-key'\nhttp_headers={ Authorization='old-header' }\n", utf8);
    var store = new ConfigStore(new FakeEnvironment()); var plan = store.Plan(store.Load(home), Settings());
    Assert(!plan.ConfigPreview.Contains("old-key") && !plan.ConfigPreview.Contains("old-header") && !plan.AuthPreview.Contains("ah-test-key"));
    Assert(Encoding.UTF8.GetString(plan.Config).Contains("old-key"));
});
Test("new install save, encrypted backup, restore to absent files", () =>
{
    var home = Home(); var env = new FakeEnvironment(); var store = new ConfigStore(env);
    var backup = store.Apply(store.Plan(store.Load(home), Settings()));
    Assert(File.Exists(Path.Combine(home, "config.toml")) && File.Exists(Path.Combine(home, "auth.json")));
    Assert(env.Read() == "https://gateway.example.com");
    Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(backup)).Contains("ah-test-key"));
    Assert(!store.HasPending(home));
    store.Restore(store.Load(home));
    Assert(!File.Exists(Path.Combine(home, "config.toml")) && !File.Exists(Path.Combine(home, "auth.json")) && env.Read() is null);
});
Test("restore original auth and config byte-for-byte", () =>
{
    var home = Home(); var config = Bytes("# original\r\nmodel='old'\r\n"); var auth = Bytes("{ \"tokens\": { \"access_token\": \"fake-original\" } }\r\n");
    File.WriteAllBytes(Path.Combine(home, "config.toml"), config); File.WriteAllBytes(Path.Combine(home, "auth.json"), auth);
    var env = new FakeEnvironment { Value = "https://old.example/codex" }; var store = new ConfigStore(env);
    var original = store.Load(home); store.Apply(store.Plan(original, Settings())); store.Restore(store.Load(home));
    Assert(File.ReadAllBytes(Path.Combine(home, "config.toml")).SequenceEqual(config));
    Assert(File.ReadAllBytes(Path.Combine(home, "auth.json")).SequenceEqual(auth));
    Assert(env.Read() == original.AuthApiBaseUrl);
});
Test("reject concurrent changes before save", () =>
{
    var home = Home(); var env = new FakeEnvironment(); var store = new ConfigStore(env); var plan = store.Plan(store.Load(home), Settings());
    File.WriteAllText(Path.Combine(home, "config.toml"), "# external edit\n", utf8);
    Throws<InvalidOperationException>(() => store.Apply(plan));
    Assert(File.ReadAllText(Path.Combine(home, "config.toml")) == "# external edit\n"); Assert(env.Read() is null);
});
Test("reject restore after external changes", () =>
{
    var home = Home(); var store = new ConfigStore(new FakeEnvironment()); store.Apply(store.Plan(store.Load(home), Settings()));
    File.AppendAllText(Path.Combine(home, "config.toml"), "# user edit\n", utf8);
    Throws<InvalidOperationException>(() => store.Restore(store.Load(home)));
});
Test("roll back files on environment write failure", () =>
{
    var home = Home(); var config = Bytes("model='old'\n"); var auth = Bytes("{\"OPENAI_API_KEY\":\"fake-old\"}");
    File.WriteAllBytes(Path.Combine(home, "config.toml"), config); File.WriteAllBytes(Path.Combine(home, "auth.json"), auth);
    var env = new FakeEnvironment { ThrowOnce = true }; var store = new ConfigStore(env);
    Throws<IOException>(() => store.Apply(store.Plan(store.Load(home), Settings())));
    Assert(File.ReadAllBytes(Path.Combine(home, "config.toml")).SequenceEqual(config));
    Assert(File.ReadAllBytes(Path.Combine(home, "auth.json")).SequenceEqual(auth));
    Assert(env.Read() is null && !store.HasPending(home) && store.LatestBackup(home) is null);
});
Test("roll back config when auth file is read-only", () =>
{
    var home = Home(); var config = Bytes("model='old'\n"); var auth = Bytes("{\"OPENAI_API_KEY\":\"fake-old\"}");
    var configPath = Path.Combine(home, "config.toml"); var authPath = Path.Combine(home, "auth.json");
    File.WriteAllBytes(configPath, config); File.WriteAllBytes(authPath, auth); File.SetAttributes(authPath, FileAttributes.ReadOnly);
    try
    {
        var store = new ConfigStore(new FakeEnvironment());
        Throws<IOException>(() => store.Apply(store.Plan(store.Load(home), Settings())));
        Assert(File.ReadAllBytes(configPath).SequenceEqual(config) && File.ReadAllBytes(authPath).SequenceEqual(auth));
        Assert(!store.HasPending(home));
    }
    finally { File.SetAttributes(authPath, FileAttributes.Normal); }
});
Test("enforce per-home write lock", () =>
{
    var home = Home(); var dir = Path.Combine(home, "codex-config-backups"); Directory.CreateDirectory(dir);
    using var locked = new FileStream(Path.Combine(dir, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    var store = new ConfigStore(new FakeEnvironment()); Throws<IOException>(() => store.Apply(store.Plan(store.Load(home), Settings())));
    Assert(!File.Exists(Path.Combine(home, "config.toml")));
});
Test("recover interrupted save from encrypted journal", () =>
{
    var home = Home(); var env = new FakeEnvironment { Value = "https://previous.example/codex" };
    var store = new ConfigStore(env); var config = Bytes("model='original'\n");
    File.WriteAllBytes(Path.Combine(home, "config.toml"), config);
    var backup = store.Apply(store.Plan(store.Load(home), Settings()));
    File.WriteAllText(Path.Combine(home, "codex-config-backups", "pending.json"), Path.GetFileName(backup));
    Assert(store.HasPending(home)); store.Recover(home);
    Assert(File.ReadAllBytes(Path.Combine(home, "config.toml")).SequenceEqual(config));
    Assert(!File.Exists(Path.Combine(home, "auth.json")) && env.Read() == "https://previous.example/codex");
    Assert(!store.HasPending(home) && store.LatestBackup(home) is null);
});
Test("interrupted recovery refuses unexpected external edits", () =>
{
    var home = Home(); var store = new ConfigStore(new FakeEnvironment());
    var backup = store.Apply(store.Plan(store.Load(home), Settings()));
    File.WriteAllText(Path.Combine(home, "codex-config-backups", "pending.json"), Path.GetFileName(backup));
    File.AppendAllText(Path.Combine(home, "config.toml"), "# external\n");
    Throws<IOException>(() => store.Recover(home));
    Assert(store.HasPending(home) && File.ReadAllText(Path.Combine(home, "config.toml")).Contains("# external"));
});

Test("automatic auth falls back for generic URL, HTTP", () =>
{
    Assert((Settings("https://gateway.example.com/v1") with { Kind = ConnectionKind.ResponsesApi }).Decide().Mode == AuthenticationMode.ApiKey);
    Assert(Settings("http://127.0.0.1:9988").Decide().Mode == AuthenticationMode.ApiKey);
    Assert((Settings("https://gateway.example.com/custom").Decide().Mode == AuthenticationMode.ApiKey));
    Throws<ArgumentException>(() => (Settings("http://127.0.0.1:9988") with { Authentication = AuthenticationChoice.Pat }).Decide());
});
Test("API Key rendering has no PAT and preserves auth URL/environment", () =>
{
    var home = Home(); File.WriteAllText(Path.Combine(home, "config.toml"), "chatgpt_base_url='https://old.example/backend-api/'\n");
    var env = new FakeEnvironment { Value = "https://old-auth.example" }; var store = new ConfigStore(env);
    var plan = store.Plan(store.Load(home), Settings("https://gateway.example.com/v1") with { Kind = ConnectionKind.ResponsesApi });
    using var auth = JsonDocument.Parse(plan.Auth);
    Assert(auth.RootElement.GetProperty("auth_mode").GetString() == "apikey" && !auth.RootElement.TryGetProperty("personal_access_token", out _));
    Assert(plan.AuthApiBaseUrl == env.Value && new TomlDocument(plan.Config).String("chatgpt_base_url") == "https://old.example/backend-api/");
});
Test("Krill official lines and legacy origins produce current PAT endpoints", () =>
{
    foreach (var line in KrillLines.All)
        foreach (var origin in new[] { line.Origin }.Concat(line.LegacyOrigins))
        {
            var settings = Settings(origin + "/backend-api/codex") with { Authentication = AuthenticationChoice.Pat };
            var normalized = settings.Normalize(); var decision = normalized.Decide();
            Assert(decision.Mode == AuthenticationMode.Pat && decision.ModelBaseUrl == line.Origin + "/backend-api/codex");
            Assert(decision.ChatGptBaseUrl == line.Origin + "/backend-api/" && decision.AuthApiOrigin == line.Origin);
            Assert(KrillLines.Find(origin)?.Id == line.Id);
            using var auth = JsonDocument.Parse(normalized.RenderAuth());
            Assert(auth.RootElement.TryGetProperty("personal_access_token", out _) && !auth.RootElement.TryGetProperty("auth_mode", out _));
        }
    Assert(KrillLines.Cdn.Origin == "https://api.kbsfdxc.com");
    Assert(KrillLines.Find("https://api.kbsfdxc.com.attacker.example") is null);
    Assert(KrillLines.Find("https://user@api.kbsfdxc.com") is null);
    Assert((Settings("https://api.cdn-krill-ai.com/v1") with { Kind = ConnectionKind.ResponsesApi }).Normalize().BaseUrl == "https://api.cdn-krill-ai.com/v1");
});
Test("detailed catalog loads local schema, Chinese and lifecycle", () =>
{
    var catalog = ConfigSchema.Instance;
    Assert(catalog.Entries.Count == 983 && catalog.SchemaVersion.StartsWith("local-"));
    Assert(catalog.Find("model_reasoning_effort")!.Chinese.Contains("推理"));
    Assert(catalog.Find("features.fast_mode")!.ManagedConnection);
    Assert(catalog.Entries.Where(e => e.Stage == "removed").All(e => !e.CanEdit));
});
Test("catalog validates scalar types, enums, ranges and dynamic paths", () =>
{
    var catalog = ConfigSchema.Instance;
    catalog.Validate("model_reasoning_effort", "\"high\"");
    catalog.Validate("mcp_servers.demo.command", "\"node\"");
    Throws<ArgumentException>(() => catalog.Validate("model_context_window", "\"wrong\""));
    Throws<ArgumentException>(() => catalog.Validate("web_search", "\"wrong\""));
    Throws<ArgumentException>(() => catalog.Validate("history.persistence", "\"wrong\""));
    catalog.Validate("does_not_exist", "true");
    Throws<ArgumentException>(() => catalog.Validate("agents.max_concurrent_threads_per_session", "0"));
    Throws<ArgumentException>(() => catalog.Validate("audio.microphone_channel", "false"));
    Throws<ArgumentException>(() => catalog.Validate("mcp_servers.demo.command", "\"node\"\nother = true"));
});
Test("detailed edits merge, unset values and preserve unrelated source", () =>
{
    var home = Home(); File.WriteAllText(Path.Combine(home, "config.toml"), "# retain\nmodel_reasoning_effort='low' # effort\n[history]\npersistence='save-all'\n[features]\ncontext_management=true\n");
    var store = new ConfigStore(new FakeEnvironment());
    var plan = store.Plan(store.Load(home), Settings(), new Dictionary<string, string?> {
        ["model_reasoning_effort"] = "\"high\"", ["model_context_window"] = "500000", ["history.persistence"] = null, ["mcp_servers.demo.command"] = "\"node\""
    });
    var doc = new TomlDocument(plan.Config);
    Assert(doc.String("model_reasoning_effort") == "high" && (long)doc.Get("model_context_window")! == 500000);
    Assert(doc.Get("history.persistence") is null && doc.Get("mcp_servers.demo.command") as string == "node");
    Assert(doc.Text.StartsWith("# retain") && doc.Text.Contains("# effort"));
    Assert(plan.ConfigPreview.Contains("model_context_window = 500000"));
});
Test("remove dotted and inline values without corrupting neighbors", () =>
{
    foreach (var original in new[] { "features = { fast_mode=true, context_management=true }\n", "features.fast_mode=true\nfeatures.context_management=true\n" })
    {
        var doc = new TomlDocument(Bytes(original)); doc.Remove("features.fast_mode");
        Assert(doc.Get("features.fast_mode") is null && doc.Get("features.context_management") is true);
    }
});
Test("Unicode and cached web search survive roundtrip", () =>
{
    var text = "中文 😀\nsecond line";
    var doc = new TomlDocument(null); doc.Set("developer_instructions", text);
    Assert(doc.String("developer_instructions") == text);
    var store = new ConfigStore(new FakeEnvironment());
    var plan = store.Plan(store.Load(Home()), Settings() with { WebSearchMode = "cached" });
    Assert(new TomlDocument(plan.Config).String("web_search") == "cached");
});
Test("UI saves preserve absent preferences and existing preference values", () =>
{
    var store = new ConfigStore(new FakeEnvironment());
    var settings = Settings() with { PreservePreferenceValues = true };
    var fresh = store.Plan(store.Load(Home()), settings);
    var empty = new TomlDocument(fresh.Config);
    Assert(empty.Get("features.fast_mode") is null && empty.Get("features.context_management") is null && empty.Get("web_search") is null);
    var path = Home();
    File.WriteAllText(Path.Combine(path, "config.toml"), "web_search='cached' # keep\nfeatures = { fast_mode=false, context_management=false }\n");
    var plan = store.Plan(store.Load(path), settings);
    var doc = new TomlDocument(plan.Config);
    Assert(doc.String("web_search") == "cached" && doc.Get("features.fast_mode") is false && doc.Get("features.context_management") is false);
    Assert(doc.Text.Contains("# keep"));
});
Test("UI preference overrides validate and reset without changing neighbors", () =>
{
    var store = new ConfigStore(new FakeEnvironment()); var path = Home();
    File.WriteAllText(Path.Combine(path, "config.toml"), "features = { fast_mode=true, context_management=false }\nweb_search='live'\n");
    var settings = Settings() with { PreservePreferenceValues = true };
    var plan = store.Plan(store.Load(path), settings, new Dictionary<string, string?> { ["features.fast_mode"] = null, ["web_search"] = "\"cached\"" });
    var doc = new TomlDocument(plan.Config);
    Assert(doc.Get("features.fast_mode") is null && doc.Get("features.context_management") is false && doc.String("web_search") == "cached");
    Throws<ArgumentException>(() => store.Plan(store.Load(path), settings, new Dictionary<string, string?> { ["features.fast_mode"] = "\"true\"" }));
    Throws<ArgumentException>(() => store.Plan(store.Load(path), settings, new Dictionary<string, string?> { ["web_search"] = "\"unsupported\"" }));
    Throws<ArgumentException>(() => store.Plan(store.Load(path), settings, new Dictionary<string, string?> { ["openai_base_url"] = "\"https://elsewhere.example\"" }));
});
tests.Add(("model retrieval only reads chosen model endpoint and keeps auth decision", async () =>
{
    var requests = new List<string>();
    using var probe = new ModelCatalogClient(new FakeHandler(request =>
    {
        requests.Add(request.RequestUri!.AbsolutePath);
        Assert(request.Headers.Authorization?.Parameter == "ah-test-key");
        return new(HttpStatusCode.OK) { Content = new StringContent("{\"models\":[{\"slug\":\"one\"},{\"id\":\"two\"},{\"model\":\"one\"}]}") };
    }));
    var settings = Settings() with { Authentication = AuthenticationChoice.Pat };
    var result = await probe.ReadModelsAsync(settings);
    Assert(result.Models.SequenceEqual(new[] { "one", "two" }));
    Assert(requests.SequenceEqual(new[] { "/backend-api/codex/models" }) && settings.Decide().Mode == AuthenticationMode.Pat);
    var withoutModel = await probe.ReadModelsAsync(settings with { Model = "" });
    Assert(withoutModel.Models.Count == 2 && requests.Count == 2);
}));
tests.Add(("model retrieval does not fall back or echo service errors", async () =>
{
    var requests = 0;
    using var probe = new ModelCatalogClient(new FakeHandler(_ => { requests++; return new(HttpStatusCode.NotFound) { Content = new StringContent("ah-test-key") }; }));
    try { await probe.ReadModelsAsync(Settings()); throw new InvalidOperationException("Expected failure"); }
    catch (HttpRequestException error) { Assert(!error.Message.Contains("ah-test-key") && requests == 1); }
}));

Test("config-only save needs no credentials and creates no authentication or connection overrides", () =>
{
    var home = Home(); var env = new FakeEnvironment(); var store = new ConfigStore(env);
    var plan = store.Plan(store.Load(home), new("", "", ""), new Dictionary<string, string?> {
        ["future.flag"] = "true", ["model_context_window"] = "300000"
    }, updateConnection: false);
    store.Apply(plan);
    var saved = store.Load(home); var doc = new TomlDocument(saved.Config);
    Assert(doc.Get("future.flag") is true && doc.Get("model_context_window") is 300000L);
    Assert(doc.Get("model_provider") is null && doc.Get("openai_base_url") is null && doc.Get("model") is null);
    Assert(saved.Auth is null && env.Value is null);
    store.Restore(saved);
    Assert(!File.Exists(Path.Combine(home, "config.toml")) && !File.Exists(Path.Combine(home, "auth.json")));
});
Test("config-only save preserves a read-only auth file byte-for-byte and the user environment", () =>
{
    var home = Home(); var env = new FakeEnvironment { Value = "https://existing.example" }; var store = new ConfigStore(env);
    var authFile = Path.Combine(home, "auth.json");
    var original = Bytes("{ \"tokens\":{\"access_token\":\"fake-existing-token\"}, \"extra\":true }\r\n");
    File.WriteAllBytes(authFile, original); File.SetAttributes(authFile, FileAttributes.ReadOnly);
    try
    {
        store.Apply(store.Plan(store.Load(home), new("", "", ""), new Dictionary<string, string?> { ["future.option"] = "[1, 2]" }, updateConnection: false));
        Assert(File.ReadAllBytes(authFile).SequenceEqual(original) && env.Value == "https://existing.example");
        Assert(new TomlDocument(store.Load(home).Config).Get("future.option") is TomlArray { Count: 2 });
    }
    finally { File.SetAttributes(authFile, FileAttributes.Normal); }
});
Test("model-only edits preserve the existing provider and authentication without requiring a key", () =>
{
    var home = Home(); var store = new ConfigStore(new FakeEnvironment());
    File.WriteAllText(Path.Combine(home, "config.toml"), "model_provider='custom'\nmodel='original'\nweb_search='indexed'\n");
    var plan = store.Plan(store.Load(home), new("", "", "chosen-model"), updateConnection: false, updateModel: true);
    store.Apply(plan); var doc = new TomlDocument(store.Load(home).Config);
    Assert(doc.String("model") == "chosen-model" && doc.String("model_provider") == "custom" && doc.String("web_search") == "indexed");
    Assert(store.Load(home).Auth is null);
    Throws<ArgumentException>(() => store.Plan(store.Load(home), new("", "", ""), updateConnection: false, updateModel: true));
});
Test("unknown and quoted paths edit and remove without touching comments or neighbors", () =>
{
    var sample = "# retain\r\n[future.'a.b']\r\nvalue = 1 # keep\r\nother = 'safe'\r\n[projects.'D:\\a.b']\r\ntrust_level = 'trusted'\r\n";
    var doc = new TomlDocument(Bytes(sample));
    var path = "future.\"a.b\".value";
    ConfigSchema.Instance.Validate(path, "2"); doc.SetRaw(path, "2");
    Assert(doc.Get(path) is 2L && doc.Text.Contains("value = 2 # keep\r\n") && doc.Text.Contains("other = 'safe'\r\n"));
    Assert(ConfigSchema.Instance.Find("projects.'D:\\a.b'.trust_level") is not null);
    doc.SetRaw("future.\"a.b\".\"new.key\"", "true");
    Assert(doc.Get("future.\"a.b\".\"new.key\"") is true);
    Assert(doc.Remove(path) && doc.Get(path) is null && doc.Get("future.\"a.b\".other") as string == "safe");
    var unlisted = ConfigSchema.Instance.UnlistedEntries(doc.Model, []).ToArray();
    Assert(unlisted.Any(e => e.Path == "future.\"a.b\".\"new.key\""));
    Throws<ArgumentException>(() => ConfigSchema.Instance.Validate("future.test", "1\nsecond = 2"));
    Throws<ArgumentException>(() => ConfigSchema.Instance.Validate("\"model\"", "\"bad-override\""));
    Throws<ArgumentException>(() => TomlDocument.ParsePath("fake = 0 # injected"));
    Throws<ArgumentException>(() => TomlDocument.ParsePath("future.<name>.option"));
});
Test("ordinary PAT save leaves legacy baseline untouched", () =>
{
    var home = Home(); var store = new ConfigStore(new FakeEnvironment());
    var dir = Path.Combine(home, "codex-config-backups"); Directory.CreateDirectory(dir);
    var legacy = Path.Combine(dir, "pat-test-baseline.dat"); File.WriteAllText(legacy, "opaque legacy file");
    store.Apply(store.Plan(store.Load(home), Settings()));
    Assert(File.ReadAllText(legacy) == "opaque legacy file");
    store.Restore(store.Load(home)); Assert(File.ReadAllText(legacy) == "opaque legacy file");
});

Test("Krill recommendations stage all seven keys and preserve unrelated configuration and authentication", () =>
{
    var home = Home(); var env = new FakeEnvironment(); var store = new ConfigStore(env);
    File.WriteAllText(Path.Combine(home, "config.toml"), "# keep\nweb_search='disabled' # search\n[analytics]\nenabled=true\n[other]\nflag='retain'\n");
    var before = store.Load(home); var edits = new Dictionary<string, string?> { ["model_context_window"] = "500000" };
    var template = BuiltInTemplates.KrillCommunity;
    var application = template.StageSelected(new TomlDocument(before.Config).Model, edits, template.Settings.Select(setting => setting.Path));
    Assert(template.Settings.Count == 7 && application.Count == 7 && edits.Count == 8);
    Assert(File.ReadAllBytes(Path.Combine(home, "config.toml")).SequenceEqual(before.Config!));
    var plan = store.Plan(before, new("", "", ""), edits, updateConnection: false);
    store.Apply(plan); var doc = new TomlDocument(store.Load(home).Config);
    foreach (var setting in template.Settings)
        Assert(ConfigSchema.Literal(doc.Get(setting.Path)) == setting.Value, setting.Path);
    Assert(doc.Get("model_context_window") is 500000L && doc.Get("other.flag") as string == "retain" && doc.Text.Contains("# search"));
    Assert(store.Load(home).Auth is null && env.Value is null);
    store.Restore(store.Load(home));
    Assert(File.ReadAllBytes(Path.Combine(home, "config.toml")).SequenceEqual(before.Config!));
});

Test("template diff respects effective draft values and default opt ins", () =>
{
    var template = BuiltInTemplates.KrillCommunity;
    var original = new TomlDocument(Bytes("web_search='live'\n[analytics]\nenabled=true\n")).Model;
    var draft = new Dictionary<string, string?> { ["web_search"] = "\"cached\"", ["features.fast_mode"] = "false" };
    var diff = template.BuildDiff(original, draft);
    Assert(diff.Single(x => x.Setting.Path == "web_search") is { CurrentValue: "\"cached\"", HasDraft: true, Same: false, SelectedByDefault: false });
    Assert(diff.Count(x => x.SelectedByDefault) == 1);
    Assert(diff.Where(x => !x.Setting.SelectByDefault).Count() == 4 && draft.Count == 2);
});
Test("equal and repeated template applications produce no redundant edits", () =>
{
    var template = BuiltInTemplates.KrillCommunity;
    var original = new TomlDocument(Bytes("web_search='live'\n")).Model;
    var draft = new Dictionary<string, string?>();
    var all = template.Settings.Select(setting => setting.Path).ToArray();
    Assert(template.StageSelected(original, draft, all).Count == 6);
    Assert(template.StageSelected(original, draft, all).Count == 0 && !draft.ContainsKey("web_search"));
});
Test("template undo restores prior draft and preserves unrelated and later edits", () =>
{
    var template = BuiltInTemplates.KrillCommunity;
    var original = new TomlDocument(null).Model;
    var draft = new Dictionary<string, string?> { ["web_search"] = "'cached'", ["other.flag"] = "true" };
    var application = template.StageSelected(original, draft, ["web_search", "features.fast_mode", "analytics.enabled"]);
    draft["features.fast_mode"] = "false";
    var undone = application.Undo(draft);
    Assert(undone.Restored == 2 && undone.Preserved == 1);
    Assert(draft["web_search"] == "'cached'" && draft["features.fast_mode"] == "false" && draft["other.flag"] == "true" && !draft.ContainsKey("analytics.enabled"));
    Assert(application.Undo(draft).Restored == 0);
});
Test("template reverting draft to file value can be undone", () =>
{
    var original = new TomlDocument(Bytes("web_search='live'\n")).Model;
    var draft = new Dictionary<string, string?> { ["web_search"] = null };
    var application = BuiltInTemplates.KrillCommunity.StageSelected(original, draft, ["web_search"]);
    Assert(application.Count == 1 && draft.Count == 0);
    application.Undo(draft); Assert(draft.ContainsKey("web_search") && draft["web_search"] is null);
});
Test("template validates all selected paths before mutating the draft", () =>
{
    var draft = new Dictionary<string, string?> { ["other.flag"] = "true" };
    var original = new TomlDocument(null).Model;
    var invalid = new ConfigTemplate("invalid", "Invalid", "test", "https://example.com", "", [new("web_search", "\"live\""), new("analytics.enabled", "\"bad\"")]);
    Throws<ArgumentException>(() => invalid.StageSelected(original, draft, ["web_search", "analytics.enabled"]));
    Throws<ArgumentException>(() => BuiltInTemplates.KrillCommunity.StageSelected(original, draft, ["unknown.field"]));
    Assert(draft.Count == 1);
});
Test("curated help covers common settings, connections and complete template with verification provenance", () =>
{
    foreach (var setting in BuiltInTemplates.KrillCommunity.Settings) Assert(ConfigHelp.Find(setting.Path) is not null);
    foreach (var key in new[] { "model", "model_reasoning_effort", "service_tier", "model_context_window", "model_auto_compact_token_limit", "connection.provider", "connection.address", "connection.credential", "connection.protocol" })
        Assert(ConfigHelp.Find(key) is { Summary.Length: > 0, Effect.Length: > 0, DefaultBehavior.Length: > 0, VerifiedAt.Length: > 0 });
    Assert(ConfigHelp.Find("features.fast_mode") is { Verified: true } fast && fast.Effect.Contains("不等于") && fast.RelatedKeys.Contains("service_tier"));
    Assert(ConfigHelp.Find("features.token_budget.enabled") is { Verified: false } budget && budget.Limitations.Contains("账单"));
    Assert(ConfigHelp.Find("future.unknown") is null);
});
Test("field differences share file preview redaction and retain numeric token settings", () =>
{
    foreach (var path in new[] { "oauth.tokens.access_token", "Authorization", "mcp_servers.demo.env.SESSION", "model_providers.demo.http_headers.X" }) Assert(TomlDocument.IsSensitivePath(path));
    foreach (var path in new[] { "features.token_budget.enabled", "model_auto_compact_token_limit", "max_output_tokens" }) Assert(!TomlDocument.IsSensitivePath(path));
    var doc = new TomlDocument(Bytes("[oauth.tokens]\naccess_token='fake-token'\n[features.token_budget]\nenabled=true\n"));
    Assert(!doc.Masked().Contains("fake-token") && doc.Masked().Contains("enabled=true"));
});
Test("template library saves, edits, deletes and restores built-ins without connection fields", () =>
{
    var directory = Path.Combine(runRoot, "template-library");
    var library = new TemplateRepository(directory);
    Assert(library.Load().Any(template => template.Id == BuiltInTemplates.KrillCommunity.Id));
    var saved = library.Save(TemplateRepository.New("我的常用设置", [new("web_search", "\"live\"")]), create: true);
    Assert(library.Load().Any(template => template.Id == saved.Id && template.Name == "我的常用设置"));
    var edited = library.Save(saved with { Name = "新的名称", Settings = [new("analytics.enabled", "false", false)] });
    Assert(new TemplateRepository(directory).Load().Single(template => template.Id == saved.Id) is { Name: "新的名称", Settings.Count: 1 } persisted && !persisted.Settings[0].SelectByDefault);
    Assert(library.Load().Count == 2);
    Throws<ArgumentException>(() => library.Save(TemplateRepository.New("密钥模板", [new("oauth.tokens.access_token", "\"secret\"")]), create: true));
    library.Delete(saved.Id); Assert(library.Load().All(template => template.Id != saved.Id));
    library.Delete(BuiltInTemplates.KrillCommunity.Id); Assert(library.Load().All(template => template.Id != BuiltInTemplates.KrillCommunity.Id));
    var replacement = library.Save(BuiltInTemplates.KrillCommunity with { Name = "我改过的内置模板" });
    Assert(library.Load().Single(template => template.Id == replacement.Id).Name == replacement.Name);
    var kept = library.Save(TemplateRepository.New("保留的自定义模板", [new("analytics.enabled", "false")]), true);
    library.RestoreBuiltIns(); Assert(library.Load().Single(template => template.Id == BuiltInTemplates.KrillCommunity.Id).Name == BuiltInTemplates.KrillCommunity.Name);
    Assert(library.Load().Any(template => template.Id == kept.Id));
});
Test("invalid template edits do not change the library and corrupt files are preserved", () =>
{
    var directory = Path.Combine(runRoot, "invalid-template-library"); var library = new TemplateRepository(directory);
    var saved = library.Save(TemplateRepository.New("Valid", [new("web_search", "'live'")]), true);
    var path = Path.Combine(directory, "templates.json"); var before = File.ReadAllBytes(path);
    Throws<ArgumentException>(() => library.Save(saved with { Settings = [new("web_search", "'live'"), new("\"web_search\"", "'cached'")] }));
    Throws<ArgumentException>(() => library.Save(saved with { Settings = [new("analytics.enabled", "'bad'")] }));
    Throws<ArgumentException>(() => library.Save(saved with { Settings = [new("future", "{ flag = true }"), new("future.flag", "false")] }));
    Throws<ArgumentException>(() => library.Save(saved with { Settings = [new("future.items", "[{ token = 'nested-secret' }]")] }));
    Throws<ArgumentException>(() => library.Save(saved with { Settings = [new("profiles.work.openai_base_url", "'https://example.com'")] }));
    Assert(File.ReadAllBytes(path).SequenceEqual(before));
    File.WriteAllText(path, "{ broken");
    Throws<InvalidDataException>(() => library.Load()); Throws<InvalidDataException>(() => library.Save(saved));
    Assert(File.ReadAllText(path) == "{ broken");
});
Test("capturing current settings includes drafts, skips removed values and excludes nested credentials", () =>
{
    var original = new TomlDocument(Bytes("model='some-model'\nopenai_base_url='https://example.com'\nweb_search='cached'\nmodel_context_window=100000\nfuture.items=[{ token='secret' }]\n[future.\"a.b\"]\nflag=true\n[mcp_servers.test.env]\nPASSWORD='secret'\n")).Model;
    var draft = new Dictionary<string, string?> { ["web_search"] = "'live'", ["model_context_window"] = null, ["analytics.enabled"] = "false", ["features.token_budget.enabled"] = "true" };
    var captured = TemplateRepository.Capture(original, draft);
    Assert(captured.Count == 4 && captured.Single(setting => setting.Path == "web_search").Value == "\"live\"");
    Assert(captured.Any(setting => setting.Path == "future.\"a.b\".flag") && !captured.Single(setting => setting.Path == "features.token_budget.enabled").SelectByDefault);
    Assert(captured.All(setting => !setting.Value.Contains("secret") && setting.Path != "model_context_window"));
    var template = TemplateRepository.Validate(TemplateRepository.New("Captured", captured));
    var target = new Dictionary<string, string?>(); template.StageSelected(new TomlDocument(null).Model, target, template.Settings.Select(setting => setting.Path));
    Assert(target.Count == 4 && new TomlDocument(Bytes("web_search=" + target["web_search"])).String("web_search") == "live");
    Assert(TomlDocument.ValueAt(original, "model_context_window") is 100000L && draft.Count == 4);
});

LegacyConnectionTests.Register(tests, runRoot);
GatewayTests.Register(tests, runRoot);
GatewayProcessTests.Register(tests, runRoot);
NativeBridgeTests.Register(tests, runRoot);
DetailsAndStartupTests.Register(tests, runRoot);
UpdateTests.Register(tests, runRoot);
LocalStateTests.Register(tests, runRoot);
var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + "\n" + error); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed. Isolated files: {runRoot}");
return failures == 0 ? 0 : 1;

sealed class FakeEnvironment : IUserEnvironment
{
    public string? Value { get; set; }
    public bool ThrowOnce { get; set; }
    public string? Read() => Value;
    public void Write(string? value) { if (ThrowOnce) { ThrowOnce = false; throw new IOException("Injected environment failure"); } Value = value; }
}
sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
}

