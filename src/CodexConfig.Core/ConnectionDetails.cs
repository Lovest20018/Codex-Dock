using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

// Dock-only labels live outside config.toml and never become provider/protocol options.
public sealed record ConnectionDetails(string Name = "", string Note = "")
{
    public const string FileName = "codex-dock-details.dat";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexDock.details.v1");

    public ConnectionDetails Normalize()
    {
        if (Name is null || Note is null) throw new ArgumentException("连接名称和备注不能为 null。");
        var name = Name.Trim(); var note = Note.Replace("\r\n", "\n").Trim();
        if (name.Length > 60 || name.Any(char.IsControl)) throw new ArgumentException("连接名称最多 60 个字符，不能包含换行。");
        if (note.Length > 500 || note.Any(c => char.IsControl(c) && c is not ('\n' or '\t'))) throw new ArgumentException("备注最多 500 个字符，请移除无效控制字符。");
        return new(name, note);
    }

    public byte[]? Protect()
    {
        var value = Normalize();
        if (value == new ConnectionDetails()) return null;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        try { return ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static ConnectionDetails Read(byte[]? encrypted)
    {
        if (encrypted is null) return new();
        byte[]? bytes = null;
        try
        {
            bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return (JsonSerializer.Deserialize<ConnectionDetails>(bytes) ?? throw new JsonException()).Normalize();
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException)
        { throw new InvalidDataException("连接名称和备注无法读取，请从备份恢复。", error); }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }
}
