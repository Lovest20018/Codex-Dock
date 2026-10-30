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

internal static class GatewayTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        string Home() { var home = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home); return home; }
        ConnectionSettings Settings() => new("https://fixture.invalid/v1", "fake-upstream-key", "fixture-model") { Mode = ConnectionMode.OfficialWithApi, Kind = ConnectionKind.ResponsesApi, PreservePreferenceValues = true };
        Test("mixed native route preserves identity, isolates keys and restores preferences", () =>
        {
            var home = Home(); var env = new FakeEnvironment { Value = "https://prior.invalid" }; var store = new ConfigStore(env, _ => null);
            File.WriteAllBytes(Path.Combine(home, "auth.json"), LegacyConnectionTests.Auth());
            File.WriteAllText(Path.Combine(home, "config.toml"), "features.enable_request_compression=true\nchatgpt_base_url='https://prior.invalid/backend-api'\n");
            var before = store.Load(home); var plan = store.Plan(before, Settings()); var connection = MixedConnection.Read(plan.Bridge!); var doc = new TomlDocument(plan.Config);
            Assert(doc.String("model_provider") == "openai" && doc.String("openai_base_url") == connection.NativeBaseUrl);
            Assert(doc.Get("features.enable_request_compression") is false && doc.Get("chatgpt_base_url") is null && plan.AuthApiBaseUrl is null);
            Assert(plan.Auth!.SequenceEqual(before.Auth!) && !doc.Text.Contains("fake-upstream-key") && !plan.ConfigPreview.Contains(connection.LocalToken));
            store.Apply(plan); store.Restore(store.Load(home)); Assert(before.SameContents(store.Load(home)));
            store.Apply(plan); var read = store.ReadSettings(store.Load(home));
            Assert(read.Mode == ConnectionMode.OfficialWithApi && read.ApiKey == Settings().ApiKey && read.ProviderId == "openai");
            var current = store.Load(home); store.Apply(store.Plan(current, read, new Dictionary<string, string?> { ["analytics.enabled"] = "false" }, updateConnection: false));
            Assert(store.Load(home).Auth!.SequenceEqual(before.Auth!) && store.Load(home).Bridge!.SequenceEqual(plan.Bridge!));
            var direct = store.Plan(store.Load(home), read with { Mode = ConnectionMode.Direct });
            Assert(new TomlDocument(direct.Config).Get("features.enable_request_compression") is true && direct.Bridge is null && direct.AuthApiBaseUrl == "https://prior.invalid");
            store.Restore(store.Load(home)); Assert(current.SameContents(store.Load(home)));
        });
        Test("mixed save rejects unsupported credential stores and stale refreshed tokens", () =>
        {
            var store = new ConfigStore(new FakeEnvironment(), _ => null); var home = Home();
            File.WriteAllBytes(Path.Combine(home, "auth.json"), LegacyConnectionTests.Auth());
            File.WriteAllText(Path.Combine(home, "config.toml"), "cli_auth_credentials_store='keyring'\n");
            Throws<InvalidOperationException>(() => store.Plan(store.Load(home), Settings()));
            File.WriteAllText(Path.Combine(home, "config.toml"), ""); var plan = store.Plan(store.Load(home), Settings());
            File.WriteAllText(Path.Combine(home, "auth.json"), Encoding.UTF8.GetString(LegacyConnectionTests.Auth()).Replace("fake-access-token", "fake-refreshed-token"));
            Throws<InvalidOperationException>(() => store.Apply(plan)); Assert(!File.Exists(Path.Combine(home, MixedConnection.FileName)));
        });
        Test("native model catalog retains reported capabilities without inventing Fast", () =>
        {
            using var json = JsonDocument.Parse(NativeModelCatalog.Convert(Encoding.UTF8.GetBytes("{\"data\":[{\"id\":\"fixture-model\"},{\"id\":\"fixture-model\"}]}")));
            var entries = json.RootElement.GetProperty("models"); Assert(entries.GetArrayLength() == 1);
            var entry = entries[0]; Assert(entry.GetProperty("slug").GetString() == "fixture-model" && entry.GetProperty("service_tiers").GetArrayLength() == 0 && !entry.GetProperty("supports_search_tool").GetBoolean());
        });
        Test("mixed setup does not overwrite unrelated providers or incompatible ordinary edits", () =>
        {
            var home = Home(); var store = new ConfigStore(new FakeEnvironment(), _ => null);
            File.WriteAllBytes(Path.Combine(home, "auth.json"), LegacyConnectionTests.Auth());
            File.WriteAllText(Path.Combine(home, "config.toml"), "[model_providers.codex_config_mixed]\nbase_url='https://another.invalid/v1'\nexperimental_bearer_token='fake-unrelated-key'\n");
            Throws<InvalidOperationException>(() => store.Plan(store.Load(home), Settings()));
            File.WriteAllText(Path.Combine(home, "config.toml"), ""); store.Apply(store.Plan(store.Load(home), Settings()));
            var before = store.Load(home);
            Throws<InvalidOperationException>(() => store.Plan(before, store.ReadSettings(before), new Dictionary<string, string?> { ["features.enable_request_compression"] = "true" }, updateConnection: false));
            Throws<InvalidOperationException>(() => store.Plan(before, store.ReadSettings(before), new Dictionary<string, string?> { ["features.enable_request_compression"] = null }, updateConnection: false));
            Assert(before.SameContents(store.Load(home)));
        });
        Test("existing mixed routes update and migrate while preserving unrelated provider settings", () =>
        {
            var home = Home(); LegacyConnectionTests.WriteFixture(home);
            var store = new ConfigStore(new FakeEnvironment(), _ => null);
            var old = store.Load(home); var settings = store.ReadSettings(old);
            store.Apply(store.Plan(old, settings));
            var upgraded = store.Load(home); var connection = MixedConnection.Read(upgraded.Bridge!);
            Assert(connection.NativeRoute && new TomlDocument(upgraded.Config).String("openai_base_url") == connection.NativeBaseUrl);
            store.Apply(store.Plan(upgraded, settings with { ApiKey = "fake-replaced-upstream" }));
            var current = store.Load(home); Assert(store.ReadSettings(current).ApiKey == "fake-replaced-upstream");
            var changed = new TomlDocument(current.Config);
            changed.Set("model_providers.codex_config_mixed.base_url", "https://other.invalid/v1");
            File.WriteAllBytes(Path.Combine(home, "config.toml"), changed.Bytes);
            current = store.Load(home);
            Throws<InvalidOperationException>(() => store.Plan(current, settings));
            var direct = store.Plan(current, settings with { Mode = ConnectionMode.Direct });
            Assert(new TomlDocument(direct.Config).Get("model_providers.codex_config_mixed.base_url") as string == "https://other.invalid/v1");
            changed.Set("model_provider", MixedConnection.Provider);
            File.WriteAllBytes(Path.Combine(home, "config.toml"), changed.Bytes);
            Assert(!connection.OwnsRoute(changed) && store.ReadSettings(store.Load(home)).Mode == ConnectionMode.Direct);
        });
        tests.Add(("gateway forwards native and legacy paths with isolated authentication and session metadata", async () =>
        {
            var connection = MixedConnection.Create(Settings()); byte[] auth = LegacyConnectionTests.Auth();
            var calls = 0;
            using var handler = new FakeHandler(request =>
            {
                calls++; Assert(request.Headers.Authorization?.Parameter == connection.ApiKey);
                Assert(!request.Headers.Contains("Cookie") && !request.Headers.Contains("chatgpt-account-id") && !request.Headers.Contains("x-openai-actor-authorization"));
                if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"fixture-model\"}]}") };
                Assert(request.Headers.GetValues("session-id").Single() == "fixture-session" && request.Headers.GetValues("thread-id").Single() == "fixture-thread");
                Assert(request.Headers.GetValues("x-openai-subagent").Single() == "guardian" && !request.Headers.Contains("x-codex-turn-state"));
                return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.completed\"}\n\n", Encoding.UTF8, "text/event-stream") };
            });
            await using var bridge = new ModelBridge(() => connection, () => auth, handler); await bridge.StartAsync();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
            foreach (var path in new[] { "/backend-api/codex/responses", "/v1/responses" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{connection.Port}" + path);
                request.Headers.Authorization = new("Bearer", "fake-access-token");
                foreach (var (name, value) in new[] { ("session-id", "fixture-session"), ("thread-id", "fixture-thread"), ("x-openai-subagent", "guardian"), ("x-codex-turn-state", "account-bound"), ("Cookie", "secret=fake-cookie"), ("chatgpt-account-id", "fake-account"), ("x-openai-actor-authorization", "fake-actor") }) request.Headers.TryAddWithoutValidation(name, value);
                request.Content = new StringContent("{\"model\":\"fixture-model\",\"service_tier\":\"priority\"}", Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(request); Assert(response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync()).Contains("response.completed"));
            }
            client.DefaultRequestHeaders.Authorization = new("Bearer", connection.LocalToken);
            using var catalog = await client.GetAsync(connection.NativeBaseUrl + "/models");
            Assert((await catalog.Content.ReadAsStringAsync()).Contains("\"slug\":\"fixture-model\""));
            using var invalid = await client.PostAsync($"http://127.0.0.1:{connection.Port}/backend-api/wham/accounts/check", null); Assert(invalid.StatusCode == HttpStatusCode.NotFound);
            client.DefaultRequestHeaders.Authorization = new("Bearer", "fake-access-token");
            using var forbidden = await client.PostAsync($"http://127.0.0.1:{connection.Port}/codex-config/stop", null); Assert(forbidden.StatusCode == HttpStatusCode.Forbidden);
            client.DefaultRequestHeaders.Authorization = new("Bearer", "wrong");
            using var rejected = await client.GetAsync(connection.NativeBaseUrl + "/models"); Assert(rejected.StatusCode == HttpStatusCode.Unauthorized && calls == 3);
        }));
        tests.Add(("gateway reports upstream status and request ID without refreshing official identity", async () =>
        {
            var connection = MixedConnection.Create(Settings()); var status = 401;
            using var handler = new FakeHandler(_ => { var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{\"error\":{\"code\":\"invalid_api_key\",\"message\":\"do-not-expose-raw-provider-body\"}}") }; response.Headers.Add("x-request-id", "fixture-request-id"); return response; });
            await using var bridge = new ModelBridge(() => connection, () => LegacyConnectionTests.Auth(), handler); await bridge.StartAsync();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }); client.DefaultRequestHeaders.Authorization = new("Bearer", connection.LocalToken);
            foreach (var code in new[] { 401, 403, 429, 500, 302 })
            {
                status = code; using var response = await client.PostAsync(connection.NativeBaseUrl + "/responses", new StringContent("{}"));
                var body = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(body);
                Assert((int)response.StatusCode == (code is 401 or 403 or 302 ? 502 : code));
                Assert(json.RootElement.GetProperty("error").GetProperty("upstream_status").GetInt32() == code && response.Headers.GetValues("x-request-id").Single() == "fixture-request-id");
                Assert(json.RootElement.GetProperty("error").GetProperty("upstream_code").GetString() == "invalid_api_key");
                Assert(!body.Contains("do-not-expose") && !body.Contains(connection.ApiKey));
            }
        }));
        tests.Add(("SSE disconnect cancels the upstream request without replay", async () =>
        {
            var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var upstream = builder.Build(); var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
            upstream.Run(async context =>
            {
                Interlocked.Increment(ref calls); context.Response.ContentType = "text/event-stream";
                await context.Response.WriteAsync("data: started\n\n"); await context.Response.Body.FlushAsync();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
                catch (OperationCanceledException) { cancelled.TrySetResult(); }
            });
            await upstream.StartAsync(); var connection = MixedConnection.Create(Settings() with { BaseUrl = upstream.Urls.Single() + "/v1" });
            await using var bridge = new ModelBridge(() => connection, () => null); await bridge.StartAsync();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
            using var request = new HttpRequestMessage(HttpMethod.Post, connection.NativeBaseUrl + "/responses") { Content = new StringContent("{}") };
            request.Headers.Authorization = new("Bearer", connection.LocalToken);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
            {
                await using var body = await response.Content.ReadAsStreamAsync(timeout.Token); var buffer = new byte[64];
                Assert(await body.ReadAsync(buffer, timeout.Token) > 0);
                timeout.Cancel();
            }
            client.Dispose(); await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert(calls == 1);
        }));
        tests.Add(("unsupported upstream websocket handshake remains available for native HTTP fallback", async () =>
        {
            var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var upstream = builder.Build(); upstream.Run(context => { context.Response.StatusCode = 426; return Task.CompletedTask; }); await upstream.StartAsync();
            var connection = MixedConnection.Create(Settings() with { BaseUrl = upstream.Urls.Single() + "/v1" });
            await using var bridge = new ModelBridge(() => connection, () => null); await bridge.StartAsync();
            using var socket = new ClientWebSocket(); socket.Options.Proxy = null; socket.Options.CollectHttpResponseDetails = true;
            socket.Options.SetRequestHeader("Authorization", "Bearer " + connection.LocalToken);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await socket.ConnectAsync(new Uri(connection.NativeBaseUrl.Replace("http:", "ws:") + "/responses"), timeout.Token); throw new InvalidOperationException("Handshake should fail."); }
            catch (WebSocketException) { Assert(socket.HttpStatusCode == HttpStatusCode.UpgradeRequired); }
            var status = await BridgeProcess.StatusAsync(connection); Assert(status?.LastUpstreamStatus == 426);
        }));
        tests.Add(("gateway websocket preserves frames and isolates credentials", async () =>
        {
            var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var upstream = builder.Build(); upstream.UseWebSockets();
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            upstream.Run(async context =>
            {
                Assert(context.Request.Headers.Authorization == "Bearer fake-upstream-key" && !context.Request.Headers.ContainsKey("chatgpt-account-id"));
                Assert(context.Request.Headers["session-id"] == "ws-session");
                using var socket = await context.WebSockets.AcceptWebSocketAsync(); var bytes = new byte[4096]; var frame = await socket.ReceiveAsync(bytes, context.RequestAborted);
                await socket.SendAsync(bytes.AsMemory(0, frame.Count), frame.MessageType, frame.EndOfMessage, context.RequestAborted); received.TrySetResult();
                try { await socket.ReceiveAsync(bytes, context.RequestAborted); } catch (Exception error) when (error is WebSocketException or OperationCanceledException) { }
            });
            await upstream.StartAsync();
            var settings = Settings() with { BaseUrl = upstream.Urls.Single() + "/v1" }; var connection = MixedConnection.Create(settings);
            await using var bridge = new ModelBridge(() => connection, () => LegacyConnectionTests.Auth()); await bridge.StartAsync();
            using var socket = new ClientWebSocket(); socket.Options.Proxy = null; socket.Options.SetRequestHeader("Authorization", "Bearer " + connection.LocalToken); socket.Options.SetRequestHeader("session-id", "ws-session");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(new Uri(connection.NativeBaseUrl.Replace("http:", "ws:") + "/responses"), timeout.Token);
            var payload = Encoding.UTF8.GetBytes("{\"model\":\"fixture-model\"}"); await socket.SendAsync(payload.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
            var answer = new byte[4096]; var result = await socket.ReceiveAsync(answer.AsMemory(), timeout.Token); Assert(answer.AsSpan(0, result.Count).SequenceEqual(payload));
            await received.Task.WaitAsync(timeout.Token); socket.Abort();
        }));
    }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Gateway recovery assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
