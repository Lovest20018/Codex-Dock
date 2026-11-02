using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

internal sealed class ProfileWindow : Window
{
    private readonly ProfileStore store = new();
    private readonly SettingsEditor editor = new() { OrdinaryProfile = true };
    private readonly Dictionary<string, string?> changes = new(StringComparer.Ordinal);
    private readonly ComboBox names = new() { IsEditable = true, MinWidth = 160 };
    private readonly TextBlock status = HelpContent.Note("");
    private readonly TextBox preview = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 200, MinHeight = 80 };
    private readonly Button save = new() { Content = "保存此 Profile", IsEnabled = false };
    private readonly Button restore = new() { Content = "恢复上次 Profile" };
    private readonly string home, defaultModel;
    private readonly byte[]? inherited;
    private ProfileSnapshot? snapshot;
    private ProfilePlan? plan;
    private readonly bool isolatedTest;

    internal ProfileWindow(string home, string model, bool isolatedTest = false)
    {
        this.isolatedTest = isolatedTest;
        this.home = home; defaultModel = model;
        var basePath = Path.Combine(home, "config.toml");
        inherited = File.Exists(basePath) && new FileInfo(basePath).Length <= 8 * 1024 * 1024 ? File.ReadAllBytes(basePath) : null;
        Title = "独立 Profile 文件"; Width = 920; Height = 790; MinWidth = 650; MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; SetResourceReference(BackgroundProperty, "Canvas");
        var layout = new Grid { Margin = new(20) }; Content = layout;
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel(); layout.Children.Add(header);
        header.Children.Add(HelpContent.Note("独立保存为 <名称>.config.toml，通过 codex --profile <名称> 选择。此窗口保存普通模型与行为设置；已存在的其他字段原样保留。", new(0, 0, 0, 10)));
        var tools = new WrapPanel(); header.Children.Add(tools);
        names.ItemsSource = store.List(home); names.Text = store.List(home).FirstOrDefault() ?? "daily"; tools.Children.Add(names);
        var open = new Button { Content = "读取 / 新建", Margin = new(8, 0, 8, 8) }; tools.Children.Add(open);
        open.Click += (_, _) => { var selected = names.Text.Trim(); if (ResolveDraft()) LoadProfile(selected); };
        foreach (var template in new[] { BuiltInTemplates.Daily, BuiltInTemplates.Deep })
        {
            var button = new Button { Content = template.Name, Margin = new(0, 0, 8, 8) }; tools.Children.Add(button);
            button.Click += (_, _) =>
            {
                if (snapshot is null) return;
                foreach (var setting in template.Settings)
                {
                    // Existing per-field drafts require explicit editing rather than silent replacement.
                    if (!changes.ContainsKey(setting.Path)) changes[setting.Path] = setting.Value;
                }
                editor.Configure(snapshot.Config, changes); Invalidate(); editor.ShowChanged();
            };
        }
        header.Children.Add(status);
        Grid.SetRow(editor, 1); layout.Children.Add(editor);
        var footer = new StackPanel(); Grid.SetRow(footer, 2); layout.Children.Add(footer); footer.Children.Add(preview);
        var actions = new WrapPanel { Margin = new(0, 10, 0, 0) }; footer.Children.Add(actions);
        var inspect = new Button { Content = "预览此 Profile", Margin = new(0, 0, 8, 0) }; actions.Children.Add(inspect); actions.Children.Add(save); restore.Margin = new(8, 0, 0, 0); actions.Children.Add(restore);
        inspect.Click += (_, _) => Preview(); save.Click += (_, _) => SaveProfile();
        restore.Click += (_, _) =>
        {
            if (snapshot is null || !ResolveDraft()) return;
            if (MessageBox.Show(this, "恢复此 Profile 上次保存前的内容？当前内容也会加密备份。", "恢复 Profile", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { store.Restore(store.Load(home, snapshot.Name)); LoadProfile(snapshot.Name); } catch (Exception ex) { status.Text = ex.Message; }
        };
        editor.Changed += Invalidate; Closing += (_, e) => { if (!ResolveDraft()) e.Cancel = true; };
        LoadProfile();
    }
    private void LoadProfile(string? selected = null)
    {
        try
        {
            var name = selected ?? names.Text.Trim(); snapshot = store.Load(home, name); names.Text = name;
            changes.Clear(); plan = null; save.IsEnabled = false; preview.Text = "先编辑，再预览所选字段。";
            editor.Configure(snapshot.Config, changes); editor.SetContext(home, defaultModel, inherited);
            status.Text = ProfileStore.FilePath(home, name) + "\n使用：codex --profile " + name + "；当前聊天和主配置未切换。";
            restore.IsEnabled = store.LatestBackup(home, name) is not null;
        }
        catch (Exception ex) { snapshot = null; save.IsEnabled = false; status.Text = ex.Message; }
    }
    private void Invalidate() { plan = null; save.IsEnabled = false; preview.Text = "有草稿修改；请预览后保存。"; }
    private void Preview()
    {
        try
        {
            if (snapshot is null) throw new InvalidOperationException("请先读取或新建 Profile。");
            editor.Revalidate(); if (editor.Errors.Count > 0) throw new ArgumentException(string.Join("\n", editor.Errors.Values.Distinct()));
            plan = store.Plan(snapshot, changes); preview.Text = plan.Preview + (plan.Warnings.Count > 0 ? "\n\n" + string.Join("\n", plan.Warnings) : "");
            save.IsEnabled = changes.Count > 0;
        }
        catch (Exception ex) { plan = null; save.IsEnabled = false; preview.Text = ex.Message; }
    }
    private void SaveProfile()
    {
        if (plan is null || snapshot is null) return;
        try { store.Save(plan); names.ItemsSource = store.List(home); LoadProfile(snapshot.Name); status.Text += "\n已保存，原内容已加密备份。"; }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    private bool ResolveDraft()
    {
        if (changes.Count == 0) return true;
        if (isolatedTest) { changes.Clear(); return true; }
        var result = MessageBox.Show(this, "此 Profile 有未保存修改。是：预览并保存；否：放弃草稿；取消：继续编辑。", "未保存 Profile", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.No) { changes.Clear(); return true; }
        if (result != MessageBoxResult.Yes) return false;
        Preview(); if (plan is null) return false;
        SaveProfile(); return changes.Count == 0;
    }
}
