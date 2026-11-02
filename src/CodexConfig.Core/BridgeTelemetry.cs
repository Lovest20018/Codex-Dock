using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

public sealed record BridgeMetrics(long LegacyCompactionRequests, long V2Requests, long V2Triggers,
    long CompactionItems, long SuccessfulCompactions, long InspectionLimited,
    DateTimeOffset? LastTriggerAt, DateTimeOffset? LastCompactionAt, string? LastCompactionProtocol,
    string? LastCompactionTransport, int Waiting, int Active, double? LastQueueMs,
    double? LastHeadersMs, double? LastFirstDataMs, double? LastDurationMs, string? LastStreamEnd)
{
    public string? LastTriggerProtocol { get; init; }
    public string? LastTriggerTransport { get; init; }
}

// Only counters, durations and fixed labels survive a request. No payloads are retained.
internal sealed class BridgeTelemetry
{
    private readonly object gate = new();
    private BridgeMetrics metrics = new(0, 0, 0, 0, 0, 0, null, null, null, null, 0, 0, null, null, null, null, null);
    internal BridgeMetrics Snapshot() { lock (gate) return metrics; }
    internal void Update(Func<BridgeMetrics, BridgeMetrics> update) { lock (gate) metrics = update(metrics); }
    internal Transfer Begin() { Update(m => m with { Waiting = m.Waiting + 1 }); return new(this); }

    internal sealed class Transfer(BridgeTelemetry owner) : IDisposable
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private bool entered, ended;
        private double? queue, headers, first, upstream;
        internal void Enter()
        {
            entered = true; queue = watch.Elapsed.TotalMilliseconds;
            owner.Update(m => m with { Waiting = m.Waiting - 1, Active = m.Active + 1 });
        }
        internal void Upstream() => upstream = watch.Elapsed.TotalMilliseconds;
        internal void Headers() => headers ??= watch.Elapsed.TotalMilliseconds - (upstream ?? queue ?? 0);
        internal void Data() => first ??= watch.Elapsed.TotalMilliseconds - (upstream ?? queue ?? 0);
        internal void End(string reason)
        {
            if (ended) return; ended = true;
            owner.Update(m => m with { Waiting = m.Waiting - (entered ? 0 : 1), Active = m.Active - (entered ? 1 : 0),
                LastQueueMs = queue, LastHeadersMs = headers, LastFirstDataMs = first, LastDurationMs = watch.Elapsed.TotalMilliseconds, LastStreamEnd = reason });
        }
        public void Dispose() => End("transport_error");
    }

    internal Observer Observe(bool legacy, string transport)
    {
        if (legacy) Update(m => m with { LegacyCompactionRequests = m.LegacyCompactionRequests + 1, LastTriggerAt = DateTimeOffset.UtcNow,
            LastTriggerProtocol = "legacy", LastTriggerTransport = transport });
        return new(this, legacy, transport);
    }

    internal sealed class Observer(BridgeTelemetry owner, bool legacy, string transport) : IDisposable
    {
        // Oversized/unparseable events are forwarded intact and explicitly reported as uninspected.
        private const int MaxEvent = 2 * 1024 * 1024;
        private readonly MemoryStream pending = new();
        private readonly HashSet<string> items = new(StringComparer.Ordinal), completed = new(StringComparer.Ordinal), responsesWithItems = new(StringComparer.Ordinal);
        private string activeResponse = "";
        private bool dropping, v2;
        private bool sse;
        private int newlineCount;

        internal void Request(byte[] bytes)
        {
            try
            {
                using var json = JsonDocument.Parse(bytes); var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;
                if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object) root = response;
                var triggers = root.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Array
                    ? input.EnumerateArray().Count(item => Type(item) == "compaction_trigger") : 0;
                if (triggers == 0) return;
                v2 = true;
                owner.Update(m => m with { V2Requests = m.V2Requests + 1, V2Triggers = m.V2Triggers + triggers, LastTriggerAt = DateTimeOffset.UtcNow,
                    LastTriggerProtocol = "v2", LastTriggerTransport = transport });
            }
            catch (JsonException) { Limited(); }
        }

        internal void SetSse(bool value)
        {
            sse = value; transport = value ? "sse" : "http";
            if (legacy || v2) owner.Update(m => m with { LastTriggerTransport = transport });
        }
        internal void Feed(ReadOnlySpan<byte> bytes)
        {
            foreach (var value in bytes)
            {
                if (!dropping)
                {
                    if (pending.Length >= MaxEvent) { pending.SetLength(0); dropping = true; Limited(); }
                    else pending.WriteByte(value);
                }
                if (!sse) continue;
                if (value == '\n') newlineCount++;
                else if (value != '\r') newlineCount = 0;
                if (newlineCount < 2) continue;
                if (!dropping) Event();
                pending.SetLength(0); dropping = false; newlineCount = 0;
            }
        }
        private void Event()
        {
            var data = string.Join('\n', Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length).Split('\n')
                .Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line[5..].TrimStart(' ').TrimEnd('\r')));
            if (data.Length > 0 && data != "[DONE]") Response(Encoding.UTF8.GetBytes(data));
        }
        internal void Finish()
        {
            if (!dropping && pending.Length > 0) { if (sse) Event(); else Response(pending.ToArray()); }
            pending.SetLength(0); dropping = false; newlineCount = 0;
        }
        internal void Response(byte[] bytes)
        {
            if (bytes.Length > MaxEvent) { Limited(); return; }
            try
            {
                using var json = JsonDocument.Parse(bytes); var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;
                var type = Type(root);
                var response = root.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.Object ? r : root;
                var id = Text(response, "id") ?? Text(root, "response_id") ?? activeResponse;
                if (type == "response.created") activeResponse = id;
                if (type == "response.output_item.done" && root.TryGetProperty("item", out var item))
                    Item(item, id, root.TryGetProperty("output_index", out var index) ? index.ToString() : "0");
                var terminal = type == "response.completed" || type is null && root.TryGetProperty("output", out _) && (legacy || Text(root, "status") == "completed");
                if (!terminal) return;
                if (response.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
                {
                    var position = 0;
                    foreach (var entry in output.EnumerateArray()) Item(entry, id, (position++).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                if (Text(response, "status") is not (null or "completed") || response.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null) return;
                if (!responsesWithItems.Contains(id) || completed.Contains(id)) return;
                if (completed.Count >= 1024) { Limited(); return; }
                completed.Add(id);
                owner.Update(m => m with { SuccessfulCompactions = m.SuccessfulCompactions + 1, LastCompactionAt = DateTimeOffset.UtcNow,
                    LastCompactionProtocol = v2 ? "v2" : legacy ? "legacy" : "observed", LastCompactionTransport = transport });
            }
            catch (JsonException) { Limited(); }
        }
        private void Item(JsonElement item, string response, string index)
        {
            if (Type(item) != "compaction") return;
            var key = response + "/" + (Text(item, "id") ?? index);
            if (items.Contains(key)) return;
            if (items.Count >= 1024) { Limited(); return; }
            items.Add(key); responsesWithItems.Add(response);
            owner.Update(m => m with { CompactionItems = m.CompactionItems + 1 });
        }
        private void Limited() => owner.Update(m => m with { InspectionLimited = m.InspectionLimited + 1 });
        private static string? Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private static string? Type(JsonElement element) => Text(element, "type");
        public void Dispose() { pending.Dispose(); items.Clear(); completed.Clear(); responsesWithItems.Clear(); }
    }
}
