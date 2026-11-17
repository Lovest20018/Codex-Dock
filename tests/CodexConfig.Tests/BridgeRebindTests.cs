using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CodexConfig.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

internal static class BridgeRebindTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("model bridge retires a replaced WebSocket only after its response finishes", () => CheckAsync(root)));
        tests.Add(("model bridge refuses late requests while gracefully retiring an idle old route", () => CheckAsync(root, sendLateMessage: true)));
        tests.Add(("socket route fences idle sends atomically and keeps unknown messages busy", () =>
        {
            var key = "A"; var route = new BridgeSocketRoute(key, () => key);
            if (!route.BeginMessage()) throw new Exception("Initial request rejected");
            key = "B"; if (route.TryRetire()) throw new Exception("Busy response was retired");
            route.ClientMessage(Encoding.UTF8.GetBytes("{\"type\":\"response.create\"}"));
            route.ServerMessage(Encoding.UTF8.GetBytes("{\"type\":\"response.completed\"}"));
            if (!route.TryRetire() || route.BeginMessage()) throw new Exception("Replaced route accepted another message");
            return Task.CompletedTask;
        }));
    }
    private static async Task CheckAsync(string root, bool sendLateMessage = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestsA = 0; var requestsB = 0; var expectedKeys = true;
        async Task<WebApplication> Upstream(string label)
        {
            var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build(); app.UseWebSockets();
            app.Map("/v1/responses", async context =>
            {
                expectedKeys &= context.Request.Headers.Authorization == "Bearer fake-model-" + label;
                if (!context.WebSockets.IsWebSocketRequest) { await context.Response.WriteAsync(label); return; }
                using var socket = await context.WebSockets.AcceptWebSocketAsync(); var buffer = new byte[4096];
                try
                {
                    var input = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                    if (input.MessageType == WebSocketMessageType.Close) return;
                    if (label == "A") Interlocked.Increment(ref requestsA); else Interlocked.Increment(ref requestsB);
                    async Task Send(string type) => await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, response = new { id = "resp_fixture", status = type == "response.completed" ? "completed" : "in_progress", endpoint = label, output = Array.Empty<object>() } })).AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
                    await Send("response.created");
                    if (label == "A") { entered.TrySetResult(); await release.Task.WaitAsync(timeout.Token); }
                    await Send("response.completed");
                    // Keep the peer alive until the bridge retires this stream or the test closes it.
                    while (socket.State == WebSocketState.Open)
                    {
                        if ((await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token)).MessageType == WebSocketMessageType.Close)
                        { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "acknowledged", timeout.Token); return; }
                        if (label == "A") Interlocked.Increment(ref requestsA); else Interlocked.Increment(ref requestsB);
                    }
                }
                catch (Exception error) when (error is WebSocketException or OperationCanceledException) { }
            });
            await app.StartAsync(timeout.Token); return app;
        }
        await using var a = await Upstream("A"); await using var b = await Upstream("B");
        var home = Path.Combine(root, "bridge-rebind-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
        var connection = MixedConnection.Create(new(a.Urls.Single() + "/v1", "fake-model-A", "model") { Kind = ConnectionKind.ResponsesApi });
        var path = Path.Combine(home, MixedConnection.FileName); File.WriteAllBytes(path, connection.Protect());
        await using var bridge = new ModelBridge(() => MixedConnection.Read(File.ReadAllBytes(path)), NativeBridgeTests.Auth);
        await bridge.StartAsync(timeout.Token);
        async Task<ClientWebSocket> Open()
        {
            var socket = new ClientWebSocket(); socket.Options.Proxy = null; socket.Options.SetRequestHeader("Authorization", "Bearer " + connection.LocalToken);
            await socket.ConnectAsync(new Uri(connection.LocalBaseUrl.Replace("http://", "ws://") + "/responses"), timeout.Token); return socket;
        }
        async Task Send(ClientWebSocket socket) => await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"response.create\",\"model\":\"model\",\"input\":[]}").AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
        async Task<(WebSocketMessageType Type, string Text)> Read(ClientWebSocket socket)
        { var buffer = new byte[4096]; var result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token); return (result.MessageType, Encoding.UTF8.GetString(buffer, 0, result.Count)); }
        using var first = await Open();
        try
        {
            await Send(first); var created = await Read(first); await entered.Task.WaitAsync(timeout.Token);
            if (!created.Text.Contains("response.created")) throw new Exception("First response did not begin");
            File.WriteAllBytes(path, (connection with { BaseUrl = b.Urls.Single() + "/v1", ApiKey = "fake-model-B" }).Protect());
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }); http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.LocalToken);
            using var response = await http.PostAsync(connection.LocalBaseUrl + "/responses", new StringContent("{}"), timeout.Token);
            if (await response.Content.ReadAsStringAsync(timeout.Token) != "B") throw new Exception("New HTTP request retained old route");
            var completion = Read(first); await Task.Delay(500, timeout.Token);
            if (completion.IsCompleted) throw new Exception("Route update interrupted an active response");
            release.TrySetResult(); var completed = await completion;
            if (completed.Type != WebSocketMessageType.Text || !completed.Text.Contains("response.completed") || !completed.Text.Contains("\"A\"")) throw new Exception("Original response was not delivered intact");
            if (sendLateMessage) await Send(first);
            var close = await Read(first);
            if (close.Type != WebSocketMessageType.Close || first.CloseStatus != WebSocketCloseStatus.NormalClosure) throw new Exception("Idle old socket was not retired with a normal close frame");
            await first.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "acknowledged", timeout.Token);
            using var second = await Open();
            try
            {
                await Send(second); _ = await Read(second); var next = await Read(second);
                if (!next.Text.Contains("\"B\"") || requestsA != 1 || requestsB != 1 || !expectedKeys) throw new Exception("New socket used the wrong route or credential");
            }
            finally { second.Abort(); }
        }
        finally { release.TrySetResult(); first.Abort(); }
    }
}
