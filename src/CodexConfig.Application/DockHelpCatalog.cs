namespace CodexConfig.Application;

public sealed record DockHelpTopic(string Id, string Title, string Text, string Destination, string Action);
public static class DockHelpCatalog
{
    public const string QuickStartText = "先选择 Codex 实际使用的配置目录。在“连接”填写服务或读取官方文件登录，在“模型与行为”读取服务模型或手动填写模型 ID。\n\n修改先进入草稿。确认前后差异后，Dock 会加密备份并保存。应用后可在 Codex 聊天中核对实际采用的设置。\n\n主配置、独立 Profile、方案与手机设置各自保存。切换页面保留草稿，切换目录或退出时先处理未保存修改。";
    public static IReadOnlyList<DockHelpTopic> Topics { get; } = Array.AsReadOnly(new[]
    {
        new DockHelpTopic("start", "开始配置与保存", QuickStartText, "connection", "前往连接页"),
        new DockHelpTopic("preview", "已保存文件和脱敏范围", "预览读取磁盘上的主配置和认证摘要，草稿不参与。配置副本去除注释、原排版、凭据和地址参数，认证摘要仅显示认证方式与字段是否存在。复制结果适合核对，恢复原文件请使用备份。\n\n单文件读取上限 8 MiB，单页显示上限约 100 万字符。现有聊天的记录可在“聊天参数”中核对。", "preview", "查看文件预览"),
        new DockHelpTopic("sources", "配置来源与实验功能", "Dock 用所选本机执行端的临时 app-server 读取已保存的配置层与来源。项目目录和 Profile 会改变读取范围。该操作不会启动聊天或执行工具。\n\n实验功能列表来自同一执行端的新进程。已保存的文件参与，草稿不参与。选择的开关修改会加入主配置草稿，仍需保存和应用；当前桌面和现有聊天可能使用不同设置。", "sources", "查看配置来源"),
        new DockHelpTopic("profiles", "Profile 与方案", "通过 codex --profile <名称> 选择独立的 Profile。编辑区只显示普通模型和行为设置。删除一项会移除此 Profile 的覆盖。连接、权限和私密字段会保留在源文件中。\n\n方案库存储普通设置。加入方案只修改主配置草稿，仍需确认保存。外部改动或无效字段会阻止覆盖，原草稿会保留。", "profiles", "管理 Profile"),
        new DockHelpTopic("gateways", "模型网关与手机连接", "API Key 直连通常无需模型网关。官方账号加独立 API 服务使用本机模型网关，手机访问使用独立网关。两类服务按已保存的设置分别启停，自动启动默认关闭。\n\n可从高级工具打开“模型网关”。压缩触发、返回条目和成功完成分别计数，统计只覆盖当前模型网关进程。手机设置与扫码在“手机连接”中完成。", "gateways", "查看模型网关"),
        new DockHelpTopic("tray", "窗口、托盘与开机启动", "关闭窗口通常收起到托盘，草稿与客户端增强继续保留。明确退出停止本窗口的增强，已经启动的网关可独立运行；停止网关并退出会先核对两类服务。\n\n开机启动绑定当前 Dock 程序与配置目录。同一 Windows 用户共用一个启动项，更换其他程序或目录的注册前需要核对。", "settings", "查看 Windows 设置"),
        new DockHelpTopic("recovery", "损坏文件与备份恢复", "文件无法读取时，先核对目录、格式与当前 Windows 用户。修复后重新读取，也可查看本目录的加密备份。\n\n确认差异后可恢复配置、登录、相关环境变量及模型目录依赖。普通恢复会先加密备份当前状态，未完成的保存使用原事务记录恢复并归档。外部修改或身份冲突会阻止恢复，应用到 Codex 另行确认。", "recovery", "查看恢复记录"),
        new DockHelpTopic("updates", "软件更新与安装", "手动检查从指定 GitHub 仓库读取 Windows 稳定版本。下载后及安装前会校验 SHA-256。安装前需处理草稿，并等待当前目录的两个网关退出。\n\nLovest Code 在 Android 上独立检查更新。", "updates", "查看软件更新")
    });
    public static IReadOnlyList<DockHelpTopic> Search(string query) => Topics.Where(topic => (topic.Title + "\n" + topic.Text).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
}
