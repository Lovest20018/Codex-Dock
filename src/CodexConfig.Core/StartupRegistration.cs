using Microsoft.Win32;

namespace CodexConfig.Core;

public interface IStartupRegistration
{
    string? Read();
    void Write(string? command);
}

public sealed class WindowsStartupRegistration(string keyPath = WindowsStartupRegistration.RunKey) : IStartupRegistration
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "CodexDock";
    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ValueName) as string;
    }
    public void Write(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (command is null) key.DeleteValue(ValueName, throwOnMissingValue: false);
        else key.SetValue(ValueName, command, RegistryValueKind.String);
    }
}

public sealed class MemoryStartupRegistration : IStartupRegistration
{
    public string? Command { get; private set; }
    public string? Read() => Command;
    public void Write(string? command) => Command = command;
}

public static class StartupCommand
{
    public static string Create(string executable, string home)
    {
        var command = Quote(Path.GetFullPath(executable)) + " --start-in-tray --home " + Quote(Path.GetFullPath(home));
        if (command.Length > 260) throw new ArgumentException("程序与配置目录的路径过长，无法设置开机启动，请使用较短的路径。");
        return command;
    }
    public static bool Targets(string? command, string executable) => command?.StartsWith(Quote(Path.GetFullPath(executable)) + " ", StringComparison.OrdinalIgnoreCase) == true;
    // CommandLineToArgvW requires trailing backslashes to be doubled inside quotes.
    private static string Quote(string path)
    {
        if (path.Any(c => c == '"' || char.IsControl(c))) throw new ArgumentException("启动路径包含无效字符。");
        return "\"" + path + new string('\\', path.Length - path.TrimEnd('\\').Length) + "\"";
    }
}
