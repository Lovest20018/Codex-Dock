using System.Runtime.InteropServices;

namespace CodexConfig.WinUI;

// Bounded native Unicode reads; no retained OLE/WinRT clipboard objects.
internal sealed class NativeClipboardFixture
{
    internal const string KnownTestText = "CODEX_DOCK_CLIPBOARD_TEST_20261117";
    internal static string LastSkipReason { get; private set; } = "not checked";
    private readonly string previous;
    private string? ownedText;
    internal bool Restored { get; private set; }
    internal bool Superseded { get; private set; }
    private NativeClipboardFixture(string text) => previous = text;
    internal static async Task<NativeClipboardFixture?> TryCreateAsync(string knownText)
    {
        var text = await ReadBoundedTextAsync(knownText.Length + 2);
        if (text != knownText && text != knownText + "\n" && text != knownText + "\r\n")
        { LastSkipReason = "Current Unicode text does not match the authorized marker"; return null; }
        LastSkipReason = "Eligible authorized test marker"; return new(text);
    }
    internal void RequireUnchanged()
    { if (ReadBoundedText(previous.Length + 1) != previous) throw new OperationCanceledException("剪贴板已改变，未覆盖新内容。"); }
    internal async Task VerifyOwnedTextAsync(string expected)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (ReadBoundedText(expected.Length + 1) == expected) { ownedText = expected; return; }
            await Task.Delay(50);
        }
        throw new IOException("复制内容未核对为本次测试内容，未记录其他文本。");
    }
    internal bool OwnsContent => ownedText is not null && ReadBoundedText(ownedText.Length + 1) == ownedText;
    internal async Task RestoreAsync(Action waiting)
    {
        if (ownedText is null) { Restored = ReadBoundedText(previous.Length + 1) == previous; return; }
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (!OwnsContent) { Superseded = true; return; }
            try
            {
                NativeWorkspaceClipboard.Copy(previous);
                if (await ReadBoundedTextAsync(previous.Length + 1) == previous) { Restored = true; return; }
            }
            catch (Exception error) when (error is COMException or IOException) { }
            if (attempt == 10) waiting();
            await Task.Delay(100);
        }
        throw new IOException("测试文字恢复未通过，验收不能标记完成。");
    }
    private static async Task<string?> ReadBoundedTextAsync(int maximum)
    { for (var attempt = 0; attempt < 10; attempt++) { var text = ReadBoundedText(maximum); if (text is not null) return text; await Task.Delay(50); } return null; }
    private static string? ReadBoundedText(int maximum)
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            var data = GetClipboardData(13); if (data == IntPtr.Zero) return null;
            var size = GlobalSize(data).ToUInt64(); if (size < 2 || size > (ulong)(maximum + 1) * 2) return null;
            var pointer = GlobalLock(data); if (pointer == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(pointer, (int)size / 2)?.TrimEnd('\0'); }
            finally { _ = GlobalUnlock(data); }
        }
        finally { _ = CloseClipboard(); }
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr memory);
}
