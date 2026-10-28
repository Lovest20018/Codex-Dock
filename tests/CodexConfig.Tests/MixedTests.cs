using System.Diagnostics;
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

internal static class MixedTests
{
    internal static byte[] Auth()
    {
        var claims = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["email"] = "offline@example.invalid", ["sub"] = "offline-test-user", ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            ["https://api.openai.com/auth"] = new { chatgpt_account_id = "offline-test-account", chatgpt_plan_type = "plus", chatgpt_user_id = "offline-test-user" }
        }))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            auth_mode = "chatgpt", tokens = new { id_token = "eyJhbGciOiJub25lIn0." + claims + ".offline", access_token = "fake-official-access", refresh_token = "fake-official-refresh", account_id = "offline-test-account" },
            last_refresh = DateTimeOffset.UtcNow
        });
    }

    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        string Home() { var directory = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); return directory; }
        ConnectionSettings Settings() => new("https://krill.example/backend-api/codex", "fake-krill-pat", "gpt-6.1-sol") { Mode = ConnectionMode.OfficialWithApi, PreservePreferenceValues = true };

        Test("mixed identity and model credentials are separate, encrypted and masked", () =>
        {
            var home = Home(); var auth = Auth();
            File.WriteAllBytes(Path.Combine(home, "auth.json"), auth);
            var env = new FakeEnvironment { Value = "https://old-pat.example" }; var store = new ConfigStore(env);
            File.WriteAllText(Path.Combine(home, "config.toml"), "# keep\nchatgpt_base_url='https://old-pat.example/backend-api/'\nservice_tier='fast'\napproval_policy='on-request'\napprovals_reviewer='auto_review'\n[features]\nfast_mode=true\n");
            var plan = store.Plan(store.Load(home), Settings());
            var bridge = MixedConnection.Read(plan.Bridge!); var doc = new TomlDocument(plan.Config);
            Assert(plan.Auth!.SequenceEqual(auth) && plan.AuthApiBaseUrl is null);
            Assert(doc.String("openai_base_url") == bridge.LocalBaseUrl && doc.String("model_provider") == MixedConnection.Provider);
            Assert(doc.Get("model_providers." + MixedConnection.Provider + ".experimental_bearer_token") as string == bridge.LocalToken);
            Assert(doc.String("chatgpt_base_url") is null && doc.String("cli_auth_credentials_store") == "file");
            Assert(doc.String("service_tier") == "fast" && doc.String("approvals_reviewer") == "auto_review");
            foreach (var text in new[] { Encoding.UTF8.GetString(plan.Config), plan.ConfigPreview, plan.AuthPreview, Encoding.UTF8.GetString(plan.Bridge!) })
                Assert(!text.Contains("fake-krill-pat") && !text.Contains("fake-official-access"));
            Assert(!plan.ConfigPreview.Contains(bridge.LocalToken));
            store.Apply(plan);
            var loaded = store.ReadSettings(store.Load(home));
            Assert(loaded.Mode == ConnectionMode.OfficialWithApi && loaded.ApiKey == "fake-krill-pat" && env.Value is null);
            var unchanged = store.Plan(store.Load(home), loaded, new Dictionary<string, string?> { ["analytics.enabled"] = "false" }, updateConnection: false);
            Assert(unchanged.Bridge!.SequenceEqual(plan.Bridge!) && unchanged.Auth!.SequenceEqual(auth));
            store.Apply(unchanged);
        });

        Test("mixed save strips old PAT credentials and restores all files and environment", () =>
        {
            var home = Home(); var env = new FakeEnvironment { Value = "https://old.example" }; var store = new ConfigStore(env);
            var original = new ConnectionSettings("https://old.example", "fake-old-pat", "old-model");
            store.Apply(store.Plan(store.Load(home), original));
            var before = store.Load(home);
            var node = System.Text.Json.Nodes.JsonNode.Parse(Auth())!.AsObject(); node["personal_access_token"] = "fake-stale-pat"; node["OPENAI_API_KEY"] = "fake-stale-key";
            var mixed = store.Plan(before, Settings(), officialAuth: Encoding.UTF8.GetBytes(node.ToJsonString()));
            using var auth = JsonDocument.Parse(mixed.Auth!);
            Assert(!auth.RootElement.TryGetProperty("personal_access_token", out _) && !auth.RootElement.TryGetProperty("OPENAI_API_KEY", out _));
            store.Apply(mixed);
            store.Restore(store.Load(home));
            var restored = store.Load(home);
            Assert(restored.Config!.SequenceEqual(before.Config!) && restored.Auth!.SequenceEqual(before.Auth!) && restored.Bridge is null && restored.AuthApiBaseUrl == before.AuthApiBaseUrl);
        });

        Test("switching from mixed to direct and restoring retains the encrypted connection", () =>
        {
            var home = Home(); var store = new ConfigStore(new FakeEnvironment());
            store.Apply(store.Plan(store.Load(home), Settings(), officialAuth: Auth()));
            var before = store.Load(home);
            var direct = new ConnectionSettings("https://api.example/v1", "fake-api-key", "model") { Kind = ConnectionKind.ResponsesApi };
            store.Apply(store.Plan(before, direct));
            Assert(store.Load(home).Bridge is null && store.ReadSettings(store.Load(home)).Mode == ConnectionMode.Direct);
            store.Restore(store.Load(home));
            Assert(store.Load(home).Bridge!.SequenceEqual(before.Bridge!) && store.Load(home).Auth!.SequenceEqual(before.Auth!));
            store.Apply(store.Plan(store.Load(home), direct));
            store.Apply(store.Plan(store.Load(home), Settings(), officialAuth: Auth()));
            Assert(store.ReadSettings(store.Load(home)).Mode == ConnectionMode.OfficialWithApi, "Empty managed table blocked switching back to mixed mode.");
        });

        Test("mixed rejects missing login, profile overrides and conflicting built-in provider", () =>
        {
            var store = new ConfigStore(new FakeEnvironment());
            Throws<InvalidOperationException>(() => store.Plan(store.Load(Home()), Settings()));
            foreach (var config in new[] { "profile='work'\n", "forced_login_method='api'\n", "[model_providers.openai]\nname='override'\n", "[model_providers.codex_config_mixed]\nname='occupied'\n" })
            {
                var home = Home(); File.WriteAllText(Path.Combine(home, "config.toml"), config);
                Throws<InvalidOperationException>(() => store.Plan(store.Load(home), Settings(), officialAuth: Auth()));
            }
            foreach (var value in new[] { "{}", "{\"tokens\":1}", "{\"tokens\":{\"access_token\":\"x\"}}", "{\"tokens\":{},\"auth_mode\":42}" })
                Assert(!OfficialCredentials.HasLogin(Encoding.UTF8.GetBytes(value)));
        });

        Test("mixed ordinary edits cannot redirect requests or switch credential stores", () =>
        {
            var home = Home(); var store = new ConfigStore(new FakeEnvironment());
            store.Apply(store.Plan(store.Load(home), Settings(), officialAuth: Auth()));
            foreach (var (path, value) in new[] { ("profile", "'work'"), ("cli_auth_credentials_store", "'keyring'"), ("forced_login_method", "'api'"), ("model_providers.codex_config_mixed.http_headers.Authorization", "'other'") })
                Throws<InvalidOperationException>(() => store.Plan(store.Load(home), Settings(), new Dictionary<string, string?> { [path] = value }, updateConnection: false));
        });

        Test("mixed runtime file participates in conflicts and failed transaction rollback", () =>
        {
            var home = Home(); var env = new FakeEnvironment { Value = "https://old.example" }; var store = new ConfigStore(env);
            var before = store.Load(home); var plan = store.Plan(before, Settings(), officialAuth: Auth());
            env.ThrowOnce = true;
            Throws<IOException>(() => store.Apply(plan));
            var rolledBack = store.Load(home);
            Assert(rolledBack.Config is null && rolledBack.Auth is null && rolledBack.Bridge is null && rolledBack.AuthApiBaseUrl == before.AuthApiBaseUrl);
            store.Apply(store.Plan(rolledBack, Settings(), officialAuth: Auth()));
            var next = store.Plan(store.Load(home), Settings());
            File.WriteAllBytes(Path.Combine(home, MixedConnection.FileName), [1, 2, 3]);
            Throws<InvalidOperationException>(() => store.Apply(next));
            Throws<InvalidOperationException>(() => store.Restore(store.Load(home)));
        });

        Test("native login uses staging home and removes inherited inference credentials", () =>
        {
            var executable = Environment.ProcessPath!; var home = Home();
            var info = CodexLoginClient.StartInfo(executable, home);
            Assert(!info.UseShellExecute && info.CreateNoWindow && info.Environment["CODEX_HOME"] == home);
            Assert(info.ArgumentList.Contains("login") && info.ArgumentList.Contains("forced_login_method=\"chatgpt\""));
            foreach (var name in new[] { "CODEX_ACCESS_TOKEN", "OPENAI_API_KEY", "CODEX_AUTHAPI_BASE_URL", "OPENAI_BASE_URL" }) Assert(!info.Environment.ContainsKey(name));
            Throws<ArgumentException>(() => CodexLoginClient.StartInfo("codex.cmd", home));
        });

        tests.Add(("loopback bridge replaces OAuth, preserves Fast and auto-review, and refuses unrelated traffic", async () =>
        {
            var connection = MixedConnection.Create(Settings());
            var calls = new List<(string Path, string Body)>();
            var nextStatus = HttpStatusCode.OK;
            using var handler = new FakeHandler(request =>
            {
                Assert(request.Headers.Authorization?.Parameter == "fake-krill-pat");
                Assert(!request.Headers.Contains("chatgpt-account-id") && !request.Headers.Contains("Cookie") && !request.Headers.Contains("OpenAI-Project"));
                calls.Add((request.RequestUri!.AbsolutePath, request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
                var response = new HttpResponseMessage(nextStatus) { Content = new StringContent("event: response.completed\ndata: {\"service_tier\":\"fast\"}\n\n") };
                response.Content.Headers.ContentType = new("text/event-stream");
                response.Headers.TryAddWithoutValidation("Set-Cookie", "must-not-pass");
                return response;
            });
            await using var bridge = new ModelBridge(() => connection, Auth, handler);
            await bridge.StartAsync();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(connection.LocalBaseUrl + "/"), Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Authorization = new("Bearer", "fake-official-access");
            client.DefaultRequestHeaders.TryAddWithoutValidation("chatgpt-account-id", "official-account");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "official-session");
            client.DefaultRequestHeaders.TryAddWithoutValidation("OpenAI-Project", "official-project");
            foreach (var payload in new[] { "{\"model\":\"gpt-6.1-sol\",\"service_tier\":\"fast\",\"stream\":true}", "{\"model\":\"codex-auto-review\",\"input\":\"review\"}" })
            {
                using var answer = await client.PostAsync("responses", new StringContent(payload, Encoding.UTF8, "application/json"));
                Assert(answer.IsSuccessStatusCode && !answer.Headers.Contains("Set-Cookie"));
                Assert((await answer.Content.ReadAsStringAsync()).Contains("response.completed"));
                Assert(calls.Last().Body == payload && calls.Last().Path == "/backend-api/codex/responses");
            }
            using (var health = await client.GetAsync("/codex-config/health"))
            {
                var status = JsonSerializer.Deserialize<BridgeStatus>(await health.Content.ReadAsStringAsync())!;
                Assert(status.Requests == 2 && status.FastRequests == 1 && status.AutoReviewRequests == 1);
            }
            Assert((await client.GetAsync("/backend-api/accounts/check")).StatusCode == HttpStatusCode.NotFound);
            using (var browser = new HttpRequestMessage(HttpMethod.Post, "responses") { Content = new StringContent("{}") })
            {
                browser.Headers.TryAddWithoutValidation("Origin", "https://other.example");
                Assert((await client.SendAsync(browser)).StatusCode == HttpStatusCode.Forbidden);
            }
            Assert(calls.Count == 2);
            client.DefaultRequestHeaders.Authorization = new("Bearer", "unrecognized");
            Assert((await client.PostAsync("responses", new StringContent("{}"))).StatusCode == HttpStatusCode.Unauthorized);
            client.DefaultRequestHeaders.Authorization = new("Bearer", connection.LocalToken);
            foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Redirect })
            {
                nextStatus = status;
                Assert((await client.PostAsync("responses", new StringContent("{}"))).StatusCode == HttpStatusCode.BadGateway);
            }
            Assert(calls.Count == 5);
            client.DefaultRequestHeaders.Authorization = new("Bearer", "fake-official-access");
            Assert((await client.PostAsync("/codex-config/stop", new StringContent(""))).StatusCode == HttpStatusCode.Forbidden && !bridge.StopRequested);
            client.DefaultRequestHeaders.Authorization = new("Bearer", connection.LocalToken);
            Assert((await client.PostAsync("/codex-config/stop", new StringContent(""))).StatusCode == HttpStatusCode.Accepted && bridge.StopRequested);
        }));

        tests.Add(("Responses WebSocket bridge replaces auth and preserves model and tier", async () =>
        {
            var upstreamPort = MixedConnection.Create(Settings()).Port;
            var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
            builder.WebHost.UseUrls($"http://127.0.0.1:{upstreamPort}");
            await using var upstream = builder.Build(); upstream.UseWebSockets();
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            upstream.Run(async context =>
            {
                Assert(context.Request.Path == "/upstream/responses" && context.Request.Headers.Authorization == "Bearer fake-krill-pat");
                Assert(!context.Request.Headers.ContainsKey("chatgpt-account-id"));
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                var buffer = new byte[4096]; var result = await socket.ReceiveAsync(buffer.AsMemory(), context.RequestAborted);
                var body = Encoding.UTF8.GetString(buffer.AsSpan(0, result.Count)); received.TrySetResult(body);
                await socket.SendAsync(Encoding.UTF8.GetBytes(body).AsMemory(), WebSocketMessageType.Text, true, context.RequestAborted);
                try { await socket.ReceiveAsync(buffer.AsMemory(), context.RequestAborted); } catch (Exception) { }
            });
            await upstream.StartAsync();
            var connection = MixedConnection.Create(Settings() with { BaseUrl = $"http://127.0.0.1:{upstreamPort}/upstream", Kind = ConnectionKind.ResponsesApi });
            await using var bridge = new ModelBridge(() => connection, Auth); await bridge.StartAsync();
            using var client = new ClientWebSocket();
            client.Options.Proxy = null;
            client.Options.SetRequestHeader("Authorization", "Bearer fake-official-access");
            client.Options.SetRequestHeader("chatgpt-account-id", "do-not-forward");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await client.ConnectAsync(new Uri($"ws://127.0.0.1:{connection.Port}/v1/responses"), timeout.Token);
            const string payload = "{\"model\":\"codex-auto-review\",\"service_tier\":\"fast\"}";
            await client.SendAsync(Encoding.UTF8.GetBytes(payload).AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
            var answer = new byte[4096]; var frame = await client.ReceiveAsync(answer.AsMemory(), timeout.Token);
            Assert(Encoding.UTF8.GetString(answer.AsSpan(0, frame.Count)) == payload && await received.Task.WaitAsync(timeout.Token) == payload);
            client.Abort();
        }));

        if (Environment.GetEnvironmentVariable("CODEX_CONFIG_TEST_CLI") is { Length: > 0 } cli)
            tests.Add(("native Codex offline mixed identity and inference routing", () => NativeCliAsync(cli, Home(), Settings())));
    }

    private static async Task NativeCliAsync(string executable, string home, ConnectionSettings settings)
    {
        var store = new ConfigStore(new FakeEnvironment());
        var plan = store.Plan(store.Load(home), settings, officialAuth: Auth()); store.Apply(plan);
        var connection = MixedConnection.Read(plan.Bridge!);
        // Keep the real CLI's account discovery offline as well. This is a separate fake official host,
        // not a production override and not part of the inference bridge's allowed endpoints.
        var officialPort = MixedConnection.Create(settings).Port;
        var mockBuilder = WebApplication.CreateSlimBuilder();
        mockBuilder.Logging.ClearProviders();
        mockBuilder.WebHost.UseUrls($"http://127.0.0.1:{officialPort}");
        await using var official = mockBuilder.Build();
        official.MapGet("/backend-api/wham/accounts/check", () => Results.Json(new
        {
            accounts = new[] { new { id = "offline-test-account", workspace_backend_origin = "https://chatgpt.com", account_routing_override = "NO_CONSTRAINT", plan_type = "plus" } }
        }));
        await official.StartAsync();
        var requests = new List<string>();
        using var handler = new FakeHandler(request =>
        {
            Assert(request.Headers.Authorization?.Parameter == "fake-krill-pat", "Official token reached upstream.");
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            requests.Add(body);
            var message = new { id = "msg_offline", type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text = "offline-ok", annotations = Array.Empty<object>() } } };
            var events = new object[]
            {
                new { type = "response.created", response = new { id = "resp_offline", status = "in_progress", output = Array.Empty<object>() } },
                new { type = "response.output_item.added", output_index = 0, item = message },
                new { type = "response.output_item.done", output_index = 0, item = message },
                new { type = "response.completed", response = new { id = "resp_offline", status = "completed", output = new[] { message }, usage = new { input_tokens = 1, output_tokens = 1, total_tokens = 2 } } }
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Concat(events.Select(value => "data: " + JsonSerializer.Serialize(value) + "\n\n")), Encoding.UTF8, "text/event-stream") };
        });
        await using var bridge = new ModelBridge(() => connection, () => File.ReadAllBytes(Path.Combine(home, "auth.json")), handler);
        await bridge.StartAsync();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = home };
        start.ArgumentList.Add("app-server"); start.ArgumentList.Add("--listen"); start.ArgumentList.Add("stdio://");
        start.ArgumentList.Add("-c"); start.ArgumentList.Add($"chatgpt_base_url=\"http://127.0.0.1:{officialPort}/backend-api\"");
        start.Environment["CODEX_HOME"] = home;
        foreach (var name in new[] { "CODEX_ACCESS_TOKEN", "CODEX_API_KEY", "OPENAI_API_KEY", "OPENAI_BASE_URL", "CODEX_AUTHAPI_BASE_URL" }) start.Environment.Remove(name);
        start.Environment["HTTP_PROXY"] = start.Environment["HTTPS_PROXY"] = start.Environment["ALL_PROXY"] = "http://127.0.0.1:9";
        start.Environment["NO_PROXY"] = "127.0.0.1,localhost";
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try
        {
            async Task Send(object message) { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message)); await process.StandardInput.FlushAsync(); }
            async Task<JsonElement> ReadId(int id)
            {
                while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
                {
                    using var parsed = JsonDocument.Parse(line);
                    if (parsed.RootElement.TryGetProperty("id", out var found) && found.ValueKind == JsonValueKind.Number && found.GetInt32() == id)
                    {
                        if (parsed.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetRawText());
                        return parsed.RootElement.GetProperty("result").Clone();
                    }
                }
                throw new InvalidOperationException("Native CLI stopped: " + await errors);
            }
            await Send(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "codex_config_offline_test", version = "0.9.0" }, capabilities = new { experimentalApi = true } } });
            await ReadId(1); await Send(new { method = "initialized" });
            await Send(new { id = 2, method = "account/read", @params = new { refreshToken = false } });
            var account = await ReadId(2);
            Assert(account.GetProperty("account").GetProperty("type").GetString() == "chatgpt", "Mixed provider lost official identity.");
            await Send(new { id = 3, method = "thread/start", @params = new { model = settings.Model, modelProvider = MixedConnection.Provider, cwd = home, approvalPolicy = "never", sandbox = "read-only", experimentalRawEvents = false } });
            var thread = await ReadId(3); var threadId = thread.GetProperty("thread").GetProperty("id").GetString();
            await Send(new { id = 4, method = "turn/start", @params = new { threadId, input = new[] { new { type = "text", text = "Say offline-ok only.", text_elements = Array.Empty<object>() } }, serviceTier = "fast" } });
            await ReadId(4);
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                using var parsed = JsonDocument.Parse(line);
                if (parsed.RootElement.TryGetProperty("method", out var method) && method.GetString() == "turn/completed")
                {
                    var turn = parsed.RootElement.GetProperty("params").GetProperty("turn");
                    Assert(turn.GetProperty("status").GetString() == "completed", "Native inference failed: " + turn.GetRawText());
                    break;
                }
            }
            Assert(requests.Count > 0, "Native CLI did not reach bridge.");
            using var payload = JsonDocument.Parse(requests.Last());
            Assert(payload.RootElement.GetProperty("service_tier").GetString() is "fast" or "priority", "Fast parameter was lost.");
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); await errors; }
    }

    private static void Assert(bool condition, string message = "Mixed connection assertion failed") { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
