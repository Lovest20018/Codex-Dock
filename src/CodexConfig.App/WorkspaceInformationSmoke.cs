using System.IO;
using System.Text;
using System.Windows;
using CodexConfig.Application;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunWorkspaceInformationSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        var previousHome = home; var previous = store.Load(home); var oldTab = currentTab;
        var fixture = Path.Combine(output, "wpf-information-home"); Directory.CreateDirectory(fixture);
        var config = Encoding.UTF8.GetBytes("# fake-wpf-comment\nmodel='wpf-saved-model'\n[future]\nurl='https://fake-user:fake-pass@wpf.invalid/v1?key=fake-query#fake-fragment'\n[mcp_servers.fixture]\nenv={PRIVATE='fake-wpf-env'}\n");
        var auth = Encoding.UTF8.GetBytes("{\"auth_mode\":\"chatgpt\",\"tokens\":{\"access_token\":\"fake-wpf-token\"},\"fake-private-person\":\"fake-private-email\"}");
        File.WriteAllBytes(Path.Combine(fixture, "config.toml"), config); File.WriteAllBytes(Path.Combine(fixture, "auth.json"), auth);
        try
        {
            home = fixture; Check(Reload(), "WPF file preview fixture loads without rewriting its native authentication");
            PreviewClicked(PreviewButton, new()); ConfigTab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(PreviewBox.IsReadOnly && PreviewBox.Text.Contains("wpf-saved-model") && !PreviewBox.Text.Contains("fake-") && ReviewHint.Text.Contains("不含原注释"),
                "WPF current file review uses the shared normalized redaction for comments nested secrets and URL parameters");
            AuthTab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(PreviewBox.Text.Contains("chatgpt") && PreviewBox.Text.Contains("other_fields_hidden") && !PreviewBox.Text.Contains("fake-"), "WPF authentication review uses the shared presence only summary");
            CloseOverlay(); ModelBox.Text = "wpf-unsaved-model"; PreviewClicked(PreviewButton, new()); ConfigTab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(Dirty && PreviewBox.Text.Contains("wpf-unsaved-model") && config.SequenceEqual(File.ReadAllBytes(Path.Combine(fixture, "config.toml"))) && auth.SequenceEqual(File.ReadAllBytes(Path.Combine(fixture, "auth.json"))) && store.LatestBackup(fixture) is null,
                "WPF save review retains its draft projection while saved files and backups stay unchanged");
            await Settle(); Capture(output, "wpf-safe-file-review.png"); CloseOverlay();
            HelpClicked(this, new()); OpenFolderClicked(this, new());
            Check(smokeHelpText?.StartsWith(DockHelpCatalog.QuickStartText) == true && smokeOpenedDirectory == fixture, "WPF help shares the quick start text and explicit folder opening uses the isolated runtime");
            home = Path.Combine(output, "wpf-information-missing"); smokeOpenedDirectory = null; OpenFolderClicked(this, new());
            Check(smokeOpenedDirectory is null && !Directory.Exists(home), "WPF opening an absent configuration folder does not create it");
        }
        finally { CloseOverlay(); home = previousHome; Reload(previous); currentTab = oldTab; ShowPage("overview"); }
    }
}
