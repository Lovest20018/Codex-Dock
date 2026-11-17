using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

public sealed record SessionDiagnosticFile(string Name, string Path, DateTime UpdatedAt);
public sealed record SessionObservation(string File, string? Model, string? Reasoning, string? ServiceTier, string? Reviewer,
    DateTimeOffset? ObservedAt, string? ReviewStatus, bool ContainsConfigurationUpdate);

// Read only a user-selected rollout from this home, retaining whitelisted metadata, never conversation text.
public static class SessionDiagnostics
{
    public static IReadOnlyList<SessionDiagnosticFile> List(string home, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var root = Path.Combine(Path.GetFullPath(home), "sessions");
        if (!Directory.Exists(root)) return [];
        var files = new List<SessionDiagnosticFile>();
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0));
        var directories = 0;
        while (pending.Count > 0 && directories++ < 600)
        {
            cancellation.ThrowIfCancellationRequested();
            var (path, depth) = pending.Pop();
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "rollout-*.jsonl").Take(1000))
            {
                cancellation.ThrowIfCancellationRequested();
                if (!File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) files.Add(new(Path.GetFileName(file), file, File.GetLastWriteTimeUtc(file)));
            }
            if (depth < 3) foreach (var directory in Directory.EnumerateDirectories(path).OrderDescending().Take(40)) pending.Push((directory, depth + 1));
        }
        return files.OrderByDescending(file => file.UpdatedAt).Take(30).ToArray();
    }
    public static SessionObservation Read(string home, string path, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(Path.Combine(home, "sessions")); path = Path.GetFullPath(path);
        if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path).StartsWith("rollout-", StringComparison.Ordinal) && path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) { }
        else throw new InvalidDataException("请选择当前配置目录中的聊天记录。");
        for (var check = path; check.Length >= root.Length; check = Path.GetDirectoryName(check)!)
        {
            if (File.GetAttributes(check).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("聊天路径包含链接，未读取。");
            if (check.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // The current tail is enough for recent settings. No recursive parsing of prompts or tool arguments.
        var length = stream.Length; var count = (int)Math.Min(length, 8 * 1024 * 1024); var tail = length > count;
        stream.Seek(length - count, SeekOrigin.Begin);
        var bytes = new byte[count]; var read = 0;
        while (read < count)
        { cancellation.ThrowIfCancellationRequested(); var received = stream.Read(bytes, read, Math.Min(8192, count - read)); if (received == 0) break; read += received; }
        using var buffer = new MemoryStream(bytes, 0, read, false);
        using var reader = new StreamReader(buffer, Encoding.UTF8, true, 8192);
        if (tail) reader.ReadLine();
        string? model = null, reasoning = null, tier = null, reviewer = null, reviewStatus = null;
        DateTimeOffset? time = null; var updates = false;
        while (reader.ReadLine() is { } line)
        {
            cancellation.ThrowIfCancellationRequested();
            if (line.Length > 1024 * 1024) continue;
            try
            {
                using var json = JsonDocument.Parse(line); var item = json.RootElement;
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) continue;
                string? Value(JsonElement source, string name) => source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (Value(item, "type") == "event_msg" && Value(payload, "type") == "thread_settings_applied" && payload.TryGetProperty("thread_settings", out var settings) && settings.ValueKind == JsonValueKind.Object)
                {
                    model = Value(settings, "model"); reasoning = Value(settings, "reasoning_effort"); tier = Value(settings, "service_tier"); reviewer = Value(settings, "approvals_reviewer");
                    if (DateTimeOffset.TryParse(Value(item, "timestamp"), out var stamp)) time = stamp;
                }
                else if (Value(item, "type") == "turn_context")
                {
                    model = Value(payload, "model") ?? model; reasoning = Value(payload, "effort") ?? reasoning; reviewer = Value(payload, "approvals_reviewer") ?? reviewer;
                    if (DateTimeOffset.TryParse(Value(item, "timestamp"), out var stamp)) time = stamp;
                }
                else if (Value(item, "type") == "response_item" && Value(payload, "type") == "configuration_update" && payload.TryGetProperty("reasoning", out var update) && update.ValueKind == JsonValueKind.Object)
                { reasoning = Value(update, "effort") ?? reasoning; updates = true; }
                else if (Value(item, "type") == "event_msg" && Value(payload, "type") == "auto_approval_review_completed") reviewStatus = Value(payload, "status");
            }
            catch (JsonException) { } // A partial final line is normal while the chat is active.
        }
        return new(Path.GetFileName(path), Clean(model), Clean(reasoning), Clean(tier), Clean(reviewer), time, Clean(reviewStatus), updates);
    }
    private static string? Clean(string? value) => value is { Length: > 0 and <= 200 } && !value.Any(char.IsControl) ? value : null;
}
