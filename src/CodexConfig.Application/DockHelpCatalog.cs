namespace CodexConfig.Application;

public sealed record DockHelpTopic(string Id, string Title, string Text, string Destination, string Action);
public static class DockHelpCatalog
{
    public const string QuickStartText = "先选择 Codex 实际使用的配置目录，完成服务连接与模型设置，或读取同目录的官方文件登录。新版入口为“Codex 配置”：在连接页填写服务，在模型与行为页选择模型。\n\n修改先进入草稿。查看目标文件与前后差异，确认后自动加密备份并保存；保存完成后，再单独应用到 Codex，并在聊天中核对。\n\n主配置、独立 Profile、方案与手机设置有各自的保存范围。切换页面保留草稿，切换目录或明确退出时先处理未保存选择。";
    public static IReadOnlyList<DockHelpTopic> Topics { get; } = Array.AsReadOnly(new[]
    {
        new DockHelpTopic("start", "开始配置与保存", QuickStartText, "connection", "前往连接页"),
        new DockHelpTopic("preview", "文件预览与生效来源", "文件预览只读磁盘已保存的主配置和认证摘要，不包含未保存草稿。注释与原排版不展示，凭据及地址参数隐藏。\n\n配置来源页通过明确选择的本机执行端读取配置层；聊天参数页读取明确选中的本机记录。这些结果的范围不同，均不能单独证明当前聊天或远端服务的实际档位。", "preview", "查看文件预览"),
        new DockHelpTopic("profiles", "Profile 与方案", "Profile 独立保存普通模型和行为设置，不切换主配置或现有聊天。通过执行端的 --profile 参数选择。\n\n方案库存储普通设置；加入方案只修改主配置草稿，仍需单独确认保存。外部改动或无效字段会阻止覆盖，原草稿会保留。", "profiles", "管理 Profile"),
        new DockHelpTopic("gateways", "模型网关与手机连接", "API Key 直连通常无需模型网关。官方账号加独立 API 服务使用本机模型网关；手机访问使用独立的手机网关。两类网关按已保存设置分别启停。\n\n自动启动默认关闭，刷新只读状态。压缩触发、返回条目和成功完成分别计数，统计只覆盖当前模型网关进程。新版的模型网关位于“运行与增强”；手机入口、扫码、设备授权和手机服务启停统一在“手机访问”管理。", "gateways", "查看模型网关"),
        new DockHelpTopic("tray", "窗口、托盘与开机启动", "关闭窗口通常收起到托盘，草稿与客户端增强继续保留。明确退出停止本窗口的增强，已经启动的网关可独立运行；停止网关并退出会先核对两类服务。\n\n开机启动绑定当前 Dock 程序与配置目录。同一 Windows 用户共用一个启动项，更换其他程序或目录的注册前需要核对。", "settings", "查看 Windows 设置"),
        new DockHelpTopic("recovery", "损坏文件与备份恢复", "文件无法读取时保留原文件，先核对目录、格式与当前 Windows 用户。修复后重新读取；也可查看本目录已有的加密备份。\n\n恢复先展示差异与来源，确认后再执行。未完成事务先恢复再编辑。备份不能绕过外部修改与身份冲突保护，恢复不会自动重启 Codex。", "recovery", "查看恢复记录"),
        new DockHelpTopic("updates", "正式版更新与预览", "软件更新只在手动检查后读取指定仓库的正式发布。安装包校验通过后才可安装，安装前处理草稿并等待当前目录的两个网关退出。\n\n正式版安装器安装现有主程序；WinUI 仍是独立预览入口。切换预览文件前先从托盘明确退出原预览，保留整个程序目录。", "updates", "查看软件更新")
    });
    public static IReadOnlyList<DockHelpTopic> Search(string query) => Topics.Where(topic => (topic.Title + "\n" + topic.Text).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
}
