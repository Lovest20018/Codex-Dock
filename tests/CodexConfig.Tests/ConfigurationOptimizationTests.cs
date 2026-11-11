using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CodexConfig.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

internal static class ConfigurationOptimizationTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        string Home() { var path = Path.Combine(root, "optimization-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
        void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
        Test("catalog provenance, removed options and array/table constraints match the verified schema", () =>
        {
            var schema = ConfigSchema.Instance;
            Assert(schema.Entries.Count >= 1064 && schema.VerifiedRuntime == "codex-cli 0.168.0-alpha.4");
            Assert(schema.Find("features.reasoning_effort_override") is { Stage: "stable", Default.ValueKind: JsonValueKind.True });
            Assert(schema.Find("features.secret_auth_storage")!.Default.GetBoolean());
            Assert(schema.Find("features.code_mode_tool_description_first") is { CanEdit: false, Stage: "removed" });
            Throws<ArgumentException>(() => schema.Validate("audio.microphone_channel", "0")); schema.Validate("audio.microphone_channel", "1");
            Throws<ArgumentException>(() => schema.Validate("mcp_servers.demo.args", "[1, 'two']"));
            schema.Validate("mcp_servers.demo.args", "['one', 'two']");
            schema.Validate("features.guardianv3", "{ enabled = true }");
            Throws<ArgumentException>(() => schema.Validate("features.guardianv3", "{ enabled = 'wrong' }"));
            schema.Validate("approval_policy", "{ granular = { sandbox_approval = true, rules = false, mcp_elicitations = false } }");
            Throws<ArgumentException>(() => schema.Validate("approval_policy", "{ granular = { sandbox_approval = true } }"));
            Throws<ArgumentException>(() => schema.Validate("approval_policy", "{ granular = { sandbox_approval = 'wrong' } }"));
        });
        Test("configured and expanded entries preserve quoted names, multiple instances, explicit false and removal drafts", () =>
        {
            var document = Doc("model_provider='merchant.v1'\n[mcp_servers.'alpha.one']\ncommand='first'\nenabled=false\n[mcp_servers.second]\nurl='https://example.invalid/mcp'\n[model_providers.'merchant.v1']\nname='First'\n[model_providers.second]\nname='Second'\n[future]\nflag=false\n");
            var schema = ConfigSchema.Instance; var entries = schema.ConfiguredEntries(document.Model, ["mcp_servers.second.enabled"]).ToArray();
            Assert(entries.Single(entry => entry.Path == "mcp_servers.\"alpha.one\".enabled").DefinitionPath == "mcp_servers.<name>.enabled");
            Assert(entries.Any(entry => entry.Path == "future.flag") && entries.All(entry => !entry.Dynamic));
            var expanded = schema.ExpandedEntries(document.Model).ToArray();
            foreach (var id in new[] { "merchant.v1", "second" })
                Assert(expanded.Any(entry => entry.Path == TomlDocument.FormatPath(["model_providers", id, "capabilities", "remote_compaction"])));
            Assert(expanded.Any(entry => entry.Path == "mcp_servers.\"alpha.one\".tool_timeout_sec"));
            Assert(document.Get("mcp_servers.\"alpha.one\".enabled") is false && document.Get("mcp_servers.second.enabled") is null);
        });
        Test("saving validates changed permission, MCP and provider groups while preserving unrelated legacy values", () =>
        {
            var store = new ConfigStore(new FixtureEnvironment()); var home = Home();
            var source = "# keep unknown\nfuture_enabled=false\n[mcp_servers.old]\ncommand='command'\nurl='https://legacy.invalid'\n";
            File.WriteAllText(Path.Combine(home, "config.toml"), source);
            var before = store.Load(home); var settings = store.ReadSettings(before);
            var harmless = store.Plan(before, settings, new Dictionary<string, string?> { ["analytics.enabled"] = "false" }, updateConnection: false);
            Assert(new TomlDocument(harmless.Config).Text.Contains("[mcp_servers.old]\ncommand='command'\nurl='https://legacy.invalid'\n") && new TomlDocument(harmless.Config).Get("future_enabled") is false);
            Throws<ArgumentException>(() => store.Plan(before, settings, new Dictionary<string, string?> { ["mcp_servers.old.tool_timeout_sec"] = "12" }, updateConnection: false));
            foreach (var edits in new[] {
                new Dictionary<string,string?> { ["default_permissions"]="':workspace'", ["sandbox_mode"]="'workspace-write'" },
                new Dictionary<string,string?> { ["mcp_servers.new.url"]="'ftp://example.invalid'" },
                new Dictionary<string,string?> { ["mcp_servers.new.command"]="'cmd'", ["mcp_servers.new.startup_timeout_sec"]="0" },
                new Dictionary<string,string?> { ["model_providers.new.auth.command"]="'auth-tool'", ["model_providers.new.env_key"]="'MY_KEY'" },
                new Dictionary<string,string?> { ["model_providers.openai.capabilities.remote_compaction"]="'v2'" }
            }) Throws<ArgumentException>(() => store.Plan(before, settings, edits, updateConnection: false));
            Assert(File.ReadAllText(Path.Combine(home, "config.toml")) == source && !Directory.Exists(Path.Combine(home, "codex-config-backups")));
        });
        Test("model capabilities constrain reasoning without inventing Fast support or changing source files", () =>
        {
            var home = Home(); var catalog = Path.Combine(home, "models.json");
            File.WriteAllText(catalog, "{\"models\":[{\"slug\":\"only-high\",\"context_window\":123456,\"supported_reasoning_levels\":[{\"effort\":\"high\"}],\"service_tiers\":[]}]}");
            File.WriteAllText(Path.Combine(home, "config.toml"), "model='only-high'\nmodel_catalog_json='models.json'\n");
            var store = new ConfigStore(new FixtureEnvironment()); var before = store.Load(home); var settings = store.ReadSettings(before);
            var capability = ModelCapabilities.Read(home, new TomlDocument(before.Config));
            Assert(capability is { ContextWindow: 123456, ReasoningLevels.Length: 1, ServiceTiers.Length: 0 } && !capability.SupportsTier("fast"));
            Throws<ArgumentException>(() => store.Plan(before, settings, new Dictionary<string, string?> { ["model_reasoning_effort"] = "'xhigh'" }, updateConnection: false));
            var fast = store.Plan(before, settings, new Dictionary<string, string?> { ["service_tier"] = "'fast'" }, updateConnection: false);
            Assert(fast.Notes.Any(note => note.Contains("未确认")));
            Throws<ArgumentException>(() => store.Plan(before, settings, new Dictionary<string, string?> { ["service_tier"] = "'fast'", ["features.fast_mode"] = "false" }, updateConnection: false));
            Assert(before.SameContents(store.Load(home)));
        });
        Test("profile files have encrypted reversible saves, file conflicts and credential/permission exclusion", () =>
        {
            var home = Home(); var store = new ProfileStore(); File.WriteAllText(Path.Combine(home, "config.toml"), "model='base-model'\n");
            File.WriteAllText(Path.Combine(home, "auth.json"), "base-auth-untouched");
            var name = "daily-test"; var path = ProfileStore.FilePath(home, name);
            var original = "# keep comment\r\nmodel='profile-model'\r\nfuture_flag=false\r\nfuture_items=[{token='fixture-secret'}]\r\nmodel_reasoning_effort='low'\r\n";
            File.WriteAllText(path, original); var before = store.Load(home, name);
            var plan = store.Plan(before, new Dictionary<string,string?> { ["model_reasoning_effort"] = "'high'", ["analytics.enabled"] = "false" });
            Assert(!plan.Preview.Contains("fixture-secret") && new TomlDocument(plan.Config).Get("future_flag") is false);
            var backup = store.Save(plan); Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(backup)).Contains("fixture-secret"));
            Assert(store.List(home).SequenceEqual(new[] { name }));
            store.Restore(store.Load(home, name)); Assert(File.ReadAllText(path) == original);
            var conflict = store.Plan(store.Load(home, name), new Dictionary<string,string?> { ["analytics.enabled"] = "true" });
            File.AppendAllText(path, "# external\r\n"); Throws<InvalidOperationException>(() => store.Save(conflict));
            Throws<InvalidOperationException>(() => store.Restore(store.Load(home, name)));
            foreach (var invalid in new[] { "../escape", "a.b", "CON", "", "a/b" }) Throws<ArgumentException>(() => ProfileStore.FilePath(home, invalid));
            foreach (var key in new[] { "model_providers.test.env_key", "cli_auth_credentials_store", "oauth.tokens.access_token", "default_permissions", "sandbox_mode", "features.enable_request_compression" })
                Throws<ArgumentException>(() => store.Plan(before, new Dictionary<string,string?> { [key] = "'not-written'" }));
            Throws<ArgumentException>(() => store.Plan(before, new Dictionary<string,string?> { ["future_public"] = "[{ token='hidden-secret' }]" }));
            Assert(File.ReadAllText(Path.Combine(home,"config.toml")) == "model='base-model'\n" && File.ReadAllText(Path.Combine(home,"auth.json")) == "base-auth-untouched");
            var fresh = store.Load(home, "new-profile"); store.Save(store.Plan(fresh, new Dictionary<string,string?> { ["model"] = "'chosen-model'" }));
            Assert(new TomlDocument(store.Load(home,"new-profile").Config).String("model") == "chosen-model");
            store.Restore(store.Load(home,"new-profile")); Assert(!File.Exists(ProfileStore.FilePath(home,"new-profile")));
        });
        Test("source snapshots distinguish file values, project/managed/session origins and unknowns, and redact nested secrets", () =>
        {
            using var json = JsonDocument.Parse("""
                {"config":{"features":{"fast_mode":false},"model_reasoning_effort":"high","future_items":[{"token":"response-secret","flag":false}],"a.b":{"flag":true}},
                 "origins":{"features":{"name":{"type":"system","file":"managed.toml"},"version":"1"},"model_reasoning_effort":{"name":{"type":"project","dotCodexFolder":"project/.codex"},"version":"1"},"\"a.b\".flag":{"name":{"type":"sessionFlags"},"version":"1"}},
                 "layers":[{"name":{"type":"user","file":"home/config.toml"},"version":"1","config":{}},{"name":{"type":"project","dotCodexFolder":"untrusted/.codex"},"version":"1","config":{},"disabledReason":"not trusted"}]}
                """);
            var local = Bytes("model_reasoning_effort='low'\nunknown_field=false\nfuture_items=[{token='file-secret'}]\n");
            var result = ConfigReadClient.Parse(json.RootElement, local, "local-codex.exe", "project");
            Assert(result.Rows.Single(row => row.Path == "model_reasoning_effort") is { FileValue: "\"low\"", EffectiveValue: "\"high\"" } effort && effort.Source.StartsWith("项目配置"));
            Assert(result.Rows.Single(row => row.Path == "features.fast_mode") is { FileValue: "未设置", EffectiveValue: "false" } flag && flag.Source.StartsWith("组织"));
            Assert(result.Rows.Single(row => row.Path == "unknown_field").EffectiveValue.Contains("未知"));
            Assert(result.Rows.Single(row => row.Path == "\"a.b\".flag").Source.Contains("进程参数"));
            var serialized = JsonSerializer.Serialize(result); Assert(!serialized.Contains("file-secret") && !serialized.Contains("response-secret"));
            Assert(result.Layers[1].State.StartsWith("未采用"));
        });
        tests.Add(("v2 SSE and legacy JSON compaction diagnostics require returned items and terminal success", async () =>
        {
            var connection = Connection(); var phase = 0; string? forwarded = null;
            using var handler = new FixtureHandler(async request =>
            {
                forwarded = await request.Content!.ReadAsStringAsync();
                return phase switch
                {
                    0 => Response(":" + new string('x', 16370) + "\r\n\r\n" + Events("first", true), "text/event-stream"),
                    1 => Response("{\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"compaction\"}]}]}", "application/json"),
                    2 => Response(Events("failed", false), "text/event-stream"),
                    3 => Response("{\"object\":\"response.compaction\",\"output\":[{\"type\":\"compaction\",\"encrypted_content\":\"private-compaction-text\"}]}", "application/json"),
                    4 => new(HttpStatusCode.TooManyRequests) { Content = new StringContent("{\"error\":{\"code\":\"rate_limit\",\"message\":\"private-upstream-error\"}}") },
                    _ => Response("data: " + new string('x', 2 * 1024 * 1024 + 1) + "\n\ndata: [DONE]\n\n", "text/event-stream")
                };
            });
            await using var bridge = new ModelBridge(() => connection, () => null, handler); await bridge.StartAsync();
            using var client = Client(connection); var requestBody = "{\"model\":\"fixture\",\"input\":[{\"type\":\"compaction_trigger\"},{\"type\":\"message\",\"content\":\"private-user-text compaction_trigger\"}]}";
            for (phase = 0; phase < 6; phase++)
            {
                using var response = await client.PostAsync(connection.NativeBaseUrl + (phase == 3 ? "/responses/compact" : "/responses"), new StringContent(phase == 3 ? "{}" : requestBody, Encoding.UTF8, "application/json"));
                var content = await response.Content.ReadAsStringAsync();
                if (phase == 0) Assert(content.Contains("private-compaction-text") && forwarded == requestBody);
                if (phase == 4) Assert(!content.Contains("private-upstream-error"));
                var metrics = (await BridgeProcess.StatusAsync(connection))!.Metrics!;
                Assert(metrics.SuccessfulCompactions == (phase < 3 ? 1 : 2));
                if (phase == 2) Assert(metrics.CompactionItems == 2); // returned item in a failed response is not a success
            }
            var status = (await BridgeProcess.StatusAsync(connection))!; var m = status.Metrics!;
            Assert(m is { LegacyCompactionRequests: 1, V2Requests: 5, V2Triggers: 5, CompactionItems: 3, SuccessfulCompactions: 2, Waiting: 0, Active: 0 });
            Assert(m.InspectionLimited > 0 && m.LastCompactionAt is not null && m.LastCompactionProtocol == "legacy" && m.LastTriggerProtocol == "v2" && m.LastQueueMs >= 0 && m.LastHeadersMs >= 0 && m.LastFirstDataMs >= 0 && m.LastDurationMs > 0);
            Assert(!JsonSerializer.Serialize(status).Contains("private-"));
        }));
        tests.Add(("websocket compaction diagnostics inspect fragmented frames without changing forwarded messages", async () =>
        {
            var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var upstream = builder.Build(); upstream.UseWebSockets();
            var received = new List<string>();
            upstream.Map("/v1/responses", async context =>
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                for (var iteration = 0; iteration < 2; iteration++)
                {
                    received.Add(await Message(socket));
                    foreach (var evt in EventObjects("ws" + iteration, true))
                    {
                        var bytes = Encoding.UTF8.GetBytes(evt); var split = bytes.Length / 2;
                        await socket.SendAsync(bytes.AsMemory(0, split), WebSocketMessageType.Text, false, CancellationToken.None);
                        await socket.SendAsync(bytes.AsMemory(split), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            });
            await upstream.StartAsync(); var connection = Connection(upstream.Urls.Single() + "/v1");
            await using var bridge = new ModelBridge(() => connection, () => null); await bridge.StartAsync();
            using var socket = new ClientWebSocket(); socket.Options.Proxy = null; socket.Options.SetRequestHeader("Authorization", "Bearer " + connection.LocalToken);
            await socket.ConnectAsync(new Uri(connection.NativeBaseUrl.Replace("http://", "ws://") + "/responses"), CancellationToken.None);
            var request = "{\"type\":\"response.create\",\"input\":[{\"type\":\"compaction_trigger\"}],\"private\":\"private-ws-text\"}";
            for (var iteration = 0; iteration < 2; iteration++)
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(request).AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None);
                foreach (var expected in EventObjects("ws" + iteration, true)) Assert(await Message(socket) == expected);
            }
            socket.Abort();
            BridgeStatus? status = null;
            for (var i = 0; i < 50; i++) { status = await BridgeProcess.StatusAsync(connection); if (status?.Metrics is { Active: 0 }) break; await Task.Delay(20); }
            Assert(received.Count == 2 && received.All(body => body == request));
            Assert(status?.Metrics is { V2Requests: 2, V2Triggers: 2, CompactionItems: 2, SuccessfulCompactions: 2, LastCompactionTransport: "websocket", Active: 0 });
            Assert(!JsonSerializer.Serialize(status).Contains("private-"));
        }));
        tests.Add(("JSON v2 completion, timeout and transport failures retain distinct diagnostic outcomes", async () =>
        {
            var connection = Connection(); var phase = 0;
            using var handler = new FixtureHandler(_ => phase switch
            {
                0 => Task.FromResult(Response("{\"id\":\"json_v2\",\"status\":\"completed\",\"output\":[{\"type\":\"compaction\",\"encrypted_content\":\"private-json-result\"}]}", "application/json")),
                1 => Task.FromException<HttpResponseMessage>(new OperationCanceledException()),
                _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("private-transport-error"))
            });
            await using var bridge = new ModelBridge(() => connection, () => null, handler); await bridge.StartAsync(); using var client = Client(connection);
            foreach (var expected in new[] { (200,"completed"), (504,"timeout"), (502,"transport_error") })
            {
                using var response = await client.PostAsync(connection.NativeBaseUrl + "/responses", new StringContent("{\"input\":[{\"type\":\"compaction_trigger\"}]}",Encoding.UTF8,"application/json"));
                Assert((int)response.StatusCode == expected.Item1); var status = (await BridgeProcess.StatusAsync(connection))!;
                Assert(status.Metrics is { SuccessfulCompactions: 1, CompactionItems: 1, Waiting: 0, Active: 0 } && status.Metrics.LastStreamEnd == expected.Item2);
                Assert(!JsonSerializer.Serialize(status).Contains("private-")); phase++;
            }
        }));
        tests.Add(("gateway queue and active gauges cover slot contention and drain after completion", async () =>
        {
            var connection = Connection(); var entered = 0; var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new FixtureHandler(async _ => { Interlocked.Increment(ref entered); await release.Task; return Response("{}", "application/json"); });
            await using var bridge = new ModelBridge(() => connection, () => null, handler); await bridge.StartAsync(); using var client = Client(connection);
            var calls = Enumerable.Range(0,17).Select(_ => client.PostAsync(connection.NativeBaseUrl + "/responses", new StringContent("{}",Encoding.UTF8,"application/json"))).ToArray();
            try
            {
                BridgeStatus? status = null;
                for (var i = 0; i < 150; i++) { status = await BridgeProcess.StatusAsync(connection); if (status?.Metrics is { Waiting: 1, Active: 16 }) break; await Task.Delay(20); }
                Assert(status?.Metrics is { Waiting: 1, Active: 16 } && entered == 16);
            }
            finally { release.TrySetResult(); foreach (var response in await Task.WhenAll(calls)) response.Dispose(); }
            Assert((await BridgeProcess.StatusAsync(connection))?.Metrics is { Waiting: 0, Active: 0, LastDurationMs: > 0 });
        }));
        if (Environment.GetEnvironmentVariable("CODEX_CONFIG_TEST_CLI") is { Length: > 0 } cli)
            tests.Add(("native config/read resolves project layers and explicitly reports unsupported profile source reading", async () =>
            {
                var home = Home(); var project = Path.Combine(Home(), "project"); Directory.CreateDirectory(project); Directory.CreateDirectory(Path.Combine(project, ".git")); Directory.CreateDirectory(Path.Combine(project,".codex"));
                var config = "model='fixture-model'\nmodel_reasoning_effort='low'\n[projects." + TomlDocument.Quote(project) + "]\ntrust_level='trusted'\n";
                File.WriteAllText(Path.Combine(home, "config.toml"), config); File.WriteAllText(Path.Combine(project, ".codex", "config.toml"), "model_reasoning_effort='high'\n");
                File.WriteAllText(Path.Combine(home, "daily.config.toml"), "model_reasoning_effort='medium'\n");
                var reader = new ConfigReadClient(); var result = await reader.ReadAsync(cli, home, project, null, Bytes(config));
                Assert(result.Rows.Single(row => row.Path == "model_reasoning_effort") is { FileValue: "\"low\"", EffectiveValue: "\"high\"" } row && row.Source.StartsWith("项目配置"));
                var unsupported = false;
                try { await reader.ReadAsync(cli, home, home, "daily", Bytes("model_reasoning_effort='medium'\n")); }
                catch (NotSupportedException error) { unsupported = error.Message.Contains("生效来源未确认"); }
                Assert(unsupported && File.ReadAllText(Path.Combine(home,"config.toml")) == config && File.ReadAllText(Path.Combine(home,"daily.config.toml")) == "model_reasoning_effort='medium'\n");
            }));
    }
    private static IEnumerable<string> EventObjects(string id, bool success)
    {
        var item = new { id = "cmp_" + id, type = "compaction", encrypted_content = "private-compaction-text 中文" };
        return new[] {
            JsonSerializer.Serialize(new { type = "response.created", response = new { id, status = "in_progress" } }),
            JsonSerializer.Serialize(new { type = "response.output_item.done", response_id = id, output_index = 0, item }),
            success ? JsonSerializer.Serialize(new { type = "response.completed", response = new { id, status = "completed", output = new[] { item } } })
                : JsonSerializer.Serialize(new { type = "response.failed", response = new { id, status = "failed", error = new { code = "fixture_error" } } })
        };
    }
    private static string Events(string id, bool success) => string.Concat(EventObjects(id, success).Select(evt => "data: " + evt + "\r\n\r\n"));
    private static HttpResponseMessage Response(string text, string type) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, type) };
    private static MixedConnection Connection(string url = "https://fixture.invalid/v1") => MixedConnection.Create(new(url, "fake-upstream-secret", "fixture") { Mode = ConnectionMode.OfficialWithApi, Kind = ConnectionKind.ResponsesApi });
    private static HttpClient Client(MixedConnection connection) { var client = new HttpClient(new HttpClientHandler { UseProxy = false }); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.LocalToken); return client; }
    private static async Task<string> Message(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); using var data = new MemoryStream(); var buffer = new byte[16384];
        while (true) { var result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token); if (result.MessageType == WebSocketMessageType.Close) throw new IOException("Unexpected websocket close"); data.Write(buffer,0,result.Count); if (result.EndOfMessage) return Encoding.UTF8.GetString(data.ToArray()); }
    }
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static TomlDocument Doc(string text) => new(Bytes(text));
    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Configuration optimization assertion failed."); }
    private static void Throws<T>(Action action) where T: Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class FixtureEnvironment : IUserEnvironment { public string? Read() => null; public void Write(string? value) { } }
    private sealed class FixtureHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request); }
}
