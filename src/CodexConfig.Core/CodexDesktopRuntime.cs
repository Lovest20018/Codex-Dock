using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace CodexConfig.Core;

internal static class CodexDesktopRuntime
{
    private static readonly string[] RuntimeFiles = ["codex.exe", "codex-code-mode-host.exe", "codex-command-runner.exe",
        "codex-windows-sandbox-setup.exe", "codex-windows-sandbox-service.exe", "rg.exe"];
    private sealed record RuntimeFile(string Name, long Size, string Sha256);

    internal static IReadOnlyList<string> Registered() => WindowsCodexDesktopPlatform.RegisteredExecutables()
        .Select(gui => Path.Combine(Path.GetDirectoryName(gui)!, "resources", "codex.exe"))
        .Where(File.Exists).OrderByDescending(PackageVersion).ToArray();

    private static Version PackageVersion(string runtime)
    {
        var packageName = Directory.GetParent(runtime)?.Parent?.Parent?.Name;
        return Version.TryParse(packageName?.Split('_').ElementAtOrDefault(1), out var version) ? version : new Version(0, 0);
    }

    internal static bool IsStoreRuntime(string executable)
    {
        if (!Path.GetFileName(executable).Equals("codex.exe", StringComparison.OrdinalIgnoreCase)) return false;
        var resources = Path.GetDirectoryName(executable);
        var app = Path.GetDirectoryName(resources);
        var package = Path.GetFileName(Path.GetDirectoryName(app));
        return string.Equals(Path.GetFileName(resources), "resources", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(app), "app", StringComparison.OrdinalIgnoreCase) &&
            package?.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) == true &&
            package.EndsWith("__2p2nqsd0c76g0", StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<Process> StartAsync(ProcessStartInfo start, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var executable = Path.GetFullPath(start.FileName);
        var packaged = IsStoreRuntime(executable);
        if (packaged) CheckRegistration(executable);
        try { return Start(start); }
        catch (Win32Exception error) when (packaged && error.NativeErrorCode == 5)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var extracted = CodexExecutableLocator.DesktopCandidates(local).Where(File.Exists).Select(path => Path.GetDirectoryName(path)!);
            var cache = Path.Combine(local, "CodexConfig", "native-runtime");
            start.FileName = await ResolveCopyAsync(executable, extracted, cache, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            CheckRegistration(executable);
            return Start(start);
        }
    }

    private static Process Start(ProcessStartInfo start) => Process.Start(start) ?? throw new IOException("无法启动所选 Codex 执行端。");

    private static void CheckRegistration(string executable)
    {
        if (!Registered().Contains(executable, StringComparer.OrdinalIgnoreCase))
            throw new IOException("该商店版 Codex 执行端已移动、更新或注销，请重新查找执行端。");
    }

    internal static async Task<string> ResolveCopyAsync(string executable, IEnumerable<string> extractedDirectories, string cache, CancellationToken cancellation)
    {
        var source = Path.GetDirectoryName(executable)!;
        var files = new List<RuntimeFile>();
        foreach (var name in RuntimeFiles)
        {
            var file = Path.Combine(source, name);
            if (!File.Exists(file) && name != "codex.exe") continue;
            cancellation.ThrowIfCancellationRequested();
            await using var stream = Open(file);
            files.Add(new(name, stream.Length, Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation).ConfigureAwait(false))));
        }
        foreach (var directory in extractedDirectories)
            if (await MatchesAsync(directory, files, cancellation).ConfigureAwait(false)) return Path.Combine(directory, "codex.exe");

        var identity = string.Join("\n", files.Select(file => file.Name + ":" + file.Sha256));
        var version = Path.Combine(cache, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant());
        Directory.CreateDirectory(version);
        foreach (var directory in Directory.EnumerateDirectories(version, "runtime*"))
            if (await MatchesAsync(directory, files, cancellation).ConfigureAwait(false)) return Path.Combine(directory, "codex.exe");

        // Publish a complete directory in one rename. Reuse a concurrent winner
        // when its files match, and leave an in-use copy in place if validation fails.
        var operationId = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(version, ".staging-" + operationId);
        var published = Path.Combine(version, "runtime");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in files)
            {
                await using var input = Open(Path.Combine(source, file.Name));
                await using var output = new FileStream(Path.Combine(staging, file.Name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
                await input.CopyToAsync(output, cancellation).ConfigureAwait(false);
            }
            if (!await MatchesAsync(staging, files, cancellation).ConfigureAwait(false))
                throw new IOException("Codex 执行端副本校验失败，请重新查找执行端。");
            cancellation.ThrowIfCancellationRequested();
            try { Directory.Move(staging, published); }
            catch (IOException) when (Directory.Exists(published))
            {
                if (await MatchesAsync(published, files, cancellation).ConfigureAwait(false)) return Path.Combine(published, "codex.exe");
                published += "-" + operationId;
                cancellation.ThrowIfCancellationRequested();
                Directory.Move(staging, published);
            }
            return Path.Combine(published, "codex.exe");
        }
        finally
        {
            if (Directory.Exists(staging))
                try
                {
                    foreach (var file in files) File.Delete(Path.Combine(staging, file.Name));
                    Directory.Delete(staging);
                }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException) { }
        }
    }

    private static FileStream Open(string file) => new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);

    private static async Task<bool> MatchesAsync(string directory, IReadOnlyList<RuntimeFile> expected, CancellationToken cancellation)
    {
        try
        {
            foreach (var file in expected)
            {
                cancellation.ThrowIfCancellationRequested();
                var candidate = Path.Combine(directory, file.Name);
                if (new FileInfo(candidate).Length != file.Size) return false;
                await using var stream = Open(candidate);
                if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation).ConfigureAwait(false)).Equals(file.Sha256, StringComparison.Ordinal)) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
