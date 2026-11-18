using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexConfig.Core;

public sealed record ExperimentalFeature(string Name, string Stage, bool? Enabled, bool? DefaultEnabled, string Title, string Description)
{
    public string Path => TomlDocument.FormatPath(["features", Name]);
    public bool CanStage => ExperimentalFeatureClient.ValidName(Name) && Stage is "stable" or "beta" or "underDevelopment";
    public string StageLabel => Stage switch { "stable" => "稳定", "beta" => "Beta", "underDevelopment" => "开发中", "deprecated" => "已弃用", "removed" => "已移除", _ => "阶段未确认" };
}
public sealed record ExperimentalFeatureSnapshot(DateTimeOffset ReadAt, string RuntimeInfo, IReadOnlyList<ExperimentalFeature> Features);

// A fresh, short-lived execution-end snapshot. It never changes configuration,
// opens a conversation or claims to observe an already running desktop instance.
public sealed class ExperimentalFeatureClient
{
    public static bool ValidName(string value) => Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_]{0,99}$", RegexOptions.CultureInvariant);
    public async Task<ExperimentalFeatureSnapshot> ReadAsync(string executable, string home, string project, string? profile, CancellationToken cancellation = default)
    {
        using var process = await CodexDesktopRuntime.StartAsync(ConfigReadClient.StartInfo(executable, home, project, profile), cancellation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var stderr = DrainAsync(process.StandardError);
        using var cancel = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        try
        {
            var sequence = 0;
            async Task Send(object message)
            { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), timeout.Token); await process.StandardInput.FlushAsync(timeout.Token); }
            async Task<JsonElement> Request(string method, object parameters)
            {
                var id = ++sequence; await Send(new { id, method, @params = parameters });
                for (var lines = 0; lines < 10000; lines++)
                {
                    var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                    if (line is null) throw new NotSupportedException("所选执行端未返回实验功能列表；可能不支持此接口或所选 Profile。");
                    if (line.Length > 4 * 1024 * 1024) throw new InvalidDataException("实验功能响应超过读取上限。");
                    using var json = JsonDocument.Parse(line); var message = json.RootElement;
                    if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("id", out var responseId) || !responseId.TryGetInt32(out var number) || number != id) continue;
                    if (message.TryGetProperty("error", out _)) throw new NotSupportedException("所选执行端未能读取实验功能；支持状态未确认，未修改配置。");
                    return message.GetProperty("result").Clone();
                }
                throw new InvalidDataException("执行端未返回匹配的实验功能响应。");
            }
            var initialized = await Request("initialize", new { clientInfo = new { name = "codex_dock_feature_reader", version = "1.0" }, capabilities = new { experimentalApi = true } });
            await Send(new { method = "initialized" });
            var runtime = initialized.TryGetProperty("userAgent", out var info) ? Display(info, 300) : "";
            var features = new Dictionary<string, ExperimentalFeature>(StringComparer.Ordinal); var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            for (var page = 0; page < 20; page++)
            {
                var result = await Request("experimentalFeature/list", new { cursor, limit = 100 });
                var parsed = ParsePage(result);
                foreach (var feature in parsed.Features)
                    if (!features.TryAdd(feature.Name, feature)) throw new InvalidDataException("实验功能列表包含重复名称，请重新读取。");
                if (features.Count > 2000) throw new InvalidDataException("实验功能列表超过读取上限。");
                if (parsed.NextCursor is not { } next) return new(DateTimeOffset.UtcNow, runtime, Array.AsReadOnly(features.Values.OrderBy(feature => feature.Name, StringComparer.Ordinal).ToArray()));
                if (!cursors.Add(next)) throw new InvalidDataException("实验功能分页游标重复，未采用不完整列表。");
                cursor = next;
            }
            throw new InvalidDataException("实验功能分页超过读取上限，未采用不完整列表。");
        }
        catch (JsonException) { throw new InvalidDataException("执行端的实验功能响应格式不受支持。"); }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(); await stderr;
        }
    }

    public static (IReadOnlyList<ExperimentalFeature> Features, string? NextCursor) ParsePage(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 100)
            throw new InvalidDataException("实验功能列表缺少有效的 data 数组。");
        var features = new List<ExperimentalFeature>(); var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() is not { Length: > 0 and <= 200 } nameText || nameText.Any(char.IsControl))
                throw new InvalidDataException("实验功能名称格式不受支持。");
            var key = name.GetString()!;
            if (!names.Add(key)) throw new InvalidDataException("实验功能列表包含重复名称。");
            var stage = item.TryGetProperty("stage", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            if (stage is not ("stable" or "beta" or "underDevelopment" or "deprecated" or "removed")) stage = "unknown";
            bool? Flag(string property) => item.TryGetProperty(property, out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False ? flag.GetBoolean() : null;
            features.Add(new(key, stage, Flag("enabled"), Flag("defaultEnabled"), item.TryGetProperty("displayName", out var title) ? Display(title, 200) : "",
                item.TryGetProperty("description", out var description) ? Display(description, 2000) : ""));
        }
        string? next = null;
        if (response.TryGetProperty("nextCursor", out var cursor) && cursor.ValueKind != JsonValueKind.Null)
        {
            if (cursor.ValueKind != JsonValueKind.String || cursor.GetString() is not { Length: > 0 and <= 1000 } token || token.Any(char.IsControl))
                throw new InvalidDataException("实验功能分页游标无效。");
            next = token;
        }
        return (features.AsReadOnly(), next);
    }
    private static string Display(JsonElement value, int limit) => value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= limit && !text.Any(c => char.IsControl(c) && c is not ('\n' or '\t')) ? text : "";
    private static async Task DrainAsync(StreamReader input) { var buffer = new char[4096]; while (await input.ReadAsync(buffer) > 0) { } }
}
