using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexConfig.Core;

public static class CodexExecutableLocator
{
    public static string? Find()
    {
        var path = string.Join(Path.PathSeparator, new[] { Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User), Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) });
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var running = RunningExecutables().ToArray();
        return Find(path, local, roaming, running);
    }

    // Bounded discovery under known installation roots; never run npm, a shell shim or a candidate executable.
    public static string? Find(string? path, string localAppData, string roamingAppData, IEnumerable<string>? runningExecutables = null)
    {
        var prefixes = (path ?? "").Split(Path.PathSeparator).Select(Normalize).Where(value => value is not null).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var candidates = (runningExecutables ?? []).Where(IsRuntimePath)
            .Concat(prefixes.Select(directory => Path.Combine(directory, "codex.exe")))
            .Concat(DesktopCandidates(localAppData))
            .Append(Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "bin", "codex.exe"))
            .Concat(prefixes.Append(Path.Combine(roamingAppData, "npm")).Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(NpmCandidates));
        return candidates.Select(Normalize).Where(value => value is not null).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> DesktopCandidates(string localAppData)
    {
        var bin = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        yield return Path.Combine(bin, "codex.exe");
        foreach (var directory in VersionDirectories(bin)) yield return Path.Combine(directory, "codex.exe");
    }

    private static string[] VersionDirectories(string root)
    {
        try { return new DirectoryInfo(root).GetDirectories().OrderByDescending(directory => directory.LastWriteTimeUtc).ThenBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase).Select(directory => directory.FullName).ToArray(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> NpmCandidates(string prefix)
    {
        var scope = Path.Combine(prefix, "node_modules", "@openai");
        var package = Path.Combine(scope, "codex");
        var architectures = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? new[] { ("arm64", "aarch64"), ("x64", "x86_64") } : [("x64", "x86_64")];
        foreach (var (architecture, target) in architectures)
            foreach (var vendor in new[] { Path.Combine(package, "node_modules", "@openai", "codex-win32-" + architecture, "vendor"),
                Path.Combine(scope, "codex-win32-" + architecture, "vendor"), Path.Combine(package, "vendor") })
                foreach (var executableDirectory in new[] { "bin", "codex" })
                    yield return Path.Combine(vendor, target + "-pc-windows-msvc", executableDirectory, "codex.exe");
    }

    private static IEnumerable<string> RunningExecutables()
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName("codex"); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { yield break; }
        foreach (var process in processes)
        {
            string? path = null;
            using (process)
                try { path = process.MainModule?.FileName; }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            if (path is not null) yield return path;
        }
    }

    private static bool IsRuntimePath(string path) => Path.GetFileName(path).Equals("codex.exe", StringComparison.OrdinalIgnoreCase) &&
        (path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
         Path.GetFileName(Path.GetDirectoryName(path))?.Equals("resources", StringComparison.OrdinalIgnoreCase) is true);

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
