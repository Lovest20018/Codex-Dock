using System.Runtime.InteropServices;

namespace CodexConfig.Core;

public static class EnhancedLaunchShortcut
{
    public static string Arguments(string home)
    {
        if (!Path.IsPathFullyQualified(home) || home.Any(character => character == '"' || char.IsControl(character)))
            throw new ArgumentException("配置目录必须是有效的绝对路径。");
        var path = Path.GetFullPath(home);
        return "--enhanced-launch --home \"" + path + new string('\\', path.Length - path.TrimEnd('\\').Length) + "\"";
    }
    public static void Create(string destination, string executable, string home)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!Path.IsPathFullyQualified(destination) || !destination.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
            !Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new ArgumentException("请使用有效的快捷方式位置与 Dock 程序。");
        var arguments = Arguments(home);
        var stage = Path.Combine(Path.GetDirectoryName(destination)!, ".dock-" + Guid.NewGuid().ToString("N") + ".lnk");
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows 快捷方式组件不可用。"))!;
            dynamic link = shortcut = ((dynamic)shell).CreateShortcut(stage);
            link.TargetPath = Path.GetFullPath(executable); link.Arguments = arguments;
            link.WorkingDirectory = Path.GetDirectoryName(executable); link.Description = "通过 Codex Dock 启动并接入已开启的客户端增强";
            link.IconLocation = executable + ",0"; link.Save();
            File.Move(stage, destination, true);
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
            if (File.Exists(stage)) File.Delete(stage);
        }
    }
}
