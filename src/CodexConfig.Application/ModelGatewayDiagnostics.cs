using CodexConfig.Core;

namespace CodexConfig.Application;

// Presentation is shared by both shells. Only counters and fixed labels leave
// this projection; upstream error text, request IDs and response bodies do not.
public sealed record ModelGatewayDiagnostics(string Requests, string Configuration, string Compaction, string Latency, string Upstream)
{
    public const string Scope = "仅统计经过此模型网关的本次进程流量，重启后重新累计；直连及其他进程不在统计范围内。不保存聊天或压缩正文。";
    public string MetricsText => Configuration + "\n" + Compaction + "\n" + Latency + "\n" + Scope;
    public static string? ReadPreference(byte[]? config)
    {
        var document = new TomlDocument(config); var provider = document.String("model_provider") ?? "openai";
        return document.Get(TomlDocument.FormatPath(["model_providers", provider, "capabilities", "remote_compaction"])) as string is { } value && value is "v2" or "unsupported" ? value : null;
    }
    public static ModelGatewayDiagnostics FromStatus(BridgeStatus status, string? preference)
    {
        static string Count(long value) => value >= 0 ? value.ToString("N0") : "未提供";
        static string Milliseconds(double? value) => value is { } number && double.IsFinite(number) && number >= 0 ? $"{number:N0} ms" : "未测得";
        static string Protocol(string? value) => value switch { "v2" => "v2", "legacy" => "旧端点", "observed" => "观察到返回条目", _ => "协议未提供" };
        static string Transport(string? value) => value switch { "http" => "HTTP", "sse" => "SSE", "websocket" => "WebSocket", _ => "传输未提供" };
        static string Recent(DateTimeOffset? time, string? protocol, string? transport) => time is { } value
            ? value.LocalDateTime.ToString("g") + "（" + Protocol(protocol) + " / " + Transport(transport) + "）" : "未观察到";
        var requests = "本次进程请求 " + Count(status.Requests) + " 次" + (status.ProtocolVersion >= 6
            ? " · Fast " + Count(status.FastRequests) + " · Ultra Fast " + Count(status.UltrafastRequests) + " · 审核 " + Count(status.AutoReviewRequests)
            : "\n此网关版本未提供完整的速度与审核分类。") +
            (status.ProtocolVersion != ModelBridge.ProtocolVersion ? "\n当前后台与本程序协议版本不同，可手动重启后重新核对。" : "");
        var configuration = "压缩配置：" + (preference switch { "v2" => "文件允许 v2，服务支持需核对", "unsupported" => "文件选择普通摘要压缩", _ => "跟随执行端默认，能力未单独核实" });
        var upstream = status.LastUpstreamStatus is >= 100 and <= 599 ? "最近上游 HTTP " + status.LastUpstreamStatus : "最近上游 HTTP 状态：未提供";
        if (status.Metrics is not { } metrics) return new(requests, configuration, "此网关版本未返回压缩指标，不能据此判断是否成功。", "此网关版本未返回排队与耗时指标。", upstream);
        var compaction = "客户端压缩请求：旧端点 " + Count(metrics.LegacyCompactionRequests) + " 次 · v2 " + Count(metrics.V2Requests) + " 次（trigger " + Count(metrics.V2Triggers) + " 个）" +
            "\n服务返回压缩条目 " + Count(metrics.CompactionItems) + " 个 · 确认完成 " + Count(metrics.SuccessfulCompactions) + " 次" +
            "\n最近触发：" + Recent(metrics.LastTriggerAt, metrics.LastTriggerProtocol, metrics.LastTriggerTransport) +
            "\n最近确认完成：" + Recent(metrics.LastCompactionAt, metrics.LastCompactionProtocol, metrics.LastCompactionTransport) +
            (metrics.InspectionLimited != 0 ? "\n" + Count(metrics.InspectionLimited) + " 段数据无法完整检查，计数可能不全。" : "") +
            "\n请求触发、返回条目与成功完成分别计数；HTTP 200 不能单独证明压缩成功。";
        var latency = "连接：排队 " + Count(metrics.Waiting) + " · 活跃 " + Count(metrics.Active) +
            "\n最近一次：队列 " + Milliseconds(metrics.LastQueueMs) + " · 上游响应头 " + Milliseconds(metrics.LastHeadersMs) +
            "\n首数据 " + Milliseconds(metrics.LastFirstDataMs) + " · 总计 " + Milliseconds(metrics.LastDurationMs) +
            "\n结束：" + (metrics.LastStreamEnd switch { "completed" => "完整转发", "upstream_error" => "上游错误", "client_disconnected" => "客户端断开", "timeout" => "等待超时",
                "headers_timeout" => "响应头等待超时", "stream_idle_timeout" => "数据空闲超时",
                "peer_closed" or "peer_close" => "连接关闭", "handshake_failed" => "握手失败", "transport_error" => "传输异常", _ => "未提供或无法识别" });
        return new(requests, configuration, compaction, latency, upstream);
    }
}
