using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private void InitializeWorkspaceKeyboard()
    {
        Root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        void Add(VirtualKey key, VirtualKeyModifiers modifiers)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += async (_, args) =>
            {
                if (!CanUseWorkspaceShortcut) return;
                if (nativeAcceptanceInteractive) nativeKeyboardInvocations.Add(modifiers + ":" + key);
                args.Handled = true; await ExecuteWorkspaceShortcutAsync(key, modifiers);
            };
            Root.KeyboardAccelerators.Add(accelerator);
        }
        Add(VirtualKey.S, VirtualKeyModifiers.Control); Add(VirtualKey.F, VirtualKeyModifiers.Control); Add(VirtualKey.F1, VirtualKeyModifiers.None); Add(VirtualKey.H, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift);
    }
    private bool CanUseWorkspaceShortcut => initialized && !ApplicationBusy && !dialogOpen && !closing && !closePrompt;
    private async Task ExecuteWorkspaceShortcutAsync(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if (!CanUseWorkspaceShortcut) return;
        if (key == VirtualKey.F1 && modifiers == VirtualKeyModifiers.None || key == VirtualKey.H && modifiers == (VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift))
        { NavigateTo(HelpNav); FocusSearch(HelpSearchInput); return; }
        if (modifiers != VirtualKeyModifiers.Control) return;
        if (key == VirtualKey.F)
        {
            if (ReferenceEquals(currentPage, HelpNav)) FocusSearch(HelpSearchInput);
            else if (ReferenceEquals(currentPage, SourcesNav)) FocusSearch(SourceSearchInput);
            else if (ReferenceEquals(currentPage, AdvancedNav)) FocusSearch(AdvancedSearchInput);
            else { NavigateTo(CatalogNav); FocusSearch(CatalogSearchInput); }
            return;
        }
        if (key != VirtualKey.S) return;
        if (ReferenceEquals(currentPage, ProfilesNav))
        { if (SaveProfileButton.IsEnabled) await SaveProfileAsync(false); return; }
        if (ReferenceEquals(currentPage, TemplatesNav) && editingTemplate is not null)
        { if (SaveTemplateButton.IsEnabled) { SaveTemplateEdit(); RefreshView(); } return; }
        if (currentPage == MobileSettingsNav || currentPage == MobileNav)
        {
            if (mobileDraft.HasDraft && SaveMobileButton.IsEnabled)
            {
                if (currentPage == MobileNav) await PrepareMobilePairAsync();
                else await SaveMobileAsync();
            }
            return;
        }
        if (ApplicationFooter.Visibility == Visibility.Visible && ConnectionDraftActions.Visibility == Visibility.Visible)
        {
            if (currentPage == ConnectionNav && connectionReview is not null && SetupSaveOnlyButton.IsEnabled) await ConfirmConnectionReviewAsync(apply: false);
            else if (SaveConnectionButton.IsEnabled) await SaveConnectionAsync();
        }
    }
    private void FocusSearch(TextBox input)
    {
        // An explicit field target takes precedence over remembered page scrolling.
        pageNavigationRevision++;
        Root.UpdateLayout(); input.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        input.Focus(FocusState.Keyboard); input.SelectAll();
    }
}
