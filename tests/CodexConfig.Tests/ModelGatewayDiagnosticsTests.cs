using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class ModelGatewayDiagnosticsTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        Test("gateway diagnostic presentation separates configured support trigger items and confirmed completion", () =>
        {
            var status = new BridgeStatus(8, 2, 1, ModelBridge.ProtocolVersion, LastUpstreamStatus: 200,
                Metrics: Metrics() with { LegacyCompactionRequests = 3, V2Requests = 2, V2Triggers = 4, CompactionItems = 1, SuccessfulCompactions = 0 }, UltrafastRequests: 1);
            var view = ModelGatewayDiagnostics.FromStatus(status, "v2");
            Assert(view.Requests.Contains("8 次") && view.Requests.Contains("Fast 2") && view.Requests.Contains("Ultra Fast 1"));
            Assert(view.Configuration.Contains("文件允许 v2") && view.Compaction.Contains("旧端点 3 次") && view.Compaction.Contains("v2 2 次（trigger 4 个）") &&
                view.Compaction.Contains("压缩条目 1 个 · 确认完成 0 次") && view.Compaction.Contains("最近确认完成：未观察到") && view.Upstream.Contains("200") && view.Compaction.Contains("HTTP 200 不能"));
        });
        Test("gateway diagnostics label the most recent trigger and successful completion independently", () =>
        {
            var metrics = Metrics() with { LastTriggerAt = DateTimeOffset.UtcNow, LastTriggerProtocol = "legacy", LastTriggerTransport = "http",
                LastCompactionAt = DateTimeOffset.UtcNow.AddMinutes(-1), LastCompactionProtocol = "v2", LastCompactionTransport = "websocket", InspectionLimited = 2,
                LastQueueMs = 7, LastHeadersMs = 21, LastFirstDataMs = 34, LastDurationMs = 55, LastStreamEnd = "completed", Waiting = 1, Active = 2 };
            var view = ModelGatewayDiagnostics.FromStatus(new(5, 0, 0, ModelBridge.ProtocolVersion, Metrics: metrics), "unsupported");
            Assert(view.Compaction.Contains("旧端点 / HTTP") && view.Compaction.Contains("v2 / WebSocket") && view.Compaction.Contains("2 段数据无法完整检查"));
            Assert(view.Latency.Contains("排队 1 · 活跃 2") && view.Latency.Contains("7 ms") && view.Latency.Contains("55 ms") && view.Latency.Contains("完整转发") && view.Configuration.Contains("普通摘要"));
        });
        Test("gateway diagnostic projection never publishes upstream messages identifiers or unknown labels", () =>
        {
            var metrics = Metrics() with { LastTriggerAt = DateTimeOffset.UtcNow, LastTriggerProtocol = "fake-private-protocol", LastTriggerTransport = "fake-private-transport",
                LastCompactionAt = DateTimeOffset.UtcNow, LastCompactionProtocol = "fake-private-protocol", LastCompactionTransport = "fake-private-transport",
                LastStreamEnd = "fake-private-error", LastQueueMs = double.NaN, LastHeadersMs = double.PositiveInfinity, LastFirstDataMs = -1, V2Requests = -1 };
            var view = ModelGatewayDiagnostics.FromStatus(new(1, 0, 0, ModelBridge.ProtocolVersion, LastError: "fake-private-upstream-message",
                LastRequestId: "fake-private-request-id", LastUpstreamCode: "fake-private-code", LastRequestKind: "fake-private-kind", Metrics: metrics), "fake-private-preference");
            var text = JsonSerializer.Serialize(view); Assert(!text.Contains("fake-private") && !text.Contains("NaN") && !text.Contains("Infinity") &&
                view.Latency.Contains("未测得") && view.Latency.Contains("无法识别") && view.Compaction.Contains("v2 未提供 次"));
        });
        Test("SSE diagnostic proves the final response only after a complete event and scopes it to its own request", () =>
        {
            var monitor = new BridgeTelemetry();
            using var first = monitor.Begin("http"); using var second = monitor.Begin("http");
            first.Enter(); second.Enter(); first.HeaderMetadata(true, "req_one"); second.HeaderMetadata(true, "req_two");
            using var one = monitor.Observe(false, "http", first); using var two = monitor.Observe(false, "http", second);
            one.SetSse(true); two.SetSse(true);
            var completed = "data: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp_one\"}}";
            one.Feed(Encoding.UTF8.GetBytes(completed + "\r")); one.Feed("\n\r"u8); one.Feed("\n"u8);
            two.Feed(Encoding.UTF8.GetBytes(completed.Replace("resp_one", "resp_undelimited", StringComparison.Ordinal) + "\n"));
            one.Finish(); first.End("client_disconnected");
            Assert(monitor.Snapshot().LastTransfer is { RequestId: "req_one", FinalResponseId: "resp_one", Transport: "sse", Phase: "client_disconnected" });
            two.Finish(); second.End("completed");
            Assert(monitor.Snapshot().LastTransfer is { RequestId: "req_two", FinalResponseId: null, Phase: "incomplete" });
            using var listing = monitor.Begin(); listing.End("completed");
            Assert(monitor.Snapshot().LastTransfer?.RequestId == "req_two");
        });
        Test("WebSocket diagnostic separates consecutive responses and rejects failed or half-frame IDs", () =>
        {
            var monitor = new BridgeTelemetry(); using var transfer = monitor.Begin("websocket"); transfer.Enter(); transfer.HeaderMetadata(false, "req_upgrade");
            using var socket = monitor.Observe(false, "websocket", transfer);
            socket.Request("{\"type\":\"response.create\"}"u8.ToArray());
            socket.Feed("{\"type\":\"response.completed\",\"response\":{\"id\":\"resp_ws1\"}}"u8); socket.Finish();
            Assert(monitor.Snapshot().LastTransfer is { RequestId: "req_upgrade", FinalResponseId: "resp_ws1", Transport: "websocket", Phase: "completed" });
            socket.Request("{\"type\":\"response.create\"}"u8.ToArray());
            socket.Feed("{\"type\":\"response.failed\",\"response\":{\"id\":\"resp_failed\"}}"u8); socket.Finish();
            Assert(monitor.Snapshot().LastTransfer is { FinalResponseId: null, Phase: "upstream_error" });
            socket.Request("{\"type\":\"response.create\"}"u8.ToArray());
            socket.Feed("{\"type\":\"response.completed\",\"response\":{\"id\":\"resp_unsent_boundary\"}}"u8);
            transfer.End("peer_closed");
            Assert(monitor.Snapshot().LastTransfer is { FinalResponseId: null, Phase: "peer_closed" });
        });
        Test("transfer summaries reject token-shaped or multiline values and label unknown response IDs", () =>
        {
            var summary = ModelGatewayDiagnostics.FromStatus(new(2, 0, 0, Metrics: Metrics() with
            { LastTransfer = new(DateTimeOffset.UnixEpoch, "stream_idle_timeout", "http", "req_ok", "sk-fixture-token") }), null).TransferSummary;
            Assert(summary is not null && summary.Contains("数据空闲超时") &&
                summary.Contains("请求 ID req_ok") && summary.Contains("最终响应 ID 未收到") && !summary.Contains("fixture-token"));
            Assert(BridgeTransferDiagnostic.SafeIdentifier("resp_line\nbody") is null && BridgeTransferDiagnostic.SafeIdentifier(new string('a', 151)) is null);
        });
        Test("old gateway diagnostic payloads retain unknown values instead of synthesizing zero successes", () =>
        {
            var view = ModelGatewayDiagnostics.FromStatus(new(0, 0, 0), null);
            Assert(view.Requests.Contains("未提供完整") && view.Requests.Contains("协议版本不同") && view.Compaction.Contains("未返回压缩指标") &&
                !view.Compaction.Contains("确认完成 0") && view.Latency.Contains("未返回") && ModelGatewayDiagnostics.Scope.Contains("重启后重新累计"));
        });
        Test("gateway diagnostic configuration projection uses quoted provider names and retains only known preferences", () =>
        {
            var config = Encoding.UTF8.GetBytes("model_provider='merchant.v1'\n[model_providers.'merchant.v1'.capabilities]\nremote_compaction='v2'\n");
            Assert(ModelGatewayDiagnostics.ReadPreference(config) == "v2");
            Assert(ModelGatewayDiagnostics.ReadPreference(Encoding.UTF8.GetBytes("[model_providers.openai.capabilities]\nremote_compaction='unknown-private-value'\n")) is null);
            Assert(ModelGatewayDiagnostics.ReadPreference(null) is null);
        });
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
    internal static BridgeMetrics Metrics() => new(0, 0, 0, 0, 0, 0, null, null, null, null, 0, 0, null, null, null, null, null);
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
