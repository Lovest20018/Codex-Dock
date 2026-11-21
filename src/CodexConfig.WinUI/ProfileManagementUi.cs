using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private readonly ProfileApplicationService profileManagement;
    private readonly ProfileDraftViewModel profileDraft = new();
    private bool loadingProfile;
    private string profileMessage = "选择名称并读取，或新建独立 Profile。";
    private void ResetProfileHome()
    {
        loadingProfile = true;
        try { profileDraft.Load(null); ProfilesPanel.ProfileDocumentInput.Text = ""; ProfilesPanel.ProfileNameInput.Text = ""; InvalidateSource(); ReadProfileNames(); ReadStartupSettings(); }
        finally { loadingProfile = false; }
    }
    private void ReadProfileNames()
    {
        try
        {
            var previous = loadingProfile; loadingProfile = true;
            try { ProfilesPanel.ProfileNamesInput.ItemsSource = profileManagement.List(viewModel.Home); ProfilesPanel.ProfileNamesInput.SelectedItem = profileDraft.Snapshot?.Name; }
            finally { loadingProfile = previous; }
        }
        catch (Exception) { ProfilesPanel.ProfileNamesInput.ItemsSource = Array.Empty<string>(); profileMessage = "无法读取此目录的 Profile 列表，请检查权限和文件名。"; }
    }
    private void RefreshProfileControls()
    {
        if (ProfilesPanel.ProfileDocumentInput is null) return;
        var editable = !ManualApplicationBusy && !dialogOpen && !closing;
        ProfilesPanel.ProfileNamesInput.IsEnabled = ProfilesPanel.ProfileNameInput.IsEnabled = ProfilesPanel.OpenProfileButton.IsEnabled = ProfilesPanel.RefreshProfilesButton.IsEnabled = editable;
        ProfilesPanel.ProfileDocumentInput.IsEnabled = editable && profileDraft.Snapshot is not null && !HasPendingRecovery;
        if (!loadingProfile && ProfilesPanel.ProfileDocumentInput.Text != profileDraft.Text) ProfilesPanel.ProfileDocumentInput.Text = profileDraft.Text;
        SaveProfileButton.IsEnabled = editable && !HasPendingRecovery && profileDraft.HasDraft;
        DiscardProfileButton.IsEnabled = editable && profileDraft.HasDraft;
        RestoreProfileButton.IsEnabled = false;
        try { if (profileDraft.Snapshot is { } snapshot) RestoreProfileButton.IsEnabled = editable && !HasPendingRecovery && profileManagement.CanRestore(snapshot.Home, snapshot.Name); }
        catch (Exception) { profileMessage = "无法读取此 Profile 的备份状态。"; }
        ProfilesPanel.ProfileStatusText.Text = profileMessage;
        ProfilesPanel.ProfilePathText.Text = profileDraft.Snapshot is { } value ? ProfileStore.FilePath(value.Home, value.Name) + "\n使用：codex --profile " + value.Name + "；主配置与现有聊天未切换。" +
            (profileDraft.PreservedFields > 0 ? "\n另有 " + profileDraft.PreservedFields + " 项连接、权限或不可编辑字段保留在文件中，编辑区不展示其内容。" : "") : "尚未读取 Profile。新建前先填写名称。";
        ProfileFooter.Visibility = ReferenceEquals(currentPage, ProfilesNav) ? Visibility.Visible : Visibility.Collapsed;
        ProfileDraftStatus.Text = profileDraft.HasDraft ? "Profile 有未保存修改，查看差异后单独保存。" : "Profile 独立保存，不切换主配置或当前聊天。";
        ProfileMainDraftButton.Visibility = connectionDraft.HasDraft ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void ProfileSelectionChanged(object sender, SelectionChangedEventArgs args) { if (!updatingControls && !loadingProfile && ProfilesPanel.ProfileNamesInput.SelectedItem is string name) ProfilesPanel.ProfileNameInput.Text = name; }
    internal void ProfileDocumentChanged(TextBox sender, TextBoxTextChangingEventArgs args)
    { if (initialized && !updatingControls && !loadingProfile && !ManualApplicationBusy && !dialogOpen && !closing) profileDraft.Text = sender.Text; }
    internal async void OpenProfileClicked(object sender, RoutedEventArgs args) => await LoadProfileAsync(ProfilesPanel.ProfileNameInput.Text.Trim());
    private async Task<bool> LoadProfileAsync(string name)
    {
        if (ManualApplicationBusy || dialogOpen || closing || !await ResolveProfileDraftAsync()) return false;
        try
        {
            var snapshot = profileManagement.Read(viewModel.Home, name);
            loadingProfile = true; try { profileDraft.Load(snapshot); ProfilesPanel.ProfileDocumentInput.Text = profileDraft.Text; ProfilesPanel.ProfileNameInput.Text = name; } finally { loadingProfile = false; }
            ReadProfileNames();
            profileMessage = snapshot.Config is null ? "已创建内存草稿；确认保存前不会创建 Profile 文件。" : "已读取已保存 Profile。编辑区仅包含普通模型与行为设置。";
            RefreshView(); return true;
        }
        catch (Exception error) { profileMessage = error is ArgumentException ? error.Message : "Profile 无法读取；原文件及当前草稿已保留。"; RefreshView(); return false; }
    }
    internal void RefreshProfilesClicked(object sender, RoutedEventArgs args) { ReadProfileNames(); RefreshView(); }
    private async void SaveProfileClicked(object sender, RoutedEventArgs args) => await SaveProfileAsync(false);
    private async void RestoreProfileClicked(object sender, RoutedEventArgs args) { if (await ResolveProfileDraftAsync()) await SaveProfileAsync(true); }
    private void DiscardProfileClicked(object sender, RoutedEventArgs args)
    { if (!ManualApplicationBusy && !dialogOpen && !closing && profileDraft.Snapshot is { } snapshot) { profileDraft.Load(snapshot); profileMessage = "已撤销此 Profile 的未保存修改。"; RefreshView(); } }
    private async Task<bool> SaveProfileAsync(bool restore)
    {
        if (ManualApplicationBusy || dialogOpen || closing || HasPendingRecovery || profileDraft.Snapshot is not { } before) return false;
        using var operation = busy.Begin(WindowOperation.SaveProfile, "正在保存独立配置…"); RefreshView(); var revision = profileDraft.Revision;
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            var review = restore ? profileManagement.PrepareRestore(before) : profileManagement.PrepareReview(before, profileDraft.Changes());
            var content = new StackPanel { Spacing = 14 };
            content.Children.Add(new TextBlock { Text = "只写入此 Profile 并加密备份其原内容。主配置、连接草稿、账号与当前聊天保持各自状态。\n\n目标：" + review.Target + (review.Source is { } source ? "\n恢复来源：" + source : ""), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            foreach (var change in review.Changes) content.Children.Add(new TextBlock { Text = change.Name + "\n" + change.Path + "\n当前：" + change.Previous + "\n修改为：" + change.Next, TextWrapping = TextWrapping.Wrap });
            if (review.Changes.Count == 0) content.Children.Add(new TextBlock { Text = review.HasChanges ? "文件状态或格式将恢复为备份内容。" : "没有配置值变化。确认后会重新核对文件。", TextWrapping = TextWrapping.Wrap });
            if (review.Warnings.Count > 0) content.Children.Add(new TextBlock { Text = string.Join("\n", review.Warnings), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
            var dialog = new ContentDialog { Title = restore ? "确认恢复此 Profile" : "确认保存此 Profile", PrimaryButtonText = restore ? "确认恢复" : "确认保存", CloseButtonText = "返回编辑",
                DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, Content = new ScrollViewer { MaxHeight = 420, Content = content } };
            if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return false;
            var result = profileManagement.Save(review, () => (viewModel.Home, profileDraft.Snapshot?.Name), () => { if (closing || profileDraft.Revision != revision) throw new OperationCanceledException(); }, lifetime.Token);
            profileMessage = result.Message;
            if (!result.Succeeded || result.Saved is null) return false;
            loadingProfile = true; try { profileDraft.Load(result.Saved); } finally { loadingProfile = false; }
            ReadProfileNames(); InvalidateSource(); return true;
        }
        catch (Exception error) { profileMessage = error is ArgumentException or InvalidDataException ? "Profile 输入未通过校验，请检查 TOML 写法和普通字段范围。" : error.Message; return false; }
        finally { operation.Dispose(); RefreshView(); }
    }
    private async Task<bool> ResolveProfileDraftAsync()
    {
        if (!profileDraft.HasDraft) return true;
        if (ManualApplicationBusy || dialogOpen || closing) return false;
        var dialog = new ContentDialog { Title = "Profile 有未保存修改", Content = "可单独查看并保存此 Profile，放弃修改，或返回继续编辑。主配置草稿会保留。",
            PrimaryButtonText = "查看并保存", SecondaryButtonText = "放弃修改", CloseButtonText = "继续编辑", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot };
        var choice = await ShowDialogAsync(dialog);
        if (choice == ContentDialogResult.Primary) return await SaveProfileAsync(false);
        if (choice != ContentDialogResult.Secondary) return false;
        profileDraft.Load(profileDraft.Snapshot); RefreshView(); return true;
    }
}
