using System.Text.Json;
using CodexConfig.Core;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private bool nativeAcceptanceInteractive;
    private int nativePickerOpens, nativePickerCancels, nativePickerSelections;
    private readonly List<string> nativeKeyboardInvocations = [];
    private async Task RunNativeAcceptanceHostAsync(string output)
    {
        var original = store.Load(viewModel.Home); var selectedHome = Path.Combine(output, "native-selected-home"); Directory.CreateDirectory(selectedHome);
        File.WriteAllText(Path.Combine(selectedHome, "config.toml"), "model='native-selected-model'\n"); var selectedConfig = File.ReadAllBytes(Path.Combine(selectedHome, "config.toml"));
        Title = "Codex Dock · 系统验收（隔离）"; nativeAcceptanceInteractive = true;
        NavigateTo(ConnectionNav); connectionDraft.Model = "native-keyboard-draft";
        var deadline = DateTimeOffset.UtcNow.AddMinutes(15);
        try
        {
            while (!File.Exists(Path.Combine(output, "native-acceptance.done")))
            {
                if (DateTimeOffset.UtcNow > deadline) throw new TimeoutException("Native acceptance host exceeded fifteen minutes.");
                await File.WriteAllTextAsync(Path.Combine(output, "native-state.json"), JsonSerializer.Serialize(new
                { home = viewModel.Home, page = currentPage.Tag, dialogOpen, hasDraft = connectionDraft.HasDraft,
                    keyboard = nativeKeyboardInvocations.ToArray(), nativePickerOpens, nativePickerCancels, nativePickerSelections, selectedHome }, new JsonSerializerOptions { WriteIndented = true }));
                await Task.Delay(250);
            }
            if (nativePickerOpens < 2 || nativePickerCancels < 1 || nativePickerSelections != 1 || viewModel.Home != selectedHome || connectionDraft.HasDraft ||
                !nativeKeyboardInvocations.Contains("Control:S") || !nativeKeyboardInvocations.Contains("Control:F") || !nativeKeyboardInvocations.Contains("Control, Shift:H") ||
                !original.SameContents(store.Load(original.Home)) || !selectedConfig.SequenceEqual(File.ReadAllBytes(Path.Combine(selectedHome, "config.toml"))) || store.LatestBackup(original.Home) is not null || store.LatestBackup(selectedHome) is not null)
                throw new InvalidOperationException("The native acceptance actions or read-only invariants are incomplete.");
            await WriteSmokeReportAsync(output, new List<string>
            {
                "Physical Ctrl S key reaches the existing native confirmation without an implicit write",
                "Physical Ctrl F and Ctrl Shift H keys reach scoped search and help in the isolated window",
                "Native Windows folder picker opens and cancellation preserves the original workspace draft",
                "Native folder selection reaches the existing unsaved draft confirmation and changes only the selected fixture workspace",
                "Native picker and keyboard acceptance leaves both fixture configurations without saves backups or real desktop actions"
            });
        }
        finally { nativeAcceptanceInteractive = false; }
    }
}
