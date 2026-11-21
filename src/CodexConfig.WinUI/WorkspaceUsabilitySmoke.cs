using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunWorkspaceUsabilitySmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        var previous = viewModel.Home; var originalSize = AppWindow.Size;
        var home = Path.Combine(output, "usability-home"); Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='usability-saved-model'\nopenai_base_url='https://usability.invalid/v1'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-usability-key\"}");
        var desktop = (SmokeDesktopApplyRuntime)applicationRuntime; var restarts = desktop.Restarts;
        async Task Shortcut(VirtualKey key, VirtualKeyModifiers modifiers = VirtualKeyModifiers.Control) => await ExecuteWorkspaceShortcutAsync(key, modifiers);
        async Task SettleUi()
        { for (var i = 0; i < 250 && FilePreviewPanel.IsReading; i++) await Task.Delay(20); await Task.Delay(35); Root.UpdateLayout(); }
        try
        {
            NavigateTo(WorkspaceNav); await SwitchHomeAsync(home); var original = store.Load(home);
            Check(Root.KeyboardAccelerators.Count == 4 && Root.KeyboardAccelerators.Any(key => key.Key == VirtualKey.S && key.Modifiers == VirtualKeyModifiers.Control) &&
                Root.KeyboardAccelerators.Any(key => key.Key == VirtualKey.F && key.Modifiers == VirtualKeyModifiers.Control), "WinUI registers save search and help keyboard accelerators on its root");
            await Shortcut(VirtualKey.S);
            Check(!connectionDraft.HasDraft && original.SameContents(store.Load(home)) && store.LatestBackup(home) is null, "Ctrl S on a clean workspace creates no save backup or desktop operation");
            NavigateTo(ConnectionNav); connectionDraft.Model = "shortcut-main-draft";
            var confirmations = 0; smokeDialog = _ => { confirmations++; return Task.FromResult(ContentDialogResult.None); };
            await Shortcut(VirtualKey.S);
            Check(confirmations == 1 && connectionDraft.HasDraft && original.SameContents(store.Load(home)) && store.LatestBackup(home) is null,
                "Ctrl S on main configuration opens the existing confirmation and cancellation retains all edits");
            var navigation = currentPage; dialogOpen = true;
            await Shortcut(VirtualKey.F); await Shortcut(VirtualKey.S); await Shortcut(VirtualKey.F1, VirtualKeyModifiers.None); dialogOpen = false;
            Check(ReferenceEquals(navigation, currentPage) && confirmations == 1, "Keyboard commands cannot navigate save or open help over a modal dialog");
            using (busy.Begin(WindowOperation.ApplyDesktop, "正在应用到 Codex…"))
            { await Shortcut(VirtualKey.F); await Shortcut(VirtualKey.S); }
            Check(ReferenceEquals(navigation, currentPage) && confirmations == 1, "Keyboard commands are inert while desktop application is busy");
            NavigateTo(ProfilesNav); await LoadProfileAsync("shortcut-only"); ProfilesPanel.ProfileDocumentInput.Text = "model='profile-shortcut-model'\n";
            var profileRevision = profileDraft.Revision; await Shortcut(VirtualKey.S);
            Check(confirmations == 2 && profileDraft.HasDraft && profileDraft.Revision == profileRevision && connectionDraft.Model == "shortcut-main-draft" && !File.Exists(ProfileStore.FilePath(home, "shortcut-only")) && original.SameContents(store.Load(home)),
                "Ctrl S in Profiles reviews only its independent file and keeps the main draft unsaved");
            profileDraft.Load(null); NavigateTo(TemplatesNav); EnsureTemplateLibrary();
            BeginTemplateEdit(TemplateRepository.New("", []), true); TemplatesPanel.TemplateNameInput.Text = "键盘保存隔离方案";
            templateRows.Single().Path.Text = "model_verbosity"; templateRows.Single().Value.Text = "'low'";
            await Shortcut(VirtualKey.S); var savedTemplate = templateLibrary.Read().Templates.Single(template => template.Name == "键盘保存隔离方案");
            Check(editingTemplate is null && savedTemplate.Settings.Single().Path == "model_verbosity" && connectionDraft.HasDraft && original.SameContents(store.Load(home)) && confirmations == 2,
                "Ctrl S in the template editor saves only the scheme library without writing the main configuration");
            NavigateTo(CatalogNav); await Shortcut(VirtualKey.F); await SettleUi();
            Check(ReferenceEquals(currentPage, CatalogNav) && ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), CatalogPanel.CatalogSearchInput),
                "Ctrl F opens full configuration and focuses its searchable input");
            NavigateTo(SourcesNav); await Shortcut(VirtualKey.F); await SettleUi();
            Check(ReferenceEquals(currentPage, SourcesNav) && ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), SourcesPanel.SourceSearchInput),
                "Ctrl F in configuration sources focuses its existing filter without starting a native read");
            await Shortcut(VirtualKey.F1, VirtualKeyModifiers.None); await SettleUi();
            Check(HelpPanel.Visibility == Visibility.Visible && ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), HelpPanel.HelpSearchInput) && connectionDraft.HasDraft,
                "F1 opens searchable help and preserves all main configuration edits");
            await Shortcut(VirtualKey.F); Check(ReferenceEquals(currentPage, HelpNav), "Ctrl F in help retains the help search scope");
            NavigateTo(ConnectionNav); await Shortcut(VirtualKey.H, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift);
            Check(HelpPanel.Visibility == Visibility.Visible, "Ctrl Shift H offers a help shortcut when a global utility owns F1");
            NavigateTo(MobileSettingsNav); await SettleUi(); MobilePanel.MobileUrlInput.Text = "https://shortcut-phone.invalid"; MobilePanel.MobileUsernameInput.Text = "fixture-phone-user"; MobilePanel.MobilePasswordInput.Password = "fake-fixture-phone-secret";
            await Shortcut(VirtualKey.S);
            Check(confirmations == 3 && mobileDraft.HasDraft && connectionDraft.Model == "shortcut-main-draft" && original.SameContents(store.Load(home)),
                "Ctrl S on phone settings uses its explicit save and start review while keeping model settings separate");
            mobileDraft.Load(mobileDraft.Settings); NavigateTo(ConnectionNav);
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary); await Shortcut(VirtualKey.S);
            Check(!connectionDraft.HasDraft && new TomlDocument(store.Load(home).Config).String("model") == "shortcut-main-draft" && desktop.Restarts == restarts,
                "Confirmed Ctrl S performs the same encrypted configuration save and still requires separate desktop application");
            original = store.Load(home); connectionDraft.Model = "layout-unsaved-model"; smokeDialog = _ => Task.FromResult(ContentDialogResult.None);

            var minimum = windowSizing.QueryMinimum();
            Check(minimum.Width > 500 && minimum.Height > 400 && minimum.Width <= AppWindow.Size.Width,
                "The native window procedure reports a DPI adjusted minimum size within the current monitor");
            var pages = Pages;
            var layoutScreenshots = new[] { HelpNav, AboutNav, UpdatesNav, FilePreviewNav, MobileNav, MobileSettingsNav, ConnectionNav };
            foreach (var size in new[] { new Windows.Graphics.SizeInt32(1120, 820), new(900, 760), new(720, 540) })
            {
                AppWindow.Resize(size); await SettleUi();
                foreach (var page in pages)
                {
                    NavigateTo(page, revealStart: true); await SettleUi();
                    var pageControl = PageBody.Children.OfType<UserControl>().Single(panel => panel.Visibility == Visibility.Visible);
                    if (pageControl.Content is null || pageControl.ActualWidth <= 0 || pageControl.ActualWidth > PageBody.ActualWidth)
                        throw new InvalidOperationException($"The page content did not lay out at {size.Width}x{size.Height} on {page.Tag}.");
                    if (size.Width is 1120 or 720 && layoutScreenshots.Contains(page))
                        await CaptureAsync(output, $"page-{page.Tag}-{size.Width}x{size.Height}.png");
                    foreach (var panel in VisualDescendants<ActionWrapPanel>(Root))
                    {
                        if (!VisibleAncestors(panel) || panel.ActualWidth < 1) continue;
                        foreach (var child in panel.Children.OfType<FrameworkElement>().Where(child => child.Visibility == Visibility.Visible))
                        {
                            var point = child.TransformToVisual(panel).TransformPoint(new());
                            if (point.X < -1 || point.X + child.ActualWidth > panel.ActualWidth + 1 || point.Y + child.ActualHeight > panel.ActualHeight + 1)
                                throw new InvalidOperationException($"An action leaves its wrap panel at {size.Width}x{size.Height} on {page.Tag}: {child.Name}");
                        }
                    }
                    if (ProfileFooter.Visibility == Visibility.Visible)
                    {
                        var point = SaveProfileButton.TransformToVisual(Root).TransformPoint(new());
                        if (point.X < 0 || point.Y < 0 || point.X + SaveProfileButton.ActualWidth > Root.ActualWidth + 1 || point.Y + SaveProfileButton.ActualHeight > Root.ActualHeight + 1) throw new InvalidOperationException("Profile save action escaped the resized window.");
                    }
                    else if (ApplicationFooter.Visibility == Visibility.Visible)
                    {
                        var action = ConnectionDraftActions.Visibility == Visibility.Visible ? SaveConnectionButton : ApplyButton;
                        var point = action.TransformToVisual(Root).TransformPoint(new());
                        if (point.X < 0 || point.Y < 0 || point.X + action.ActualWidth > Root.ActualWidth + 1 || point.Y + action.ActualHeight > Root.ActualHeight + 1)
                            throw new InvalidOperationException($"The visible {action.Name} action escaped {size.Width}x{size.Height} on {page.Tag}.");
                    }
                }
                Check(connectionDraft.HasDraft && original.SameContents(store.Load(home)), $"All task pages keep action rows and independent save controls contained at {size.Width}x{size.Height}");
            }
            Root.RequestedTheme = ElementTheme.Dark; NavigateTo(ProfilesNav); await SettleUi(); PageScroll.ChangeView(null, PageScroll.ScrollableHeight, null, true);
            await CaptureAsync(output, "usability-profile-minimum-dark.png");
            Check(ProfileFooter.ActualHeight > SaveProfileButton.ActualHeight && SaveProfileButton.ActualWidth > 100, "A compact Profile footer wraps its actions into reachable rows");
            Root.Width = 460; Root.Height = 420; Root.HorizontalAlignment = HorizontalAlignment.Left; Root.VerticalAlignment = VerticalAlignment.Top;
            await SettleUi();
            await CaptureAsync(output, "usability-profile-logical-pressure.png");
            Check(ProfileMainDraftButton.TransformToVisual(Root).TransformPoint(new()).Y > SaveProfileButton.TransformToVisual(Root).TransformPoint(new()).Y && SaveProfileButton.TransformToVisual(Root).TransformPoint(new()).Y + SaveProfileButton.ActualHeight <= Root.ActualHeight,
                "A reduced logical viewport wraps the Profile footer into multiple lines without losing its save action");
            Root.Width = Root.Height = double.NaN; Root.HorizontalAlignment = HorizontalAlignment.Stretch; Root.VerticalAlignment = VerticalAlignment.Stretch;
            NavigateTo(SettingsNav); await SettleUi(); PageScroll.ChangeView(null, PageScroll.ScrollableHeight, null, true);
            await CaptureAsync(output, "usability-window-actions-minimum-dark.png");
            MinimizeWindowClicked(SettingsPanel.MinimizeWindowButton, new());
            Check(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } && connectionDraft.HasDraft,
                "Explicit taskbar minimization retains the same window and unsaved draft");
            RestoreWindow(); await SettleUi();
            var ownedTray = tray is null; if (ownedTray) InitializeTray(); HideWindowClicked(SettingsPanel.HideWindowButton, new());
            Check(!AppWindow.IsVisible && connectionDraft.HasDraft && !closing, "The Windows settings tray action hides the existing window without applying or saving");
            smokeDialog = dialog => Task.FromResult(dialog.Title.ToString() == "退出 Codex Dock" ? ContentDialogResult.Primary : ContentDialogResult.None);
            RestoreWindow(); await SettleUi(); await RequestWindowExitAsync();
            Check(!closing && AppWindow.IsVisible && connectionDraft.HasDraft && original.SameContents(store.Load(home)),
                "Cancelling explicit window exit preserves its accessible window drafts and saved files");
            if (ownedTray) { tray?.Dispose(); tray = null; }
            templateLibrary.Delete(savedTemplate.Id); templateLibrarySnapshot = null;
        }
        finally
        {
            dialogOpen = false; profileDraft.Load(null); editingTemplate = null; mobileDraft.Load(mobileDraft.Settings);
            LoadConnectionDraft(); smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary); await SwitchHomeAsync(previous); smokeDialog = null;
            AppWindow.Resize(originalSize); Root.RequestedTheme = ElementTheme.Light; NavigateTo(WorkspaceNav);
            Root.Width = Root.Height = double.NaN; Root.HorizontalAlignment = HorizontalAlignment.Stretch; Root.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }
    private static bool VisibleAncestors(DependencyObject element)
    { for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current)) if (current is UIElement { Visibility: Visibility.Collapsed }) return false; return true; }
    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        { var child = VisualTreeHelper.GetChild(root, index); if (child is T value) yield return value; foreach (var nested in VisualDescendants<T>(child)) yield return nested; }
    }
}
