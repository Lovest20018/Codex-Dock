using System.Diagnostics;

namespace CodexConfig.Core;

public sealed class CodexLoginClient
{
    public static string? FindExecutable()
    {
        var known = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin", "codex.exe");
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Path.Combine(path.Trim('"'), "codex.exe"))
            .Append(known).FirstOrDefault(File.Exists);
    }

    public static ProcessStartInfo StartInfo(string executable, string stagingHome)
    {
        if (!Path.IsPathFullyQualified(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
            throw new ArgumentException("请选择 Codex 原生 codex.exe。");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = stagingHome
        };
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("cli_auth_credentials_store=\"file\"");
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("forced_login_method=\"chatgpt\"");
        start.ArgumentList.Add("login");
        start.Environment["CODEX_HOME"] = stagingHome;
        foreach (var name in new[] { "CODEX_ACCESS_TOKEN", "CODEX_API_KEY", "OPENAI_API_KEY", "OPENAI_BASE_URL", "CODEX_AUTHAPI_BASE_URL", "CHATGPT_BASE_URL" }) start.Environment.Remove(name);
        return start;
    }

    public async Task<byte[]> LoginAsync(string executable, string stagingRoot, CancellationToken cancellation = default)
    {
        stagingRoot = Path.GetFullPath(stagingRoot);
        var staging = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var process = Process.Start(StartInfo(executable, staging)) ?? throw new IOException("无法启动 Codex 登录。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            using var cancelProcess = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
            // CLI output contains a browser authorization URL; it never enters our logs or preview.
            var output = DrainAsync(process.StandardOutput);
            var error = DrainAsync(process.StandardError);
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(output, error).ConfigureAwait(false);
            }
            timeout.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidOperationException("官方登录未完成。请检查浏览器和网络，或选择可用的 codex.exe 后重试。");
            var path = Path.Combine(staging, "auth.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Codex 没有生成有效的官方登录文件。");
            var auth = await File.ReadAllBytesAsync(path, cancellation).ConfigureAwait(false);
            if (!OfficialCredentials.HasLogin(auth)) throw new InvalidDataException("Codex 登录结果不包含完整的官方凭据。");
            return auth;
        }
        finally
        {
            // Only the dedicated, randomly named staging directory is removed.
            if (Path.GetDirectoryName(Path.GetFullPath(staging)) == stagingRoot && Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) > 0) { }
        Array.Clear(buffer);
    }
}
