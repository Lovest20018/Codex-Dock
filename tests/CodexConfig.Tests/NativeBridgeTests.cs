using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using CodexConfig.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

internal static class NativeBridgeTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        if (Environment.GetEnvironmentVariable("CODEX_CONFIG_TEST_CLI") is not { Length: > 0 } cli) return;
        foreach (var provider in new[] { "openai", MixedConnection.Provider }) tests.Add(("native Codex identity, catalog and inference: " + provider, () => Check(cli, provider, root)));
    }
    private static async Task Check(string cli, string provider, string root)
    {
        var home = Path.GetFullPath(Path.Combine(root, Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(home);
        var settings = new ConnectionSettings("https://fixture.invalid/v1", "fake-upstream-key", "gpt-6.1-sol") { Mode = ConnectionMode.OfficialWithApi, Kind = ConnectionKind.ResponsesApi, PreservePreferenceValues = true };
        var store = new ConfigStore(new FixtureEnvironment()); var plan = store.Plan(store.Load(home), settings, officialAuth: Auth()); store.Apply(plan);
        var connection = MixedConnection.Read(plan.Bridge!); var doc = new TomlDocument(plan.Config); doc.Set("model_provider", provider); doc.Set("features.fast_mode", true); doc.Set("features.responses_websockets", false); doc.Set("features.responses_websockets_v2", false); doc.Set("features.enable_request_compression", false); File.WriteAllBytes(Path.Combine(home, "config.toml"), doc.Bytes);
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); await using var official = builder.Build();
        official.MapGet("/backend-api/wham/accounts/check", () => Results.Json(new { accounts = new[] { new { id = "offline-test-account", workspace_backend_origin = "https://chatgpt.com", account_routing_override = "NO_CONSTRAINT", plan_type = "plus" } } })); await official.StartAsync(); var officialUrl = official.Urls.Single();
        using var handler = new CaptureHandler(); await using var bridge = new ModelBridge(() => connection, () => File.ReadAllBytes(Path.Combine(home, "auth.json")), handler); await bridge.StartAsync();
        var start = new ProcessStartInfo(cli) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = home };
        foreach (var arg in new[] { "app-server", "--listen", "stdio://", "-c", "chatgpt_base_url=\"" + officialUrl + "/backend-api\"" }) start.ArgumentList.Add(arg);
        start.Environment["CODEX_HOME"] = home; foreach (var n in new[] { "CODEX_ACCESS_TOKEN", "CODEX_API_KEY", "OPENAI_API_KEY", "OPENAI_BASE_URL", "CODEX_AUTHAPI_BASE_URL" }) start.Environment.Remove(n);
        start.Environment["HTTP_PROXY"] = start.Environment["HTTPS_PROXY"] = start.Environment["ALL_PROXY"] = "http://127.0.0.1:9"; start.Environment["NO_PROXY"] = "127.0.0.1,localhost";
        using var process = Process.Start(start)!; var errors = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            async Task Send(object o) { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(o)); await process.StandardInput.FlushAsync(); }
            async Task<JsonElement> Read(int id) { while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line) { using var j = JsonDocument.Parse(line); var r = j.RootElement; if (r.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.Number && v.GetInt32() == id) { if (r.TryGetProperty("error", out var err)) throw new Exception(err.GetRawText()); return r.GetProperty("result").Clone(); } } throw new Exception("Native process ended"); }
            await Send(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "codex_dock_offline_probe", version = "0.12.0" }, capabilities = new { experimentalApi = true } } }); await Read(1); await Send(new { method = "initialized" });
            await Send(new { id = 2, method = "account/read", @params = new { refreshToken = false } }); var account = await Read(2); if (account.GetProperty("account").GetProperty("type").GetString() != "chatgpt") throw new Exception("Identity lost");
            await Send(new { id = 3, method = "thread/start", @params = new { model = settings.Model, modelProvider = provider, cwd = home, approvalPolicy = "never", sandbox = "read-only", experimentalRawEvents = false } }); var thread = await Read(3); var threadId = thread.GetProperty("thread").GetProperty("id").GetString();
            await Send(new { id = 4, method = "turn/start", @params = new { threadId, input = new[] { new { type = "text", text = "Say offline-ok only.", text_elements = Array.Empty<object>() } }, serviceTier = "priority" } }); await Read(4); var completed = false;
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line) { using var j = JsonDocument.Parse(line); if (j.RootElement.TryGetProperty("method", out var m) && m.GetString() == "turn/completed") { var turn = j.RootElement.GetProperty("params").GetProperty("turn"); if (turn.GetProperty("status").GetString() != "completed") throw new Exception(turn.GetRawText()); completed = true; break; } }
            if (!completed || handler.Responses == 0 || !handler.OnlyConfiguredKey || !handler.NoAccountHeader || !handler.SawSessionHeader || !handler.SawPriority) throw new Exception("Native bridge routing, identity or metadata failed");
            if (provider == "openai" && handler.Models == 0) throw new Exception("Native catalog was not requested");
        }
        finally { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await errors; }
    }
    internal static byte[] Auth() { var claims = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["email"] = "offline@example.invalid", ["sub"] = "offline-test-user", ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), ["https://api.openai.com/auth"] = new { chatgpt_account_id = "offline-test-account", chatgpt_plan_type = "plus", chatgpt_user_id = "offline-test-user" } })).TrimEnd('=').Replace('+', '-').Replace('/', '_'); return JsonSerializer.SerializeToUtf8Bytes(new { auth_mode = "chatgpt", tokens = new { id_token = "eyJhbGciOiJub25lIn0." + claims + ".offline", access_token = "fake-official-access", refresh_token = "fake-official-refresh", account_id = "offline-test-account" }, last_refresh = DateTimeOffset.UtcNow }); }
    sealed class FixtureEnvironment : IUserEnvironment { public string? Read() => null; public void Write(string? value) { } }
    sealed class CaptureHandler : HttpMessageHandler
    {
        public int Responses, Models; public bool OnlyConfiguredKey = true, NoAccountHeader = true, SawSessionHeader, SawPriority;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            OnlyConfiguredKey &= r.Headers.Authorization?.Parameter == "fake-upstream-key"; NoAccountHeader &= !r.Headers.Contains("chatgpt-account-id"); SawSessionHeader |= r.Headers.Contains("session-id");
            if (r.Method == HttpMethod.Get) { Models++; return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { models = new[] { new { slug = "gpt-6.1-sol", display_name = "Fixture", description = "Offline fixture", base_instructions = "You are a coding assistant.", default_reasoning_level = "low", supported_reasoning_levels = new[] { new { effort = "low", description = "Low" } }, shell_type = "unified_exec", visibility = "list", supported_in_api = true, priority = 1, support_verbosity = false, apply_patch_tool_type = "freeform", truncation_policy = new { mode = "tokens", limit = 10000 }, experimental_supported_tools = Array.Empty<string>(), input_modalities = new[] { "text" }, service_tiers = new[] { new { id = "priority", name = "Fast", description = "Fixture only" } }, supports_parallel_tool_calls = true, supports_reasoning_summaries = false } } }), Encoding.UTF8, "application/json") }; }
            Responses++; var body = r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct); SawPriority |= body.Contains("priority");
            var message = new { id = "msg_offline", type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text = "offline-ok", annotations = Array.Empty<object>() } } };
            object[] events = [new { type = "response.created", response = new { id = "resp_offline", status = "in_progress", output = Array.Empty<object>() } }, new { type = "response.output_item.added", output_index = 0, item = message }, new { type = "response.output_item.done", output_index = 0, item = message }, new { type = "response.completed", response = new { id = "resp_offline", status = "completed", output = new[] { message }, usage = new { input_tokens = 1, output_tokens = 1, total_tokens = 2 } } }];
            return new(HttpStatusCode.OK) { Content = new StringContent(string.Concat(events.Select(e => "data: " + JsonSerializer.Serialize(e) + "\n\n")), Encoding.UTF8, "text/event-stream") };
        }
    }



}
