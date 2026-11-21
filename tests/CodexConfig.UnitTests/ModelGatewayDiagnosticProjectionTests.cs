using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;
using Xunit;

namespace CodexConfig.UnitTests;

public sealed class ModelGatewayDiagnosticProjectionTests
{
    [Fact(DisplayName = "gateway diagnostic presentation separates configured support trigger items and confirmed completion")]
    public void ReportsConfigurationTriggersAndCompletions()
    {
        var status = new BridgeStatus(8, 2, 1, ModelBridge.ProtocolVersion, LastUpstreamStatus: 200,
            Metrics: Metrics() with { LegacyCompactionRequests = 3, V2Requests = 2, V2Triggers = 4, CompactionItems = 1, SuccessfulCompactions = 0 }, UltrafastRequests: 1);
        var view = ModelGatewayDiagnostics.FromStatus(status, "v2");
        Assert.True(view.Requests.Contains("8 次") && view.Requests.Contains("Fast 2") && view.Requests.Contains("Ultra Fast 1"));
        Assert.True(view.Configuration.Contains("文件允许 v2") && view.Compaction.Contains("旧端点 3 次") && view.Compaction.Contains("v2 2 次（trigger 4 个）") &&
            view.Compaction.Contains("压缩条目 1 个 · 确认完成 0 次") && view.Compaction.Contains("最近确认完成：未观察到") && view.Upstream.Contains("200") && view.Compaction.Contains("HTTP 200 不能"));
    }

    [Fact(DisplayName = "gateway diagnostics label the most recent trigger and successful completion independently")]
    public void LabelsLatestTriggerAndCompletionIndependently()
    {
        var metrics = Metrics() with { LastTriggerAt = DateTimeOffset.UtcNow, LastTriggerProtocol = "legacy", LastTriggerTransport = "http",
            LastCompactionAt = DateTimeOffset.UtcNow.AddMinutes(-1), LastCompactionProtocol = "v2", LastCompactionTransport = "websocket", InspectionLimited = 2,
            LastQueueMs = 7, LastHeadersMs = 21, LastFirstDataMs = 34, LastDurationMs = 55, LastStreamEnd = "completed", Waiting = 1, Active = 2 };
        var view = ModelGatewayDiagnostics.FromStatus(new(5, 0, 0, ModelBridge.ProtocolVersion, Metrics: metrics), "unsupported");
        Assert.True(view.Compaction.Contains("旧端点 / HTTP") && view.Compaction.Contains("v2 / WebSocket") && view.Compaction.Contains("2 段数据无法完整检查"));
        Assert.True(view.Latency.Contains("排队 1 · 活跃 2") && view.Latency.Contains("7 ms") && view.Latency.Contains("55 ms") && view.Latency.Contains("完整转发") && view.Configuration.Contains("普通摘要"));
    }

    [Fact(DisplayName = "gateway diagnostic projection never publishes upstream messages identifiers or unknown labels")]
    public void RedactsPrivateOrUnknownDiagnostics()
    {
        var metrics = Metrics() with { LastTriggerAt = DateTimeOffset.UtcNow, LastTriggerProtocol = "fake-private-protocol", LastTriggerTransport = "fake-private-transport",
            LastCompactionAt = DateTimeOffset.UtcNow, LastCompactionProtocol = "fake-private-protocol", LastCompactionTransport = "fake-private-transport",
            LastStreamEnd = "fake-private-error", LastQueueMs = double.NaN, LastHeadersMs = double.PositiveInfinity, LastFirstDataMs = -1, V2Requests = -1 };
        var view = ModelGatewayDiagnostics.FromStatus(new(1, 0, 0, ModelBridge.ProtocolVersion, LastError: "fake-private-upstream-message",
            LastRequestId: "fake-private-request-id", LastUpstreamCode: "fake-private-code", LastRequestKind: "fake-private-kind", Metrics: metrics), "fake-private-preference");
        var text = JsonSerializer.Serialize(view); Assert.True(!text.Contains("fake-private") && !text.Contains("NaN") && !text.Contains("Infinity") &&
            view.Latency.Contains("未测得") && view.Latency.Contains("无法识别") && view.Compaction.Contains("v2 未提供 次"));
    }

    [Fact(DisplayName = "SSE diagnostic proves the final response only after a complete event and scopes it to its own request")]
    public void SseRequiresCompleteEventForItsOwnRequest()
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
        Assert.True(monitor.Snapshot().LastTransfer is { RequestId: "req_one", FinalResponseId: "resp_one", Transport: "sse", Phase: "client_disconnected" });
        two.Finish(); second.End("completed");
        Assert.True(monitor.Snapshot().LastTransfer is { RequestId: "req_two", FinalResponseId: null, Phase: "incomplete" });
        using var listing = monitor.Begin(); listing.End("completed");
        Assert.True(monitor.Snapshot().LastTransfer?.RequestId == "req_two");
    }

    [Fact(DisplayName = "WebSocket diagnostic separates consecutive responses and rejects failed or half-frame IDs")]
    public void WebSocketScopesEachCompletedResponse()
    {
        var monitor = new BridgeTelemetry(); using var transfer = monitor.Begin("websocket"); transfer.Enter(); transfer.HeaderMetadata(false, "req_upgrade");
        using var socket = monitor.Observe(false, "websocket", transfer);
        socket.Request("{\"type\":\"response.create\"}"u8.ToArray());
        socket.Feed("{\"type\":\"response.completed\",\"response\":{\"id\":\"resp_ws1\"}}"u8); socket.Finish();
        Assert.True(monitor.Snapshot().LastTransfer is { RequestId: "req_upgrade", FinalResponseId: "resp_ws1", Transport: "websocket", Phase: "completed" });
        socket.Request("{\"type\":\"response.create\"}"u8.ToArray());
        socket.Feed("{\"type\":\"response.failed\",\"response\":{\"id\":\"resp_failed\"}}"u8); socket.Finish();
        Assert.True(monitor.Snapshot().LastTransfer is { FinalResponseId: null, Phase: "upstream_error" });
        socket.Request("{\"type\":\"response.create\"}"u8.ToArray());
        socket.Feed("{\"type\":\"response.completed\",\"response\":{\"id\":\"resp_unsent_boundary\"}}"u8);
        transfer.End("peer_closed");
        Assert.True(monitor.Snapshot().LastTransfer is { FinalResponseId: null, Phase: "peer_closed" });
    }

    [Fact(DisplayName = "transfer summaries reject token-shaped or multiline values and label unknown response IDs")]
    public void TransferSummaryRejectsSensitiveOrMultilineIdentifiers()
    {
        var summary = ModelGatewayDiagnostics.FromStatus(new(2, 0, 0, Metrics: Metrics() with
        { LastTransfer = new(DateTimeOffset.UnixEpoch, "stream_idle_timeout", "http", "req_ok", "sk-fixture-token") }), null).TransferSummary;
        Assert.True(summary is not null && summary.Contains("数据空闲超时") &&
            summary.Contains("请求 ID req_ok") && summary.Contains("最终响应 ID 未收到") && !summary.Contains("fixture-token"));
        Assert.True(BridgeTransferDiagnostic.SafeIdentifier("resp_line\nbody") is null && BridgeTransferDiagnostic.SafeIdentifier(new string('a', 151)) is null);
    }

    [Fact(DisplayName = "old gateway diagnostic payloads retain unknown values instead of synthesizing zero successes")]
    public void OldStatusKeepsUnknownMetrics()
    {
        var view = ModelGatewayDiagnostics.FromStatus(new(0, 0, 0), null);
        Assert.True(view.Requests.Contains("未提供完整") && view.Requests.Contains("协议版本不同") && view.Compaction.Contains("未返回压缩指标") &&
            !view.Compaction.Contains("确认完成 0") && view.Latency.Contains("未返回") && ModelGatewayDiagnostics.Scope.Contains("重启后重新累计"));
    }

    [Fact(DisplayName = "gateway diagnostic configuration projection uses quoted provider names and retains only known preferences")]
    public void ReadsQuotedProviderPreferences()
    {
        var config = Encoding.UTF8.GetBytes("model_provider='merchant.v1'\n[model_providers.'merchant.v1'.capabilities]\nremote_compaction='v2'\n");
        Assert.True(ModelGatewayDiagnostics.ReadPreference(config) == "v2");
        Assert.True(ModelGatewayDiagnostics.ReadPreference(Encoding.UTF8.GetBytes("[model_providers.openai.capabilities]\nremote_compaction='unknown-private-value'\n")) is null);
        Assert.True(ModelGatewayDiagnostics.ReadPreference(null) is null);
    }

    private static BridgeMetrics Metrics() => new(0, 0, 0, 0, 0, 0, null, null, null, null, 0, 0, null, null, null, null, null);
}
