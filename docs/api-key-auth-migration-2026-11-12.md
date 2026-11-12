# API Key 认证与旧 PAT 迁移（2026-11-12）

本地开发构建参考 Superba `v0.1.1-alpha.16`，固定提交 `719d6c5937c775b1c6ccd407169154eca844443b`。上游因桌面端 26.1106 的 PAT 账号确认、排队消息、项目选择与归档问题改用 API Key；来源为[发布说明](https://github.com/krill-lab/superba/releases/tag/v0.1.1-alpha.16)、[认证类型](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.16/crates/superba-core/src/auth.rs)、[认证写入](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.16/crates/superba-config/src/authjson.rs)和[迁移实现](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.16/crates/superba-switch/src/pat_migration.rs)。本项目按现有 C# 配置事务实现，未直接导入上游 Rust 源码。

## 当前行为

- 直连仅使用 API Key；移除“自动 / PAT / API Key”认证选择器。服务类型仍区分 Codex 中转路径和普通 Responses API，已有加密网关中的数字不变。
- 直连写入完整的 `{"auth_mode":"apikey","OPENAI_API_KEY":"当前密钥"}`，不合并旧 `personal_access_token` 或官方 `tokens`。
- 确认保存直连时，移除 `chatgpt_base_url`，清空用户级 `CODEX_AUTHAPI_BASE_URL`，选择文件凭据。若所选文件限制为仅 ChatGPT 登录，一并移除该本地限制；组织策略、活动 profile 及其他配置层仍可能覆盖它，确认页逐项列出变化。
- 旧 PAT 可正常读取；已有 `auth_mode=apikey` 时优先使用 `OPENAI_API_KEY`，即使同时遗留 PAT。官方账号凭据和独立供应商的认证来源不被当作旧 PAT 自动迁移。
- 检测到旧 PAT 时，连接页显示“迁移为 API Key”。点击后进入现有差异确认，使用当前表单的密钥，保留外部修改检查、原子保存、加密备份和失败回滚；返回调整保留草稿，重新读取可取消迁移草稿。
- 读取、切页、保存普通设置或查看预览不会迁移。迁移成功并重新读取后不再提示；恢复备份可逐字节还原原认证、配置和环境变量。
- 官方账号加独立模型网关继续保留真实官方认证。API Key 直连无需官方登录；官方认证由原生 Codex 完成，Dock 只重新读取当前文件或显式暂存当前目录的加密备份，见[官方登录收敛](official-login-convergence-2026-11-12.md)。

## 与上游的边界

### Codex 桌面 Fast 入口

2026-11-12 只读核对本机 Codex `26.1106.6628.0` 的前端资源：服务档位访问条件允许 `chatgpt`、`personalAccessToken`、`amazonBedrock`，API Key 登录返回空的 `serviceTierAccess`，随后过滤 Fast 等非标准档位。因此从旧 PAT 迁移为 API Key 会使该版本桌面中的 Fast／闪电入口消失；`features.fast_mode=true` 不能绕过此认证条件。Superba alpha.16 的发布说明也确认：该桌面版本在 API Key 模式下不发送速度档位，所设 Fast / Ultrafast 只对终端 Codex 生效。问题同时涉及入口与发送流程，不能仅凭本地配置写入或模型目录读取验证。

[官方 Speed 文档](https://learn.chatgpt.com/docs/agent-configuration/speed)将桌面 Fast 模式与 ChatGPT 登录关联。需要原生入口时可使用官方账号加独立模型入口，但模型、账号和上游服务仍须支持对应档位。图标显示、请求实际携带的档位和服务端执行结果应分别核对，不能将设置项开启或图标出现当作真实加速已生效。

后续按用户指定核对 CodexPlusPlus `2.6.6` 的混合模式：官方身份与模型传输分离可继续使用，不必等待 Codex 更新；其纯 API 原生 Fast 则依赖另一套运行时模块补丁。Dock 当前混合路由的原生内核隔离验证已通过；模型目录、桌面入口和真实服务验收仍须分别完成。见 [CodexPlusPlus 混合登录与 Fast 评估](codexplusplus-mixed-review-2026-11-12.md)。

后续按用户要求实现了默认关闭的 [纯 API 速度兼容](desktop-speed-compatibility.md)，在应用设置显式开启并重启后，处理上述四处原生门控。当前本地实现、支持范围和验证限度以该文档为准。旧 PAT 的读取与迁移仍保持显式确认，未修改原生 Codex 安装文件，也未自动切回旧 PAT 或更改用户真实认证。

Superba 只对自己管理且未发生外部认证变化的配置进行一次自动迁移，并在线检查密钥。本项目保留既有“写入前确认”约定，使用显式迁移入口；保存本地配置不强制联网，不把文件写入成功当作密钥已被服务接受。服务不接受旧密钥作为 API Key 时，需要用户提供服务支持的密钥。

迁移或切换后应完全退出 Codex（含托盘）并重新打开，再新建聊天核对。没有代替用户修改真实认证、账号安全设置或运行模型任务。

## 验证

Release 构建零警告、零错误。完整核心回归 **132/132 通过**，包含新增的 7 项认证迁移检查和使用本机 `codex.exe` 的 `account/read` 兼容检查：隔离目录迁移后返回 `apiKey`，未运行真实模型任务。迁移界面专项 **11 项通过**，覆盖差异确认、取消、写入、恢复、冲突和最小窗口缩放。

证据保存在 `artifacts/api-key-2026-11-12/core-final.log`、`core-final/` 和 `ui-migration/`。交付程序的全量 WPF、升级安装、逐文件核对和卸载结果以该本地包目录中的 `installer-verification.json` 为准；源码回归不代替真实服务端密钥或公网链路验收。
