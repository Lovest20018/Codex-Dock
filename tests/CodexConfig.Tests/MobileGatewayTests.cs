using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using CodexConfig.Core;

namespace CodexConfig.Tests;

public static class MobileGatewayTests
{
    public static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static async Task Throws<T>(Func<Task> action) where T : Exception
        {
            try { await action(); } catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
        static int Port() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
        string Home() { var home = Path.Combine(root, "mobile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home); return home; }
        tests.Add(("mobile HTTPS root validation excludes credentials, paths and loopback", async () =>
        {
            Assert(MobileGatewayManager.NormalizePublicUrl(" https://PHONE.example.com:443/ ") == "https://phone.example.com", "canonical HTTPS root");
            foreach (var value in new[] { "http://phone.example.com", "https://phone.example.com/proxy", "https://phone.example.com?secret=x", "https://phone.example.com/#pair=x",
                "https://user:secret@phone.example.com", "https://localhost", "https://a.localhost", "https://127.0.0.1", "https://[::1]", "https://a..com", "file:///C:/test" })
                await Throws<ArgumentException>(() => Task.FromResult(MobileGatewayManager.NormalizePublicUrl(value)));
        }));
        tests.Add(("mobile settings use per-home DPAPI storage and preserve Codex files", async () =>
        {
            var home = Home(); var configPath = Path.Combine(home, "config.toml"); var original = "# isolated test\nmodel = \"fake-model\"\n";
            await File.WriteAllTextAsync(configPath, original);
            var manager = new MobileGatewayManager(home, Path.Combine(root, "mobile-settings-test"));
            Assert(manager.Load() is null && !Directory.Exists(manager.StateDirectory), "load must not create data or start processes");
            await Throws<ArgumentException>(() => manager.SaveAsync("http://example.com", "tester", "isolated-test-password", Port()));
            Assert(!Directory.Exists(manager.StateDirectory), "invalid URL cannot create settings");
            var saved = await manager.SaveAsync("https://phone.example.com", "tester", "isolated-test-password", Port());
            var bytes = await File.ReadAllBytesAsync(manager.SettingsPath);
            Assert(!Encoding.UTF8.GetString(bytes).Contains("isolated-test-password") && !Encoding.UTF8.GetString(bytes).Contains(saved.ControlToken), "no plaintext password or control bearer on disk");
            Assert(manager.Load() == saved, "encrypted round trip");
            // Existing 0.13.0 DPAPI settings have no proxyAddress and protocol 1.
            var oldJson = JsonNode.Parse(JsonSerializer.Serialize(saved, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
            oldJson["protocol"] = 1; oldJson.AsObject().Remove("proxyAddress");
            await File.WriteAllBytesAsync(manager.SettingsPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(oldJson.ToJsonString()), Encoding.UTF8.GetBytes("CodexDock.mobile.v1"), DataProtectionScope.CurrentUser));
            Assert(manager.Load() == saved && manager.Load()!.ProxyAddress == "", "old settings migrate in memory with loopback default and unchanged identity");
            var preserved = await manager.SaveAsync(saved.PublicUrl, "tester", "", saved.Port);
            Assert(preserved.Auth == saved.Auth, "blank password preserves the saved hash");
            await Throws<ArgumentException>(() => manager.SaveAsync(saved.PublicUrl, "other", "", saved.Port));
            Assert(await File.ReadAllTextAsync(configPath) == original && !File.Exists(Path.Combine(home, "auth.json")), "mobile never rewrites model settings or official login");
            Assert(new MobileGatewayManager(Home(), Path.Combine(root, "mobile-settings-test")).StateDirectory != manager.StateDirectory, "home scopes do not overlap");
            bytes[0] ^= 0x7f; await File.WriteAllBytesAsync(manager.SettingsPath, bytes);
            await Throws<InvalidDataException>(() => Task.FromResult(manager.Load()));
        }));
        tests.Add(("mobile network access requires one explicit proxy and preserves login when changing transport", async () =>
        {
            foreach (var value in new[] { "192.168.8.2", "10.2.3.4", "172.16.1.2", "172.31.255.254", "198.51.100.2" })
                Assert(MobileGatewayManager.NormalizeProxyAddress(value) == value, "private canonical proxy");
            foreach (var value in new[] { "0.0.0.0", "0.2.3.4", "127.0.0.1", "224.0.0.1", "255.255.255.255", "169.254.1.2", "192.168.8.0/24", "*", "localhost", "::1", "192.168.008.2" })
                await Throws<ArgumentException>(() => Task.FromResult(MobileGatewayManager.NormalizeProxyAddress(value)));
            var manager = new MobileGatewayManager(Home(), Path.Combine(root, "mobile-lan-settings-test"));
            await Throws<ArgumentException>(() => manager.SaveAsync("https://phone.example.com", "tester", "isolated-test-password", Port(), proxyAddress: "*"));
            Assert(!Directory.Exists(manager.StateDirectory), "bad proxy must not create state or stop service");
            var previous = await manager.SaveAsync("https://phone.example.com", "tester", "isolated-test-password", Port());
            var saved = await manager.SaveAsync(previous.PublicUrl, "tester", "", previous.Port, proxyAddress: "192.168.8.2");
            Assert(saved.ProxyAddress == "192.168.8.2" && manager.Load() == saved && saved.Auth == previous.Auth && saved.InstanceId == previous.InstanceId, "transport update preserves password and instance");
        }));
        tests.Add(("packaged mobile worker survives manager disposal, persists Android login and revokes devices", async () =>
        {
            var executable = Path.GetFullPath("artifacts/mobile-worker/CodexDock.Mobile/CodexDock.Mobile.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("Build mobile worker before the full test suite.", executable);
            var home = Home(); var manager = new MobileGatewayManager(home, Path.Combine(root, "mobile-process-test"));
            var configPath = Path.Combine(home, "config.toml"); await File.WriteAllTextAsync(configPath, "# fake home, no native IPC calls\n");
            var saved = await manager.SaveAsync("https://phone.example.com", "tester", "isolated-test-password", Port());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                var starts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => manager.StartAsync(timeout.Token, executable)));
                Assert(starts.All(status => status.InstanceId == saved.InstanceId && status.Protocol == MobileGatewayManager.Protocol && status.ProxyAddress == ""), "concurrent starts converge on one worker");
                var replacement = new MobileGatewayManager(home, Path.Combine(root, "mobile-process-test"));
                Assert(await replacement.StatusAsync(timeout.Token) is not null, "worker is independent of manager/UI lifetime");
                using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };
                async Task<(JsonDocument Data, string? Cookie)> Request(string path, object? body = null, string? cookie = null, string? csrf = null, string address = "127.0.0.1")
                {
                    using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, $"http://{address}:{saved.Port}" + path);
                    request.Headers.Host = "phone.example.com"; request.Headers.UserAgent.ParseAdd("BridgeMobile/2.1.7-Android");
                    if (cookie is not null) request.Headers.TryAddWithoutValidation("Cookie", cookie);
                    if (body is not null)
                    {
                        request.Headers.TryAddWithoutValidation("Origin", saved.PublicUrl);
                        if (csrf is not null) request.Headers.TryAddWithoutValidation("X-CSRF-Token", csrf);
                        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                    }
                    using var response = await client.SendAsync(request, timeout.Token); response.EnsureSuccessStatusCode();
                    var result = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(timeout.Token));
                    return (result, response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.First().Split(';')[0] : null);
                }
                var probe = await Request("/api/auth"); using (probe.Data) Assert(!probe.Data.RootElement.GetProperty("authenticated").GetBoolean(), "Android unauthenticated probe");
                var login = await Request("/api/login", new { username = "tester", password = "isolated-test-password" });
                string csrf; using (login.Data) csrf = login.Data.RootElement.GetProperty("csrf").GetString()!;
                var cookie = login.Cookie ?? throw new InvalidOperationException("missing secure session cookie");
                var authorized = await Request("/api/auth", cookie: cookie);
                using (authorized.Data) Assert(authorized.Data.RootElement.GetProperty("trustedDevice").GetBoolean(), "Android receives durable device authorization");
                await replacement.StopAsync(timeout.Token);
                Assert(await manager.StatusAsync(timeout.Token) is null, "authenticated stop closes worker");
                await manager.StartAsync(timeout.Token, executable);
                authorized = await Request("/api/auth", cookie: cookie);
                using (authorized.Data) Assert(authorized.Data.RootElement.GetProperty("authenticated").GetBoolean(), "native session survives restart");
                saved = await manager.SaveAsync(saved.PublicUrl, "tester", "", saved.Port, timeout.Token, "192.168.8.2");
                var lanStatus = await manager.StartAsync(timeout.Token, executable);
                Assert(lanStatus.ProxyAddress == "192.168.8.2", "worker reports the configured exact LAN proxy");
                authorized = await Request("/api/auth", cookie: cookie, address: "127.0.0.2");
                using (authorized.Data) Assert(authorized.Data.RootElement.GetProperty("authenticated").GetBoolean(), "wildcard bind accepts another local IPv4 destination without losing existing login");
                using (var peerHandler = new SocketsHttpHandler
                {
                    UseProxy = false,
                    ConnectCallback = async (context, cancellation) =>
                    {
                        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        try
                        {
                            socket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
                            await socket.ConnectAsync(context.DnsEndPoint, cancellation);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch { socket.Dispose(); throw; }
                    }
                })
                using (var peerClient = new HttpClient(peerHandler) { Timeout = TimeSpan.FromSeconds(5) })
                using (var fakeProxy = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{saved.Port}/api/auth"))
                {
                    fakeProxy.Headers.Host = "phone.example.com";
                    fakeProxy.Headers.TryAddWithoutValidation("X-Forwarded-For", "192.168.8.2");
                    using var denied = await peerClient.SendAsync(fakeProxy, timeout.Token);
                    Assert(denied.StatusCode == HttpStatusCode.Forbidden, "actual worker rejects an unlisted socket peer despite forged forwarding header");
                }
                Assert((await manager.StatusAsync(timeout.Token))!.LastRejectedPeer == "127.0.0.2", "private status reports the actual rejected source for NAT diagnosis");
                using (var privateProbe = new TcpClient())
                    await Throws<SocketException>(() => privateProbe.ConnectAsync("127.0.0.2", saved.ControlPort).WaitAsync(TimeSpan.FromSeconds(3)));
                var forbidden = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{saved.Port}/api/auth");
                forbidden.Headers.Host = "wrong.example.com";
                using (forbidden) using (var rejected = await client.SendAsync(forbidden, timeout.Token)) Assert(rejected.StatusCode == HttpStatusCode.Forbidden, "LAN mode keeps Host validation");
                saved = await manager.SaveAsync(saved.PublicUrl, "tester", "", saved.Port, timeout.Token);
                Assert((await manager.StartAsync(timeout.Token, executable)).ProxyAddress == "", "LAN mode can be disabled while preserving authorization");
                using var devices = await manager.ControlAsync("devices", new { action = "list" }, timeout.Token);
                var id = devices.RootElement.GetProperty("sessions")[0].GetProperty("id").GetString();
                using var revoked = await manager.ControlAsync("devices", new { action = "revoke", id }, timeout.Token);
                authorized = await Request("/api/auth", cookie: cookie);
                using (authorized.Data) Assert(!authorized.Data.RootElement.GetProperty("authenticated").GetBoolean(), "revocation defeats native cookie restore");
                Assert(await File.ReadAllTextAsync(configPath) == "# fake home, no native IPC calls\n" && !File.Exists(Path.Combine(home, "auth.json")), "worker does not alter Codex home");
            }
            finally { await manager.StopAsync(CancellationToken.None); }
        }));
    }
}
