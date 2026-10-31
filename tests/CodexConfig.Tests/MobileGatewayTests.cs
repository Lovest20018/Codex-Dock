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
            foreach (var protocol in new[] { 1, 2, 3 })
            {
                var oldJson = JsonNode.Parse(JsonSerializer.Serialize(saved, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
                oldJson["protocol"] = protocol;
                if (protocol == 2) oldJson["proxyAddress"] = "192.168.8.2";
                await File.WriteAllBytesAsync(manager.SettingsPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(oldJson.ToJsonString()), Encoding.UTF8.GetBytes("CodexDock.mobile.v1"), DataProtectionScope.CurrentUser));
                Assert(manager.Load() == saved, "old settings preserve identity and credentials without a proxy IP requirement");
            }
            var preserved = await manager.SaveAsync(saved.PublicUrl, "tester", "", saved.Port);
            Assert(preserved.Auth == saved.Auth, "blank password preserves the saved hash");
            await Throws<ArgumentException>(() => manager.SaveAsync(saved.PublicUrl, "other", "", saved.Port));
            Assert(await File.ReadAllTextAsync(configPath) == original && !File.Exists(Path.Combine(home, "auth.json")), "mobile never rewrites model settings or official login");
            Assert(new MobileGatewayManager(Home(), Path.Combine(root, "mobile-settings-test")).StateDirectory != manager.StateDirectory, "home scopes do not overlap");
            bytes[0] ^= 0x7f; await File.WriteAllBytesAsync(manager.SettingsPath, bytes);
            await Throws<InvalidDataException>(() => Task.FromResult(manager.Load()));
        }));
        tests.Add(("mobile port changes preserve identity and credentials without a proxy setting", async () =>
        {
            var manager = new MobileGatewayManager(Home(), Path.Combine(root, "mobile-lan-settings-test"));
            foreach (var port in new[] { 0, 1023, 65536 })
                await Throws<ArgumentException>(() => manager.SaveAsync("https://phone.example.com", "tester", "isolated-test-password", port));
            Assert(!Directory.Exists(manager.StateDirectory), "invalid port must not create state or stop service");
            var previous = await manager.SaveAsync("https://phone.example.com", "tester", "isolated-test-password", Port());
            var saved = await manager.SaveAsync(previous.PublicUrl, "tester", "", Port());
            Assert(manager.Load() == saved && saved.Auth == previous.Auth && saved.InstanceId == previous.InstanceId, "port update preserves password and instance");
            Assert(!JsonSerializer.Serialize(saved, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Contains("proxyAddress"), "boot contract no longer includes a proxy IP");
        }));
        tests.Add(("mobile status treats a truncated health response as unavailable without changing settings", async () =>
        {
            var manager = new MobileGatewayManager(Home(), Path.Combine(root, "mobile-truncated-status"));
            var saved = await manager.SaveAsync("https://phone.example.com", "tester", "isolated-test-password", Port());
            using var listener = new TcpListener(IPAddress.Loopback, saved.ControlPort); listener.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var interrupted = Task.Run(async () =>
            {
                using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = peer.GetStream();
                await stream.ReadAsync(new byte[4096], timeout.Token);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 80\r\nConnection: close\r\n\r\n{"), timeout.Token);
            }, timeout.Token);
            Assert(await manager.StatusAsync(timeout.Token) is null, "partial health body must not escape as a transport exception");
            await interrupted;
            Assert(manager.Load() == saved, "unavailable status preserves encrypted settings and identity");
        }));
        tests.Add(("packaged mobile worker preserves Android login across source locks and restart, and revokes devices", async () =>
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
                Assert(starts.All(status => status.InstanceId == saved.InstanceId && status.Protocol == MobileGatewayManager.Protocol), "concurrent starts converge on one worker");
                var replacement = new MobileGatewayManager(home, Path.Combine(root, "mobile-process-test"));
                Assert(await replacement.StatusAsync(timeout.Token) is not null, "worker is independent of manager/UI lifetime");
                using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };
                async Task<(JsonDocument Data, string? Cookie)> Request(string path, object? body = null, string? cookie = null, string? csrf = null, string address = "127.0.0.1", HttpStatusCode expected = HttpStatusCode.OK)
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
                    using var response = await client.SendAsync(request, timeout.Token);
                    Assert(response.StatusCode == expected, $"mobile {path} returned expected {expected}");
                    var result = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(timeout.Token));
                    return (result, response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.First().Split(';')[0] : null);
                }
                var probe = await Request("/api/auth");
                using (probe.Data)
                {
                    Assert(!probe.Data.RootElement.GetProperty("authenticated").GetBoolean(), "Android unauthenticated probe");
                    Assert(probe.Data.RootElement.GetProperty("notifications").GetBoolean(), "frozen worker exposes upstream notifications");
                }
                var login = await Request("/api/login", new { username = "tester", password = "isolated-test-password" });
                string csrf; using (login.Data) csrf = login.Data.RootElement.GetProperty("csrf").GetString()!;
                var cookie = login.Cookie ?? throw new InvalidOperationException("missing secure session cookie");
                var authorized = await Request("/api/auth", cookie: cookie);
                using (authorized.Data)
                {
                    Assert(authorized.Data.RootElement.GetProperty("trustedDevice").GetBoolean(), "Android receives durable device authorization");
                    Assert(authorized.Data.RootElement.GetProperty("enabledClients").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "codex" }), "frozen navigation contains only Codex");
                }
                foreach (var asset in new[] { "/", "/accounts.js", "/cli-access.js", "/timeline.js", "/vendor/xterm/xterm.js" })
                {
                    using var assetRequest = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{saved.Port}" + asset);
                    assetRequest.Headers.Host = "phone.example.com";
                    using var assetResponse = await client.SendAsync(assetRequest, timeout.Token);
                    Assert(assetResponse.IsSuccessStatusCode && (await assetResponse.Content.ReadAsByteArrayAsync(timeout.Token)).Length > 100, "complete upstream assets are frozen: " + asset);
                }
                using (var localManager = await manager.ControlAsync("manager", new { }, timeout.Token))
                {
                    var url = new Uri(localManager.RootElement.GetProperty("url").GetString()!);
                    Assert(url.Host == "127.0.0.1" && url.Port == saved.ControlPort && url.AbsolutePath == "/manager/" && url.Fragment.Length > 32, "manager link is local with one-use fragment");
                    using var managerPage = await client.GetAsync(new UriBuilder(url) { Fragment = "" }.Uri, timeout.Token);
                    var managerHtml = await managerPage.Content.ReadAsStringAsync(timeout.Token);
                    Assert(managerPage.IsSuccessStatusCode && managerHtml.Contains("class=\"manager-tabs\"") && managerHtml.Contains("id=\"tab-api\""), "actual frozen manager tab assets are present");
                }
                await replacement.StopAsync(timeout.Token);
                Assert(await manager.StatusAsync(timeout.Token) is null, "authenticated stop closes worker");
                await manager.StartAsync(timeout.Token, executable);
                authorized = await Request("/api/auth", cookie: cookie);
                using (authorized.Data) Assert(authorized.Data.RootElement.GetProperty("authenticated").GetBoolean(), "native session survives restart");
                authorized = await Request("/api/auth", cookie: cookie, address: "127.0.0.2");
                using (authorized.Data) Assert(authorized.Data.RootElement.GetProperty("authenticated").GetBoolean(), "default listener accepts another IPv4 destination without proxy configuration");
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
                    fakeProxy.Headers.TryAddWithoutValidation("Cookie", cookie);
                    using var accepted = await peerClient.SendAsync(fakeProxy, timeout.Token);
                    Assert(accepted.IsSuccessStatusCode, "actual worker accepts an unmapped source without an IP allowlist");
                    using var data = JsonDocument.Parse(await accepted.Content.ReadAsByteArrayAsync(timeout.Token));
                    Assert(data.RootElement.GetProperty("authenticated").GetBoolean(), "Android cookie remains valid through a changed socket peer");
                }
                using (var peerDevices = await manager.ControlAsync("devices", new { action = "list" }, timeout.Token))
                    Assert(peerDevices.RootElement.GetProperty("sessions")[0].GetProperty("ip").GetString() == "127.0.0.2", "untrusted forwarding header cannot replace the socket peer");
                using (var privateProbe = new TcpClient())
                    await Throws<SocketException>(() => privateProbe.ConnectAsync("127.0.0.2", saved.ControlPort).WaitAsync(TimeSpan.FromSeconds(3)));
                var forbidden = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{saved.Port}/api/auth");
                forbidden.Headers.Host = "wrong.example.com";
                using (forbidden) using (var rejected = await client.SendAsync(forbidden, timeout.Token)) Assert(rejected.StatusCode == HttpStatusCode.Forbidden, "mapped access keeps Host validation");
                saved = await manager.SaveAsync(saved.PublicUrl, "tester", "", saved.Port, timeout.Token);
                Assert((await manager.StartAsync(timeout.Token, executable)).Protocol == MobileGatewayManager.Protocol, "saved settings restart without proxy configuration");
                authorized = await Request("/api/auth", cookie: cookie);
                using (authorized.Data) Assert(authorized.Data.RootElement.GetProperty("authenticated").GetBoolean(), "valid cookie returns to the shared source");
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var failed = await Request("/api/login", new { username = "tester", password = "wrong" }, expected: HttpStatusCode.Forbidden);
                    failed.Data.Dispose();
                }
                var blockedLogin = await Request("/api/login", new { username = "tester", password = "isolated-test-password" }, expected: HttpStatusCode.Forbidden);
                using (blockedLogin.Data) Assert(blockedLogin.Data.RootElement.GetProperty("loginStatus").GetProperty("blocked").GetBoolean(), "new authentication remains limited");
                await manager.StopAsync(timeout.Token);
                await manager.StartAsync(timeout.Token, executable);
                authorized = await Request("/api/auth", cookie: cookie);
                using (authorized.Data)
                {
                    Assert(authorized.Data.RootElement.GetProperty("authenticated").GetBoolean(), "shared-source failures preserve the original Android cookie across worker restart");
                    Assert(authorized.Data.RootElement.GetProperty("loginStatus").GetProperty("blocked").GetBoolean(), "source login limit persists across worker restart");
                }
                using (var unblocked = await manager.ControlAsync("devices", new { action = "unblock", ip = "127.0.0.1" }, timeout.Token)) { }
                authorized = await Request("/api/auth", cookie: cookie);
                using (authorized.Data) Assert(authorized.Data.RootElement.GetProperty("authenticated").GetBoolean(), "unblocking keeps the original cookie");
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
