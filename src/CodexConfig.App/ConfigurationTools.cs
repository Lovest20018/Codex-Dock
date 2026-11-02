using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CodexConfig.Core;
using Microsoft.Win32;

namespace CodexConfig.App;

internal sealed class ConfigurationTools : Expander
{
    private readonly TextBox executable = new(), cwd = new() { Text = Environment.CurrentDirectory }, search = new();
    private readonly ComboBox profile = new() { IsEditable = true };
    private readonly TextBlock status = HelpContent.Note("读取所选本机执行端的配置层；不代表已打开聊天的会话参数。草稿不会参与读取。"), layers = HelpContent.Note("");
    private readonly DataGrid table = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, MaxHeight = 360, MinHeight = 120 };
    private readonly Button read = new() { Content = "读取配置来源" };
    private readonly CancellationToken cancellation;
    private string? home;
    private byte[]? localConfig;
    private string model = "";
    private ConfigurationReadResult? result;
    internal Action? RefreshCapabilities;
    internal ConfigurationReadResult? Snapshot => result;
    internal async Task ReadForSmokeAsync(string program, string project)
    {
        executable.Text = program; cwd.Text = project; profile.Text = ""; await Read();
    }

    internal ConfigurationTools(CancellationToken cancellation)
    {
        this.cancellation = cancellation; Header = "来源与 profile"; Margin = new(0, 0, 0, 14);
        var body = new StackPanel { Margin = new(0, 10, 0, 0) }; Content = body;
        executable.Text = CodexExecutableLocator.Find() ?? "";
        Field("本机执行端 codex.exe", executable);
        var choose = new Button { Content = "选择程序", HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 6, 0, 0) }; body.Children.Add(choose);
        choose.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "Codex 原生程序|codex.exe|应用程序|*.exe" }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) { executable.Text = dialog.FileName; Invalidate(); } };
        Field("项目目录（决定项目覆盖层）", cwd);
        var folder = new Button { Content = "选择项目目录", HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 6, 0, 0) }; body.Children.Add(folder);
        folder.Click += (_, _) => { var dialog = new OpenFolderDialog { Title = "选择配置来源的项目目录" }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) { cwd.Text = dialog.FolderName; Invalidate(); } };
        Field("Profile（留空读取主配置）", profile);
        var actions = new WrapPanel { Margin = new(0, 12, 0, 8) }; body.Children.Add(actions); actions.Children.Add(read);
        var profiles = new Button { Content = "管理 Profile 文件", Margin = new(8, 0, 8, 0) }; actions.Children.Add(profiles);
        profiles.Click += (_, _) => { if (home is not null) { new ProfileWindow(home, model) { Owner = Window.GetWindow(this) }.ShowDialog(); profile.ItemsSource = new[] { "" }.Concat(new ProfileStore().List(home)).ToArray(); Invalidate(); } };
        var refresh = new Button { Content = "重新读取本地模型目录" }; actions.Children.Add(refresh); refresh.Click += (_, _) => { RefreshCapabilities?.Invoke(); status.Text = "已重新读取本地模型能力文件；远端可用性需由服务确认。"; };
        body.Children.Add(status); body.Children.Add(layers);
        Field("筛选来源结果", search); body.Children.Add(table);
        foreach (var (title, property, width) in new[] { ("字段", "Path", 260), ("本文件值", "FileValue", 170), ("执行端读取值", "EffectiveValue", 170), ("来源", "Source", 280), ("作用范围", "Scope", 240) })
            table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        search.TextChanged += (_, _) => Filter();
        executable.TextChanged += (_, _) => Invalidate(); cwd.TextChanged += (_, _) => Invalidate(); profile.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => Invalidate()));
        read.Click += async (_, _) => await Read();

        void Field(string label, Control input) { body.Children.Add(HelpContent.Note(label, new(0, 10, 0, 6))); body.Children.Add(input); System.Windows.Automation.AutomationProperties.SetName(input, label); }
    }
    internal void Configure(string directory, byte[]? config, string selectedModel)
    {
        home = directory; localConfig = config; model = selectedModel;
        profile.ItemsSource = new[] { "" }.Concat(new ProfileStore().List(home)).ToArray(); Invalidate();
    }
    private void Invalidate() { result = null; table.ItemsSource = null; layers.Text = ""; status.Text = "尚未读取当前条件的配置快照；读取不包含主窗口未保存的草稿，也不读取现有聊天参数。"; }
    private void Filter() => table.ItemsSource = result?.Rows.Where(row => (row.Path + " " + row.Source).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    private async Task Read()
    {
        if (home is null) return;
        var expectedHome = home; var selectedCwd = cwd.Text; var selectedExe = executable.Text; var selectedProfile = profile.Text.Trim();
        read.IsEnabled = false; status.Text = "正在读取本机配置层…";
        try
        {
            var comparePath = Path.Combine(home, "config.toml");
            if (selectedProfile.Length == 0 && File.Exists(comparePath) && new FileInfo(comparePath).Length > 8 * 1024 * 1024) throw new IOException("配置文件过大。");
            var compare = selectedProfile.Length == 0 ? File.Exists(comparePath) ? File.ReadAllBytes(comparePath) : null : new ProfileStore().Load(home, selectedProfile).Config;
            var snapshot = await new ConfigReadClient().ReadAsync(selectedExe, home, selectedCwd, selectedProfile.Length == 0 ? null : selectedProfile, compare, cancellation);
            if (home != expectedHome || cwd.Text != selectedCwd || executable.Text != selectedExe || profile.Text.Trim() != selectedProfile) return;
            result = snapshot; Filter();
            status.Text = $"本机执行端快照 · {snapshot.ReadAt.LocalDateTime:g} · {snapshot.Rows.Count} 项\n{snapshot.Executable}\n项目：{snapshot.Cwd} · 比较文件：{(selectedProfile.Length == 0 ? "config.toml" : selectedProfile + ".config.toml")}\n当前聊天和组织强制要求可能另有覆盖；返回缺失项显示未知。";
            layers.Text = string.Join("\n", snapshot.Layers.Select(layer => layer.Source + " · " + layer.State));
        }
        catch (OperationCanceledException) { status.Text = "读取已取消或超时；生效来源未确认。"; }
        catch (NotSupportedException error) { status.Text = error.Message; }
        catch (Exception) { status.Text = "无法读取此执行端的配置层。请核对程序、项目目录、profile 和内核版本；生效来源未确认。"; }
        finally { read.IsEnabled = !cancellation.IsCancellationRequested; }
    }
}
