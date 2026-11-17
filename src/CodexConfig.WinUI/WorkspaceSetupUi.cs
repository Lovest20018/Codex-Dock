using CodexConfig.Application;
using Microsoft.UI.Xaml;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private void RefreshWorkspaceSetupControls()
    {
        if (WorkspaceSetupCard is null) return;
        var guide = viewModel.SetupGuide;
        var needed = guide.Kind != WorkspaceSetupKind.ExistingConnection;
        WorkspaceSetupCard.Visibility = needed && (ReferenceEquals(currentPage, WorkspaceNav) || ReferenceEquals(currentPage, ConnectionNav))
            ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceSetupTitle.Text = guide.Title; WorkspaceSetupMessage.Text = guide.Message;
        var editable = !ApplicationBusy && !dialogOpen && !closing;
        SetupConnectionButton.Visibility = guide.Kind == WorkspaceSetupKind.NeedsConnection ? Visibility.Visible : Visibility.Collapsed;
        SetupConnectionButton.IsEnabled = editable;
        SetupReloadButton.IsEnabled = editable;
        SetupRecoveryButton.Visibility = guide.Kind == WorkspaceSetupKind.Unreadable || HasPendingRecovery ? Visibility.Visible : Visibility.Collapsed;
        SetupRecoveryButton.IsEnabled = editable;
    }
}
