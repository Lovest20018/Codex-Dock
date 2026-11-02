using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

public sealed partial class SettingsEditor
{
    private void AddInstance() => CreateInstanceEditor().ShowDialog();

    internal Window CreateInstanceEditor()
    {
        var dialog = new Window { Title = "添加配置实例", Owner = Window.GetWindow(this), Width = 560, Height = 650,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize };
        dialog.SetResourceReference(BackgroundProperty, "Canvas");
        var body = new StackPanel { Margin = new(24) };
        dialog.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var group = new ComboBox { ItemsSource = new[] { "MCP 服务", "模型供应商", "权限方案" }, SelectedIndex = 0 };
        var name = new TextBox(); var valueFields = new StackPanel(); var error = Helper("", new(0, 10, 0, 0));
        Field(body, "类型", group); Field(body, "名称（点号会作为名称的一部分保存）", name); body.Children.Add(valueFields);
        body.Children.Add(Helper("添加到草稿后可继续编辑详细字段；点击主窗口的“保存配置”才写入文件。新权限方案不会自动设为默认。", new(0, 16, 0, 0)));
        var add = new Button { Content = "添加到草稿", HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) };
        body.Children.Add(add); body.Children.Add(error);
        var command = new TextBox(); var url = new TextBox(); var args = new TextBox { Text = "[]" };
        var transport = new ComboBox { ItemsSource = new[] { "命令（stdio）", "HTTP" }, SelectedIndex = 0 };
        var baseUrl = new TextBox(); var envKey = new TextBox();
        var extends = new ComboBox { ItemsSource = new[] { ":read-only", ":workspace" }, SelectedIndex = 0 };
        var description = new TextBox();
        void Fields()
        {
            valueFields.Children.Clear();
            if (group.SelectedIndex == 0)
            {
                Field(valueFields, "传输", transport);
                if (transport.SelectedIndex == 0) { Field(valueFields, "命令（保存时不会执行）", command); Field(valueFields, "参数（TOML 字符串列表）", args); }
                else Field(valueFields, "HTTP URL", url);
            }
            else if (group.SelectedIndex == 1)
            {
                Field(valueFields, "模型入口 URL", baseUrl); Field(valueFields, "认证环境变量名（可留空）", envKey);
                valueFields.Children.Add(Helper("使用 Responses 协议。凭据不写入此表单；添加后可编辑命令认证等详细选项。", new(0, 8, 0, 0)));
            }
            else { Field(valueFields, "继承的内置权限", extends); Field(valueFields, "说明", description); }
        }
        group.SelectionChanged += (_, _) => Fields(); transport.SelectionChanged += (_, _) => Fields(); Fields();
        add.Click += (_, _) =>
        {
            try
            {
                var id = name.Text.Trim();
                if (id.Length is 0 or > 100 || id.Any(char.IsControl) || id.StartsWith(':') || id.Contains('<') || id.Contains('>'))
                    throw new ArgumentException("请填写有效的实例名称。");
                var root = group.SelectedIndex switch { 0 => "mcp_servers", 1 => "model_providers", _ => "permissions" };
                var prefix = TomlDocument.FormatPath([root, id]); var document = Projected();
                if (document.Get(prefix) is not null) throw new ArgumentException("此名称已有配置，请在“已配置”中编辑。");
                var staged = new Dictionary<string, string?>();
                void Set(string key, string repr) { var path = prefix + "." + key; catalog.Validate(path, repr, true); staged[path] = repr; document.SetRaw(path, repr); }
                if (group.SelectedIndex == 0)
                {
                    if (transport.SelectedIndex == 0)
                    {
                        if (string.IsNullOrWhiteSpace(command.Text)) throw new ArgumentException("请填写命令。");
                        Set("command", TomlDocument.Quote(command.Text.Trim())); Set("args", args.Text.Trim());
                    }
                    else Set("url", TomlDocument.Quote(url.Text.Trim()));
                    Set("enabled", "true");
                }
                else if (group.SelectedIndex == 1)
                {
                    if (!Uri.TryCreate(baseUrl.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
                        throw new ArgumentException("请填写不含凭据的完整 HTTP 或 HTTPS 模型入口。");
                    Set("name", TomlDocument.Quote(id)); Set("base_url", TomlDocument.Quote(uri.AbsoluteUri.TrimEnd('/'))); Set("wire_api", "\"responses\"");
                    if (!string.IsNullOrWhiteSpace(envKey.Text)) Set("env_key", TomlDocument.Quote(envKey.Text.Trim()));
                }
                else { Set("extends", TomlDocument.Quote((string)extends.SelectedItem)); Set("description", TomlDocument.Quote(description.Text.Trim())); }
                ConfigConstraints.ValidateChanges(Projected(), document, capabilities);
                foreach (var (path, value) in staged) changes[path] = value;
                ShowConfigured(); Changed?.Invoke(); dialog.Close();
            }
            catch (Exception ex) when (ex is ArgumentException or System.IO.InvalidDataException) { error.Text = ex.Message; }
        };
        return dialog;

        static void Field(Panel target, string label, Control input)
        {
            target.Children.Add(Helper(label, new(0, 14, 0, 6))); target.Children.Add(input);
            System.Windows.Automation.AutomationProperties.SetName(input, label);
        }
    }
}
