using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private readonly DispatcherTimer localStateTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private CodexRuntimeStatus? codexRuntime;
    private bool localStatePolling;
    private bool externalConfigurationChanged;
    private string localStateError = "";

    private void InitializeLocalCodexState()
    {
        localStateTimer.Tick += async (_, _) => await RefreshLocalCodexStateAsync();
        Activated += async (_, _) =>
        {
            if (IsLoaded && smokeOutput is null) await RefreshLocalCodexStateAsync();
        };
    }

    private async Task RefreshLocalCodexStateAsync()
    {
        if (localStatePolling || closing) return;
        localStatePolling = true;
        var expectedHome = home;
        var expectedSnapshot = snapshot;
        try
        {
            // Smoke runs only read their isolated home and never inspect real processes.
            codexRuntime = smokeOutput is null ? await Task.Run(CodexRuntimeStatus.Read) : new(true, 0);
            var current = await Task.Run(() => store.Load(expectedHome));
            if (closing || home != expectedHome || !ReferenceEquals(snapshot, expectedSnapshot)) return;
            localStateError = "";
            var changed = snapshot is null || !snapshot.SameContents(current);
            var previouslyChanged = externalConfigurationChanged;
            externalConfigurationChanged = changed;
            if (changed && !Dirty && !busy && !TemplateEditor.IsEditing)
            {
                // Parse before changing controls so partial writes never replace the current view.
                _ = store.ReadSettings(current);
                Reload(current);
            }
            else if (changed && !busy)
                SetStatus("检测到外部配置更改，未保存内容已保留。可到概览重新读取配置。", true);
            else if (previouslyChanged && !busy)
                SetStatus(Dirty ? "有未保存的修改" : "配置已读取");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or CryptographicException or JsonException or ArgumentException)
        {
            if (!closing && home == expectedHome && ReferenceEquals(snapshot, expectedSnapshot))
                localStateError = "暂时无法读取最新配置，请检查文件格式与访问权限后刷新。";
        }
        finally
        {
            localStatePolling = false;
            if (!closing) RefreshOverview();
        }
    }

    private async void RefreshLocalStateClicked(object sender, RoutedEventArgs e) => await RefreshLocalCodexStateAsync();

    private async Task RunLocalStateSmokeAsync(string output, List<string> checks)
    {
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var startupHome = Path.Combine(home, "startup-connection");
        Directory.CreateDirectory(startupHome);
        File.WriteAllText(Path.Combine(startupHome, "config.toml"), "model='startup-model'\nopenai_base_url='https://startup.example.com/backend-api/codex'\n");
        File.WriteAllText(Path.Combine(startupHome, "auth.json"), "{\"personal_access_token\":\"fake-startup-pat\",\"OPENAI_API_KEY\":\"fake-startup-pat\"}");
        var startupFixture = new MainWindow(store, startupHome, output, environment);
        var startupEnvironment = environment.Read();
        startupFixture.Loaded -= startupFixture.WindowLoaded;
        try
        {
            Assert(startupFixture.BaseUrlBox.Text == "https://startup.example.com/backend-api/codex" && startupFixture.ApiKeyBox.Password == "fake-startup-pat" &&
                startupFixture.ModelBox.Text == "startup-model" && !startupFixture.Dirty,
                "Saved address and PAT must be loaded before showing the window or initializing the tray");
            startupFixture.Show(); startupFixture.ShowPage("connection");
            await Settle(); startupFixture.Capture(output, "connection-first-open.png");
            Assert(!startupFixture.SaveButton.IsEnabled && startupFixture.ApiKeyBox.Password == "fake-startup-pat", "Initial connection load must not create a draft");
            await Settle();
            var modelInput = FindVisual<TextBox>(startupFixture.ModelBox).Single(box => box.Name == "PART_EditableTextBox");
            modelInput.Text = "typed-model";
            await Settle();
            Assert(startupFixture.SaveButton.IsEnabled && startupFixture.Dirty, "Typing a model must enable saving");
            var modelSaved = startupFixture.SaveCurrent();
            Assert(modelSaved && store.ReadSettings(store.Load(startupHome)).Model == "typed-model", "Typed model must save and reload: " + startupFixture.StatusText.Text);
            startupFixture.ShowPage("connection");
            startupFixture.BaseUrlBox.Text = "https://typed.example.com/backend-api/codex";
            startupFixture.ApiKeyBox.Password = "fake-typed-pat";
            Assert(startupFixture.SaveButton.IsEnabled && startupFixture.SaveCurrent(), "Edited address and PAT must save");
            Assert(store.ReadSettings(store.Load(startupHome)).ApiKey == "fake-typed-pat" && !startupFixture.Dirty, "Saved PAT must reload without a draft");
        }
        finally { startupFixture.Close(); environment.Write(startupEnvironment); }
        checks.Add("Saved address and PAT available on first open before tray or status polling");
        checks.Add("Typed model, address and PAT enable saving and reload from disk");
        var configPath = Path.Combine(home, "config.toml");
        var authPath = Path.Combine(home, "auth.json");
        var fixture = System.Text.Encoding.UTF8.GetBytes("model='existing-model'\nmodel_provider='existing.service'\n[model_providers.'existing.service']\nname='已有服务'\nbase_url='https://existing.example.com/v1'\nexperimental_bearer_token='fake-import-token'\n");
        Directory.CreateDirectory(home); File.WriteAllBytes(configPath, fixture);
        await RefreshLocalCodexStateAsync();
        Assert(snapshot is not null && loadedSettings.ProviderId == "existing.service" && BaseUrlBox.Text == "https://existing.example.com/v1" && ModelBox.Text == "existing-model" && !Dirty, "Automatically read existing custom provider");
        Assert(File.ReadAllBytes(configPath).SequenceEqual(fixture) && !File.Exists(authPath), "Automatic detection must not write credentials or config");
        ShowPage("overview"); await Settle(); Capture(output, "codex-local-state.png");
        ModelBox.Text = "unsaved-model";
        var changed = fixture.Concat(System.Text.Encoding.UTF8.GetBytes("wire_api='responses'\n")).ToArray();
        File.WriteAllBytes(configPath, changed); await RefreshLocalCodexStateAsync();
        Assert(externalConfigurationChanged && ModelBox.Text == "unsaved-model" && snapshot!.Config!.SequenceEqual(fixture), "External change must preserve drafts and save baseline");
        Assert(!SaveCurrent() && File.ReadAllBytes(configPath).SequenceEqual(changed), "External change must block stale save");
        Reload(); Assert(!externalConfigurationChanged && !Dirty && ModelBox.Text == "existing-model", "Explicit reload synchronizes changed source");
        File.WriteAllText(authPath, "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-refreshed-key\"}");
        await RefreshLocalCodexStateAsync(); Assert(snapshot!.Auth is not null && !Dirty, "External authentication changes synchronize automatically");
        var valid = snapshot;
        File.WriteAllText(configPath, "model = [\n"); await RefreshLocalCodexStateAsync();
        Assert(ReferenceEquals(snapshot, valid) && localStateError.Length > 0 && ModelBox.Text == "existing-model", "Partial external writes preserve the valid view");
        File.WriteAllBytes(configPath, changed); await RefreshLocalCodexStateAsync();
        Assert(localStateError.Length == 0 && !externalConfigurationChanged, "Automatic refresh recovers after repaired config");
        File.Delete(configPath); File.Delete(authPath); await RefreshLocalCodexStateAsync();
        Assert(snapshot!.Config is null && snapshot.Auth is null && !Dirty, "External file deletion refreshes to an unconfigured state");
        checks.AddRange(["Startup reads existing custom provider without writing", "External edits preserve drafts and prevent stale saves", "External auth refresh", "Partial writes preserve valid view and recover", "External config deletion refresh"]);
    }

    private void AddLocalCodexStatus()
    {
        var body = new StackPanel();
        var heading = OverviewText("Codex 当前状态", size: 16);
        heading.FontWeight = FontWeights.SemiBold;
        body.Children.Add(heading);
        var runtime = OverviewText(codexRuntime?.Summary ?? "正在检测 Codex 进程…", codexRuntime is { Available: true, ProcessCount: > 0 } ? "Accent" : "Muted");
        runtime.Margin = new(0, 10, 0, 10); body.Children.Add(runtime);
        body.Children.Add(OverviewText("配置目录：" + home, "Muted", 12));
        body.Children.Add(OverviewText(snapshot is null ? "尚未读取有效配置" : snapshot.Config is null ? "config.toml 尚不存在" : "已读取 config.toml", "Muted", 12));
        if (externalConfigurationChanged)
        {
            var note = OverviewText("本机配置已在其他程序中更改。当前草稿已保留；点击“重新读取配置”处理未保存的修改后同步。", "Accent", 12);
            note.Margin = new(0, 12, 0, 0); body.Children.Add(note);
        }
        if (localStateError.Length > 0)
        {
            var note = OverviewText(localStateError, "Error", 12); note.Margin = new(0, 12, 0, 0); body.Children.Add(note);
        }
        var actions = new WrapPanel { Margin = new(0, 14, 0, 0) };
        var refresh = new Button { Content = "刷新状态", Margin = new(0, 0, 8, 0) };
        refresh.Click += RefreshLocalStateClicked; actions.Children.Add(refresh);
        var reload = new Button { Content = "重新读取配置" };
        reload.Click += ReloadClicked; actions.Children.Add(reload);
        body.Children.Add(actions);
        OverviewBody.Children.Add(OverviewSurface(body, new(0, 0, 0, 16)));
    }
}
