using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class ModelGatewayDiagnosticsTests
{
    private static BridgeMetrics Metrics() => new(0, 0, 0, 0, 0, 0, null, null, null, null, 0, 0, null, null, null, null, null);

    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("shared gateway refresh drops diagnostic snapshots for stopped unknown damaged and abandoned states", async () =>
        {
            var home = Path.Combine(root, "diagnostic-probe-" + Guid.NewGuid().ToString("N")); var runtime = new DiagnosticRuntime();
            var service = new GatewayManagementService(runtime, new(new WorkspaceSession(home)), Path.Combine(home, "preferences"));
            var state = await service.RefreshAsync(home, () => true); Assert(state?.Model.Diagnostics is not null && state.Mobile.Diagnostics is null);
            runtime.Running = false; state = await service.RefreshAsync(home, () => true); Assert(state?.Model.Diagnostics is null);
            runtime.Running = null; state = await service.RefreshAsync(home, () => true); Assert(state is { Model.Diagnostics: null, Model.Running: null });
            runtime.Fail = true; state = await service.RefreshAsync(home, () => true); Assert(state is { Model.Diagnostics: null, Model.Error: not null });
            runtime.Fail = false; runtime.Running = true; var previous = service.Overview;
            Assert(await service.RefreshAsync(home + "-abandoned", () => false) is null && ReferenceEquals(service.Overview, previous));
            service.ClearOverview(); Assert(service.Overview is null);
        }));
        tests.Add(("native authenticated gateway diagnostics expose actual local compaction and latency without conversation data", async () =>
        {
            var home = Path.Combine(root, "native-model-diagnostics-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            var store = new ConfigStore(new NoWrites(), _ => null);
            var connection = MixedConnection.Create(new("https://offline-upstream.invalid/v1", "fake-diagnostic-upstream-key", "fixture") { Kind = ConnectionKind.ResponsesApi, Mode = ConnectionMode.OfficialWithApi });
            var config = Path.Combine(home, "config.toml"); File.WriteAllText(config, "model='fixture'\nopenai_base_url='" + connection.NativeBaseUrl + "'\n[model_providers.openai.capabilities]\nremote_compaction='v2'\n");
            File.WriteAllBytes(Path.Combine(home, MixedConnection.FileName), connection.Protect());
            var requests = 0; using var handler = new Handler(async request =>
            {
                requests++; Assert((await request.Content!.ReadAsStringAsync()).Contains("fake-private-request"));
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"diagnostic-response\",\"status\":\"completed\",\"output\":[{\"type\":\"compaction\",\"id\":\"c1\",\"encrypted_content\":\"fake-private-compaction\"}]}", Encoding.UTF8, "application/json") };
                response.Headers.Add("x-request-id", "req_diagnostic"); return response;
            });
            await using var bridge = new ModelBridge(() => connection, () => null, handler); await bridge.StartAsync();
            var native = new NativeGatewayManagementRuntime(store); var definition = await native.LoadAsync(GatewayKind.Model, home, CancellationToken.None);
            var before = store.Load(home); var initial = await native.ProbeAsync(definition, CancellationToken.None);
            Assert(initial.Running == true && initial.Diagnostics!.Compaction.Contains("确认完成 0 次") && requests == 0);
            using var client = new HttpClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.LocalToken);
            using var response = await client.PostAsync(connection.NativeBaseUrl + "/responses", new StringContent("{\"model\":\"fixture\",\"input\":[{\"type\":\"compaction_trigger\"}],\"private\":\"fake-private-request\"}", Encoding.UTF8, "application/json"));
            response.EnsureSuccessStatusCode(); _ = await response.Content.ReadAsStringAsync();
            GatewayProbe? probe = null;
            for (var i = 0; i < 50; i++) { probe = await native.ProbeAsync(definition, CancellationToken.None); if (probe.Diagnostics?.Latency.Contains("活跃 0") == true) break; await Task.Delay(20); }
            Assert(probe?.Running == true && probe.Diagnostics!.Compaction.Contains("v2 1 次") && probe.Diagnostics.Compaction.Contains("确认完成 1 次") && probe.Diagnostics.Latency.Contains("ms") && requests == 1);
            Assert(probe!.Diagnostics?.TransferSummary is { } summary && summary.Contains("请求 ID req_diagnostic") && summary.Contains("最终响应 ID diagnostic-response"));
            Assert(!JsonSerializer.Serialize(probe.Diagnostics).Contains("fake-private") && before.SameContents(store.Load(home)));
            File.WriteAllText(config, File.ReadAllText(config).Replace("remote_compaction='v2'", "remote_compaction='unsupported'", StringComparison.Ordinal));
            await ThrowsAsync<IOException>(() => native.ProbeAsync(definition, CancellationToken.None));
        }));
        tests.Add(("gateway stop invalidates an earlier in-flight overview and starts a fresh stopped read", async () =>
        {
            var home = Path.Combine(root, "late-gateway-overview-" + Guid.NewGuid().ToString("N")); var runtime = new DiagnosticRuntime { DelayNextModelProbe = true };
            var service = new GatewayManagementService(runtime, new(new WorkspaceSession(home)), Path.Combine(home, "preferences"));
            var earlier = service.RefreshAsync(home, () => true); await runtime.ProbeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert((await service.ExecuteAsync(GatewayKind.Model, GatewayCommand.Stop, home, () => true)).Succeeded && service.Overview is null);
            var latest = await service.RefreshAsync(home, () => true);
            Assert(latest is { Model.Running: false, Model.Diagnostics: null });
            runtime.ReleaseProbe.TrySetResult(); Assert(await earlier is null && ReferenceEquals(service.Overview, latest));
        }));
    }
    private sealed class DiagnosticRuntime : IGatewayManagementRuntime
    {
        internal bool? Running = true; internal bool Fail, DelayNextModelProbe;
        internal readonly TaskCompletionSource ProbeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseProbe = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<GatewayDefinition> LoadAsync(GatewayKind kind, string home, CancellationToken cancellation)
            => Task.FromResult(new GatewayDefinition(kind, home, true, true, "fixture", "same-revision"));
        public async Task<GatewayProbe> ProbeAsync(GatewayDefinition definition, CancellationToken cancellation)
        {
            if (Fail && definition.Kind == GatewayKind.Model) throw new IOException("fixture unavailable");
            var result = new GatewayProbe(Running) { Diagnostics = ModelGatewayDiagnostics.FromStatus(new(1, 0, 0, ModelBridge.ProtocolVersion, Metrics: Metrics()), "v2") };
            if (DelayNextModelProbe && definition.Kind == GatewayKind.Model) { DelayNextModelProbe = false; ProbeEntered.TrySetResult(); await ReleaseProbe.Task.WaitAsync(cancellation); }
            return result;
        }
        public Task StartAsync(GatewayDefinition definition, CancellationToken cancellation) => throw new InvalidOperationException("Read-only fixture cannot start workers");
        public Task StopAsync(GatewayDefinition definition, CancellationToken cancellation) { Running = false; return Task.CompletedTask; }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request); }
    private sealed class NoWrites : IUserEnvironment { public string? Read() => null; public void Write(string? value) => throw new InvalidOperationException("No environment writes"); }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Model gateway diagnostics regression"); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
