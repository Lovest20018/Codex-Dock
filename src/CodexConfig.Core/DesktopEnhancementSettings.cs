using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexConfig.Core;

public sealed record DesktopEnhancementBinding(string Executable, string UserDataDirectory, int ProcessId, long Started,
    int Port, string SnapshotHash, string PlanHash);
public sealed record DesktopEnhancementPreference(bool Enabled = false, DesktopEnhancementBinding? Binding = null);

// No credentials or command lines are persisted: only the opt-in and a previously confirmed local instance.
public sealed class DesktopEnhancementSettings
{
    public string SettingsPath { get; }
    public DesktopEnhancementSettings(string home, string? root = null) => SettingsPath = Path.Combine(
        Path.GetDirectoryName(new GatewayStartupStore(home, root).SettingsPath)!, "desktop-enhancements.json");
    public DesktopEnhancementPreference Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        try
        {
            if (new FileInfo(SettingsPath).Length > 16384) throw new InvalidDataException();
            var settings = JsonSerializer.Deserialize<DesktopEnhancementPreference>(File.ReadAllBytes(SettingsPath)) ?? throw new InvalidDataException();
            if (settings.Binding is { } binding && (!Path.IsPathFullyQualified(binding.Executable) || !Path.IsPathFullyQualified(binding.UserDataDirectory) ||
                binding.ProcessId <= 0 || binding.Started <= 0 || binding.Port is < 1024 or > 65535 ||
                !Hash(binding.SnapshotHash) || !Hash(binding.PlanHash))) throw new InvalidDataException();
            return settings;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
        { throw new InvalidDataException("自动接回设置无法读取，已跳过自动操作。请重新核对客户端增强设置。", error); }
    }
    public void SetEnabled(bool enabled) => Save(Load() with { Enabled = enabled });
    public void Remember(DesktopEnhancementBinding binding) => Save(Load() with { Binding = binding });
    private void Save(DesktopEnhancementPreference settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var stage = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                JsonSerializer.Serialize(stream, settings, new JsonSerializerOptions { WriteIndented = true });
            File.Move(stage, SettingsPath, true);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    public static string SnapshotHash(ConfigSnapshot snapshot)
    {
        var config = new TomlDocument(snapshot.Config);
        var paths = TomlDocument.ValuePaths(config.Model).Order(StringComparer.Ordinal).ToDictionary(path => path, path => config.Get(path));
        const string runtimePipe = @"\\.\pipe\codex-computer-use-";
        if (paths.TryGetValue(CodexApplySnapshot.RuntimePipePath, out var pipe) && pipe is string name &&
            name.StartsWith(runtimePipe, StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(name[runtimePipe.Length..], "D", out _))
            paths[CodexApplySnapshot.RuntimePipePath] = "<Codex runtime pipe>";
        JsonNode? auth = snapshot.Auth is null ? null : JsonNode.Parse(snapshot.Auth.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? snapshot.Auth.AsSpan(3) : snapshot.Auth);
        if (auth is JsonObject data && OfficialCredentials.HasLogin(snapshot.Auth))
        {
            data.Remove("last_refresh");
            if (data["tokens"] is JsonObject tokens && tokens["account_id"]?.GetValue<string>() is { Length: > 0 } &&
                ReadIdentity(tokens["id_token"]?.GetValue<string>()) is { } identity)
            {
                tokens.Remove("access_token"); tokens.Remove("refresh_token"); tokens.Remove("id_token");
                tokens["local_identity"] = identity.Issuer + "\n" + identity.Subject;
            }
        }
        string? Bytes(byte[]? value) => value is null ? null : ManagedModelCatalogStore.Hash(value);
        var value = new { config = paths, auth, snapshot.AuthApiBaseUrl, bridge = Bytes(snapshot.Bridge), details = Bytes(snapshot.Details),
            catalogIndex = Bytes(snapshot.CatalogIndex), catalog = snapshot.Catalog is null ? null : new { snapshot.Catalog.Path, hash = Bytes(snapshot.Catalog.Content) } };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    }
    public static CodexDesktopTarget? Match(DesktopEnhancementBinding binding, IReadOnlyList<CodexDesktopTarget> targets) =>
        targets.SingleOrDefault(target => target.MainProcess is { } root && root.Id == binding.ProcessId && root.Started == binding.Started &&
            CodexDesktop.SamePath(target.Executable, binding.Executable) && target.UserDataDirectory is { } profile && CodexDesktop.SameDirectory(profile, binding.UserDataDirectory));
    private static (string Issuer, string Subject)? ReadIdentity(string? token)
    {
        try { return CodexApplySnapshot.AuthIdentity(token); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return null; }
    }
}
