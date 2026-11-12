using System.Text.Json;

namespace CodexConfig.Core;

public sealed class DesktopSpeedSettings
{
    public string SettingsPath { get; }
    public DesktopSpeedSettings(string home, string? root = null) => SettingsPath = Path.Combine(
        Path.GetDirectoryName(new GatewayStartupStore(home, root).SettingsPath)!, "desktop-speed.json");
    public bool Load()
    {
        if (!File.Exists(SettingsPath)) return false;
        if (new FileInfo(SettingsPath).Length > 4096) throw new InvalidDataException("速度兼容设置文件过大。");
        using var document = JsonDocument.Parse(File.ReadAllBytes(SettingsPath));
        var enabled = document.RootElement.GetProperty("enabled");
        return enabled.ValueKind is JsonValueKind.True or JsonValueKind.False ? enabled.GetBoolean()
            : throw new InvalidDataException("无法读取速度兼容设置。");
    }
    public void Save(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(new { enabled })); File.Move(temporary, SettingsPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static bool Eligible(ConfigStore store, ConfigSnapshot snapshot)
    {
        if (snapshot.Auth is null || snapshot.Config is null || snapshot.Bridge is not null || !string.IsNullOrEmpty(snapshot.AuthApiBaseUrl)) return false;
        using var auth = JsonDocument.Parse(snapshot.Auth);
        var root = auth.RootElement;
        if (!root.TryGetProperty("auth_mode", out var mode) || mode.GetString() != "apikey" ||
            root.TryGetProperty("tokens", out _) || root.TryGetProperty("personal_access_token", out _)) return false;
        var settings = store.ReadSettings(snapshot); var config = new TomlDocument(snapshot.Config);
        return settings.Mode == ConnectionMode.Direct && !settings.NeedsApiKeyMigration && settings.ApiKey.Length > 0 &&
            Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _) && (config.String("model_provider") ?? "openai") == "openai" &&
            config.String("profile") is null && config.String("forced_login_method") != "chatgpt";
    }
}
