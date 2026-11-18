using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

public sealed record BridgeTransferDiagnostic(DateTimeOffset At, string Phase, string Transport, string? RequestId, string? FinalResponseId)
{
    public static string? SafeIdentifier(string? value) => value is { Length: > 0 and <= 150 } &&
        !value.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("eyJ", StringComparison.Ordinal) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':') ? value : null;
}

public sealed record BridgeMetrics(long LegacyCompactionRequests, long V2Requests, long V2Triggers,
    long CompactionItems, long SuccessfulCompactions, long InspectionLimited,
    DateTimeOffset? LastTriggerAt, DateTimeOffset? LastCompactionAt, string? LastCompactionProtocol,
    string? LastCompactionTransport, int Waiting, int Active, double? LastQueueMs,
    double? LastHeadersMs, double? LastFirstDataMs, double? LastDurationMs, string? LastStreamEnd)
{
    public string? LastTriggerProtocol { get; init; }
    public string? LastTriggerTransport { get; init; }
    public BridgeTransferDiagnostic? LastTransfer { get; init; }
}

// Only counters, timings and bounded diagnostic metadata survive a request.
internal sealed class BridgeTelemetry
{
    private readonly object gate = new();
    private BridgeMetrics metrics = new(0, 0, 0, 0, 0, 0, null, null, null, null, 0, 0, null, null, null, null, null);
    internal BridgeMetrics Snapshot() { lock (gate) return metrics; }
    internal void Update(Func<BridgeMetrics, BridgeMetrics> update) { lock (gate) metrics = update(metrics); }
    internal Transfer Begin(string? transport = null) { Update(m => m with { Waiting = m.Waiting + 1 }); return new(this, transport); }

    internal sealed class Transfer(BridgeTelemetry owner, string? transport) : IDisposable
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly object correlation = new();
        private bool entered, ended;
        private double? queue, headers, first, upstream;
        private string? requestId, finalResponseId, terminalPhase;
        private int pendingSocketResponses;
        private readonly HashSet<string> socketTerminals = new(StringComparer.Ordinal);
        internal void Enter()
        {
            entered = true; queue = watch.Elapsed.TotalMilliseconds;
            owner.Update(m => m with { Waiting = m.Waiting - 1, Active = m.Active + 1 });
        }
        internal void Upstream() => upstream = watch.Elapsed.TotalMilliseconds;
        internal void Headers() => headers ??= watch.Elapsed.TotalMilliseconds - (upstream ?? queue ?? 0);
        internal void Data() => first ??= watch.Elapsed.TotalMilliseconds - (upstream ?? queue ?? 0);
        internal void HeaderMetadata(bool sse, string? id)
        {
            if (sse && transport == "http") transport = "sse";
            requestId = BridgeTransferDiagnostic.SafeIdentifier(id);
        }
        internal void NewResponse(bool socketRequest)
        {
            lock (correlation)
            {
                if (socketRequest) pendingSocketResponses++;
                finalResponseId = terminalPhase = null;
            }
        }
        internal void TerminalResponse(string phase, string? id)
        {
            if (transport is null) return;
            lock (correlation)
            {
                var safe = BridgeTransferDiagnostic.SafeIdentifier(id);
                if (transport == "websocket" && safe is not null && socketTerminals.Count < 1024 && !socketTerminals.Add(safe)) return;
                terminalPhase = phase;
                finalResponseId = phase == "completed" ? safe : null;
                if (transport != "websocket") return;
                if (pendingSocketResponses > 0) pendingSocketResponses--;
                var diagnostic = Diagnostic(phase, finalResponseId);
                owner.Update(m => m with { LastTransfer = diagnostic });
            }
        }
        private BridgeTransferDiagnostic Diagnostic(string phase, string? id) => new(DateTimeOffset.UtcNow, phase, transport!, requestId, id);
        internal void End(string reason)
        {
            if (ended) return; ended = true;
            BridgeTransferDiagnostic? diagnostic;
            lock (correlation)
                diagnostic = transport is null ? null : Diagnostic(reason == "completed" ? terminalPhase ?? "incomplete" : reason,
                    pendingSocketResponses == 0 ? finalResponseId : null);
            owner.Update(m => m with { Waiting = m.Waiting - (entered ? 0 : 1), Active = m.Active - (entered ? 1 : 0),
                LastQueueMs = queue, LastHeadersMs = headers, LastFirstDataMs = first, LastDurationMs = watch.Elapsed.TotalMilliseconds,
                LastStreamEnd = reason, LastTransfer = diagnostic ?? m.LastTransfer });
        }
        public void Dispose() => End("transport_error");
    }

    internal Observer Observe(bool legacy, string transport, Transfer? transfer = null, Func<string?, string?>? redactId = null)
    {
        if (legacy) Update(m => m with { LegacyCompactionRequests = m.LegacyCompactionRequests + 1, LastTriggerAt = DateTimeOffset.UtcNow,
            LastTriggerProtocol = "legacy", LastTriggerTransport = transport });
        return new(this, legacy, transport, transfer, redactId);
    }

    internal sealed class Observer(BridgeTelemetry owner, bool legacy, string transport, Transfer? transfer, Func<string?, string?>? redactId) : IDisposable
    {
        // Oversized/unparseable events are forwarded intact and explicitly reported as uninspected.
        private const int MaxEvent = 2 * 1024 * 1024;
        private readonly MemoryStream pending = new();
        private readonly HashSet<string> items = new(StringComparer.Ordinal), completed = new(StringComparer.Ordinal), responsesWithItems = new(StringComparer.Ordinal);
        private string activeResponse = "";
        private bool dropping, v2;
        private bool sse, afterCarriageReturn, lineHasData;

        internal void Request(byte[] bytes)
        {
            try
            {
                using var json = JsonDocument.Parse(bytes); var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;
                if (transport == "websocket" && Type(root) == "response.create") transfer?.NewResponse(socketRequest: true);
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
                if (sse)
                {
                    if (value == '\n' && afterCarriageReturn) { afterCarriageReturn = false; continue; }
                    afterCarriageReturn = value == '\r';
                    if (value is (byte)'\r' or (byte)'\n')
                    {
                        if (lineHasData) Append((byte)'\n');
                        else
                        {
                            if (!dropping && pending.Length > 0) Event();
                            pending.SetLength(0); dropping = false;
                        }
                        lineHasData = false;
                        continue;
                    }
                    lineHasData = true;
                }
                Append(value);
            }
        }
        private void Append(byte value)
        {
            if (dropping) return;
            if (pending.Length < MaxEvent) { pending.WriteByte(value); return; }
            pending.SetLength(0); dropping = true; Limited();
        }
        private void Event()
        {
            var data = string.Join('\n', Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length).Split('\n')
                .Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line[5..].TrimStart(' ').TrimEnd('\r')));
            if (data.Length > 0 && data != "[DONE]") Response(Encoding.UTF8.GetBytes(data));
        }
        internal void Finish()
        {
            if (!dropping && pending.Length > 0) { if (sse) Limited(); else Response(pending.ToArray()); }
            pending.SetLength(0); dropping = afterCarriageReturn = lineHasData = false;
        }
        internal void Response(byte[] bytes)
        {
            if (bytes.Length > MaxEvent) { Limited(); return; }
            try
            {
                using var json = JsonDocument.Parse(bytes); var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;
                var type = Type(root);
                var nested = root.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.Object;
                var response = nested ? r : root;
                var id = Text(response, "id") ?? Text(root, "response_id") ?? activeResponse;
                if (type == "response.created")
                {
                    activeResponse = id;
                    if (transport != "websocket") transfer?.NewResponse(socketRequest: false);
                }
                var validStatus = (!response.TryGetProperty("status", out var status) || status.ValueKind == JsonValueKind.String && status.GetString() == "completed") &&
                    (!response.TryGetProperty("error", out var error) || error.ValueKind == JsonValueKind.Null);
                if (type == "response.completed" && nested || type is null && transport == "http" && Text(root, "status") == "completed")
                {
                    var finalId = Text(response, "id");
                    transfer?.TerminalResponse(validStatus ? "completed" : "upstream_error", redactId is null ? finalId : redactId(finalId));
                }
                else if (type is "response.failed" or "response.incomplete" or "error" || type is null && Text(root, "status") is "failed" or "incomplete")
                {
                    var failedId = Text(response, "id");
                    transfer?.TerminalResponse(type == "response.incomplete" || Text(response, "status") == "incomplete" ? "incomplete" : "upstream_error",
                        redactId is null ? failedId : redactId(failedId));
                }
                if (type == "response.output_item.done" && root.TryGetProperty("item", out var item))
                    Item(item, id, root.TryGetProperty("output_index", out var index) ? index.ToString() : "0");
                var terminal = type == "response.completed" || type is null && root.TryGetProperty("output", out _) && (legacy || Text(root, "status") == "completed");
                if (!terminal) return;
                if (response.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
                {
                    var position = 0;
                    foreach (var entry in output.EnumerateArray()) Item(entry, id, (position++).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                if (!validStatus) return;
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
