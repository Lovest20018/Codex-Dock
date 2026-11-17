using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CodexConfig.Core;

// Compare the file behind a path, including Store volume paths. Never guess an
// inaccessible alias from its spelling or accept equal bytes as file identity.
internal readonly record struct WindowsFileId(ulong Volume, ulong Low, ulong High);
internal static class WindowsFileIdentity
{
    internal static SafeFileHandle Open(string path) => CreateFile(path, 0x80, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
    internal static WindowsFileId? Read(string path)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(path)) return null;
        try { using var handle = Open(path); return Read(handle); }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException) { return null; }
    }
    internal static WindowsFileId? Read(SafeFileHandle handle)
    {
        if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, 9, out AttributeInfo attributes, 8) ||
            (attributes.Attributes & 0x10) != 0 || !GetFileInformationByHandleEx(handle, 18, out IdInfo identity, 24)) return null;
        return new(identity.Volume, identity.Low, identity.High);
    }
    internal static bool Same(string left, string right) => Read(left) is { } first && Read(right) is { } second && first == second;
    [StructLayout(LayoutKind.Sequential)] private struct AttributeInfo { public uint Attributes, Tag; }
    [StructLayout(LayoutKind.Sequential)] private struct IdInfo { public ulong Volume, Low, High; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFileW", SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out AttributeInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out IdInfo info, uint size);
}
