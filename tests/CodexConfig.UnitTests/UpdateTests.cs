using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexConfig.Core;

using Xunit;

namespace CodexConfig.UnitTests;

public sealed class UpdateTests : IDisposable
{
    private static readonly byte[] installer = Encoding.UTF8.GetBytes("fake offline installer bytes; never executed");
    private static readonly string hash = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
    private const string name = "CodexDock-1.2.3-Setup-win-x64.exe";
    private const string download = GitHubUpdates.Repository + "/releases/download/v1.2.3/";
    private static string Release(bool prerelease = false, string origin = download, string? digest = null) => JsonSerializer.Serialize(new
    {
        tag_name = "v1.2.3", draft = false, prerelease,
        assets = new[] { new { name, size = installer.Length, browser_download_url = origin + name, digest = digest ?? "sha256:" + hash },
            new { name = "SHA256SUMS-1.2.3.txt", size = 100, browser_download_url = origin + "SHA256SUMS-1.2.3.txt", digest = "" } }
    });
    private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };

    [Fact(DisplayName = "updater uses fixed stable GitHub releases and compares semantic versions")]
    public async Task ChecksStableGitHubVersions()
    {
        using var client = new GitHubUpdates(new FakeHandler(request =>
        {
            Assert.True(request.RequestUri!.AbsoluteUri == GitHubUpdates.LatestApi && request.Headers.Authorization is null);
            return Text(Release());
        }));
        Assert.True((await client.CheckAsync(new(1, 2, 2)))?.Version == new Version(1, 2, 3));
        Assert.True(await client.CheckAsync(new(1, 2, 3, 0)) is null && await client.CheckAsync(new(2, 0, 0)) is null);
        using var preview = new GitHubUpdates(new FakeHandler(_ => Text(Release(true))));
        Assert.True(await preview.CheckAsync(new(1, 0, 0)) is null);
        using var empty = new GitHubUpdates(new FakeHandler(_ => new(HttpStatusCode.NotFound)));
        Assert.True(await empty.CheckAsync(new(1, 0, 0)) is null);
        using var foreign = new GitHubUpdates(new FakeHandler(_ => Text(Release(origin: "https://example.invalid/"))));
        await Assert.ThrowsAsync<InvalidDataException>(() => foreign.CheckAsync(new(1, 0, 0)));
    }

    [Fact(DisplayName = "updater verifies checksum and GitHub digest before exposing an installer")]
    public async Task VerifiesChecksumsBeforeExposingInstaller()
    {
        using var client = new GitHubUpdates(new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            GitHubUpdates.LatestApi => Text(Release()),
            download + name => new(HttpStatusCode.OK) { Content = new ByteArrayContent(installer) },
            _ => Text(hash + "  " + name + "\n")
        }));
        var update = (await client.CheckAsync(new(1, 0, 0)))!;
        var result = await client.DownloadAsync(update, directory);
        Assert.True(File.ReadAllBytes(result.Path).SequenceEqual(installer));
        using (result.OpenVerified()) { }
        File.WriteAllText(result.Path, "changed after verification");
        Assert.Throws<InvalidDataException>(() => result.OpenVerified().Dispose());
    }

    [Fact(DisplayName = "updater falls back to GitHub latest page when the REST API is rate limited")]
    public async Task FallsBackWhenGitHubRateLimitsRestApi()
    {
        using var client = new GitHubUpdates(new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri == GitHubUpdates.LatestApi) return new(HttpStatusCode.Forbidden);
            if (request.RequestUri.AbsoluteUri == GitHubUpdates.Repository + "/releases/latest")
            { var redirect = new HttpResponseMessage(HttpStatusCode.Redirect); redirect.Headers.Location = new(GitHubUpdates.Repository + "/releases/tag/v1.2.3"); return redirect; }
            if (request.Method == HttpMethod.Head) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(installer) };
            return Text("release page HTML ignored");
        }));
        var update = (await client.CheckAsync(new(1, 2, 2)))!;
        Assert.True(update.Version == new Version(1, 2, 3) && update.Size == installer.Length && update.Digest is null);
        Assert.True(await client.CheckAsync(new(1, 2, 3)) is null);
    }

    [Fact(DisplayName = "updater rejects corrupt downloads and untrusted redirects without leaving partial executables")]
    public async Task RejectsUntrustedOrCorruptDownloads()
    {
        using var client = new GitHubUpdates(new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            GitHubUpdates.LatestApi => Text(Release()),
            download + name => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[installer.Length]) },
            _ => Text(hash + "  " + name)
        }));
        var update = (await client.CheckAsync(new(1, 0, 0)))!;
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(update, directory));
        Assert.Empty(Directory.EnumerateFiles(directory));
        using var redirected = new GitHubUpdates(new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new("http://github.com/unsafe"); return response;
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => redirected.CheckAsync(new(1, 0, 0)));
        using var conflict = new GitHubUpdates(new FakeHandler(request => request.RequestUri!.AbsoluteUri == GitHubUpdates.LatestApi
            ? Text(Release(digest: "sha256:" + new string('0', 64))) : Text(hash + "  " + name)));
        var conflicting = (await conflict.CheckAsync(new(1, 0, 0)))!;
        await Assert.ThrowsAsync<InvalidDataException>(() => conflict.DownloadAsync(conflicting, directory));
    }

    private readonly string directory = Directory.CreateTempSubdirectory("codex-dock-updates-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responder(request));
        }
    }
}
