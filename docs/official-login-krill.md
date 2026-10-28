# 官方账号 + Krill 模型连接

本功能在 `feature/official-login-krill` 分支实现。`main` 的 `7e050cd` 保存了原有 0.8.0 源码基线。

## 使用

1. 在“服务连接”中选择“官方账号 + API”。
2. 如目标目录已有完整的官方登录凭据，可继续使用；否则点击“使用 ChatGPT 登录”。程序从 PATH 查找原生 `codex.exe`，也可手动选择。npm 的 `.cmd` / `.ps1` 包装器不能直接选择。
3. Codex 在系统浏览器完成登录。新凭据先留在草稿，点击“保存配置”才写入目标配置目录；取消登录或撤销修改不会覆盖原有认证。
4. 选择 Krill 线路或填写模型入口，填写入口接受的 PAT / API Key 与模型 ID，保存。
5. 在“常用设置”中按需配置 `features.fast_mode`、`service_tier`、`approval_policy`、`approvals_reviewer`。连接模式不会自动开启这些能力。
6. 完全退出并重新打开 Codex，以加载保存后的配置。重启电脑后先打开本工具，启动本地连接；关闭编辑器窗口后连接在后台继续运行。“停止本地连接”可结束后台进程，更新或移动本工具前应先停止连接。

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

可选的原生集成检查使用已安装的 Codex 0.164.0、虚构 OAuth 凭据、本机模拟官方账户服务与模型服务；外部代理指向未监听的本机端口，测试不使用真实账号和额度。检查 `account/read` 的 ChatGPT 身份及完整推理轮次，确认实际出站模型凭据和 Fast 参数。

```powershell
$env:CODEX_CONFIG_TEST_CLI = 'C:\path\to\codex.exe'
dotnet run --project tests/CodexConfig.Tests/CodexConfig.Tests.csproj -c Release
Remove-Item Env:CODEX_CONFIG_TEST_CLI

dotnet run --project src/CodexConfig.App/CodexConfig.App.csproj -c Release -- --smoke-test .ui-test-mixed-final
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/verify-mixed-bridge.ps1
```

尚未使用真实账号和 Krill PAT 验证浏览器登录、插件连接、真实 Fast 调度、自动审核结果及长会话压缩。模型入口接受同一串 PAT 不代表完整继承 PAT 身份查询、额度展示和所有账号能力。官方账号的工作区发现需要官方网络连通；模型改走 Krill 不会取消此依赖。

## 参考

- [Codex 认证与自定义 provider](https://learn.chatgpt.com/docs/auth)
- [Codex Fast 配置](https://learn.chatgpt.com/docs/agent-configuration/speed)
- [配置参考与自动审核](https://learn.chatgpt.com/docs/config-file/config-reference)
- [CodexPlusPlus 配置分离实现，分析提交 14fec652](https://github.com/BigPizzaV3/CodexPlusPlus/blob/14fec65206a2eb22570c17e52a8598052d679272/crates/codex-plus-core/src/relay_config.rs)

本分支借鉴身份与模型传输分离的思路，代码在本项目独立实现；未复制 CodexPlusPlus 的 AGPL 实现或注入脚本。
