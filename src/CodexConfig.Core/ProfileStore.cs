using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

public sealed record ProfileSnapshot(string Home, string Name, byte[]? Config);
public sealed record ProfilePlan(ProfileSnapshot Before, byte[] Config, string Preview, IReadOnlyList<string> Warnings);

// Profile files are independent, single-file edits with optimistic concurrency and encrypted undo.
public sealed class ProfileStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexDock.profile-backup.v1");
    public static void ValidateName(string name)
    {
        if (name.Length is 0 or > 64 || name.Any(c => !char.IsLetterOrDigit(c) && c is not ('_' or '-')) ||
            new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Profile 名称请使用 1–64 个文字、数字、下划线或连字符，避开 Windows 保留名称。");
    }
    public static string FilePath(string home, string name) { ValidateName(name); return Path.Combine(Path.GetFullPath(home), name + ".config.toml"); }
    public IReadOnlyList<string> List(string home) => !Directory.Exists(home) ? [] : Directory.EnumerateFiles(home, "*.config.toml", SearchOption.TopDirectoryOnly)
        .Select(path => Path.GetFileName(path)[..^12]).Where(Valid).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    private static bool Valid(string name) { try { ValidateName(name); return true; } catch (ArgumentException) { return false; } }
    public ProfileSnapshot Load(string home, string name)
    {
        var path = FilePath(home, name); byte[]? bytes = null;
        CheckLink(path);
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new IOException("Profile 文件过大。");
            bytes = File.ReadAllBytes(path); _ = new TomlDocument(bytes);
        }
        return new(Path.GetFullPath(home), name, bytes);
    }
    public ProfilePlan Plan(ProfileSnapshot before, IReadOnlyDictionary<string, string?> changes)
    {
        var document = new TomlDocument(before.Config);
        foreach (var (path, value) in changes)
        {
            ValidateOrdinary(path, value);
            if (value is null) document.Remove(path); else document.SetRaw(path, value);
        }
        var baseDocument = new TomlDocument(before.Config);
        var warnings = ConfigConstraints.ValidateChanges(baseDocument, document, ModelCapabilities.Read(before.Home, document));
        return new(before, document.Bytes, document.Masked(), warnings);
    }
    public static void ValidateOrdinary(string path, string? value)
    {
        var canonical = TomlDocument.FormatPath(TomlDocument.ParsePath(path));
        var forbidden = new[] { "profile", "profiles", "auth", "oauth", "model_provider", "model_providers", "openai_base_url", "chatgpt_base_url", "forced_login_method", "forced_chatgpt_workspace_id", "cli_auth_credentials_store", "mcp_servers", "permissions", "default_permissions", "sandbox_mode", "sandbox_workspace_write", "approval_policy", "approvals_reviewer" };
        if (TomlDocument.IsSensitivePath(canonical) || TomlDocument.ParsePath(canonical).Any(forbidden.Contains) || canonical == "features.enable_request_compression")
            throw new ArgumentException("此 Profile 编辑器只保存普通模型与行为设置，认证、连接和权限请在主配置中管理。");
        if (canonical == "model")
        {
            if (value is not null && (TomlDocument.ParseModel("value = " + value)["value"] is not string { Length: > 0 } model || model.Any(char.IsControl)))
                throw new ArgumentException("请填写有效的模型 ID。");
            return;
        }
        ConfigSchema.Instance.Validate(canonical, value, allowPreferences: true);
        if (value is not null)
        {
            // Also reject credentials nested inside an otherwise ordinary unknown value.
            var probe = TemplateRepository.New("Profile validation", [new(canonical == "model" ? "profile_model_validation" : canonical, value)]);
            _ = TemplateRepository.Validate(probe);
        }
    }
    public static bool CanEditPath(string path) { try { ValidateOrdinary(path, null); return true; } catch (ArgumentException) { return false; } }
    public string Save(ProfilePlan plan) => Write(plan.Before, plan.Config);
    public string? LatestBackup(string home, string name)
    {
        ValidateName(name); var directory = BackupDirectory(home);
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, name + "--*.profilebackup").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault() : null;
    }
    public string Restore(ProfileSnapshot current)
    {
        var path = LatestBackup(current.Home, current.Name) ?? throw new InvalidOperationException("此 Profile 尚无备份。");
        CheckLink(path);
        if (new FileInfo(path).Length > 24 * 1024 * 1024) throw new InvalidDataException("Profile 备份过大。");
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var backup = JsonSerializer.Deserialize<Backup>(plain) ?? throw new InvalidDataException("Profile 备份无效。");
            if (backup.Name != current.Name || Hash(current.Config) != backup.AfterHash) throw new InvalidOperationException("Profile 已有其他修改，不能直接恢复。");
            return Write(current, backup.Before);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private string Write(ProfileSnapshot before, byte[]? next)
    {
        var path = FilePath(before.Home, before.Name); var directory = BackupDirectory(before.Home); Directory.CreateDirectory(directory);
        using var writeLock = new FileStream(Path.Combine(directory, "profiles.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        void Unchanged() { if (Hash(Load(before.Home, before.Name).Config) != Hash(before.Config)) throw new InvalidOperationException("Profile 已被其他程序修改，请重新读取。"); }
        Unchanged();
        var backupPath = Path.Combine(directory, $"{before.Name}--{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.profilebackup");
        var plain = JsonSerializer.SerializeToUtf8Bytes(new Backup(before.Name, before.Config, Hash(next)));
        try
        {
            var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            using var backupFile = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            backupFile.Write(encrypted); backupFile.Flush(true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        try { Unchanged(); CheckLink(path); }
        catch { File.Move(backupPath, backupPath + ".cancelled"); throw; }
        if (next is null) { File.Delete(path); return backupPath; }
        var stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { stream.Write(next); stream.Flush(true); }
            try { Unchanged(); CheckLink(path); }
            catch { File.Move(backupPath, backupPath + ".cancelled"); throw; }
            File.Move(stage, path, overwrite: true);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
        return backupPath;
    }
    private static void CheckLink(string path) { if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Profile 或备份是符号链接，请选择实际文件。"); }
    private static string BackupDirectory(string home) => Path.Combine(Path.GetFullPath(home), "codex-config-backups");
    private static string Hash(byte[]? bytes) => bytes is null ? "absent" : Convert.ToHexString(SHA256.HashData(bytes));
    private sealed record Backup(string Name, byte[]? Before, string AfterHash);
}
