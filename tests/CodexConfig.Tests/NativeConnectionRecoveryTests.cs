using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CodexConfig.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

internal static class NativeConnectionRecoveryTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        if (Environment.GetEnvironmentVariable("CODEX_CONFIG_TEST_CLI") is { Length: > 0 } cli)
            tests.Add(("native persisted chat resumes through product provider preparation in both directions", () => CheckAsync(root, cli)));
    }
    private static async Task CheckAsync(string root, string cli)
    {
        var home = Path.Combine(root, "native-recovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50)); var captures = new List<(string Label, string Input, string? Effort, string? Tier)>();
        async Task<WebApplication> Upstream(string label)
        {
            var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); var app = builder.Build();
            app.MapPost("/v1/responses", async context =>
            {
                if (context.Request.Headers.Authorization != "Bearer fake-native-recovery-key") throw new Exception("Unexpected fixture authorization");
                using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: timeout.Token); var value = body.RootElement;
                captures.Add((label, value.GetProperty("input").GetRawText(), value.GetProperty("reasoning").GetProperty("effort").GetString(), value.GetProperty("service_tier").GetString()));
                var item = new { id = "fixture-message", type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text = "reply-from-" + label, annotations = Array.Empty<object>() } } };
                object[] events = [new { type = "response.created", response = new { id = "fixture-response", status = "in_progress", output = Array.Empty<object>() } }, new { type = "response.output_item.added", output_index = 0, item }, new { type = "response.output_item.done", output_index = 0, item }, new { type = "response.completed", response = new { id = "fixture-response", status = "completed", output = new[] { item }, usage = new { input_tokens = 1, output_tokens = 1, total_tokens = 2 } } }];
                context.Response.ContentType = "text/event-stream"; await context.Response.WriteAsync(string.Concat(events.Select(item => "data: " + JsonSerializer.Serialize(item) + "\n\n")), timeout.Token);
            });
            await app.StartAsync(timeout.Token); return app;
        }
        await using var a = await Upstream("A"); await using var b = await Upstream("B");
        var catalog = JsonSerializer.Serialize(new { models = new[] { new {
            slug = "fixture-model", display_name = "Fixture", description = "Offline only", base_instructions = "Reply with the fixed local fixture response.", default_reasoning_level = "low",
            supported_reasoning_levels = new[] { new { effort = "low", description = "Low" }, new { effort = "max", description = "Max" } },
            shell_type = "unified_exec", visibility = "list", supported_in_api = true, priority = 1, support_verbosity = false,
            apply_patch_tool_type = "freeform", truncation_policy = new { mode = "tokens", limit = 10000 }, experimental_supported_tools = Array.Empty<string>(), input_modalities = new[] { "text" },
            service_tiers = new[] { new { id = "ultrafast", name = "Ultrafast", description = "Fixture" } }, supports_parallel_tool_calls = true, supports_reasoning_summaries = false, supports_reasoning_effort_updates = false
        } } });
        File.WriteAllText(Path.Combine(home, "catalog.json"), catalog);
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-native-recovery-key\"}");
        void Configure(string provider, string address)
        {
            var doc = new TomlDocument(null); doc.Set("model", "fixture-model"); doc.Set("model_provider", provider); doc.Set("model_catalog_json", Path.Combine(home, "catalog.json"));
            doc.Set("model_reasoning_effort", "max"); doc.Set("openai_base_url", address + "/v1"); doc.Set("cli_auth_credentials_store", "file");
            doc.Set("web_search", "disabled"); doc.Set("approval_policy", "never"); doc.Set("sandbox_mode", "read-only"); doc.SetRaw("thread_unload_delay_secs", "0");
            foreach (var feature in new[] { "responses_websockets", "responses_websockets_v2", "enable_request_compression", "context_management", "multi_agent", "memories", "apps", "plugins" }) doc.Set("features." + feature, false);
            doc.Set("analytics.enabled", false); doc.Set("model_providers.legacy.name", "Offline legacy"); doc.Set("model_providers.legacy.base_url", a.Urls.Single() + "/v1");
            doc.Set("model_providers.legacy.wire_api", "responses"); doc.Set("model_providers.legacy.requires_openai_auth", true);
            File.WriteAllBytes(Path.Combine(home, "config.toml"), doc.Bytes);
        }
        static void Assert(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
        JsonElement ResumeParams(string id, string provider) => JsonSerializer.SerializeToElement(new { threadId = id, modelProvider = provider, model = "fixture-model", serviceTier = "ultrafast", config = new { model_reasoning_effort = "max" } });
        Configure("legacy", a.Urls.Single()); string thread;
        await using (var first = await Rpc.StartAsync(cli, home, timeout.Token))
        {
            var started = await first.Call("thread/start", new { model = "fixture-model", modelProvider = "legacy", cwd = home, approvalPolicy = "never", sandbox = "read-only", serviceTier = "ultrafast" }); thread = started.GetProperty("thread").GetProperty("id").GetString()!;
            await first.Turn(thread, "fixture-history-A");
        }
        Configure("openai", b.Urls.Single());
        var histories = Directory.GetFiles(Path.Combine(home, "sessions"), "*.jsonl", SearchOption.AllDirectories).ToDictionary(file => file, file => SHA256.HashData(File.ReadAllBytes(file)));
        var prepared = ThreadProviderStore.Prepare(home, thread, "legacy", "openai", () => { });
        Assert(prepared.ChangedRows == 1 && histories.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)), "Product preparation changed native rollout bytes");
        await using (var second = await Rpc.StartAsync(cli, home, timeout.Token))
        {
            var resumed = await second.Call("thread/resume", ResumeParams(thread, "openai"));
            Assert(resumed.GetProperty("thread").GetProperty("id").GetString() == thread && resumed.GetRawText().Contains("fixture-history-A"), "Original native chat or history was lost");
            await second.Turn(thread, "fixture-history-B");
        }
        Configure("legacy", a.Urls.Single()); ThreadProviderStore.Prepare(home, thread, "openai", "legacy", () => { });
        await using (var third = await Rpc.StartAsync(cli, home, timeout.Token))
        {
            var resumed = await third.Call("thread/resume", ResumeParams(thread, "legacy"));
            Assert(resumed.GetRawText().Contains("fixture-history-B") && resumed.GetRawText().Contains("reply-from-B"), "Reverse native recovery lost appended history");
            await third.Turn(thread, "fixture-history-return-A");
        }
        Assert(captures.Select(value => value.Label).SequenceEqual(["A", "B", "A"]) && captures.All(value => value.Effort == "max" && value.Tier == "ultrafast"), "Native recovery changed route, Max or Ultra Fast");
        Assert(captures[1].Input.Contains("fixture-history-A") && captures[2].Input.Contains("fixture-history-B"), "Native model input lost persisted context");
    }
    private sealed class Rpc : IAsyncDisposable
    {
        private readonly Process process;
        private readonly CancellationToken cancellation;
        private readonly Channel<JsonElement> messages = Channel.CreateUnbounded<JsonElement>();
        private readonly List<JsonElement> events = [];
        private readonly Task output, errors;
        private int sequence;
        private Rpc(Process process, CancellationToken cancellation)
        {
            this.process = process; this.cancellation = cancellation;
            errors = process.StandardError.ReadToEndAsync();
            output = Task.Run(async () =>
            {
                try { while (await process.StandardOutput.ReadLineAsync(cancellation) is { } line) { using var document = JsonDocument.Parse(line); await messages.Writer.WriteAsync(document.RootElement.Clone(), cancellation); } }
                finally { messages.Writer.TryComplete(); }
            }, cancellation);
        }
        internal static async Task<Rpc> StartAsync(string cli, string home, CancellationToken cancellation)
        {
            var start = new ProcessStartInfo(cli) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = home };
            foreach (var argument in new[] { "app-server", "--listen", "stdio://" }) start.ArgumentList.Add(argument);
            start.Environment["CODEX_HOME"] = home;
            foreach (var key in new[] { "CODEX_ACCESS_TOKEN", "CODEX_API_KEY", "OPENAI_API_KEY", "OPENAI_BASE_URL", "CODEX_AUTHAPI_BASE_URL" }) start.Environment.Remove(key);
            foreach (var key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" }) start.Environment[key] = "http://127.0.0.1:9";
            start.Environment["NO_PROXY"] = "127.0.0.1,localhost";
            var rpc = new Rpc(Process.Start(start)!, cancellation);
            try { await rpc.Call("initialize", new { clientInfo = new { name = "codex_dock_recovery_fixture", version = "1.0" }, capabilities = new { experimentalApi = true } }); await rpc.Write(new { method = "initialized" }); return rpc; }
            catch { await rpc.DisposeAsync(); throw; }
        }
        private async Task Write(object request) { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request)); await process.StandardInput.FlushAsync(cancellation); }
        internal async Task<JsonElement> Call(string method, object parameters)
        {
            var id = ++sequence; await Write(new { id, method, @params = parameters });
            while (true)
            {
                var response = await messages.Reader.ReadAsync(cancellation);
                if (response.TryGetProperty("id", out var candidate) && candidate.ValueKind == JsonValueKind.Number && candidate.GetInt32() == id)
                {
                    if (response.TryGetProperty("error", out var error)) throw new IOException(error.GetRawText()); return response.GetProperty("result").Clone();
                }
                events.Add(response);
            }
        }
        internal async Task Turn(string thread, string input)
        {
            var started = await Call("turn/start", new { threadId = thread, effort = "max", serviceTier = "ultrafast", input = new[] { new { type = "text", text = input, text_elements = Array.Empty<object>() } } });
            var id = started.GetProperty("turn").GetProperty("id").GetString();
            while (true)
            {
                var completed = events.FirstOrDefault(value => value.TryGetProperty("method", out var method) && method.GetString() == "turn/completed" && value.GetProperty("params").GetProperty("turn").GetProperty("id").GetString() == id);
                if (completed.ValueKind != JsonValueKind.Undefined)
                { var turn = completed.GetProperty("params").GetProperty("turn"); if (turn.GetProperty("status").GetString() != "completed") throw new IOException(turn.GetRawText()); return; }
                events.Add(await messages.Reader.ReadAsync(cancellation));
            }
        }
        public async ValueTask DisposeAsync()
        {
            if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync();
            try { await output; } catch (OperationCanceledException) { }
            await errors; process.Dispose();
        }
    }
}
