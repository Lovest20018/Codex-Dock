using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexConfig.Core;

public sealed record ThreadProviderPreparation(int ChangedRows, bool Recovered, string? Backup);

// Called only after the native client has confirmed the selected local chat is
// unloaded. It changes indexes only; rollout history is never opened for writing.
public static class ThreadProviderStore
{
    private const string DirectoryName = "codex-dock-thread-backups";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexDock.thread-provider.v1");
    private sealed record Row(string Database, string? Host, string Before, string After);
    private sealed record Journal(int Version, string Home, string Thread, Row[] Rows);
    public static ThreadProviderPreparation Prepare(string home, string thread, string source, string target, Action validate)
    {
        home = Path.GetFullPath(home);
        if (!Guid.TryParseExact(thread, "D", out _) || !Provider(source) || !Provider(target)) throw new ArgumentException("聊天或供应商标识无法核对。");
        Safe(home, home);
        var folder = Path.Combine(home, DirectoryName); Safe(home, folder); Directory.CreateDirectory(folder);
        Safe(home, Path.Combine(folder, "operation.lock"));
        using var lifecycle = new FileStream(Path.Combine(folder, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var recovered = Recover(home, thread, validate);
        validate();
        var rows = Discover(home, thread, source, target);
        if (rows.All(row => row.Before == row.After)) return new(0, recovered, null);
        var journal = new Journal(1, home, thread, rows);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".pending");
        WriteJournal(path, journal);
        try { Apply(journal, reverse: false, validate); }
        catch
        {
            // Cross-database WAL commits can be interrupted. Keep the journal even
            // if SQLite rolled back; the next idle preparation reconciles each row.
            throw new IOException("聊天索引准备未完成，备份已保留。请等待聊天空闲后重试恢复。");
        }
        File.Move(path, Path.ChangeExtension(path, ".backup"));
        return new(rows.Count(row => row.Before != row.After), recovered, Path.ChangeExtension(path, ".backup"));
    }
    private static bool Provider(string value) => value.Length is > 0 and <= 100 && Regex.IsMatch(value, @"^[A-Za-z0-9_.-]+$") && value is not "codex_plus_managed";
    private static Row[] Discover(string home, string thread, string source, string target)
    {
        var files = Directory.EnumerateFiles(home, "state_*.sqlite").Where(path => Regex.IsMatch(Path.GetFileName(path), @"^state_\d+\.sqlite$")).ToList();
        files.AddRange(new[] { "codex.db", "codex-dev.db" }.Select(name => Path.Combine(home, "sqlite", name)).Where(File.Exists));
        if (files.Count > 12) throw new IOException("聊天索引版本过多，无法唯一核对。");
        var rows = new List<Row>(); var canonical = 0;
        foreach (var path in files)
        {
            Safe(home, path); using var db = new NativeSqlite(path);
            var relative = Path.GetRelativePath(home, path);
            if (db.HasTable("threads"))
            {
                var found = db.Query("SELECT model_provider FROM threads WHERE id=?1", thread);
                if (found.Count > 1) throw new IOException("聊天索引不唯一。");
                if (found.Count == 1)
                {
                    canonical++; if (found[0][0] != source) throw new IOException("聊天供应商已改变，请重新读取后重试。");
                    rows.Add(new(relative, null, source, target));
                }
            }
            if (!db.HasTable("local_thread_catalog")) continue;
            var hosts = db.HasTable("local_thread_catalog_hosts")
                ? db.Query("SELECT host_id FROM local_thread_catalog_hosts WHERE LOWER(COALESCE(host_kind,''))='local'").Select(row => row[0]).ToArray()
                : ["local"];
            if (hosts.Length > 1) throw new IOException("检测到多个本机聊天目录归属，已停止准备。");
            if (hosts.Length != 1 || string.IsNullOrWhiteSpace(hosts[0])) continue;
            var local = db.Query("SELECT model_provider FROM local_thread_catalog WHERE host_id=?1 AND thread_id=?2", hosts[0], thread);
            if (local.Count > 1 || local.Any(row => row[0] != source && row[0] != target)) throw new IOException("本机聊天目录与当前供应商冲突。");
            if (local.Count == 1) rows.Add(new(relative, hosts[0], local[0][0]!, target));
        }
        if (canonical != 1) throw new IOException("无法唯一找到本机原生聊天索引，历史保持不变。");
        return rows.ToArray();
    }
    private static bool Recover(string home, string thread, Action validate)
    {
        var pending = Directory.GetFiles(Path.Combine(home, DirectoryName), "*.pending");
        if (pending.Length > 100) throw new IOException("待恢复聊天记录过多，请先核对备份目录。");
        var recovered = false;
        foreach (var path in pending)
        {
            Safe(home, path);
            if (new FileInfo(path).Length > 65536) throw new IOException("聊天恢复记录超出支持范围。");
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            Journal journal;
            try { journal = JsonSerializer.Deserialize<Journal>(plain) ?? throw new IOException("聊天恢复记录无效。"); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            if (journal.Version != 1 || journal.Home != home || journal.Rows.Length is 0 or > 12 || !Guid.TryParseExact(journal.Thread, "D", out _)) throw new IOException("聊天恢复记录不属于当前目录。");
            if (journal.Thread != thread) continue; // A different chat must first become idle itself.
            Apply(journal, reverse: true, validate);
            File.Move(path, Path.ChangeExtension(path, ".recovered")); recovered = true;
        }
        return recovered;
    }
    private static void Apply(Journal journal, bool reverse, Action validate)
    {
        var paths = journal.Rows.Select(row => row.Database).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var relative in paths) Safe(journal.Home, Path.GetFullPath(relative, journal.Home));
        using var db = new NativeSqlite(Path.GetFullPath(paths[0], journal.Home), writable: true);
        for (var i = 1; i < paths.Length; i++) db.Execute("ATTACH DATABASE ?1 AS d" + i, Path.GetFullPath(paths[i], journal.Home));
        string Table(Row row) => (Array.FindIndex(paths, path => path.Equals(row.Database, StringComparison.OrdinalIgnoreCase)) is var i && i == 0 ? "main" : "d" + i) +
            (row.Host is null ? ".threads" : ".local_thread_catalog");
        db.Execute("BEGIN IMMEDIATE");
        try
        {
            validate();
            foreach (var row in journal.Rows)
            {
                if (!Provider(row.Before) || !Provider(row.After)) throw new IOException("聊天恢复记录供应商无效。");
                var table = Table(row); var where = row.Host is null ? "id=?1" : "thread_id=?1 AND host_id=?2";
                var values = row.Host is null ? new[] { journal.Thread } : new[] { journal.Thread, row.Host };
                var found = db.Query("SELECT model_provider FROM " + table + " WHERE " + where, values);
                if (found.Count != 1) throw new IOException("聊天目录在准备时发生变化。");
                var actual = found[0][0];
                if (actual != row.Before && !(reverse && actual == row.After)) throw new IOException("聊天索引出现外部变更，已保留恢复记录。");
                var desired = reverse ? row.Before : row.After;
                if (actual == desired) continue;
                var affected = row.Host is null
                    ? db.Execute("UPDATE " + table + " SET model_provider=?1 WHERE id=?2 AND model_provider=?3", desired, journal.Thread, actual)
                    : db.Execute("UPDATE " + table + " SET model_provider=?1 WHERE thread_id=?2 AND host_id=?3 AND model_provider=?4", desired, journal.Thread, row.Host, actual);
                if (affected != 1) throw new IOException("聊天索引已改变，准备已取消。");
            }
            validate(); db.Execute("COMMIT");
        }
        catch { try { db.Execute("ROLLBACK"); } catch (IOException) { } throw; }
    }
    private static void WriteJournal(string path, Journal value)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value);
        try
        {
            var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.Write(encrypted); file.Flush(flushToDisk: true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    internal static void Safe(string home, string path)
    {
        path = Path.GetFullPath(path); home = Path.GetFullPath(home);
        if (path != home && !path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("聊天索引不在当前配置目录。");
        var current = path;
        while (true)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("聊天索引路径包含链接，已停止写入。");
            if (current.Equals(home, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current) ?? throw new IOException("聊天索引路径无法核对。");
        }
    }
}
