using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class UpstreamGatewayReuseTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("ordinary reasoning descriptors always retain the required native description field", () =>
        {
            foreach (var description in new string?[] { null, "", "High", "Line one\nLine two", new('x', 1001) })
            {
                var descriptor = new Dictionary<string, object?> { ["effort"] = "high" };
                if (description is not null) descriptor["description"] = description;
                var input = JsonSerializer.SerializeToUtf8Bytes(new { data = new[] { new { id = "reasoning-fixture", supported_reasoning_levels = new[] { descriptor }, default_reasoning_level = "high" } } });
                using var converted = JsonDocument.Parse(NativeModelCatalog.Convert(input));
                var model = converted.RootElement.GetProperty("models")[0];
                Assert(model.GetProperty("supported_reasoning_levels")[0].TryGetProperty("description", out var text) && text.ValueKind == JsonValueKind.String &&
                    text.GetString() == (description == "High" ? "High" : "") && model.GetProperty("default_reasoning_level").GetString() == "high");
            }
            return Task.CompletedTask;
        }));
        tests.Add(("direct connection saves ignore hidden gateway wait drafts and retain the saved wait", () =>
        {
            foreach (var input in new[] { "", "59", "invalid", "3601", "900" })
            {
                var home = Path.Combine(root, "direct-hidden-wait-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
                File.WriteAllText(Path.Combine(home, "config.toml"), "model='fixture-model'\nopenai_base_url='https://fixture.invalid/v1'\n");
                File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-direct-key\"}");
                File.WriteAllBytes(Path.Combine(home, ConnectionDetails.FileName), new ConnectionDetails { GatewayHeaderTimeoutSeconds = 600 }.Protect()!);
                var store = new ConfigStore(new MemoryEnvironment(), _ => null); var before = store.Load(home);
                using var draft = new ConnectionDraftViewModel(store); draft.Load(before);
                draft.Mode = ConnectionMode.OfficialWithApi; draft.GatewayHeaderTimeout = input;
                if (input != "900") Throws<ArgumentException>(() => draft.Request());
                draft.Mode = ConnectionMode.Direct;
                Assert(!draft.HasDraft && !draft.DetailsChanged && draft.GatewayHeaderTimeout == input && draft.Settings().Details.GatewayHeaderTimeoutSeconds == 600);
                draft.Model = "direct-changed-model"; draft.Name = "Direct connection";
                var session = new WorkspaceSession(home); session.Loaded(home, before); var save = new ConfigurationSaveService(store, new(session));
                var review = save.PrepareReview(draft.Request()); var saved = save.Save(review, before, () => home, () => { });
                Assert(saved.Succeeded && saved.Saved is not null && store.ReadSettings(saved.Saved).Details.GatewayHeaderTimeoutSeconds == 600 &&
                    new TomlDocument(saved.Saved.Config).String("model") == "direct-changed-model" && before.Auth!.AsSpan().SequenceEqual(saved.Saved.Auth!));
                draft.Mode = ConnectionMode.OfficialWithApi;
                if (input != "900") Throws<ArgumentException>(() => draft.Request());
                else Assert(draft.Settings().Details.GatewayHeaderTimeoutSeconds == 900);
            }
            return Task.CompletedTask;
        }));
        tests.Add(("ordinary API model catalogs retain declared image reasoning context and speed capabilities without inventing defaults", () =>
        {
            var bytes = Encoding.UTF8.GetBytes("""
                {"data":[{"id":"fixture-vision","input_modalities":["text","image"],"context_window":1048576,
                "supports_image_detail_original":true,"supports_reasoning_effort_updates":true,
                "supported_reasoning_levels":[{"effort":"high","description":"High"}],"default_reasoning_level":"high",
                "service_tiers":[{"id":"priority","name":"Priority"}],"private_unknown":{"token":"never-copy"}},
                {"id":"fixture-no-capabilities"}]}
                """);
            using var doc = JsonDocument.Parse(NativeModelCatalog.Convert(bytes)); var models = doc.RootElement.GetProperty("models"); var first = models[0]; var second = models[1];
            Assert(first.GetProperty("input_modalities").GetArrayLength() == 2 && first.GetProperty("context_window").GetInt64() == 1048576 &&
                first.GetProperty("supports_image_detail_original").GetBoolean() && first.GetProperty("supports_reasoning_effort_updates").GetBoolean() &&
                first.GetProperty("default_reasoning_level").GetString() == "high" && first.GetProperty("service_tiers")[0].GetProperty("id").GetString() == "priority" && !first.TryGetProperty("private_unknown", out _));
            Assert(second.GetProperty("input_modalities").EnumerateArray().Single().GetString() == "text" && second.GetProperty("service_tiers").GetArrayLength() == 0 && !second.TryGetProperty("context_window", out _));
            return Task.CompletedTask;
        }));
        tests.Add(("malformed API capability declarations fall back conservatively and native descriptors preserve future metadata", () =>
        {
            var bytes = Encoding.UTF8.GetBytes("""
                {"models":[{"id":"bad-fields","context_window":"1048576","input_modalities":["image",7],"supports_search_tool":"true",
                "supported_reasoning_levels":[{"effort":"high"},{"effort":null}],"default_reasoning_level":"high","service_tiers":[{"id":"priority"},"ultrafast"]},
                {"slug":"native-model","supported_reasoning_levels":[],"context_window":999999,"input_modalities":["text","image"],"future_metadata":{"keep":42}}]}
                """);
            using var doc = JsonDocument.Parse(NativeModelCatalog.Convert(bytes)); var models = doc.RootElement.GetProperty("models");
            Assert(!models[0].TryGetProperty("context_window", out _) && models[0].GetProperty("service_tiers").GetArrayLength() == 0 &&
                models[0].GetProperty("supported_reasoning_levels").GetArrayLength() == 0 && models[0].GetProperty("default_reasoning_level").ValueKind == JsonValueKind.Null && !models[0].GetProperty("supports_search_tool").GetBoolean());
            Assert(models[1].GetProperty("future_metadata").GetProperty("keep").GetInt32() == 42 && models[1].GetProperty("context_window").GetInt64() == 999999);
            return Task.CompletedTask;
        }));
        tests.Add(("gateway header wait is encrypted reviewed and restored independently of native configuration and identity", () =>
        {
            var home = Path.Combine(root, "gateway-wait-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "config.toml"), "model='fixture-model'\n"); File.WriteAllBytes(Path.Combine(home, "auth.json"), LegacyConnectionTests.Auth());
            var store = new ConfigStore(new MemoryEnvironment(), _ => null); var settings = Settings();
            store.Apply(store.Plan(store.Load(home), settings)); var before = store.Load(home); var connection = MixedConnection.Read(before.Bridge!);
            var session = new WorkspaceSession(home); session.Loaded(home, before); var save = new ConfigurationSaveService(store, new(session));
            using var draft = new ConnectionDraftViewModel(store); draft.Load(before); draft.GatewayHeaderTimeout = "600";
            var review = save.PrepareReview(draft.Request());
            Assert(before.SameContents(store.Load(home)) && review.Changes.Any(change => change.Previous == "120 秒" && change.Next.Contains("600 秒")) && !review.Description.Contains(settings.ApiKey));
            var result = save.Save(review, before, () => home, () => { }); Assert(result.Succeeded && result.Saved is not null);
            var saved = result.Saved!; var changed = MixedConnection.Read(saved.Bridge!);
            Assert(saved.Config!.AsSpan().SequenceEqual(before.Config!) && saved.Auth!.AsSpan().SequenceEqual(before.Auth!) &&
                changed.HeaderTimeoutSeconds == 600 && changed.Port == connection.Port && changed.LocalToken == connection.LocalToken &&
                store.ReadSettings(saved).Details.GatewayHeaderTimeoutSeconds == 600);
            store.Restore(saved); Assert(before.SameContents(store.Load(home)));
            foreach (var seconds in new[] { 0, 59, 3601 }) Throws<ArgumentException>(() => new ConnectionDetails { GatewayHeaderTimeoutSeconds = seconds }.Normalize());
            Throws<InvalidDataException>(() => MixedConnection.Read((connection with { HeaderTimeoutSeconds = 0 }).Protect()));
            return Task.CompletedTask;
        }));
        tests.Add(("a running gateway keeps each request header budget while later requests use the newly saved wait", async () =>
        {
            var clock = new AdvancingClock(); var connection = MixedConnection.Create(Settings()) with { HeaderTimeoutSeconds = 600 };
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            using var handler = new AsyncHandler(async (_, cancellation) =>
            {
                Interlocked.Increment(ref calls); entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellation); }
                catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
                throw new InvalidOperationException();
            });
            await using var bridge = new ModelBridge(() => connection, () => null, handler, timeProvider: clock); await bridge.StartAsync();
            using var client = Client(connection); var pending = client.PostAsync(connection.NativeBaseUrl + "/responses", Body()); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            connection = connection with { HeaderTimeoutSeconds = 60 }; clock.Advance(TimeSpan.FromSeconds(61));
            Assert(!cancelled.Task.IsCompleted && !pending.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(540)); using var response = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert(response.StatusCode == HttpStatusCode.GatewayTimeout && (await response.Content.ReadAsStringAsync()).Contains("model_response_headers_timeout") && calls == 1);
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously); cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = client.PostAsync(connection.NativeBaseUrl + "/responses", Body()); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromSeconds(61)); using var next = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert(next.StatusCode == HttpStatusCode.GatewayTimeout && calls == 2);
            var status = await BridgeProcess.StatusAsync(connection); Assert(status?.Metrics?.LastStreamEnd == "headers_timeout");
        }));
        tests.Add(("receiving headers ends the header deadline and subsequent response data uses its own idle budget", async () =>
        {
            var clock = new AdvancingClock(); var connection = MixedConnection.Create(Settings()) with { HeaderTimeoutSeconds = 60 };
            using var stream = new WaitingStream();
            using var handler = new AsyncHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
            await using var bridge = new ModelBridge(() => connection, () => null, handler, timeProvider: clock); await bridge.StartAsync();
            using var client = Client(connection); var pending = client.PostAsync(connection.NativeBaseUrl + "/responses", Body());
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); clock.Advance(TimeSpan.FromSeconds(61));
            Assert(!pending.IsCompleted && !stream.Cancelled.Task.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(240)); using var response = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert(response.StatusCode == HttpStatusCode.GatewayTimeout && (await response.Content.ReadAsStringAsync()).Contains("model_stream_idle_timeout"));
            var status = await BridgeProcess.StatusAsync(connection); Assert(status?.Metrics?.LastStreamEnd == "stream_idle_timeout");
        }));
        tests.Add(("cancelling a header wait cancels the upstream once and releases its active request", async () =>
        {
            var connection = MixedConnection.Create(Settings()); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
            using var handler = new AsyncHandler(async (_, cancellation) =>
            { calls++; entered.TrySetResult(); try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellation); } catch (OperationCanceledException) { cancelled.TrySetResult(); throw; } throw new InvalidOperationException(); });
            await using var bridge = new ModelBridge(() => connection, () => null, handler); await bridge.StartAsync(); using var client = Client(connection);
            using var cancellation = new CancellationTokenSource(); var pending = client.PostAsync(connection.NativeBaseUrl + "/responses", Body(), cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel(); try { await pending; } catch (OperationCanceledException) { }
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var attempt = 0; attempt < 50 && (await BridgeProcess.StatusAsync(connection))?.Metrics?.Active != 0; attempt++) await Task.Delay(10);
            Assert(calls == 1 && (await BridgeProcess.StatusAsync(connection))?.Metrics?.Active == 0);
        }));
        tests.Add(("catalog and error bodies reset the idle budget on data and classify a later stall by phase", async () =>
        {
            foreach (var isCatalog in new[] { true, false })
            {
                var clock = new AdvancingClock(); var connection = MixedConnection.Create(Settings()); using var stream = new PulsedStream();
                using var handler = new AsyncHandler((_, _) => Task.FromResult(new HttpResponseMessage(isCatalog ? HttpStatusCode.OK : HttpStatusCode.BadRequest) { Content = new StreamContent(stream) }));
                await using var bridge = new ModelBridge(() => connection, () => null, handler, timeProvider: clock); await bridge.StartAsync(); using var client = Client(connection);
                var pending = isCatalog ? client.GetAsync(connection.NativeBaseUrl + "/models") : client.PostAsync(connection.NativeBaseUrl + "/responses", Body());
                await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); clock.Advance(TimeSpan.FromSeconds(299)); stream.Pulse.TrySetResult();
                await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5)); clock.Advance(TimeSpan.FromSeconds(2)); Assert(!stream.Cancelled.Task.IsCompleted && !pending.IsCompleted);
                clock.Advance(TimeSpan.FromSeconds(299)); using var response = await pending.WaitAsync(TimeSpan.FromSeconds(5));
                Assert(response.StatusCode == HttpStatusCode.GatewayTimeout && (await response.Content.ReadAsStringAsync()).Contains("model_stream_idle_timeout") &&
                    (await BridgeProcess.StatusAsync(connection))?.Metrics?.LastStreamEnd == "stream_idle_timeout");
            }
        }));
        tests.Add(("compaction rejection preserves trigger and tool history with no retry or synthetic summary", async () =>
        {
            var connection = MixedConnection.Create(Settings()); var calls = 0;
            const string original = """{"stream":true,"input":[{"type":"function_call","call_id":"fixture-call","name":"fixture","arguments":"{}"},{"type":"function_call_output","call_id":"fixture-call","output":"retain-tool-history"},{"type":"compaction","encrypted_content":"foreign-opaque-value"},{"type":"compaction_trigger"}]}""";
            var status = HttpStatusCode.BadRequest; var code = "subscription_sharing_unsupported_capability";
            using var handler = new AsyncHandler(async (request, cancellation) =>
            {
                calls++; Assert(await request.Content!.ReadAsStringAsync(cancellation) == original);
                return new(status) { Content = new StringContent(JsonSerializer.Serialize(new { error = new { code, message = "private-upstream-error" } }), Encoding.UTF8, "application/json") };
            });
            await using var bridge = new ModelBridge(() => connection, () => null, handler); await bridge.StartAsync(); using var client = Client(connection);
            foreach (var item in new[] { (HttpStatusCode.BadRequest, "subscription_sharing_unsupported_capability"), (HttpStatusCode.UnprocessableEntity, "unsupported_compaction"), (HttpStatusCode.BadRequest, "unrelated_error") })
            {
                (status, code) = item; using var response = await client.PostAsync(connection.NativeBaseUrl + "/responses", new StringContent(original, Encoding.UTF8, "application/json"));
                var body = await response.Content.ReadAsStringAsync(); Assert(response.StatusCode == status && body.Contains(code) && !body.Contains("private-upstream-error") && !body.Contains("summary"));
            }
            Assert(calls == 3);
        }));
        tests.Add(("native and incomplete compaction streams pass unchanged without inventing a completion", async () =>
        {
            var connection = MixedConnection.Create(Settings()); var output = "";
            using var handler = new AsyncHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(output, Encoding.UTF8, "text/event-stream") }));
            await using var bridge = new ModelBridge(() => connection, () => null, handler); await bridge.StartAsync(); using var client = Client(connection);
            foreach (var stream in new[] { "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[{\"type\":\"compaction\",\"encrypted_content\":\"native-opaque\"}]}}\n\n", "data: {\"type\":\"response.created\",\"response\":{\"status\":\"in_progress\"}}\n\n" })
            {
                output = stream; using var response = await client.PostAsync(connection.NativeBaseUrl + "/responses", Body()); Assert(await response.Content.ReadAsStringAsync() == stream);
            }
        }));
    }
    private static ConnectionSettings Settings() => new("https://gateway-fixture.invalid/v1", "fake-upstream-reuse-key", "fixture-model") { Kind = ConnectionKind.ResponsesApi, Mode = ConnectionMode.OfficialWithApi, PreservePreferenceValues = true };
    private static StringContent Body() => new("{}", Encoding.UTF8, "application/json");
    private static HttpClient Client(MixedConnection connection)
    { var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) }; client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.LocalToken); return client; }
    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request, cancellationToken); }
    private sealed class MemoryEnvironment : IUserEnvironment { public string? Read() => null; public void Write(string? value) { } }
    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Upstream gateway reuse assertion failed"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class WaitingStream : Stream
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { Entered.TrySetResult(); try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); } catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; } return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException(); public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class PulsedStream : Stream
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Pulse = new(TaskCreationOptions.RunContinuationsAsynchronously),
            Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously), Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool first = true;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                if (first) { first = false; Entered.TrySetResult(); await Pulse.Task.WaitAsync(cancellationToken); buffer.Span[0] = (byte)' '; return 1; }
                Waiting.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0;
            }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException(); public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class AdvancingClock : TimeProvider
    {
        private TimeSpan elapsed; private readonly List<ManualTimer> timers = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new ManualTimer(this, callback, state); lock (timers) timers.Add(timer); timer.Change(dueTime, period); return timer; }
        internal void Advance(TimeSpan duration)
        {
            ManualTimer[] due;
            lock (timers) { elapsed += duration; due = timers.Where(timer => timer.Due <= elapsed).ToArray(); foreach (var timer in due) timer.Due = TimeSpan.MaxValue; }
            foreach (var timer in due) timer.Fire();
        }
        private sealed class ManualTimer(AdvancingClock clock, TimerCallback callback, object? state) : ITimer
        {
            internal TimeSpan Due = TimeSpan.MaxValue; private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) { lock (clock.timers) { if (disposed) return false; Due = dueTime == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : clock.elapsed + dueTime; return true; } }
            internal void Fire() { if (!disposed) callback(state); }
            public void Dispose() { lock (clock.timers) { disposed = true; Due = TimeSpan.MaxValue; } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
