using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CodexConfig.Core;

internal enum DesktopProcessRole { Unknown, Main, Helper }
internal sealed record DesktopProcessArguments(DesktopProcessRole Role, string? UserDataDirectory)
{
    internal static readonly DesktopProcessArguments Unknown = new(DesktopProcessRole.Unknown, null);

    // Keep only the two lifecycle facts. Never expose or log a desktop command line.
    internal static DesktopProcessArguments Read(SafeProcessHandle process)
    {
        var query = Query.Value;
        if (query is null) return Unknown;
        _ = query(process, 60, IntPtr.Zero, 0, out var required);
        if (required < Marshal.SizeOf<UnicodeString>() || required > 131072) return Unknown;
        var memory = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (query(process, 60, memory, required, out _) < 0) return Unknown;
            var value = Marshal.PtrToStructure<UnicodeString>(memory);
            var offset = value.Buffer.ToInt64() - memory.ToInt64();
            if (value.Length == 0 || value.Length % 2 != 0 || value.Length > value.MaximumLength ||
                offset < Marshal.SizeOf<UnicodeString>() || offset > required - value.Length) return Unknown;
            return Parse(Marshal.PtrToStringUni(value.Buffer, value.Length / 2)!);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    internal static DesktopProcessArguments Parse(string command)
    {
        if (command.Length is 0 or > 65535) return Unknown;
        var arguments = CommandLineToArgvW(command, out var count);
        if (arguments == IntPtr.Zero) return Unknown;
        try
        {
            var helper = false; string? directory = null;
            for (var i = 1; i < count; i++)
            {
                var argument = Marshal.PtrToStringUni(Marshal.ReadIntPtr(arguments, i * IntPtr.Size))!;
                if (argument == "--type" || argument.StartsWith("--type=", StringComparison.Ordinal)) helper = true;
                string? path = null;
                if (argument.StartsWith("--user-data-dir=", StringComparison.Ordinal)) path = argument[16..];
                else if (argument == "--user-data-dir" && i + 1 < count)
                    path = Marshal.PtrToStringUni(Marshal.ReadIntPtr(arguments, ++i * IntPtr.Size));
                if (path is not null)
                {
                    if (!Path.IsPathFullyQualified(path) || path.IndexOfAny(['\r', '\n', '\0']) >= 0) return Unknown;
                    path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                    if (directory is not null && !CodexDesktop.SameDirectory(directory, path)) return Unknown;
                    directory = path;
                }
            }
            return new(helper ? DesktopProcessRole.Helper : DesktopProcessRole.Main, directory);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return Unknown; }
        finally { LocalFree(arguments); }
    }

    private static readonly Lazy<QueryInformation?> Query = new(() =>
    {
        // This optional information class is not a Windows compatibility contract.
        // If unavailable, discovery falls back to ancestry and multi-instance launch stays blocked.
        if (!NativeLibrary.TryLoad("ntdll.dll", typeof(DesktopProcessArguments).Assembly, DllImportSearchPath.System32, out var library)) return null;
        return NativeLibrary.TryGetExport(library, "NtQueryInformationProcess", out var address)
            ? Marshal.GetDelegateForFunctionPointer<QueryInformation>(address) : null;
    });
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int QueryInformation(SafeProcessHandle process, int information, IntPtr buffer, uint size, out uint required);
    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
