using System.Text;
using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;

internal static class WorkspaceInformationTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        var home = Path.Combine(root, "information-" + Guid.NewGuid().ToString("N"));
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        ConfigurationPreviewSnapshot Project(string? config, string? auth = null, string? env = null) => ConfigurationPreviewService.Project(home, Files(config, auth, env));
        Test("file preview normalizes TOML without comments and preserves ordinary scalar types", () =>
        {
            var result = Project("# fake-comment-secret\nmodel='disk-model' # fake-inline-secret\ncount=42\nactive=false\nratio=1.25\ndate=2026-11-16\n");
            var text = result.Config.Text; var doc = new TomlDocument(Encoding.UTF8.GetBytes(text));
            Assert(result.Config.State == ConfigurationPreviewState.Ready && doc.String("model") == "disk-model" && doc.Get("count") is 42L && doc.Get("active") is false && doc.Get("ratio") is 1.25);
            Assert(text.Contains("2026-11-16") && !text.Contains("fake-") && !Directory.Exists(home));
        });
        Test("file preview hides nested environment headers tokens and URL credentials in table arrays", () =>
        {
            const string config = "[mcp_servers.example]\nenv={SECRET='fake-env'}\nhttp_headers={Authorization='fake-header'}\nbase_url='https://fake-user:fake-password@provider.invalid/v1?key=fake-query#fake-fragment'\n[[custom]]\nname='ordinary'\naccess_token='fake-token'\n[[custom]]\nname='second'\nlinks=['https://u:p@provider.invalid/a?q=fake-array']\n";
            var text = Project(config).Config.Text;
            Assert(text.Contains("ordinary") && text.Contains("second") && text.Contains("provider.invalid/v1") && text.Contains("••••••••"));
            Assert(!text.Contains("fake-") && !text.Contains("u:p@")); _ = new TomlDocument(Encoding.UTF8.GetBytes(text));
        });
        Test("file preview authentication shows presence only and omits unknown identity fields and names", () =>
        {
            var text = Project(null, "{\"auth_mode\":\"chatgpt\",\"tokens\":{\"id_token\":\"fake-id\"},\"OPENAI_API_KEY\":\"fake-key\",\"personal_access_token\":\"fake-pat\",\"fake-private-name\":\"fake-person@example.invalid\"}").Auth.Text;
            using var json = JsonDocument.Parse(text); Assert(json.RootElement.GetProperty("auth_mode").GetString() == "chatgpt" && json.RootElement.GetProperty("other_fields_hidden").GetInt32() == 1);
            Assert(!text.Contains("fake-") && !text.Contains("id_token") && text.Contains("personal_access_token"));
            Assert(!Project(null, "{\"auth_mode\":\"fake-unknown-mode\"}").Auth.Text.Contains("fake-unknown"));
        });
        Test("file preview isolates malformed config auth and unreadable environment without revealing source", () =>
        {
            var result = Project("private='fake-private'\nmodel=[broken", "{\"auth_mode\":\"apikey\"}", "https://provider.invalid/v1");
            Assert(result.Config.State == ConfigurationPreviewState.Unreadable && result.Auth.State == ConfigurationPreviewState.Ready && result.Environment.State == ConfigurationPreviewState.Ready && !JsonSerializer.Serialize(result).Contains("fake-private"));
            result = Project("model='ordinary'", "{\"fake-private\": unfinished");
            Assert(result.Config.State == ConfigurationPreviewState.Ready && result.Auth.State == ConfigurationPreviewState.Unreadable && !result.Auth.Text.Contains("fake-private"));
            Assert(Project(null, "[]").Auth.State == ConfigurationPreviewState.Unreadable);
            result = ConfigurationPreviewService.Project(home, Files("model='ordinary'") with { EnvironmentUnreadable = true });
            Assert(result.Environment.State == ConfigurationPreviewState.Unreadable && result.Config.State == ConfigurationPreviewState.Ready);
        });
        Test("file preview bounds each source and display without returning malformed or oversized raw data", () =>
        {
            var result = ConfigurationPreviewService.Project(home, new(new(new byte[8 * 1024 * 1024 + 1]), new(new byte[8 * 1024 * 1024 + 1]), null));
            Assert(result.Config.State == ConfigurationPreviewState.Unreadable && result.Auth.State == ConfigurationPreviewState.Unreadable);
            result = Project("ordinary='" + new string('x', 1024 * 1024 + 10) + "'");
            Assert(result.Config.State == ConfigurationPreviewState.Ready && result.Config.Truncated && result.Config.Text.Length < 1024 * 1024 + 100);
            result = ConfigurationPreviewService.Project(home, new(new([0xff, 0xfe, 0]), new([0xff, 0xfe]), null));
            Assert(result.Config.State == ConfigurationPreviewState.Unreadable && result.Auth.State == ConfigurationPreviewState.Unreadable);
        });
        Test("file preview environment restricts output to one redacted URL and hides non URL values", () =>
        {
            var result = Project(null, null, "https://fake-user:fake-pass@provider.invalid/v1?key=fake-query#fake-fragment");
            Assert(result.Environment.Text.Contains("CODEX_AUTHAPI_BASE_URL") && result.Environment.Text.Contains("provider.invalid/v1") && !result.Environment.Text.Contains("fake-"));
            Assert(!Project(null, null, "fake-unstructured-secret").Environment.Text.Contains("fake-"));
            Assert(Project(null).Environment.State == ConfigurationPreviewState.Missing);
        });
        tests.Add(("native file preview reads only two fixture files and one injected environment without writing", async () =>
        {
            var nativeHome = home + "-native"; Directory.CreateDirectory(nativeHome); var env = new ReadOnlyEnvironment("https://provider.invalid/v1?key=fake-query");
            File.WriteAllText(Path.Combine(nativeHome, "config.toml"), "model='native-disk'\n"); File.WriteAllText(Path.Combine(nativeHome, "auth.json"), "{\"tokens\":{\"access_token\":\"fake-token\"}}");
            File.WriteAllText(Path.Combine(nativeHome, "ignored-secret"), "fake-ignored");
            var before = Directory.GetFiles(nativeHome).ToDictionary(path => Path.GetFileName(path), path => File.ReadAllBytes(path));
            var result = await new ConfigurationPreviewService(new NativeConfigurationPreviewRuntime(env)).ReadAsync(nativeHome + Path.DirectorySeparatorChar, () => nativeHome);
            Assert(env.Reads == 2 && result.Config.Text.Contains("native-disk") && !JsonSerializer.Serialize(result).Contains("fake-") && Directory.GetFiles(nativeHome).Length == before.Count);
            Assert(before.All(pair => pair.Value.SequenceEqual(File.ReadAllBytes(Path.Combine(nativeHome, pair.Key!)))));
        }));
        tests.Add(("native missing file preview and directory actions never create a home", async () =>
        {
            var missing = home + "-missing"; var runtime = new DirectoryRuntime(); var directory = new WorkspaceDirectoryService(runtime);
            var result = await new ConfigurationPreviewService(new NativeConfigurationPreviewRuntime(new ReadOnlyEnvironment(null))).ReadAsync(missing, () => missing);
            Assert(result.Config.State == ConfigurationPreviewState.Missing && result.Auth.State == ConfigurationPreviewState.Missing && !directory.Open(missing, () => missing) && !Directory.Exists(missing) && runtime.Opened.Count == 0);
            Assert(directory.CurrentPath(missing, () => missing) == missing);
        }));
        tests.Add(("file preview rejects any file or environment changed between the two reads", async () =>
        {
            foreach (var second in new[] { Files("model='new'"), Files("model='old'", "{}"), Files("model='old'", env: "https://new.invalid"), Files("model='old'") with { Config = new(null, true) } })
                await Cancelled(() => new ConfigurationPreviewService(new SequenceRuntime(Files("model='old'"), second)).ReadAsync(home, () => home));
        }));
        tests.Add(("file preview rejects a changed directory and observes cancellation from an uncooperative reader", async () =>
        {
            await Cancelled(() => new ConfigurationPreviewService(new SequenceRuntime(Files("model='old'"), Files("model='old'"))).ReadAsync(home, () => home + "-other"));
            using var cancellation = new CancellationTokenSource(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<ConfigurationPreviewFiles>(TaskCreationOptions.RunContinuationsAsynchronously);
            var runtime = new DelegateRuntime(() => { entered.TrySetResult(); return release.Task; });
            var pending = new ConfigurationPreviewService(runtime).ReadAsync(home, () => home, cancellation.Token); await entered.Task;
            cancellation.Cancel(); release.SetResult(Files("model='late'")); await Cancelled(() => pending);
        }));
        tests.Add(("file preview freezes the initial bytes so mutation of a runtime buffer is detected", async () =>
        {
            var files = Files("model='old'"); var count = 0;
            var runtime = new DelegateRuntime(() => { if (++count == 2) files.Config.Bytes![7] = (byte)'X'; return Task.FromResult(files); });
            await Cancelled(() => new ConfigurationPreviewService(runtime).ReadAsync(home, () => home));
        }));
        Test("workspace directory validates both absolute paths and refuses stale contexts before opening", () =>
        {
            var runtime = new DirectoryRuntime(); var directory = new WorkspaceDirectoryService(runtime);
            Throws<ArgumentException>(() => directory.CurrentPath("relative", () => home));
            Throws<OperationCanceledException>(() => directory.CurrentPath(home, () => "relative"));
            Throws<OperationCanceledException>(() => directory.Open(home, () => home + "-other")); Assert(runtime.Opened.Count == 0);
            Directory.CreateDirectory(home); Assert(directory.Open(home + Path.DirectorySeparatorChar, () => home) && runtime.Opened.Single() == home);
        });
        Test("workspace directory launches explorer with one literal argument including spaces and metacharacters", () =>
        {
            var path = Path.Combine(home, "space & semicolon; dollar$"); var info = NativeWorkspaceDirectoryRuntime.StartInfo(path);
            Assert(info.FileName == "explorer.exe" && !info.UseShellExecute && info.ArgumentList.Single() == path && info.Arguments.Length == 0);
        });
        Test("central help searches shared topics and exposes navigation destinations only", () =>
        {
            Assert(DockHelpCatalog.Topics.Count == 7 && DockHelpCatalog.Topics.Select(topic => topic.Id).Distinct().Count() == 7);
            Assert(DockHelpCatalog.Search(" profile ").Any(topic => topic.Id == "profiles") && DockHelpCatalog.Search("no-such-topic-123").Count == 0);
            Assert(DockHelpCatalog.Topics.All(topic => new[] { "connection", "preview", "profiles", "gateways", "settings", "recovery", "updates" }.Contains(topic.Destination)));
            Assert(DockHelpCatalog.Topics.First().Text == DockHelpCatalog.QuickStartText && ((IList<DockHelpTopic>)DockHelpCatalog.Topics).IsReadOnly);
        });
    }
    private static ConfigurationPreviewFiles Files(string? config, string? auth = null, string? env = null) => new(new(config is null ? null : Encoding.UTF8.GetBytes(config)), new(auth is null ? null : Encoding.UTF8.GetBytes(auth)), env);
    private sealed class SequenceRuntime(params ConfigurationPreviewFiles[] results) : IConfigurationPreviewRuntime
    { private int index; public Task<ConfigurationPreviewFiles> ReadAsync(string home, CancellationToken cancellation) => Task.FromResult(results[Math.Min(index++, results.Length - 1)]); }
    private sealed class DelegateRuntime(Func<Task<ConfigurationPreviewFiles>> read) : IConfigurationPreviewRuntime
    { public Task<ConfigurationPreviewFiles> ReadAsync(string home, CancellationToken cancellation) => read(); }
    private sealed class ReadOnlyEnvironment(string? value) : IUserEnvironment
    { internal int Reads; public string? Read() { Reads++; return value; } public void Write(string? text) => throw new InvalidOperationException("No environment writes."); }
    private sealed class DirectoryRuntime : IWorkspaceDirectoryRuntime
    { internal readonly List<string> Opened = []; public void Open(string path) => Opened.Add(path); }
    private static async Task Cancelled(Func<Task> action)
    { try { await action(); } catch (OperationCanceledException) { return; } throw new InvalidOperationException("Expected cancelled stale preview."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Workspace information regression"); }
}
