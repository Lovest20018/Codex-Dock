using System.IO;
using System.Text.Json;
using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunRuntimeConflictSmokeAsync(string output)
    {
        Directory.CreateDirectory(output); var checks = new List<string>();
        await RunRuntimeConflictChecksAsync(output, checks);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { status = "PASS", tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RunRuntimeConflictChecksAsync(string output, List<string> checks)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        const string pipe = "mcp_servers.node_repl.env.SKY_CUA_NATIVE_PIPE_DIRECTORY";
        const string first = @"\\.\pipe\codex-computer-use-11111111-2222-3333-4444-555555555555";
        const string second = @"\\.\pipe\codex-computer-use-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var directory = Path.Combine(output, "runtime-conflict-home-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "config.toml");
        File.WriteAllText(configPath, "model='initial-runtime-model'\nopenai_base_url='https://fixture.invalid/v1'\n[mcp_servers.node_repl.env]\nSKY_CUA_NATIVE_PIPE_DIRECTORY='" + first + "'\n");
        File.WriteAllText(Path.Combine(directory, "auth.json"), new ConnectionSettings("https://fixture.invalid/v1", "fake-runtime-ui-key", "initial-runtime-model").RenderAuth());
        var memory = new IsolatedGatewayEnvironment(); var isolated = new ConfigStore(memory, _ => null);
        var window = new MainWindow(isolated, directory, output, memory); window.Loaded -= window.WindowLoaded;
        try
        {
            window.Show(); window.ShowPage("connection");
            window.ModelBox.Text = "reviewed-runtime-model"; window.OpenConfigurationReview();
            var document = new TomlDocument(File.ReadAllBytes(configPath)); document.Set(pipe, second);
            File.WriteAllText(configPath, document.Text + "# native runtime update\n");
            await window.RefreshLocalCodexStateAsync();
            Check(!window.externalConfigurationChanged && window.Dirty && window.ConfirmWriteButton.IsEnabled,
                "Runtime refresh keeps the reviewed draft and confirmation usable without a false conflict");
            window.ConfirmWriteClicked(window.ConfirmWriteButton, new());
            var saved = new TomlDocument(isolated.Load(directory).Config);
            Check(window.reviewSaved && saved.String("model") == "reviewed-runtime-model" && saved.Get(pipe) as string == second && saved.Text.Contains("# native runtime update"),
                "Confirmed save preserves the latest runtime pipe and comments while applying the reviewed model");
            window.Width = 960; window.Height = 640; await window.Settle(); window.Capture(output, "runtime-conflict-save-complete.png");
            window.CloseOverlay();
            var next = new TomlDocument(File.ReadAllBytes(configPath)); next.Set(pipe, first); File.WriteAllBytes(configPath, next.Bytes);
            window.RestoreClicked(window.RestoreButton, new());
            var restored = new TomlDocument(isolated.Load(directory).Config);
            Check(window.PreviewOverlay.IsVisible && window.ReviewTitle.Text == "配置已恢复" && restored.String("model") == "initial-runtime-model" && restored.Get(pipe) as string == first,
                "Backup restoration accepts runtime rotation and offers the shared apply panel without rewriting the old pipe");
            window.CloseOverlay(); window.ModelBox.Text = "kept-draft"; window.OpenConfigurationReview();
            next = new TomlDocument(File.ReadAllBytes(configPath)); next.Set("service_tier", "ultrafast"); File.WriteAllBytes(configPath, next.Bytes);
            await window.RefreshLocalCodexStateAsync(); window.ConfirmWriteClicked(window.ConfirmWriteButton, new());
            Check(window.externalConfigurationChanged && window.reviewWriteFailed && window.ModelBox.Text == "kept-draft" && new TomlDocument(isolated.Load(directory).Config).String("service_tier") == "ultrafast",
                "Actual setting changes still block confirmation and preserve both the draft and external value");
        }
        finally { window.CloseOverlay(); window.Close(); }
    }
}
