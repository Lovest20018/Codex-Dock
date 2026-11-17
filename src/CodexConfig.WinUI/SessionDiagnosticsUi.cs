using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private SessionDiagnosticService sessionDiagnostics = new();
    private SessionDiagnosticListing? diagnosticListing;
    private CancellationTokenSource? diagnosticCancellation;
    private bool readingDiagnostics;
    private string diagnosticMessage = "尚未读取。刷新后请选择明确对应本次操作的聊天记录。";
    private void RefreshDiagnosticControls()
    {
        if (DiagnosticFilesInput is null) return;
        var editable = !ManualApplicationBusy && !dialogOpen && !closing;
        DiagnosticFilesInput.IsEnabled = RefreshDiagnosticsButton.IsEnabled = editable;
        ReadDiagnosticButton.IsEnabled = editable && diagnosticListing is not null && DiagnosticFilesInput.SelectedItem is SessionDiagnosticFile;
        CancelDiagnosticButton.Visibility = readingDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        CancelDiagnosticButton.IsEnabled = readingDiagnostics;
        DiagnosticStatusText.Text = diagnosticMessage;
    }
    private void ResetDiagnosticHome()
    {
        diagnosticCancellation?.Cancel(); diagnosticListing = null; DiagnosticFilesInput.ItemsSource = Array.Empty<SessionDiagnosticFile>();
        DiagnosticResultText.Text = "尚未读取所选聊天。"; diagnosticMessage = "尚未读取当前目录。刷新后请选择明确对应本次操作的聊天记录。";
    }
    private void DiagnosticSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        diagnosticCancellation?.Cancel(); DiagnosticResultText.Text = "尚未读取所选聊天。";
        if (initialized) RefreshView();
    }
    private async void RefreshDiagnosticsClicked(object sender, RoutedEventArgs args) => await RefreshDiagnosticsAsync();
    private async Task RefreshDiagnosticsAsync()
    {
        if (ManualApplicationBusy || dialogOpen || closing) return;
        ResetDiagnosticHome(); readingDiagnostics = true; diagnosticMessage = "正在列出当前目录的近期聊天…"; RefreshView();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); diagnosticCancellation = cancellation;
        try
        {
            var listing = await sessionDiagnostics.ListAsync(viewModel.Home, () => viewModel.Home, cancellation.Token);
            if (closing) return;
            diagnosticListing = listing; DiagnosticFilesInput.ItemsSource = listing.Files;
            diagnosticMessage = listing.Files.Count == 0 ? "此目录未发现可读取的近期聊天记录。" : "已列出 " + listing.Files.Count + " 条近期记录。选择后点击读取。";
        }
        catch (OperationCanceledException) { diagnosticMessage = "列表读取已取消，请重新刷新。"; }
        catch (Exception) { diagnosticMessage = "暂时无法列出本机聊天，请检查当前目录和读取权限。"; }
        finally { diagnosticCancellation = null; readingDiagnostics = false; if (!closing) RefreshView(); }
    }
    private async void ReadDiagnosticClicked(object sender, RoutedEventArgs args) => await ReadDiagnosticAsync();
    private async Task ReadDiagnosticAsync()
    {
        if (ManualApplicationBusy || dialogOpen || closing || diagnosticListing is not { } listing || DiagnosticFilesInput.SelectedItem is not SessionDiagnosticFile file) return;
        readingDiagnostics = true; DiagnosticResultText.Text = "正在读取所选聊天的参数…"; diagnosticMessage = "正在读取参数元数据…"; RefreshView();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); diagnosticCancellation = cancellation;
        try
        {
            var observed = await sessionDiagnostics.ReadAsync(listing, file, () => (viewModel.Home, (DiagnosticFilesInput.SelectedItem as SessionDiagnosticFile)?.Path), cancellation.Token);
            if (closing) return;
            DiagnosticResultText.Text = SessionDiagnosticService.Describe(observed); diagnosticMessage = "已读取所选聊天的客户端参数记录。";
        }
        catch (OperationCanceledException) { if (!closing) DiagnosticResultText.Text = diagnosticMessage = "读取已取消或选择已改变，请重新读取。"; }
        catch (Exception) { if (!closing) DiagnosticResultText.Text = diagnosticMessage = "暂时无法读取所选聊天，请刷新列表后重试。"; }
        finally { diagnosticCancellation = null; readingDiagnostics = false; if (!closing) RefreshView(); }
    }
    private void CancelDiagnosticClicked(object sender, RoutedEventArgs args) => diagnosticCancellation?.Cancel();
    private void OpenSourcesClicked(object sender, RoutedEventArgs args) => NavigateTo(SourcesNav);
}
