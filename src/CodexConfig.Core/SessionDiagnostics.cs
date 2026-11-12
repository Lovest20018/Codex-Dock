using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

public sealed record SessionDiagnosticFile(string Name, string Path, DateTime UpdatedAt);
public sealed record SessionObservation(string File, string? Model, string? Reasoning, string? ServiceTier, string? Reviewer,
    DateTimeOffset? ObservedAt, string? ReviewStatus, bool ContainsConfigurationUpdate);

// Read only a user-selected rollout from this home, retaining whitelisted metadata, never conversation text.
public static class SessionDiagnostics
{
    public static IReadOnlyList<SessionDiagnosticFile> List(string home)
    {
        var root = Path.Combine(Path.GetFullPath(home), "sessions");
        if (!Directory.Exists(root)) return [];
        var files = new List<SessionDiagnosticFile>();
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0));
        var directories = 0;
        while (pending.Count > 0 && directories++ < 600)
        {
            var (path, depth) = pending.Pop();
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "rollout-*.jsonl").Take(1000))
                if (!File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) files.Add(new(Path.GetFileName(file), file, File.GetLastWriteTimeUtc(file)));
            if (depth < 3) foreach (var directory in Directory.EnumerateDirectories(path).OrderDescending().Take(40)) pending.Push((directory, depth + 1));
        }
        return files.OrderByDescending(file => file.UpdatedAt).Take(30).ToArray();
    }
    public static SessionObservation Read(string home, string path)
    {
        var root = Path.GetFullPath(Path.Combine(home, "sessions")); path = Path.GetFullPath(path);
        if (!Path.GetRelativePath(root, path).StartsWith("..", StringComparison.Ordinal) && Path.GetFileName(path).StartsWith("rollout-", StringComparison.Ordinal) && path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) { }
        else throw new InvalidDataException("请选择当前配置目录中的聊天记录。");
        for (var check = path; check.Length >= root.Length; check = Path.GetDirectoryName(check)!)
        {
            if (File.GetAttributes(check).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("聊天路径包含链接，未读取。");
            if (check.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // The current tail is enough for recent settings. No recursive parsing of prompts or tool arguments.
        var tail = stream.Length > 8 * 1024 * 1024;
        if (tail) stream.Seek(-8 * 1024 * 1024, SeekOrigin.End);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 8192);
        if (tail) reader.ReadLine();
        string? model = null, reasoning = null, tier = null, reviewer = null, reviewStatus = null;
        DateTimeOffset? time = null; var updates = false;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 1024 * 1024) continue;
            try
            {
                using var json = JsonDocument.Parse(line); var item = json.RootElement;
                if (!item.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) continue;
                string? Value(JsonElement source, string name) => source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (Value(item, "type") == "event_msg" && Value(payload, "type") == "thread_settings_applied" && payload.TryGetProperty("thread_settings", out var settings))
                {
                    model = Value(settings, "model"); reasoning = Value(settings, "reasoning_effort"); tier = Value(settings, "service_tier"); reviewer = Value(settings, "approvals_reviewer");
                    if (DateTimeOffset.TryParse(Value(item, "timestamp"), out var stamp)) time = stamp;
                }
                else if (Value(item, "type") == "turn_context")
                {
                    model = Value(payload, "model") ?? model; reasoning = Value(payload, "effort") ?? reasoning; reviewer = Value(payload, "approvals_reviewer") ?? reviewer;
                    if (DateTimeOffset.TryParse(Value(item, "timestamp"), out var stamp)) time = stamp;
                }
                else if (Value(item, "type") == "response_item" && Value(payload, "type") == "configuration_update" && payload.TryGetProperty("reasoning", out var update))
                { reasoning = Value(update, "effort") ?? reasoning; updates = true; }
                else if (Value(item, "type") == "event_msg" && Value(payload, "type") == "auto_approval_review_completed") reviewStatus = Value(payload, "status");
            }
            catch (JsonException) { } // A partial final line is normal while the chat is active.
        }
        return new(Path.GetFileName(path), Clean(model), Clean(reasoning), Clean(tier), Clean(reviewer), time, Clean(reviewStatus), updates);
    }
    private static string? Clean(string? value) => value is { Length: > 0 and <= 200 } && !value.Any(char.IsControl) ? value : null;
}
