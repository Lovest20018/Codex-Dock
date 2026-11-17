using System.Runtime.InteropServices;

namespace CodexConfig.Core;

// Windows ships SQLite. This narrow adapter avoids installing a second database
// engine and binds all user values instead of assembling them into SQL.
internal sealed class NativeSqlite : IDisposable
{
    private IntPtr database;
    internal NativeSqlite(string path, bool writable = false, bool create = false)
    {
        var result = Open(path, out database, (writable ? 2 : 1) | (create ? 4 : 0) | 0x10000, IntPtr.Zero);
        if (result != 0) { Dispose(); throw new IOException("无法打开本地聊天索引（SQLite " + result + "）。"); }
        _ = BusyTimeout(database, 1500);
    }
    internal IReadOnlyList<string?[]> Query(string sql, params string?[] values)
    {
        var result = Prepare(database, sql, -1, out var statement, IntPtr.Zero);
        if (result != 0) throw new IOException("聊天索引结构不受支持（SQLite " + result + "）。");
        try
        {
            for (var i = 0; i < values.Length; i++)
            {
                result = values[i] is null ? BindNull(statement, i + 1) : BindText(statement, i + 1, values[i]!, -1, new IntPtr(-1));
                if (result != 0) throw new IOException("无法绑定聊天索引参数。");
            }
            var rows = new List<string?[]>();
            while ((result = Step(statement)) == 100)
            {
                if (rows.Count >= 256) throw new IOException("聊天索引查询结果不明确，已停止修改。");
                var row = new string?[Columns(statement)];
                for (var column = 0; column < row.Length; column++)
                {
                    var bytes = ColumnBytes(statement, column);
                    if (bytes > 32768) throw new IOException("聊天索引字段超出支持范围。");
                    var pointer = ColumnText(statement, column); row[column] = pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer, bytes);
                }
                rows.Add(row);
            }
            if (result != 101) throw new IOException("聊天索引操作未完成（SQLite " + result + "）。");
            return rows;
        }
        finally { _ = Finalize(statement); }
    }
    internal int Execute(string sql, params string?[] values) { _ = Query(sql, values); return Changes(database); }
    internal bool HasTable(string name) => Query("SELECT name FROM sqlite_master WHERE type='table' AND name=?1", name).Count == 1;
    public void Dispose() { if (database != IntPtr.Zero) { _ = Close(database); database = IntPtr.Zero; } }
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_open_v2")]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr database, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_close_v2")] private static extern int Close(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_busy_timeout")] private static extern int BusyTimeout(IntPtr database, int milliseconds);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_prepare_v2")]
    private static extern int Prepare(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_text")]
    private static extern int BindText(IntPtr statement, int position, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int bytes, IntPtr destructor);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_null")] private static extern int BindNull(IntPtr statement, int position);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_step")] private static extern int Step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_count")] private static extern int Columns(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_text")] private static extern IntPtr ColumnText(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_bytes")] private static extern int ColumnBytes(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_finalize")] private static extern int Finalize(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_changes")] private static extern int Changes(IntPtr database);
}
