# Dock、手机与安卓运行设置清单（2026-11-02）

本清单补充 [Codex TOML 全量清单](configuration-options-2026-11-02.md)。下面的默认值来自生产源码，不读取或导出用户密码、连接密钥、Cookie 或设备令牌。内部身份字段用于解释保存边界，不建议开放为普通用户编辑项。

## Windows 连接与应用

来源：`ConnectionSettings.cs`、`ConfigStore.cs`、`ConnectionDetails.cs`、`ApplicationSettings.cs`。普通配置走统一草稿和备份；开机启动立即写入当前用户启动项。

| 设置 / 字段 | 当前代码默认或规则 | 建议 |
| --- | --- | --- |
| 配置目录 | CODEX_HOME 优先，否则用户 .codex；可手动选择 | 显示当前目录和生效来源，切换前处理草稿。 |
| 连接模式 Mode | Direct / OfficialWithApi；新记录默认 Direct | 模式联动说明凭据、路由和后台网关。 |
| 服务类型 Kind | PatRelay / ResponsesApi；默认 PatRelay | 按所选服务显示地址和认证要求。 |
| 认证选择 Authentication | Auto / Pat / ApiKey；默认 Auto | 显示最终判定；PAT 需要 HTTPS 和支持的后端路径。 |
| 服务地址 BaseUrl | 用户必填；规范化 /models、/responses 后缀 | 保留现有规范化，不把保存等同连通验证。 |
| PAT / API Key | 新连接必填；普通设置保存保留原凭据 | 沿用隐藏显示和加密备份。 |
| 模型 Model | 用户填写或从当前服务目录选择 | 按当前目录提供能力及推理档位。 |
| 混合网关系统代理 UseSystemProxy | 默认 false；仅作用于模型网关转发 | 按实际网络选择，显示与 Codex 命令联网的区别。 |
| 连接名称 / 备注 | 默认空；当前用户加密保存 | 保留，不混入上游模型配置。 |
| ChatGPT 登录 | 原生客户端完成，暂存到保存时写入 | 保留暂存/验证机制，不为通用优化改认证存储。 |
| FastMode / ContextManagement / WebSearch DTO 默认 | 构造函数均为 true；UI 使用 PreservePreferenceValues 保留未设置值 | 区分 DTO 初值与 Codex 默认；后续简化为显式可空覆盖更清楚。 |
| 开机启动 | 默认未登记；开启立即生效，启动时收起托盘 | 保持自选，不默认开启网关端口。 |
| 主题 | Windows 应用当前跟随系统 | 个人偏好不作为性能优化项。 |
| 窗口关闭行为 | 固定收起到托盘，保留草稿 | 这是产品行为，不是 Codex TOML 开关。 |
| 软件更新 | 手动检查、下载、校验后安装 | 与 Codex 的 check_for_update_on_startup 分开显示。 |

## 模型网关私有状态与限制

来源：`MixedConnection.cs`、`ModelBridge.cs`。这些值与官方认证、恢复旧路由的记录一起保存，不宜混入常用配置页。

| 设置 / 字段 | 当前代码默认或规则 | 建议 |
| --- | --- | --- |
| BaseUrl / ApiKey / Kind | 所选上游地址、独立凭据、协议；加密保存 | 由连接页管理。 |
| Port | 首次分配空闲本机端口，后续复用 | 保持随机分配和冲突检查。 |
| LocalToken | 32 字节随机值，以 64 位十六进制保存 | 程序管理，不展示或手工编辑。 |
| NativeRoute | 新混合连接为 true | 保留原生 OpenAI 路由；不要把兼容 provider 表当实际活跃路由。 |
| UseSystemProxy | 默认 false | 仅网关出站代理。 |
| OriginalValues / OriginalAuthApiBaseUrl | 切换前值，用于恢复 | 保持恢复记录，不当作旧数据清理。 |
| 并发 slots | 16 | 目前无排队/吞吐瓶颈证据，保留。 |
| 单请求 / WebSocket 消息上限 | 64 MiB | 保留；达到上限再按证据调整。 |
| HTTP 等响应头 | 60 秒 | 按阶段显示超时原因，避免直接延长所有超时。 |
| WebSocket 建连 | 30 秒 | 保留。 |
| 流读取 / WebSocket 空闲 | 5 分钟，收到数据后续期 | 保留；与反向代理空闲超时对齐。 |
| 请求分类诊断 | 当前按 /responses/compact 路径标记 compact | 补充 Responses v2 trigger / compaction 响应计数与完成时间。 |

## 手机网关启动配置

来源：`MobileGateway.cs`、`src/CodexMobile/main.py`、`workbench.py`。当前持久化协议为 4，公开 HTTP 绑定 IPv4 接口；私有管理服务仅绑定本机。

| 设置 / 字段 | 当前代码默认或规则 | 建议 |
| --- | --- | --- |
| protocol | 4；旧 1/2/3 记录读取时兼容转换 | 保持版本校验。 |
| publicUrl | 用户提供受信任的 HTTPS 根域名，不含路径/账号/参数 | 继续要求完整入口检测。 |
| port | 13090；范围 1024–65535 | 无冲突保持默认。 |
| controlPort | 自动分配；范围 1024–65535，须与 port 不同 | 内部管理端口不开放编辑。 |
| controlToken | 64 位随机十六进制 | 程序管理。 |
| instanceId | 32 位随机十六进制 | 程序管理。 |
| auth.mode | password | 保持当前认证方式。 |
| auth.username | 1–200 字符 | 独立手机访问账号。 |
| 访问密码（输入项） | 12–256 字符；账号不变时留空沿用 | 保留现有更新/会话失效规则。 |
| auth.salt / auth.hash | 32 / 64 位十六进制 | 仅保存盐与派生哈希，不显示。 |
| auth.iterations | 600000，当前两端校验要求精确相等 | 不为提升速度降低。 |
| auth.sessionHours | 12，当前两端校验要求精确相等 | 若增加可配需同时做协议、迁移、撤销与双端校验。 |
| codexHome | 所选真实配置目录，绝对路径 | 随配置目录隔离。 |
| dataDir | 按配置目录身份计算的应用数据目录 | 保持隔离，不复用其他连接的数据。 |

## Web / 手机外观与阅读

来源：固定上游 `web/features/settings/presentation.js`。保存在当前浏览器/WebView；安卓原生主题和语言会做同步。

| 设置 / 字段 | 当前代码默认或规则 | 建议 |
| --- | --- | --- |
| theme | "system" | 按阅读需要选择，不改变模型用量。 |
| accent | "#4664ed" | 按阅读需要选择，不改变模型用量。 |
| fontSize | 16 | 按阅读需要选择，不改变模型用量。 |
| codeSize | 13 | 按阅读需要选择，不改变模型用量。 |
| density | "standard" | 按阅读需要选择，不改变模型用量。 |
| showReasoning | true | 按阅读需要选择，不改变模型用量。 |
| showProcess | true | 保留可追溯性；仅影响展示，不停止工具执行。 |
| showToolImages | true | 弱网或图片多时可关闭，减少预览加载。 |
| showActivity | true | 按阅读需要选择，不改变模型用量。 |
| showComputer | true | 按阅读需要选择，不改变模型用量。 |
| showPushplus | true | Dock 已隐藏部分上游快捷入口；不当作新增产品功能。 |
| showAccounts | true | Dock 已隐藏部分上游快捷入口；不当作新增产品功能。 |
| showFileThumbnails | true | 弱网或图片多时可关闭，减少预览加载。 |
| language | zh / en；由当前客户端语言设置决定 | 与安卓原生语言同步；保留。 |

## 聊天、通知与文件策略

来源：`notifications/channels.py`、`accounts/accounts.py`、`workspace/preferences.py` 和生产聊天发送流程。

| 设置 / 字段 | 当前代码默认或规则 | 建议 |
| --- | --- | --- |
| 通知默认 requests | true | 审批/输入等待通知按需要保留。 |
| 通知默认 completion | false | 经常离开电脑等待任务时可主动开启。 |
| 每聊天 requests / completion | inherit / on / off | 继承网关默认，按聊天覆盖，避免全局重复改。 |
| Codex 官方重置动态 enabled | false；当前网关所有设备共用 | 只在需要账号动态信息时启用。 |
| clickDownloadMiB | 20 MiB；允许 1–1048576 的整数 | 建议保留 20；UI 可增加常用值与合理范围提示，降低误填。 |
| 发送方式 | 发送新消息 / 完成后发送 / 补充当前任务；默认发送新消息 | 运行时需改变当前任务才使用补充；CLI 补充附件受限制。 |
| 聊天模型 / 推理 / 工作模式 / 权限 | 按会话和执行 owner 提供；可能覆盖用户默认 | 显示当前会话值与执行端，不冒充全局配置。 |

## 继承的通知通道字段

包括 23 个基础默认字段，以及多端点列表和显示名。默认值来自固定上游；部分由通知页/API 管理，不代表 Dock 首页开放所有入口。本次没有发送通知，也没有启用任何通道。

| 设置 / 字段 | 当前代码默认或规则 | 建议 |
| --- | --- | --- |
| enabled | false | 仅使用对应通知通道时填写；未使用保持默认。 |
| server | "https://ntfy.sh" | 仅使用对应通知通道时填写；未使用保持默认。 |
| topic | "" | 仅使用对应通知通道时填写；未使用保持默认。 |
| token | "" | 敏感字段，保持遮盖、独立保存，不进入日志或模板。 |
| barkEnabled | false | 仅使用对应通知通道时填写；未使用保持默认。 |
| barkServer | "https://api.day.app" | 仅使用对应通知通道时填写；未使用保持默认。 |
| barkKey | "" | 敏感字段，保持遮盖、独立保存，不进入日志或模板。 |
| pushplusEnabled | false | 仅使用对应通知通道时填写；未使用保持默认。 |
| pushplusToken | "" | 敏感字段，保持遮盖、独立保存，不进入日志或模板。 |
| webhookEnabled | false | 仅使用对应通知通道时填写；未使用保持默认。 |
| webhookUrl | "" | 仅使用对应通知通道时填写；未使用保持默认。 |
| webhookHeaders | {} | 敏感字段，保持遮盖、独立保存，不进入日志或模板。 |
| webhookProvider | "auto" | 仅使用对应通知通道时填写；未使用保持默认。 |
| webhookSignEnabled | false | 仅使用对应通知通道时填写；未使用保持默认。 |
| webhookSignSecret | "" | 敏感字段，保持遮盖、独立保存，不进入日志或模板。 |
| webhookBodyTemplate | 默认 JSON 模板 | 仅使用对应通知通道时填写；未使用保持默认。 |
| clickBase | "" | 仅使用对应通知通道时填写；未使用保持默认。 |
| includeTitle | false | 默认不附带聊天标题，按通知内容需要选择。 |
| addressEnabled | true | 仅使用对应通知通道时填写；未使用保持默认。 |
| addressName | "" | 仅使用对应通知通道时填写；未使用保持默认。 |
| securityEnabled | true | 仅使用对应通知通道时填写；未使用保持默认。 |
| mobileEnabled | false | 仅使用对应通知通道时填写；未使用保持默认。 |
| mobileAppLinks | false | 仅使用对应通知通道时填写；未使用保持默认。 |
| webhookEndpoints | [] | 多 Webhook 端点列表，按需要配置。 |
| webhookName | 默认 Webhook | 显示名；不影响模型执行。 |

## 继承的账号提醒策略

来源：`accounts/monitor.py`。均为 opt-in；被 Dock 屏蔽的上游桌面升级入口不应重新开放。

| 设置 / 字段 | 当前代码默认或规则 | 建议 |
| --- | --- | --- |
| lowQuota | false | 按账号提醒需要启用；与模型压缩/性能无关。 |
| quotaReset | false | 按账号提醒需要启用；与模型压缩/性能无关。 |
| resetExpiry | false | 按账号提醒需要启用；与模型压缩/性能无关。 |
| subscriptionExpiry | false | 按账号提醒需要启用；与模型压缩/性能无关。 |
| desktopUpdate | false | 按账号提醒需要启用；与模型压缩/性能无关。 |
| globalReset | false | 按账号提醒需要启用；与模型压缩/性能无关。 |
| globalResetSignal | false | 按账号提醒需要启用；与模型压缩/性能无关。 |

## 安卓原生设置与持久化边界

来源：自有 `MainActivity.java`、`SettingsPage.java` 及固定上游 `SettingsUi.java`。

| 设置 / 字段 | 当前代码默认或规则 | 建议 |
| --- | --- | --- |
| alerts 任务通知 | false；还需系统通知权限与通道可用 | 需要离开聊天接收提醒时启用。 |
| backgroundNotifications 后台通知 | false；依赖 alerts，可能需要电池策略允许 | 常驻提醒才启用，显示实际系统状态。 |
| settingsTheme | system / light / dark，默认 system | 与 WebView 主题同步，保留。 |
| language | zh / en，默认取系统语言 | 与 WebView 语言同步，保留。 |
| 电脑列表 / 重命名 / 当前电脑 | 按手机本地保存；地址、名称和 active 状态 | 作为连接资料管理，不放入 Codex 配置模板。 |
| 上次聊天 / 打开时间 / 草稿 | 按网关/聊天隔离的恢复状态 | 保持现有恢复机制；不当作垃圾配置自动清除。 |
| Cookie 名 / 设备绑定 / 通知游标 | 内部认证与恢复记录 | 不作为可调参数，也不导出到审查文档。 |

## 构建与发布配置

`CodexConfig.App.csproj` 的 Windows 版本、`src/CodexAndroid/android.json` 的安卓版本/SDK/签名指纹/依赖哈希、上游快照清单和 GitHub Actions 规则属于交付元数据。现有 Windows 0.15.1 / Android 0.2.2；不建议为了运行配置优化改版本号、签名、固定依赖或重新发布。

本次没有把上游源码中所有常量、历史产品入口或内部缓存都计为面向用户的配置项；实际有效的动态实例、插件和会话覆盖需按运行环境继续展开。
