# 官方账号 + Krill 模型连接

> 历史记录：本页描述 0.11.0 以前的功能。0.11.0 已移除账号登录与本地网关，当前使用方式见 [README](../README.md)。

本功能在 `feature/official-login-krill` 分支实现。`main` 的 `7e050cd` 保存了原有 0.8.0 源码基线。

## 使用

1. 在“服务连接”中选择“官方账号 + API”。
2. 如目标目录已有完整的官方登录凭据，可继续使用；否则点击“使用 ChatGPT 登录”。程序从 PATH 查找原生 `codex.exe`，也可手动选择。npm 的 `.cmd` / `.ps1` 包装器不能直接选择。
3. Codex 在系统浏览器完成登录。新凭据先留在草稿，点击“保存配置”才写入目标配置目录；取消登录或撤销修改不会覆盖原有认证。
4. 选择 Krill 线路或填写模型入口，填写入口接受的 PAT / API Key 与模型 ID，保存。
5. 在“常用设置”中按需配置 `features.fast_mode`、`service_tier`、`approval_policy`、`approvals_reviewer`。连接模式不会自动开启这些能力。
6. 完全退出并重新打开 Codex，以加载保存后的配置。打开本工具时，按已保存的配置启动本地网关；“本地网关”页显示监听地址、模型服务地址、运行状态和本次请求计数，每 3 秒刷新。可复制地址、启动或停止网关。
7. 关闭窗口会收起到系统托盘并保留草稿，网关继续运行；双击托盘图标恢复窗口，普通最小化保留任务栏入口。托盘右键选择“退出（停止网关）”，或在网关页点击“退出并停止网关”，会在处理未保存修改后停止本程序管理的网关并退出。更新或移动工具前，请从托盘退出。

## 本机 HTTP 代理

在“本地网关”页勾选“使用本机 HTTP 代理访问模型服务”，然后点击“保存配置”。界面自动读取并显示 Windows 当前用户启用的固定系统代理；没有可用固定代理时再读取 `HTTPS_PROXY` / `HTTP_PROXY` 环境变量。支持单一 `host:port` 和 Windows 的 `http=…;https=…` 写法，不解析 PAC，不支持含账号密码的代理 URL。

开启后，模型列表、Responses、压缩和 WebSocket 上游连接经过该 HTTP 代理，可配合代理软件的海外节点使用。代理软件需先启用系统代理或提供对应环境变量；仅有代理软件进程在运行不代表系统代理已开启。Windows 固定代理地址变化后，网关会在新连接时重新读取；环境变量继承自启动工具时的进程环境，修改后需退出并重新打开工具。

取消勾选并保存后，网关直连模型入口。开启但未检测到可用代理时，请求会失败，不静默回退直连。开关纳入加密连接文件和备份，代理地址不写入 Codex 的 TOML，也不修改 Windows 代理设置。Codex 到本地网关及网关健康检查始终直连回环地址；官方账号服务保持客户端原有的网络方式。

新版首次发现旧版网关时，会停止并替换为支持代理的新版网关，地址和本地认证信息保持不变，期间连接会短暂中断。新版启动的后台进程也会检测所属编辑器进程是否仍存活，以便编辑器异常退出后结束自身。

活动 profile、自定义内置 `openai` provider、强制 API 登录等可能覆盖路由，保存混合连接时会阻止这些冲突并显示原因。项目配置、启动参数、托管策略仍可能进一步限制功能，不能仅凭本文件判断所有会话的最终配置。

## 认证与转发

- 官方登录：调用 Codex 原生 `login`，使用独立临时 `CODEX_HOME` 和文件凭据存储。只清除登录子进程中的模型凭据及地址环境变量。临时凭据在读取后清理，OAuth 的交换和刷新均交给 Codex。
- `auth.json`：保留官方 `tokens`；混合保存去除 `personal_access_token` 与 `OPENAI_API_KEY`，避免残留 PAT / API 登录覆盖官方身份。官方令牌按 Codex 的文件存储格式保存，不宣称加密。
- `codex-config-connection.dat`：Windows 当前用户 DPAPI 加密，包含模型上游地址、凭据、本地端口和本地访问令牌。迁移到其他 Windows 用户或电脑时需要重新连接。
- `config.toml`：使用专用 provider `codex_config_mixed`，`requires_openai_auth=true` 保留官方账号身份，`experimental_bearer_token` 仅保存本地访问令牌。provider 的 `base_url` 与根级 `openai_base_url` 均指向本机转发。模型凭据不写入 TOML。
- 官方账号相关服务继续使用官方地址。保存时移除 `chatgpt_base_url` 覆盖与用户级 `CODEX_AUTHAPI_BASE_URL`，明确选择 `cli_auth_credentials_store="file"`。
- 本地服务仅绑定 `127.0.0.1`，不读取外部 Kestrel / ASP.NET 监听配置；拒绝未知认证和带浏览器 Origin 的请求。只接受模型列表、Responses、压缩请求及自己的健康检查和停止操作。
- HTTP/SSE 和 Responses WebSocket 转发使用独立模型凭据；认证头重新构建，不转发官方 OAuth、Cookie、账号 ID 和官方项目头。禁止 HTTP 重定向，不回退到官方推理。上游 401/403 转为连接错误，避免触发 Codex 的官方账号刷新。
- Fast 参数和 `codex-auto-review` 模型名原样保留，没有默认模型映射或强制解锁补丁。健康检查显示请求计数，不能作为后端加速或审核能力已验证的证据。

加密连接文件与 `config.toml`、`auth.json`、环境变量共同参与写锁、外部修改检查、加密备份、崩溃恢复和失败回滚。切回 PAT / API Key 时移除混合连接文件；恢复备份可返回完整混合配置。旧备份继续可读。

## 验证与边界

核心测试覆盖凭据分离、脱敏、备份恢复、外部修改、失败回滚、实际 HTTP 转发、SSE、Fast 参数、审核模型名、非法路径和认证隔离。

网关界面与代理改动另覆盖固定代理解析、HTTP 转发与 HTTPS CONNECT、代理开关保存/恢复、关闭收起到托盘、保留草稿、任务栏最小化、托盘退出释放端口，以及启动与退出并发。已对最终自包含程序验证旧网关替换和编辑器进程结束后子网关自动退出。

可选的原生集成检查使用已安装的 Codex 0.164.0、虚构 OAuth 凭据、本机模拟官方账户服务与模型服务；外部代理指向未监听的本机端口，测试不使用真实账号和额度。检查 `account/read` 的 ChatGPT 身份及完整推理轮次，确认实际出站模型凭据和 Fast 参数。

```powershell
$env:CODEX_CONFIG_TEST_CLI = 'C:\path\to\codex.exe'
dotnet run --project tests/CodexConfig.Tests/CodexConfig.Tests.csproj -c Release
Remove-Item Env:CODEX_CONFIG_TEST_CLI

dotnet run --project src/CodexConfig.App/CodexConfig.App.csproj -c Release -- --smoke-test .ui-test-mixed-final
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/verify-mixed-bridge.ps1
```

2026-10-28 在用户完成官方登录后，对本机真实混合连接进行了以下验证。结果只代表当时配置的线路和账号：

| 检查 | 结果与边界 |
| --- | --- |
| 原生 Codex 官方身份与推理 | `account/read` 返回 `chatgpt`、`requiresOpenaiAuth=true`；使用 `codex_config_mixed` 完成临时只读会话，返回预期短回复。 |
| 模型列表、HTTP/SSE | 模型列表返回 8 个条目；`gpt-6.1-sol` 完成 SSE 回复。 |
| Fast | 用户手动开启按钮后，配置为 `service_tier="priority"`；真实请求成功，响应明确返回 `service_tier="priority"`。另测字面值 `fast` 时响应为 `default`，不能把它作为 Fast 生效证据。短请求耗时不构成性能对比。 |
| 自动审核模型 | 列表包含 `codex-auto-review`，但实际调用返回 HTTP 400：`model codex-auto-review is unavailable`。尚未验证真实自动审核判定，不应宣称该后端支持自动审核。 |
| 压缩端点 | 极短示例请求调用 `/responses/compact` 返回 HTTP 200，输出包含 `compaction`；尚未验证长会话自动压缩。 |
| 本机 HTTP 代理 | 新版自动检测到 `http://127.0.0.1:7897`，在独立端口运行真实模型连接并启用代理，短请求返回 HTTP 200、预期回复及 `service_tier="priority"`；测试未改变当前全局连接，也未验证其他海外线路。 |
| 账号与插件工具 | 官方额度查询、项目列表及联网搜索调用成功；浏览器插件打开页面超时。尚未验证新插件的 OAuth 连接流程及所有插件。 |

浏览器登录过程由用户完成，未进行自动化验证。测试期间未输出认证令牌，脱敏结果保存在本地 `artifacts/live-verification-20261028/`（不进入源码包）。

模型入口接受同一串 PAT 不代表完整继承 PAT 身份查询、额度展示和所有账号能力。官方账号的工作区发现需要官方网络连通；模型改走 Krill 不会取消此依赖。

## 参考

- [Codex 认证与自定义 provider](https://learn.chatgpt.com/docs/auth)
- [Codex Fast 配置](https://learn.chatgpt.com/docs/agent-configuration/speed)
- [配置参考与自动审核](https://learn.chatgpt.com/docs/config-file/config-reference)
- [CodexPlusPlus 配置分离实现，分析提交 14fec652](https://github.com/BigPizzaV3/CodexPlusPlus/blob/14fec65206a2eb22570c17e52a8598052d679272/crates/codex-plus-core/src/relay_config.rs)

本分支借鉴身份与模型传输分离的思路，代码在本项目独立实现；未复制 CodexPlusPlus 的 AGPL 实现或注入脚本。
