# Codex Dock

Windows 原生 Codex 本地连接与配置中心，提供配置作用说明、可选社区模板与 PAT / API Key 连接助手。当前版本 **0.10.0**。应用及本地网关进程名均为 `CodexDock.exe`。

[下载最新版本](https://github.com/Lovest20018/Codex-Dock/releases/latest) · [更新与发布说明](docs/github-releases.md)

支持 **官方账号 + API**：沿用 Codex 原生官方登录，模型请求由本机转发使用独立 Krill PAT / API Key。模型凭据加密保存，并纳入备份与恢复。Fast 参数和自动审核模型原样转发，后端实际能力需分别确认。使用方法、验证范围及后台连接说明见 [官方账号 + Krill 模型连接](docs/official-login-krill.md)。PAT / API Key 模式无需登录 ChatGPT。

## 使用

运行 `CodexDock-0.10.0-Setup-win-x64.exe` 安装，或解压便携包后运行 `CodexDock.exe`。支持 Windows 10 1809 及以上 x64，内置 .NET 运行时。

界面采用状态总览结构，详见 [Codex Dock 改版交付](docs/codex-dock-delivery.md)。主要入口：

- **模型与行为 / 全部配置**：常用 / 全部 / 已修改切换，可搜索中文名称和字段。每项显示一句用途，点击“了解更多”查看怎么选；“更多选项”中可显示高级项或添加设置。
- **连接与账号**：选择服务商和线路，填写地址、PAT / API Key 与模型。只改普通设置时无需填写凭据。
- **连接名称与备注**：支持可选名称和多行备注，保存后在概览显示，信息随当前配置目录加密备份；只改这些信息不需要重新填写凭据。PAT / API Key 模式无需登录 ChatGPT。
- **本地网关**：显示本机模型转发地址、状态及请求计数，提供启停和自动读取本机 HTTP 代理的开关。关闭窗口收起到系统托盘，托盘退出会停止网关；普通最小化保留任务栏入口。
- **配置模板**：新增、编辑、复制、删除模板，也可保存当前设置。内置模板可编辑、移除或恢复。打开模板先看当前值与模板值，勾选后应用。
- **备份恢复**：每次保存前自动加密备份，可还原上次保存前的状态，仅当前 Windows 用户可读取。
- **应用设置与帮助**：可开启登录 Windows 后自动启动，立即生效。自动启动使用开启时的程序位置和配置目录，并收起到托盘。
- **软件更新**：在应用页检查 GitHub 正式版，下载并校验安装器后可安装更新。检查只发送版本请求，不上传配置或密钥。

修改设置后点击“保存配置”，再重新打开 Codex。点击“查看更改”可先检查已隐藏密钥的差异。`Ctrl+S` 保存、`Ctrl+F` 搜索、`Esc` 关闭面板。

模板单独保存在 `%LOCALAPPDATA%\CodexConfig\templates.json`，与所选 Codex 配置目录无关。保存模板不会修改 Codex；应用模板后仍需“保存配置”。模板不包含模型连接和认证字段，会跳过可识别的密钥字段（包括表格和数组中的字段）。模板数据不加密，请勿把密钥直接填进普通文本字段。

已有修改在模板中默认不勾选，相同值自动跳过；内置模板的旧实验项和隐藏警告选项默认不勾选。可“撤销模板应用”，保留其他设置和后续编辑。旧实验设置明确标出兼容性未确认，来源放在“字段与来源”中。

默认配置目录为 `CODEX_HOME`，未设置时为 `%USERPROFILE%\.codex`，可从“配置位置”选择其他目录。更换目录仅改变本工具操作的目标，不修改 `CODEX_HOME`。

## PAT 和 API Key

内置三条 Krill 线路，也支持自定义服务：

| 线路 | 地址 |
| --- | --- |
| 三网优化 CDN | `https://api.kbsfdxc.com` |
| 海外线路 | `https://api.kbsfdxc.net` |
| 国内极速 | `https://api-slb.kbsfdxc.net` |

PAT 中转使用模型入口 `<origin>/backend-api/codex`，`chatgpt_base_url` 为 `<origin>/backend-api/`，当前用户环境变量 `CODEX_AUTHAPI_BASE_URL` 为 `<origin>`。认证文件写入 `personal_access_token` 与 `OPENAI_API_KEY`。

普通 Responses API 填写完整基址，例如 `https://example.com/v1`，认证文件写入 `auth_mode=apikey` 与 `OPENAI_API_KEY`。自动认证按所选服务类型、地址和协议决定；可明确选择 PAT 或 API Key。保存和获取模型均不验证 PAT 身份或账户能力。

仅修改普通配置项或默认模型时，保留原有认证文件、服务商与用户环境变量，不要求填写 Key。修改线路、地址、Key 或认证方式时才写入连接配置。已有官方登录数据可随普通配置编辑继续保留。

## 文件保护

- 修改目标为 `config.toml`，连接助手按需同时写入 `auth.json` 和上述用户环境变量。
- 保留未编辑字段、注释、UTF-8 BOM 和原换行方式；未设置的偏好不会被补成默认值。
- 保存前检查外部修改，使用写锁、临时文件替换、加密备份和失败回滚。
- 预览对疑似密钥字段脱敏；实际认证文件仍包含服务要求的凭据。
- 原有备份格式继续可读；旧 PAT 测试基线仅为未完成事务的兼容恢复保留，不再提供创建或专用撤回流程。

目录说明、类型和默认值来自本地字段表，不代表 Codex 的最终生效值。活动 profile、项目配置和托管策略可能覆盖本文件。复杂表数组当前保留原文，未提供完整结构编辑；嵌套结构只作有限类型检查。

## 本地字段表

字段维护入口是 [`config/config-schema.json`](config/config-schema.json)。条目对应 `config.toml`，包含历史中文说明、分类、类型、枚举和必要的取值约束。人工核对的作用说明独立维护于 [`config/config-help.json`](config/config-help.json)，避免更新字段表时覆盖文案。两份数据均在构建时嵌入程序。

后续按 Codex 的配置变化维护字段与帮助来源；没有上游仓库同步、Codex 安装扫描或多版本目录。新增字段在目录尚未更新时也能通过 TOML 值编辑。维护方式见 [`docs/schema-maintenance.md`](docs/schema-maintenance.md)，本版变化见 [`docs/release-0.10.0.md`](docs/release-0.10.0.md)。

## 构建和检查

```powershell
dotnet build src/CodexConfig.App/CodexConfig.App.csproj -c Release
dotnet run --project tests/CodexConfig.Tests/CodexConfig.Tests.csproj -c Release
dotnet run --project src/CodexConfig.App/CodexConfig.App.csproj -c Release -- --smoke-test .ui-test-v0.10
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-package.ps1
```

需要 .NET 8 SDK；生成安装器另需 Inno Setup 6。发布脚本会对实际发布的 EXE 执行隔离界面检查，再生成安装包、便携包、源码包及校验和。测试使用示例 Key、独立配置目录和内存环境变量。

活动源码仅保留 `src/`、`config/`、`tests/`、`scripts/`、`docs/`、`assets/` 和 `third_party/`。构建缓存、检查产物与发布包均不进入源码包。

## 许可

项目使用 C#/.NET 8 WPF 和 Tomlyn。最初导入的字段说明、中文翻译与分组包含 Superba 的 MIT 授权内容，后续由本项目独立维护，许可和历史来源保留在 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)。
