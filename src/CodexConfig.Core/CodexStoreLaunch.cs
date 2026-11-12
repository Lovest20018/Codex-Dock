using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;

namespace CodexConfig.Core;

// Launch Store apps through their own execution alias. A direct EXE launch loses
// package identity and can select a different profile and runtime installation.
internal static class CodexStoreLaunch
{
    internal const string Family = "OpenAI.Codex_2p2nqsd0c76g0";

    internal static string Resolve(string executable, string localData, Func<string, byte[]>? readAlias = null)
    {
        var package = Directory.GetParent(Path.GetDirectoryName(executable)!)!.FullName;
        var manifest = XDocument.Load(Path.Combine(package, "AppxManifest.xml"));
        var applications = manifest.Descendants().Where(element => element.Name.LocalName == "Application" &&
            element.Attribute("Executable") is { } file && CodexDesktop.SamePath(
                Path.GetFullPath(Path.Combine(package, file.Value.Replace('/', Path.DirectorySeparatorChar))), executable)).ToArray();
        if (applications.Length != 1) throw new IOException("无法核对 Codex 商店应用的启动入口。");
        var appId = applications[0].Attribute("Id")?.Value;
        var aliases = applications[0].Descendants().Where(element => element.Name.LocalName == "Extension" &&
            element.Attribute("Category")?.Value == "windows.appExecutionAlias")
            .SelectMany(element => element.Descendants().Where(child => child.Name.LocalName == "ExecutionAlias"))
            .Select(element => element.Attribute("Alias")?.Value).Where(alias => alias is not null &&
                alias.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(alias) == alias).Distinct().ToArray();
        if (string.IsNullOrWhiteSpace(appId) || aliases.Length != 1) throw new IOException("Codex 商店应用没有唯一的执行别名。");
        var path = Path.Combine(localData, "Microsoft", "WindowsApps", Family, aliases[0]!);
        VerifyAlias((readAlias ?? ReadAlias)(path), executable, Family + "!" + appId);
        return path;
    }

    internal static void VerifyAlias(byte[] data, string executable, string appId)
    {
        if (data.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x8000001b)
            throw new IOException("Codex 启动入口不是 Windows 注册的应用执行别名。");
        var length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        if (length < 4 || length % 2 != 0 || length + 8 > data.Length)
            throw new IOException("Codex 执行别名的数据无法核对。");
        var fields = Encoding.Unicode.GetString(data, 12, length - 4).Split('\0');
        if (fields.Length < 4 || fields[0] != Family || fields[1] != appId || !CodexDesktop.SamePath(fields[2], executable))
            throw new IOException("Codex 执行别名与选定的商店应用不一致，请修复应用注册后重试。");
    }

    private static byte[] ReadAlias(string path)
    {
        using var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Codex 的专属启动入口。");
        var data = new byte[16384];
        if (!DeviceIoControl(handle, 0x000900a8, IntPtr.Zero, 0, data, data.Length, out var count, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法核对 Codex 的应用执行别名。");
        return data[..count];
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFileW", SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint control, IntPtr input, int inputSize,
        byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
