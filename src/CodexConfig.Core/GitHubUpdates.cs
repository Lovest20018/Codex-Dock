using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexConfig.Core;

public sealed record ReleaseUpdate(Version Version, Uri Page, Uri Installer, long Size, Uri Checksums, string? Digest);
public sealed record VerifiedInstaller(string Path, string Sha256)
{
    public FileStream OpenVerified()
    {
        var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(file)), Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装器在下载后发生变化，请重新下载。");
            file.Position = 0; return file;
        }
        catch { file.Dispose(); throw; }
    }
}

public sealed class GitHubUpdates : IDisposable
{
    public const string Repository = "https://github.com/Lovest20018/Codex-Dock";
    public const string LatestApi = "https://api.github.com/repos/Lovest20018/Codex-Dock/releases/latest";
    private const long MaxInstaller = 300L * 1024 * 1024;
    private readonly HttpClient client;
    public GitHubUpdates(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexDock-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<ReleaseUpdate?> CheckAsync(Version current, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var response = await GetAsync(new(LatestApi), timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await ReadBoundedAsync(response, 1024 * 1024, timeout.Token).ConfigureAwait(false));
        var root = json.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d{1,4}\.\d{1,4}\.\d{1,4}$") || !Version.TryParse(tag[1..], out var version)) throw new InvalidDataException("发布版本号无效。");
        if (version <= new Version(current.Major, current.Minor, Math.Max(0, current.Build))) return null;
        var installerName = $"CodexDock-{version}-Setup-win-x64.exe";
        var checksumName = $"SHA256SUMS-{version}.txt";
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        JsonElement Asset(string name)
        {
            var matching = assets.Where(a => a.GetProperty("name").GetString() == name).ToArray();
            return matching.Length == 1 ? matching[0] : throw new InvalidDataException("此版本缺少安装器或校验文件，请查看发布页。");
        }
        Uri AssetUri(JsonElement asset, string name)
        {
            var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if (uri.AbsoluteUri != $"{Repository}/releases/download/{tag}/{name}") throw new InvalidDataException("更新文件不属于指定 GitHub 发布。");
            return uri;
        }
        var installer = Asset(installerName); var checksum = Asset(checksumName);
        var size = installer.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaxInstaller) throw new InvalidDataException("安装器大小无效。");
        return new(version, new($"{Repository}/releases/tag/{tag}"), AssetUri(installer, installerName), size, AssetUri(checksum, checksumName),
            installer.TryGetProperty("digest", out var digest) ? digest.GetString() : null);
    }

    public async Task<VerifiedInstaller> DownloadAsync(ReleaseUpdate update, string directory, IProgress<int>? progress = null, CancellationToken cancellation = default)
    {
        var tag = "v" + update.Version;
        var name = $"CodexDock-{update.Version}-Setup-win-x64.exe";
        if (update.Installer.AbsoluteUri != $"{Repository}/releases/download/{tag}/{name}" ||
            update.Checksums.AbsoluteUri != $"{Repository}/releases/download/{tag}/SHA256SUMS-{update.Version}.txt" || update.Size <= 0 || update.Size > MaxInstaller)
            throw new InvalidDataException("更新来源无效。");
        using var checksums = await GetAsync(update.Checksums, cancellation).ConfigureAwait(false); checksums.EnsureSuccessStatusCode();
        var text = Encoding.UTF8.GetString(await ReadBoundedAsync(checksums, 64 * 1024, cancellation).ConfigureAwait(false));
        var entries = text.Split('\n').Select(line => Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?" + Regex.Escape(name) + "$"))
            .Where(match => match.Success).Select(match => match.Groups[1].Value.ToLowerInvariant()).ToArray();
        if (entries.Length != 1 || update.Digest is not null && !string.Equals(update.Digest, "sha256:" + entries[0], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装器校验信息缺失或不一致。");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, name); var stage = target + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var response = await GetAsync(update.Installer, cancellation).ConfigureAwait(false); response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != update.Size) throw new InvalidDataException("安装器大小与发布信息不一致。");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
            await using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                {
                    total += read; if (total > update.Size) throw new InvalidDataException("安装器大小超出发布信息。");
                    hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                    progress?.Report((int)(total * 100 / update.Size));
                }
                if (total != update.Size || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != entries[0]) throw new InvalidDataException("安装器 SHA-256 校验失败，请重新下载。");
            }
            File.Move(stage, target, overwrite: true); return new(target, entries[0]);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellation)
    {
        for (var i = 0; i < 6; i++)
        {
            if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("更新下载跳转到了不受支持的地址。");
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if ((int)response.StatusCode is < 300 or >= 400) return response;
            var next = response.Headers.Location; response.Dispose();
            uri = next is null ? throw new InvalidDataException("更新下载跳转无效。") : new Uri(uri, next);
        }
        throw new InvalidDataException("更新下载跳转过多。");
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximum, CancellationToken cancellation)
    {
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("更新信息过大。");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        { if (output.Length + count > maximum) throw new InvalidDataException("更新信息过大。"); await output.WriteAsync(buffer.AsMemory(0, count), cancellation).ConfigureAwait(false); }
        return output.ToArray();
    }
    public void Dispose() => client.Dispose();
}
