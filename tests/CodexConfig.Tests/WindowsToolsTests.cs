using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class WindowsToolsTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        // These commands are never launched. Keep the fake executable short so
        // complete-suite GUID output paths do not hit Windows' Run limit.
        var startupExecutable = Path.Combine(Path.GetPathRoot(Path.GetFullPath(root))!, "Dock startup fixture.exe");
        void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
        Test("startup service reads without writes and binds enable disable to the selected program and home", () =>
        {
            var registration = new StartupFixture(); var executable = startupExecutable; var home = Path.Combine(root, "startup home");
            var service = new StartupRegistrationService(registration, executable); var state = service.Read(home);
            Assert(!state.Enabled && registration.Writes == 0);
            var review = service.Prepare(state, true); Assert(registration.Writes == 0);
            state = service.Apply(review, () => home, () => true); Assert(state.Enabled && registration.Command == StartupCommand.Create(executable, home));
            service.Apply(service.Prepare(state, false), () => home, () => true); Assert(registration.Command is null && registration.Writes == 2);
        });
        Test("startup replacement requires an observed review and cannot disable a foreign registration", () =>
        {
            var registration = new StartupFixture { Command = "foreign fixture command" }; var service = new StartupRegistrationService(registration, startupExecutable);
            var home = Path.Combine(root, "startup-foreign"); var state = service.Read(home);
            Assert(state.HasOtherRegistration && !state.Enabled); Throws<InvalidOperationException>(() => service.Prepare(state, false));
            var review = service.Prepare(state, true); Assert(review.ReplacesOther && registration.Command == "foreign fixture command");
            service.Apply(review, () => home, () => true); Assert(service.Read(home).Enabled);
        });
        Test("startup rejects changed registrations stale workspace reused confirmation and busy context", () =>
        {
            var registration = new StartupFixture(); var service = new StartupRegistrationService(registration, startupExecutable); var home = Path.Combine(root, "startup-context");
            var review = service.Prepare(service.Read(home), true);
            Throws<OperationCanceledException>(() => service.Apply(review, () => home, () => false));
            Throws<OperationCanceledException>(() => service.Apply(review, () => home + "-other", () => true));
            registration.Command = "new external command"; Throws<IOException>(() => service.Apply(review, () => home, () => true));
            Assert(registration.Writes == 0 && registration.Command == "new external command");
            review = service.Prepare(service.Read(home), true); service.Apply(review, () => home, () => true);
            Throws<InvalidOperationException>(() => service.Apply(review, () => home, () => true));
        });
        Test("startup write failures and long commands preserve the prior registration", () =>
        {
            var registration = new StartupFixture { FailWrite = true }; var service = new StartupRegistrationService(registration, startupExecutable); var home = Path.Combine(root, "startup-failure");
            Throws<IOException>(() => service.Apply(service.Prepare(service.Read(home), true), () => home, () => true)); Assert(registration.Command is null);
            Throws<ArgumentException>(() => service.Prepare(service.Read(Path.Combine(root, new string('a', 260))), true)); Assert(registration.Command is null);
        });
        tests.Add(("source reader compares fresh saved files without using memory drafts and keeps redacted metadata", async () =>
        {
            var f = new Fixture(root); var request = f.Request(); var runtime = new SourceFixture((actual, bytes, _) =>
            {
                Assert(actual == request && Encoding.UTF8.GetString(bytes!).Contains("old-main"));
                return Task.FromResult(Result(actual, bytes));
            });
            var service = new ConfigurationSourceService(runtime); var result = await service.ReadAsync(request, () => request);
            Assert(runtime.Calls == 1 && result.Rows.Single(row => row.Path == "model").FileValue.Contains("old-main") &&
                !JsonSerializer.Serialize(result).Contains("fake-private") && f.Before.SameContents(f.MainStore.Load(f.Home)));
        }));
        tests.Add(("source read rejects changed conditions or saved comparison file after a delayed result", async () =>
        {
            var f = new Fixture(root); var request = f.Request();
            var runtime = new SourceFixture((actual, bytes, _) => { File.AppendAllText(Path.Combine(f.Home, "config.toml"), "# external\n"); return Task.FromResult(Result(actual, bytes)); });
            await ThrowsAsync<OperationCanceledException>(() => new ConfigurationSourceService(runtime).ReadAsync(request, () => request));
            runtime = new((actual, bytes, _) => Task.FromResult(Result(actual, bytes)));
            await ThrowsAsync<OperationCanceledException>(() => new ConfigurationSourceService(runtime).ReadAsync(request, () => request with { Project = Path.Combine(root, "another-project") }));
        }));
        tests.Add(("source profile reads use the named profile file and invalid names fail before invoking native code", async () =>
        {
            var f = new Fixture(root); var path = ProfileStore.FilePath(f.Home, "daily"); File.WriteAllText(path, "model='saved-profile'\n");
            var request = f.Request() with { Profile = "daily" }; var runtime = new SourceFixture((actual, bytes, _) =>
            { Assert(Encoding.UTF8.GetString(bytes!).Contains("saved-profile")); return Task.FromResult(Result(actual, bytes)); });
            _ = await new ConfigurationSourceService(runtime).ReadAsync(request, () => request);
            await ThrowsAsync<ArgumentException>(() => new ConfigurationSourceService(runtime).ReadAsync(request with { Profile = "../escape" }, () => request));
            Assert(runtime.Calls == 1 && f.Before.SameContents(f.MainStore.Load(f.Home)));
        }));
        tests.Add(("cancelled unsupported or failing source reads do not claim effective settings", async () =>
        {
            var f = new Fixture(root); var request = f.Request(); using var cancellation = new CancellationTokenSource();
            var cancelled = new SourceFixture((actual, bytes, _) => { cancellation.Cancel(); return Task.FromResult(Result(actual, bytes)); });
            await ThrowsAsync<OperationCanceledException>(() => new ConfigurationSourceService(cancelled).ReadAsync(request, () => request, cancellation.Token));
            var unsupported = new SourceFixture((_, _, _) => throw new NotSupportedException("fixture unsupported profile"));
            await ThrowsAsync<NotSupportedException>(() => new ConfigurationSourceService(unsupported).ReadAsync(request, () => request));
            Assert(f.Before.SameContents(f.MainStore.Load(f.Home)));
        }));
        tests.Add(("an already cancelled source request never starts a native reader", async () =>
        {
            var f = new Fixture(root); var request = f.Request(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var runtime = new SourceFixture((actual, bytes, _) => Task.FromResult(Result(actual, bytes)));
            await ThrowsAsync<OperationCanceledException>(() => new ConfigurationSourceService(runtime).ReadAsync(request, () => request, cancellation.Token));
            Assert(runtime.Calls == 0 && f.Before.SameContents(f.MainStore.Load(f.Home)));
        }));
        Test("profile ordinary editor hides nested private and privileged values while preserving them on save", () =>
        {
            var f = new Fixture(root); var path = ProfileStore.FilePath(f.Home, "daily");
            File.WriteAllText(path, "# preserve comment\nmodel='profile-model'\nfuture_items=[{token='fake-private-token'}]\nsandbox_mode='read-only'\nmodel_reasoning_effort='low'\n");
            var draft = new ProfileDraftViewModel(); draft.Load(f.Profiles.Read(f.Home, "daily"));
            Assert(draft.PreservedFields == 2 && !draft.Text.Contains("fake-private-token") && !draft.Text.Contains("sandbox_mode"));
            draft.Text = draft.Text.Replace("\"low\"", "\"high\""); var review = f.Profiles.PrepareReview(draft.Snapshot!, draft.Changes());
            Assert(!review.Preview.Contains("fake-private-token") && !review.Changes.Any(change => change.Path == "sandbox_mode"));
            f.Session.MarkDraft(true); var saved = f.Profiles.Save(review, () => (f.Home, "daily"), () => { });
            Assert(saved.Succeeded && saved.BackupPath is not null && f.Session.State.HasDraft && !f.Operations.IsBusy && File.ReadAllText(path).Contains("# preserve comment") &&
                File.ReadAllText(path).Contains("fake-private-token") && new TomlDocument(saved.Saved!.Config).String("model_reasoning_effort") == "high" && f.Before.SameContents(f.MainStore.Load(f.Home)));
        });
        Test("new profile review is frozen and creates only its own file and encrypted backup after save", () =>
        {
            var f = new Fixture(root); var draft = new ProfileDraftViewModel(); draft.Load(f.Profiles.Read(f.Home, "fresh"));
            draft.Text = "model='chosen-model'\nmodel_reasoning_effort='high'\n"; var edits = new Dictionary<string, string?>(draft.Changes());
            var review = f.Profiles.PrepareReview(draft.Snapshot!, edits); edits["model"] = "'later-change'";
            Assert(!File.Exists(review.Target) && f.Store.LatestBackup(f.Home, "fresh") is null);
            var result = f.Profiles.Save(review, () => (f.Home, "fresh"), () => { });
            Assert(result.Succeeded && result.BackupPath is not null && new TomlDocument(result.Saved!.Config).String("model") == "chosen-model" && f.Before.SameContents(f.MainStore.Load(f.Home)));
            Assert(!f.Profiles.Save(review, () => (f.Home, "fresh"), () => { }).Succeeded);
        });
        Test("invalid profile documents forbidden fields and nested credentials stay in memory and cannot save", () =>
        {
            var f = new Fixture(root); var draft = new ProfileDraftViewModel(); draft.Load(f.Profiles.Read(f.Home, "invalid"));
            foreach (var value in new[] { "model=[broken", "sandbox_mode='danger-full-access'", "future_array=[{api_key='fake-private'}]", "mcp_servers.demo.command='fixture'" })
            {
                draft.Text = value;
                if (value == "model=[broken") Throws<InvalidDataException>(() => draft.Changes()); else Throws<ArgumentException>(() => draft.Changes());
                Assert(draft.HasDraft && draft.Text == value && !File.Exists(ProfileStore.FilePath(f.Home, "invalid")));
            }
            Assert(f.Before.SameContents(f.MainStore.Load(f.Home)));
        });
        Test("profile save respects common operation lease context cancellation and external file edits", () =>
        {
            var f = new Fixture(root); var before = f.Profiles.Read(f.Home, "daily"); var review = f.Profiles.PrepareReview(before, new Dictionary<string, string?> { ["model"] = "'new'" });
            using (var lease = f.Operations.TryBeginConfigurationSave()) Assert(!f.Profiles.Save(review, () => (f.Home, "daily"), () => { }).Succeeded);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert(!f.Profiles.Save(review, () => (f.Home, "daily"), () => { }, cancellation.Token).Succeeded);
            Assert(!f.Profiles.Save(review, () => (f.Home, "another"), () => { }).Succeeded && !File.Exists(review.Target));
            File.WriteAllText(review.Target, "model='external'\n");
            Assert(!f.Profiles.Save(review, () => (f.Home, "daily"), () => { }).Succeeded && File.ReadAllText(review.Target).Contains("external") && !f.Operations.IsBusy);
        });
        Test("profile reviewed recovery restores encrypted content and can remove a newly created file", () =>
        {
            var f = new Fixture(root); var before = f.Profiles.Read(f.Home, "fresh");
            var result = f.Profiles.Save(f.Profiles.PrepareReview(before, new Dictionary<string, string?> { ["model"] = "'new'" }), () => (f.Home, "fresh"), () => { });
            var review = f.Profiles.PrepareRestore(result.Saved!); Assert(File.Exists(review.Target) && review.Source is not null);
            result = f.Profiles.Save(review, () => (f.Home, "fresh"), () => { });
            Assert(result.Succeeded && result.BackupPath is not null && !File.Exists(review.Target) && f.Before.SameContents(f.MainStore.Load(f.Home)));
        });
        Test("profile recovery rejects replaced source and external edits after a frozen review", () =>
        {
            var f = new Fixture(root); var saved = f.Profiles.Save(f.Profiles.PrepareReview(f.Profiles.Read(f.Home, "daily"), new Dictionary<string, string?> { ["model"] = "'new'" }), () => (f.Home, "daily"), () => { }).Saved!;
            var review = f.Profiles.PrepareRestore(saved); var bytes = File.ReadAllBytes(review.Source!); File.WriteAllBytes(review.Source!, [.. bytes, 0]);
            Assert(!f.Profiles.Save(review, () => (f.Home, "daily"), () => { }).Succeeded && File.ReadAllText(review.Target).Contains("new"));
            File.WriteAllBytes(review.Source!, bytes); review = f.Profiles.PrepareRestore(saved); File.AppendAllText(review.Target, "# external edit\n");
            Assert(!f.Profiles.Save(review, () => (f.Home, "daily"), () => { }).Succeeded && File.ReadAllText(review.Target).Contains("external edit"));
        });
    }
    private sealed class StartupFixture : IStartupRegistration
    { internal string? Command; internal bool FailWrite; internal int Writes; public string? Read() => Command; public void Write(string? command) { Writes++; if (FailWrite) throw new IOException("fixture failure"); Command = command; } }
    private sealed class SourceFixture(Func<ConfigurationSourceRequest, byte[]?, CancellationToken, Task<ConfigurationReadResult>> read) : IConfigurationSourceRuntime
    { internal int Calls; public Task<ConfigurationReadResult> ReadAsync(ConfigurationSourceRequest request, byte[]? config, CancellationToken cancellation) { Calls++; return read(request, config, cancellation); } }
    private static ConfigurationReadResult Result(ConfigurationSourceRequest request, byte[]? bytes)
    {
        using var json = JsonDocument.Parse("{\"config\":{\"model\":\"effective\",\"future_items\":[{\"token\":\"fake-private-response\"}]},\"layers\":[{\"name\":{\"type\":\"project\"},\"disabledReason\":\"untrusted\"}]}");
        return ConfigReadClient.Parse(json.RootElement, bytes, request.Executable, request.Project, request.Profile);
    }
    private sealed class Fixture
    {
        internal readonly string Home;
        internal readonly ProfileStore Store = new();
        internal readonly ConfigStore MainStore = new(new MemoryEnvironment(), _ => null);
        internal readonly WorkspaceSession Session;
        internal readonly WorkspaceApplyCoordinator Operations;
        internal readonly ProfileApplicationService Profiles;
        internal readonly ConfigSnapshot Before;
        internal Fixture(string root)
        {
            Home = Path.Combine(root, "windows-tools-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Home);
            File.WriteAllText(Path.Combine(Home, "config.toml"), "model='old-main'\nopenai_base_url='https://tools.invalid/v1'\n");
            File.WriteAllText(Path.Combine(Home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-tools-key\"}");
            Before = MainStore.Load(Home); Session = new(Home); Session.Loaded(Home, Before); Operations = new(Session); Profiles = new(Store, Operations);
        }
        internal ConfigurationSourceRequest Request() => new(Home, Path.Combine(Home, "fixture-codex.exe"), Home);
    }
    private sealed class MemoryEnvironment : IUserEnvironment { public string? Read() => null; public void Write(string? value) { if (value is not null) throw new InvalidOperationException(); } }
    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Windows tools regression"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
