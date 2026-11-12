using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

public sealed record GatewayStartupSettings(bool ModelOnDockStart = false, bool MobileOnDockStart = false);

// Dock preferences never modify the Codex connection draft or mobile credentials.
public sealed class GatewayStartupStore
{
    public string SettingsPath { get; }

    public GatewayStartupStore(string codexHome, string? dataRoot = null)
    {
        var home = Path.GetFullPath(codexHome).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(home))).ToLowerInvariant();
        SettingsPath = Path.Combine(dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexConfig", "gateways"), identity, "startup.json");
    }

    public GatewayStartupSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        try
        {
            if (new FileInfo(SettingsPath).Length > 4096) throw new InvalidDataException();
            using var document = JsonDocument.Parse(File.ReadAllBytes(SettingsPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
            bool Read(string name) => !document.RootElement.TryGetProperty(name, out var value) ? false
                : value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new InvalidDataException();
            return new(Read(nameof(GatewayStartupSettings.ModelOnDockStart)), Read(nameof(GatewayStartupSettings.MobileOnDockStart)));
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        { throw new InvalidDataException("网关自动启动设置无法读取。请检查 Dock 的网关设置文件，自动启动已跳过。", error); }
    }

    public void Save(GatewayStartupSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                JsonSerializer.Serialize(stream, settings, new JsonSerializerOptions { WriteIndented = true });
            File.Move(temporary, SettingsPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
