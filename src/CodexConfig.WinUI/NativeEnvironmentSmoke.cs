using System.Text.Json;
using CodexConfig.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private bool nativeEnvironmentAcceptance;
    private async Task RunNativeEnvironmentSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        async Task SettleEnvironmentAsync()
        { for (var index = 0; index < 100 && FilePreviewPanel.IsReading; index++) await Task.Delay(20); await Task.Delay(100); Root.UpdateLayout(); }
        var previous = viewModel.Home; var position = AppWindow.Position; var size = AppWindow.Size;
        var home = Path.Combine(output, "native-directory space & literal"); Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "# fake-native-comment-secret\nmodel='native-environment-model'\nopenai_base_url='https://environment.invalid/v1'\n[mcp_servers.fixture]\nenv={PRIVATE='fake-native-environment-secret'}\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-native-environment-key\"}");
        var displays = new List<object>(); var desktop = (SmokeDesktopApplyRuntime)applicationRuntime; var restarts = desktop.Restarts;
        try
        {
            Title = "Codex Dock · 显示与复制验收（隔离）"; await SwitchHomeAsync(home); var original = store.Load(home);
            connectionDraft.Model = "unsaved-native-environment-model"; var revision = connectionDraft.Revision;
            var connected = DisplayArea.FindAll(); var areas = new List<DisplayArea>();
            for (var displayIndex = 0; displayIndex < connected.Count; displayIndex++) areas.Add(connected[displayIndex]);
            areas.Sort((first, second) => second.IsPrimary.CompareTo(first.IsPrimary));
            Check(areas.Count > 0, "Native environment enumerates connected display areas without changing display settings");
            var index = 0;
            foreach (var area in areas)
            {
                var outer = area.OuterBounds;
                AppWindow.Move(new(outer.X + 50, outer.Y + 50)); await Task.Delay(600);
                for (var attempt = 0; attempt < 20 && Math.Abs(Root.XamlRoot.RasterizationScale * 96 - windowSizing.CurrentDpi) > 1; attempt++) await Task.Delay(50);
                var bounds = windowSizing.InitialBounds(); AppWindow.MoveAndResize(new(bounds.X, bounds.Y, bounds.Width, bounds.Height)); await SettleEnvironmentAsync();
                var current = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest); var work = windowSizing.CurrentWorkArea();
                var minimum = windowSizing.QueryMinimum(); var dpi = windowSizing.CurrentDpi;
                Check(current.DisplayId.Value == area.DisplayId.Value && Math.Abs(Root.XamlRoot.RasterizationScale * 96 - dpi) <= 1,
                    "Display " + index + " move reaches the selected monitor and aligns native DPI with XAML rasterization");
                Check(AppWindow.Position.X >= work.X && AppWindow.Position.Y >= work.Y && AppWindow.Position.X + AppWindow.Size.Width <= work.X + work.Width && AppWindow.Position.Y + AppWindow.Size.Height <= work.Y + work.Height &&
                    minimum.Width == Math.Min(work.Width, (int)Math.Ceiling(720 * dpi / 96.0)) && minimum.Height == Math.Min(work.Height, (int)Math.Ceiling(540 * dpi / 96.0)),
                    "Display " + index + " initial bounds and minimum tracking fit its actual work area");
                foreach (var page in new DockPage[] { ConnectionNav, ModelsNav, ProfilesNav, SettingsNav, HelpNav })
                {
                    NavigateTo(page); await SettleEnvironmentAsync();
                    foreach (var panel in VisualDescendants<ActionWrapPanel>(Root).Where(panel => VisibleAncestors(panel) && panel.ActualWidth > 1))
                        foreach (var child in panel.Children.OfType<FrameworkElement>().Where(child => child.Visibility == Visibility.Visible))
                        {
                            var point = child.TransformToVisual(panel).TransformPoint(new());
                            if (point.X < -1 || point.X + child.ActualWidth > panel.ActualWidth + 1 || point.Y + child.ActualHeight > panel.ActualHeight + 1)
                                throw new InvalidOperationException("A native display move clipped an action row on " + page.Tag);
                        }
                }
                Check(connectionDraft.Revision == revision && connectionDraft.HasDraft && original.SameContents(store.Load(home)) && desktop.Restarts == restarts,
                    "Display " + index + " page and monitor moves retain the unsaved model and never apply or save it");
                Root.RequestedTheme = index % 2 == 0 ? ElementTheme.Light : ElementTheme.Dark; NavigateTo(ConnectionNav);
                await CaptureAsync(output, "display-" + index + ".png");
                displays.Add(new { index, primary = area.IsPrimary, outer = new { outer.X, outer.Y, outer.Width, outer.Height },
                    work = new { work.X, work.Y, work.Width, work.Height }, dpi, rasterizationScale = Root.XamlRoot.RasterizationScale,
                    bounds = new { bounds.X, bounds.Y, bounds.Width, bounds.Height }, minimum = new { minimum.Width, minimum.Height } }); index++;
            }
            AppWindow.MoveAndResize(new(position.X, position.Y, size.Width, size.Height)); Root.RequestedTheme = ElementTheme.Light; await SettleEnvironmentAsync(); RestoreWindow();
            NavigateTo(FilePreviewNav); await SettleEnvironmentAsync();
            Check(FilePreviewPanel.Snapshot is not null && !FilePreviewPanel.SavedPreviewText.Text.Contains("fake-") && !FilePreviewPanel.SavedPreviewText.Text.Contains("unsaved-native-environment-model"),
                "Native copy acceptance uses the saved sanitized snapshot while the model remains an unsaved draft");
            var clipboard = await NativeClipboardFixture.TryCreateAsync(NativeClipboardFixture.KnownTestText);
            if (clipboard is not null)
            {
                try
                {
                    clipboard.RequireUnchanged(); nativeEnvironmentAcceptance = true; CopyWorkspacePathClicked(CopyWorkspacePathButton, new());
                    if (!WorkspaceFileActionStatus.Text.Contains("已复制")) throw new IOException("原生目录复制未完成，未读取剪贴板。");
                    await clipboard.VerifyOwnedTextAsync(home);
                    if (!clipboard.OwnsContent) throw new OperationCanceledException("剪贴板在验收期间改变，未读取新内容。");
                    Check(clipboard.OwnsContent, "Actual Windows path copy writes the exact current directory including spaces and metacharacters");
                    await Task.Delay(200);
                    if (!clipboard.OwnsContent) throw new OperationCanceledException("剪贴板已改变，未覆盖新内容。");
                    FilePreviewPanel.SavedPreviewSectionInput.SelectedIndex = 1; FilePreviewPanel.CopySavedPreviewClicked(FilePreviewPanel.CopySavedPreviewButton, new());
                    if (!FilePreviewPanel.SavedPreviewStatus.Text.Contains("已复制")) throw new IOException($"原生预览复制未完成，未读取剪贴板。busy={ApplicationBusy}; currentHome={FilePreviewPanel.HasCurrentSnapshot}; section={FilePreviewPanel.SelectedSection?.State}; enabled={FilePreviewPanel.CopySavedPreviewButton.IsEnabled}; message={FilePreviewPanel.Message}");
                    await clipboard.VerifyOwnedTextAsync(FilePreviewPanel.Snapshot!.Auth.Text);
                    if (!clipboard.OwnsContent) throw new OperationCanceledException("剪贴板在验收期间改变，未读取新内容。");
                    Check(clipboard.OwnsContent && !FilePreviewPanel.Snapshot!.Auth.Text.Contains("fake-") && FilePreviewPanel.Snapshot.Auth.Text.Contains("OPENAI_API_KEY"),
                        "Actual Windows authentication copy contains only the redacted presence summary");
                }
                finally
                {
                    nativeEnvironmentAcceptance = false;
                    using var operation = busy.Begin(WindowOperation.RestoreClipboard, "正在恢复剪贴板…");
                    try
                    {
                        await clipboard.RestoreAsync(() =>
                        {
                            File.WriteAllText(Path.Combine(output, "clipboard-restore-pending.txt"), "Waiting to restore this test's known synthetic text; no private clipboard object was retained.");
                            SetShellError("正在恢复验收前的测试文本", "Windows 暂时占用剪贴板；本窗口会等待恢复虚构文本，不退出验收。");
                        });
                    }
                    finally
                    {
                        operation.Dispose();
                        await File.WriteAllTextAsync(Path.Combine(output, "clipboard-result.json"), JsonSerializer.Serialize(new
                        { clipboard.Restored, clipboard.Superseded, knownTestMarkerOnly = true, privateClipboardContentRecorded = false }));
                    }
                    if (File.Exists(Path.Combine(output, "clipboard-restore-pending.txt"))) File.Delete(Path.Combine(output, "clipboard-restore-pending.txt"));
                }
                Check(clipboard.Restored || clipboard.Superseded, clipboard.Restored
                    ? "Native copy acceptance restores only its previously known synthetic text"
                    : "Native copy acceptance preserves a newer clipboard change instead of overwriting it");
            }
            nativeEnvironmentAcceptance = true; OpenWorkspaceFolderClicked(OpenWorkspaceFolderButton, new()); nativeEnvironmentAcceptance = false;
            Check(smokeOpenedDirectories.LastOrDefault() == home && WorkspaceFileActionStatus.Text.Contains("已请求打开") && original.SameContents(store.Load(home)) && store.LatestBackup(home) is null,
                "The actual directory action launches the fixture path without writing files backups or the model draft");
            await File.WriteAllTextAsync(Path.Combine(output, "environment.json"), JsonSerializer.Serialize(new
            { operatingSystem = Environment.OSVersion.VersionString, displays, clipboardRestored = clipboard?.Restored, clipboardSuperseded = clipboard?.Superseded,
                actualClipboardAcceptance = clipboard is null ? "SKIPPED: " + NativeClipboardFixture.LastSkipReason : "Known synthetic test marker only; restored as text",
                privateClipboardContentRecorded = false,
                clipboardHistoryAllowed = false, clipboardRoamingAllowed = false, explorerRequestedPath = home, originalConfigurationUnchanged = true, runtimeRestarts = desktop.Restarts - restarts },
                new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        finally
        {
            nativeEnvironmentAcceptance = false; profileDraft.Load(null); LoadConnectionDraft(); smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary);
            await SwitchHomeAsync(previous); smokeDialog = null; Root.RequestedTheme = ElementTheme.Light; AppWindow.MoveAndResize(new(position.X, position.Y, size.Width, size.Height));
        }
    }
}
