# Codex Dock · Lovest Code

<img src="assets/brand/logo-128.png" width="72" height="72" alt="Codex Dock 接入图标">

在 Windows 上管理 Codex 的连接、模型、配置与后台服务，用手机继续自己的聊天。

**Codex Dock** 是 Windows 配置应用，**Lovest Code** 是 Android 客户端。电脑端托管 Web / 手机网关，连接本机 Codex Desktop、CLI，以及已登记的 WSL / SSH 执行环境。

[Windows 下载](https://github.com/Lovest20018/Codex-Dock/releases/tag/v0.19.7) · [Android 下载](https://github.com/Lovest20018/Codex-Dock/releases/tag/android-v0.2.5) · [全部版本](https://github.com/Lovest20018/Codex-Dock/releases) · [反馈问题](https://github.com/Lovest20018/Codex-Dock/issues)

## 当前版本

| 应用 | 版本 | 这次更新 |
| --- | --- | --- |
| Windows · Codex Dock | **[0.19.7](https://github.com/Lovest20018/Codex-Dock/releases/tag/v0.19.7)** | 精修首页、连接表单和高级工具，简化模型切换、首次连接与手机配对，补充网关响应诊断。 |
| Android · Lovest Code | **[0.2.5](https://github.com/Lovest20018/Codex-Dock/releases/tag/android-v0.2.5)** | 更换为“接入 · 浅绿”图标，适配自适应启动器、Android 13+ 单色图标和通知；沿用原包名和签名，可覆盖升级。 |

Windows、Android 与 Web 现在使用同一套开口 D、接入箭头和浅绿配色，Web 页头、浏览器及添加到主屏幕图标随 Windows 手机后台更新。两个应用独立构建与发布；Windows 的更新入口只跟踪 `v*`，安卓应用内更新只跟踪 `android-v*`。

## 安装与覆盖升级

| 平台 | 安装文件 | 环境 |
| --- | --- | --- |
| Windows | [安装器 EXE](https://github.com/Lovest20018/Codex-Dock/releases/download/v0.19.7/CodexDock-0.19.7-Setup-win-x64.exe) · [便携 ZIP](https://github.com/Lovest20018/Codex-Dock/releases/download/v0.19.7/CodexDock-0.19.7-win-x64.zip) | Windows x64，目标最低版本 Windows 10 1809，随包提供 .NET、Windows App SDK 和手机后台。 |
| Android | [LovestCode-Android-0.2.5.apk](https://github.com/Lovest20018/Codex-Dock/releases/download/android-v0.2.5/LovestCode-Android-0.2.5.apk) | Android 8 / API 26 及以上。 |

**Windows 覆盖升级**可直接在安装器中选择原目录。安装器会请求运行中的 Dock 正常退出，并停止同一安装目录的网关。遇到未保存修改提示时，先在 Dock 中处理草稿。便携版请解压到独立目录，保留整个 `mobile` 文件夹和所有运行库。

**Android 覆盖升级：**直接安装新版 APK，不要先卸载。包名仍是 `io.github.lovest20018.codexdock`，原证书和应用数据目录保持；连接、授权及草稿可沿用。也可在“我的 → 版本与更新”检查新版，按系统提示允许 Lovest Code 安装应用。

更新后可在电脑启动手机服务并重新检测 HTTPS 入口。下载校验文件。 [Windows SHA-256](https://github.com/Lovest20018/Codex-Dock/releases/download/v0.19.7/SHA256SUMS-0.19.7.txt) · [Android SHA-256](https://github.com/Lovest20018/Codex-Dock/releases/download/android-v0.2.5/SHA256SUMS-Android-0.2.5.txt)

## Windows 界面

以下采用 Windows 0.19.7 界面。 [更新说明](docs/release-0.19.7.md)

| 任务入口 | 用途 |
| --- | --- |
| 首页 | 打开或回到 Codex，更换默认模型，继续设置连接。 |
| 连接与模型 | 设置登录方式，读取并选择默认模型，管理推理、速度和审批。 |
| 手机连接 | 保存手机服务设置、启停后台、检测 HTTPS、配对和管理授权设备。 |
| 高级工具 | 搜索全部配置、Profile、方案、增强、网关、配置来源、聊天参数和备份。 |

设置与帮助位于侧栏底部。页面按当前编辑对象显示保存动作；主配置、Profile、方案和手机设置分别保存。`Ctrl+S` 只处理当前编辑对象，仍需核对差异；只读页面不会顺便保存其他草稿。`Ctrl+F` 搜索当前配置或列表，`F1` / `Ctrl+Shift+H` 打开帮助。

## 配置服务与模型

1. 从首页打开 **设置连接**，确认顶部配置目录。
2. 选择 **用 ChatGPT 账号登录** 时，打开 Codex，完成登录后回到 Dock 点 **我已登录，重新读取**。
3. 使用服务商密钥时，在连接页填写地址和密钥，点击 **读取可用模型** 并选一项。也可在 **更多选项** 填写已知模型 ID。
4. 点击 **核对连接**，确认模型、服务和目标实例后保存并打开 Codex，或选 **仅保存**。保存前创建当前用户加密的备份。

已有自定义供应商、项目配置、MCP 和技能配置按原有边界保留。旧 PAT 可通过明确的迁移操作改为 API Key；普通读取或修改模型不会自动迁移认证。服务模型列表为按需读取，保存本地配置不要求服务在线。

需要保留 ChatGPT 身份时，选择 **保留 ChatGPT 登录，模型使用此密钥**。先在原生 Codex 登录，再读取该目录的官方登录，或明确选择已有加密备份。模型请求通过本机网关使用独立服务的 API Key。所需网关在应用前检查并启动。

混合连接可设置 HTTP 响应头等待，默认 **120 秒**，范围 **60–3600 秒**；收到响应头后使用独立的数据空闲计时。此设置只对经过 Dock 网关的 HTTP 请求生效。普通模型目录保留经过校验的图片、上下文、推理和服务档位声明，不根据模型名字猜测能力。

**运行与增强** 中的兼容和接续默认关闭，需要时明确开启并应用；增强需要 Dock 常驻，不修改 Codex 安装文件。专用审核可固定为 `codex-auto-review`，能力目录与配置一起核对、备份和恢复。详见 [客户端增强](docs/desktop-connection-recovery.md)、[速度兼容](docs/desktop-speed-compatibility.md) 和 [模型与专用审核](docs/model-behavior-managed-catalog.md)。

## 从手机连接电脑

需要一个证书受手机信任的 **HTTPS 根域名**，以及该入口到 Windows 电脑的网络通路。

1. 在 **手机连接 → 服务设置** 填写 HTTPS 域名、独立账号和密码，保存并启动。
2. 将 HTTPS 根站点反向代理到电脑可达的 HTTP 端口，默认 `13090`；保留外部 `Host`（含端口）、`Origin`、Cookie 与 WebSocket 升级头。
3. 在 **连接与配对** 检测 HTTPS 入口；通过后，用 Lovest Code 扫码配对，或填写域名并登录。
4. 在电脑的 **管理 Codex 接入** 页面登记并启用执行环境，再从手机查看和继续聊天。

群晖等外部代理应使用 Windows 电脑可达的 IP，不能把 `127.0.0.1` 当成另一台电脑。私有管理端口只绑定本机，不转发到公网。域名、证书、路由、穿透和防火墙由使用者配置；示例见 [手机连接说明](docs/mobile-connection.md)。

手机端保留聊天、电脑和我的三页导航；切页保留 WebView、输入草稿和阅读位置。可查看任务、处理审批、使用附件及执行端支持的项目、Git 和终端功能。在发送选项中可选择追加当前任务或完成后发送；结果不明的操作不会自动重发，任务结束后无法追加时保留草稿。

手机网关已选择移植上游 2.2.1 的 v13 同步、连接恢复、模型缓存与列表容错。遇到无法解释的关键任务或审批结构时暂停控制并保留历史；不会凭界面连通就猜测当前任务可安全写入。详见 [网关移植记录](docs/mobile-upstream-backport-2026-11-15.md)。

## 数据与运行状态

- 默认读取 `%USERPROFILE%\.codex`，设置了 `CODEX_HOME` 时优先使用；也可在窗口中选择其他目录。
- 保存前检测真实配置变更，保留已核验的客户端管道轮换与同账号令牌刷新；失败时保留草稿。
- 手机设置与设备记录按配置目录隔离，位于 `%LOCALAPPDATA%\CodexConfig\mobile\<目录标识>`。明确撤销设备或更换账号/密码后，需要重新授权。
- 关闭窗口通常收起到托盘。退出 Dock 会停止当前窗口的增强；已启动的网关可继续运行，选择“停止网关并退出”则一并停止。
- 登录 Windows 后启动 Dock、模型网关自动启动和手机服务自动启动分别设置。刷新或切页不会开启未授权自动启动的后台。

**高级工具 → 配置来源** 可自动查找或手动选择本机执行端，并指定项目和 Profile。配置层和实验功能由临时启动的内核提供。实验功能修改先加入主配置草稿，保存后可重新读取核对。已打开的聊天可以在“聊天参数”单独核对。

## 验证范围

仓库包含 Core / 共享服务、实际 WinUI 和 WPF 窗口、打包后台启停、安装与卸载、Python / HTTP、Web 浏览器以及 Android 模拟器检查。安装包验证包含文件摘要、同安装目录后台停止、其他安装目录隔离、已保存手机设置与用户文件保留。 [Windows 0.19.7 审查](docs/review-0.19.7.md) · [Android 0.2.5 审查](docs/review-android-0.2.5.md)

自动化与模拟器不能代替实体手机的相机扫码、厂商键盘、省电策略、公网 HTTPS、其他物理 DPI、Windows 登录启动或真实模型 / WSL / SSH 任务验收。功能受执行端、模型目录和实际服务能力约束，文件保存成功与现有聊天实际采用分别核对。

## 开发与文档

Windows 主界面使用 **C# / .NET 8 / WinUI 3**，保留 WPF 兼容源码；手机服务使用 Python 3.12；安卓使用 Java 17 / Android SDK 35。Web / 手机工作台基于固定的 Codex Mobile Bridge 快照，通过显式覆盖选择性移植，保留其 MIT 许可和来源记录。

| 目录 | 内容 |
| --- | --- |
| `src/CodexConfig.WinUI` | Windows 默认界面与窗口交互。 |
| `src/CodexConfig.Application`、`src/CodexConfig.Core` | 两套界面共享的配置、应用、增强与网关服务。 |
| `src/CodexConfig.App` | WPF 兼容界面。 |
| `src/CodexMobile` | 固定上游、手机后台、本机管理页和 Dock Web 适配。 |
| `src/CodexAndroid` | Lovest Code 原生壳、图标资源与页面适配。 |
| `tests`、`scripts` | 隔离回归、构建、安装验证及按平台发布。 |

[开发与构建](docs/development.md) · [发布约定](docs/github-releases.md) · [Windows 更新说明](docs/release-0.19.7.md) · [Android 更新说明](docs/release-android-0.2.5.md) · [上游来源](src/CodexMobile/UPSTREAM.md)

[MIT 许可证](LICENSE) · [第三方许可](THIRD-PARTY-NOTICES.md)
