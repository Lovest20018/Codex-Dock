using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexConfig.Core;

internal static class UpdateTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        var installer = Encoding.UTF8.GetBytes("fake offline installer bytes; never executed");
        var hash = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        const string name = "CodexDock-1.2.3-Setup-win-x64.exe";
        const string download = GitHubUpdates.Repository + "/releases/download/v1.2.3/";
        string Release(bool prerelease = false, string origin = download, string? digest = null) => JsonSerializer.Serialize(new
        {
            tag_name = "v1.2.3", draft = false, prerelease,
            assets = new[] { new { name, size = installer.Length, browser_download_url = origin + name, digest = digest ?? "sha256:" + hash },
                new { name = "SHA256SUMS-1.2.3.txt", size = 100, browser_download_url = origin + "SHA256SUMS-1.2.3.txt", digest = "" } }
        });
        HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
        tests.Add(("updater uses fixed stable GitHub releases and compares semantic versions", async () =>
        {
            using var client = new GitHubUpdates(new FakeHandler(request =>
            {
                Assert(request.RequestUri!.AbsoluteUri == GitHubUpdates.LatestApi && request.Headers.Authorization is null);
                return Text(Release());
            }));
            Assert((await client.CheckAsync(new(1, 2, 2)))?.Version == new Version(1, 2, 3));
            Assert(await client.CheckAsync(new(1, 2, 3, 0)) is null && await client.CheckAsync(new(2, 0, 0)) is null);
            using var preview = new GitHubUpdates(new FakeHandler(_ => Text(Release(true))));
            Assert(await preview.CheckAsync(new(1, 0, 0)) is null);
            using var empty = new GitHubUpdates(new FakeHandler(_ => new(HttpStatusCode.NotFound)));
            Assert(await empty.CheckAsync(new(1, 0, 0)) is null);
            using var foreign = new GitHubUpdates(new FakeHandler(_ => Text(Release(origin: "https://example.invalid/"))));
            await Throws<InvalidDataException>(() => foreign.CheckAsync(new(1, 0, 0)));
        }));
        tests.Add(("updater verifies checksum and GitHub digest before exposing an installer", async () =>
        {
            using var client = new GitHubUpdates(new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
            {
                GitHubUpdates.LatestApi => Text(Release()),
                download + name => new(HttpStatusCode.OK) { Content = new ByteArrayContent(installer) },
                _ => Text(hash + "  " + name + "\n")
            }));
            var update = (await client.CheckAsync(new(1, 0, 0)))!;
            var result = await client.DownloadAsync(update, Path.Combine(root, "verified-update"));
            Assert(File.ReadAllBytes(result.Path).SequenceEqual(installer));
            using (result.OpenVerified()) { }
            File.WriteAllText(result.Path, "changed after verification");
            try { using var file = result.OpenVerified(); throw new InvalidOperationException("Modified installer accepted."); } catch (InvalidDataException) { }
        }));
        tests.Add(("updater rejects corrupt downloads and untrusted redirects without leaving partial executables", async () =>
        {
            var directory = Path.Combine(root, "corrupt-update");
            using var client = new GitHubUpdates(new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
            {
                GitHubUpdates.LatestApi => Text(Release()),
                download + name => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[installer.Length]) },
                _ => Text(hash + "  " + name)
            }));
            var update = (await client.CheckAsync(new(1, 0, 0)))!;
            await Throws<InvalidDataException>(() => client.DownloadAsync(update, directory));
            Assert(!Directory.EnumerateFiles(directory).Any());
            using var redirected = new GitHubUpdates(new FakeHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new("http://github.com/unsafe"); return response;
            }));
            await Throws<InvalidDataException>(() => redirected.CheckAsync(new(1, 0, 0)));
            using var conflict = new GitHubUpdates(new FakeHandler(request => request.RequestUri!.AbsoluteUri == GitHubUpdates.LatestApi
                ? Text(Release(digest: "sha256:" + new string('0', 64))) : Text(hash + "  " + name)));
            var conflicting = (await conflict.CheckAsync(new(1, 0, 0)))!;
            await Throws<InvalidDataException>(() => conflict.DownloadAsync(conflicting, directory));
        }));
    }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Assertion failed."); }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
