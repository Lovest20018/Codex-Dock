using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexConfig.Core;

public sealed record CodexApplyComparison(IReadOnlyList<string> Changes)
{
    public bool CanContinue => Changes.Count == 0;
    public IReadOnlyList<string> RuntimeChanges { get; init; } = [];
}

public sealed class CodexApplyConflictException(IReadOnlyList<string> changes)
    : IOException("本地配置在确认后发生变化：" + string.Join("、", changes) + "。请读取最新配置并核对后继续。")
{
    public IReadOnlyList<string> Changes { get; } = changes;
}

// Share meaningful-change detection across launch, save, refresh and recovery.
// Writers rebase their reviewed edits onto the latest bytes before the final transaction check.
public static class CodexApplySnapshot
{
    internal const string RuntimePipePath = "mcp_servers.node_repl.env.SKY_CUA_NATIVE_PIPE_DIRECTORY";
    public static CodexApplyComparison Compare(ConfigSnapshot expected, ConfigSnapshot current)
    {
        var changes = new List<string>();
        var runtimeChanges = new List<string>();
        if (!string.Equals(expected.Home, current.Home, StringComparison.OrdinalIgnoreCase)) changes.Add("配置目录");
        var before = new TomlDocument(expected.Config).Model;
        var after = new TomlDocument(current.Config).Model;
        foreach (var path in TomlDocument.ValuePaths(before).Concat(TomlDocument.ValuePaths(after)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var previous = TomlDocument.ValueAt(before, path); var next = TomlDocument.ValueAt(after, path);
            if (Equivalent(JsonSerializer.SerializeToNode(previous), JsonSerializer.SerializeToNode(next))) continue;
            // The desktop rotates this local named-pipe endpoint on startup. It is
            // runtime state, not a user setting. Do not exempt other MCP environment values.
            if (path == RuntimePipePath && NativePipe(previous) && NativePipe(next))
                runtimeChanges.Add(path);
            else changes.Add("config.toml · " + path);
        }
        if (!SameAuth(expected.Auth, current.Auth)) changes.Add("auth.json · 登录或凭据");
        if (expected.AuthApiBaseUrl != current.AuthApiBaseUrl) changes.Add("账号服务环境变量");
        if (!Same(expected.Bridge, current.Bridge)) changes.Add("加密模型连接");
        if (!Same(expected.Details, current.Details)) changes.Add("连接资料与站点确认");
        if (!Same(expected.CatalogIndex, current.CatalogIndex)) changes.Add("模型目录管理记录");
        if (expected.Catalog is null ? current.Catalog is not null : !expected.Catalog.Same(current.Catalog)) changes.Add("模型能力目录");
        return new(changes) { RuntimeChanges = runtimeChanges };
    }

    public static void RequireCompatible(ConfigSnapshot expected, ConfigSnapshot current)
    {
        var comparison = Compare(expected, current);
        if (!comparison.CanContinue) throw new CodexApplyConflictException(comparison.Changes);
    }

    internal static bool Same(byte[]? left, byte[]? right) => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
    internal static bool SameConfig(byte[]? left, byte[]? right) => Compare(new("", left, null, null), new("", right, null, null)).CanContinue;
    internal static byte[]? RebaseConfig(byte[]? baseline, byte[]? desired, byte[]? current)
    {
        if (Same(baseline, current)) return desired;
        if (Same(baseline, desired)) return current;
        if (desired is null) return null;
        var before = new TomlDocument(baseline).Model; var after = new TomlDocument(desired).Model;
        var latest = new TomlDocument(current);
        foreach (var path in TomlDocument.ValuePaths(before).Concat(TomlDocument.ValuePaths(after)).Distinct(StringComparer.Ordinal))
        {
            var oldValue = TomlDocument.ValueAt(before, path); var newValue = TomlDocument.ValueAt(after, path);
            if (Equivalent(JsonSerializer.SerializeToNode(oldValue), JsonSerializer.SerializeToNode(newValue))) continue;
            if (path == RuntimePipePath && NativePipe(oldValue) && NativePipe(latest.Get(path)) &&
                !Equals(oldValue, latest.Get(path)) && !Equals(newValue, latest.Get(path)))
                throw new InvalidOperationException("正在编辑的运行管道已由 Codex 更新，请重新读取后核对。");
            if (newValue is null) latest.Remove(path); else latest.SetRaw(path, ConfigSchema.Literal(newValue));
        }
        return latest.Bytes;
    }

    internal static byte[]? PreserveRuntimePipe(byte[]? desired, byte[]? current)
    {
        if (desired is null || current is null) return desired;
        var document = new TomlDocument(desired); var pipe = new TomlDocument(current).Get(RuntimePipePath);
        if (NativePipe(document.Get(RuntimePipePath)) && NativePipe(pipe) && !Equals(document.Get(RuntimePipePath), pipe)) document.Set(RuntimePipePath, (string)pipe!);
        return document.Bytes;
    }

    internal static byte[]? RebaseAuth(byte[]? baseline, byte[]? desired, byte[]? current)
    {
        if (Same(baseline, current)) return desired;
        if (Same(baseline, desired) || SameAuth(baseline, desired)) return current;
        if (OfficialCredentials.HasLogin(baseline) && OfficialCredentials.HasLogin(current) && desired is not null &&
            SameAuth(OfficialCredentials.ForMixed(baseline), desired)) return OfficialCredentials.ForMixed(current);
        return desired;
    }

    internal static byte[]? PreserveFreshLogin(byte[]? desired, byte[]? current) => SameAuth(desired, current) ? current : desired;
    private static bool Equivalent(JsonNode? left, JsonNode? right) => JsonNode.DeepEquals(left, right);
    private static bool NativePipe(object? value)
    {
        const string prefix = @"\\.\pipe\codex-computer-use-";
        return value is string path && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParseExact(path[prefix.Length..], "D", out _);
    }

    internal static bool SameAuth(byte[]? before, byte[]? after)
    {
        if (Same(before, after)) return true;
        if (before is null || after is null) return false;
        try
        {
            JsonNode? Parse(byte[] bytes) => JsonNode.Parse(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? bytes.AsSpan(3) : bytes);
            var oldAuth = Parse(before); var newAuth = Parse(after);
            if (Equivalent(oldAuth, newAuth)) return true;
            if (oldAuth is not JsonObject left || newAuth is not JsonObject right ||
                !OfficialCredentials.HasLogin(before) || !OfficialCredentials.HasLogin(after)) return false;
            // A timestamp alone is not a credential or account change.
            left.Remove("last_refresh"); right.Remove("last_refresh");
            if (Equivalent(left, right)) return true;
            if (left["tokens"] is not JsonObject oldTokens || right["tokens"] is not JsonObject newTokens) return false;
            var account = oldTokens["account_id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(account) || account != newTokens["account_id"]?.GetValue<string>()) return false;
            var oldIdentity = Identity(oldTokens["id_token"]?.GetValue<string>());
            if (oldIdentity is null || oldIdentity != Identity(newTokens["id_token"]?.GetValue<string>())) return false;
            foreach (var name in new[] { "access_token", "refresh_token", "id_token" })
            { oldTokens.Remove(name); newTokens.Remove(name); }
            return Equivalent(left, right);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return false; }
    }

    // This only associates two local credential snapshots. It does not validate a login or entitlement.
    private static (string Issuer, string Subject)? Identity(string? token)
    {
        if (token is null || token.Length > 128 * 1024) return null;
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
        var root = doc.RootElement;
        var issuer = root.TryGetProperty("iss", out var iss) && iss.ValueKind == JsonValueKind.String ? iss.GetString() : null;
        var subject = root.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String ? sub.GetString() : null;
        return string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) ? null : (issuer, subject);
    }
}
