using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private ConfigurationRecoveryStatus? recoveryStatus;
    private string recoveryStatusError = "";
    private bool HasPendingRecovery => recoveryStatus?.Interrupted == true;
    private void ReadRecoveryStatus()
    {
        try { recoveryStatus = configurationRecovery.Read(viewModel.Home); recoveryStatusError = ""; }
        catch (Exception error) { recoveryStatus = null; recoveryStatusError = error.Message; }
    }
    private void RefreshRecoveryControls()
    {
        if (RecoveryPanel.RestoreConfigurationButton is null) return;
        RecoveryPanel.RecoveryStatusText.Text = recoveryStatusError.Length > 0 ? "恢复记录无法读取：" + recoveryStatusError :
            HasPendingRecovery ? "发现未完成的保存。请先查看恢复差异，恢复后再编辑或应用。" :
            recoveryStatus?.BackupPath is { } source ? "找到本目录的加密备份。恢复前会核对当前配置与备份的保存后状态。\n" + source :
            "本目录尚无备份。确认保存配置后会自动创建加密备份。";
        RecoveryPanel.RestoreConfigurationButton.Content = HasPendingRecovery ? "查看未完成保存的恢复差异" : "查看上次保存的恢复差异";
        RecoveryPanel.RestoreConfigurationButton.IsEnabled = recoveryStatus?.Available == true && !ManualApplicationBusy && !dialogOpen && !closing;
        RecoveryPanel.RefreshRecoveryButton.IsEnabled = !ManualApplicationBusy && !dialogOpen && !closing;
    }
    private void OpenRecoveryClicked(object sender, RoutedEventArgs args) => NavigateTo(RecoveryNav);
    internal void RefreshRecoveryClicked(object sender, RoutedEventArgs args) { ReadRecoveryStatus(); RefreshView(); }
    internal async void RestoreConfigurationClicked(object sender, RoutedEventArgs args) => await RestoreConfigurationAsync();
    private async Task<bool> RestoreConfigurationAsync()
    {
        if (ManualApplicationBusy || dialogOpen || closing || !await ResolveConnectionDraftAsync()) return false;
        var home = viewModel.Home; var revision = connectionDraft.Revision;
        using var operation = busy.Begin(WindowOperation.RestoreConfiguration, "正在恢复配置…"); shellError = ""; RefreshView();
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            var review = await Task.Run(() => configurationRecovery.PrepareReview(home), lifetime.Token);
            if (viewModel.Home != home || closing || connectionDraft.Revision != revision) return false;
            var body = new StackPanel { Spacing = 14 };
            body.Children.Add(new TextBlock { Text = review.Interrupted ? "恢复未完成事务之前的状态，并归档原恢复记录。确认前不会写入文件。" :
                "恢复上次保存之前的配置。确认时会先加密备份当前状态；保存后再统一应用到 Codex。", TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = "备份时间：" + review.CreatedAt.LocalDateTime.ToString("g") + "\n来源：" + review.SourcePath +
                "\n\n写入目标：\n" + string.Join("\n", review.Targets), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Opacity = 0.7 });
            foreach (var change in review.Changes)
                body.Children.Add(new TextBlock { Text = change.Name + "\n" + change.Path + "\n当前：" + change.Previous + "\n恢复为：" + change.Next, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            if (review.Changes.Count == 0) body.Children.Add(new TextBlock { Text = "备份前后没有配置值变化；确认后仍会记录本次恢复。", TextWrapping = TextWrapping.Wrap });
            var dialog = new ContentDialog { Title = "确认恢复本地配置", PrimaryButtonText = "确认恢复", CloseButtonText = "返回核对",
                DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, Content = new ScrollViewer { MaxHeight = 420, Content = body } };
            if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return false;
            configurationResult = configurationRecovery.Restore(review, () => viewModel.Home, () =>
            { if (closing || connectionDraft.Revision != revision || connectionDraft.HasDraft) throw new OperationCanceledException("工作区或草稿已改变，请重新查看恢复差异。"); }, lifetime.Token);
            if (!configurationResult.Succeeded || configurationResult.Saved is not { } saved)
            { SetShellError("恢复未完成", configurationResult.Message); return false; }
            quickModelAwaitingApply = false;
            desktopApplication.Reset(); lastApplication = null;
            viewModel.ConfigurationSaved(saved); InvalidateSavedPreview(); InvalidateSource();
            loadingConnection = true;
            try { connectionDraft.Load(saved); }
            finally { loadingConnection = false; }
            officialLoginStatus = ""; ResetModelDiscovery(); ReadRecoveryStatus(); templateApplication = null;
            return true;
        }
        catch (Exception error) { SetShellError("恢复未完成，请核对恢复记录", error.Message); return false; }
        finally { operation.Dispose(); ReadRecoveryStatus(); RefreshView(); }
    }
}
