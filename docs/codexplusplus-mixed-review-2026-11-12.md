# CodexPlusPlus 混合登录与 Fast 实现评估（2026-11-12）

后续实施：已按用户要求参考四处门控机制，在 C# 中独立实现默认关闭的[纯 API 速度兼容](desktop-speed-compatibility.md)。下文保留本次研究当时的结论；当前代码行为和验证结果见实施文档。

**官方 ChatGPT 身份与中转模型请求可以分开，不需要恢复 PAT，也不必等待 Codex 更新后才使用混合模式。** CodexPlusPlus 保留这条路径；Dock 当前本地代码已经实现同一核心机制。本轮核对源码并运行既有隔离测试，未移植注入代码、修改真实认证或发布新版本。

## 核对版本

- CodexPlusPlus：`2.6.6`，固定提交 [`fb3926fac4f9738941c57703a1fb47b7b0072cee`](https://github.com/BigPizzaV3/CodexPlusPlus/commit/fb3926fac4f9738941c57703a1fb47b7b0072cee)。没有将不同时间的 `main` 内容混作同一快照。
- 本机桌面：此前只读核对的 Codex `26.1106.6628.0`；本轮内核验证使用其运行中的 `0.167.0-alpha.4.1` 配套 `codex.exe`。
- 公开源码快照、摘要和测试日志位于本地忽略目录 `artifacts/codexplusplus-audit-2026-11-12/`。未执行或导入上游程序。

## 1. 混合模式如何分离账号和模型

CodexPlusPlus 的 [`relay_config.rs`](https://github.com/BigPizzaV3/CodexPlusPlus/blob/fb3926fac4f9738941c57703a1fb47b7b0072cee/crates/codex-plus-core/src/relay_config.rs#L6417) 将账号身份与模型传输分别配置：

| 部分 | 上游实现 | Dock 当前实现 |
| --- | --- | --- |
| 官方账号 | 保留 `auth.json` 的 ChatGPT OAuth 登录；官方混合模式移除其中的模型 `OPENAI_API_KEY` | `OfficialCredentials.ForMixed` 保留官方 tokens，移除 API Key / PAT；保存优先采用当前文件中的登录 |
| 模型凭据 | provider 使用独立的 `experimental_bearer_token`；`requires_openai_auth=true` 保留官方登录语义 | 中转 Key 独立存于 DPAPI 加密连接文件；兼容 provider 只拿到本机 token |
| 模型传输 | provider 的地址可直指 Responses API，也可经过本地协议代理；OpenAI 会话身份使用 `openai_base_url` 指向本机代理 | `model_provider=openai`，`openai_base_url` 指向本机模型网关，网关替换为中转 API Key 后转发 |
| 账号和插件服务 | 保持官方身份及对应服务通道 | 清理旧 PAT 的账号地址覆盖；模型网关只接受模型目录、Responses、压缩和 WebSocket 路径 |

这条路径保持客户端的 `chatgpt` 认证身份，模型请求使用另一份凭据。它不要求把中转密钥伪装成 PAT，也不需要将官方 OAuth token 交给中转。Dock 的 `ModelBridge` 只转发白名单请求头，重新设置上游 Bearer，过滤 `chatgpt-account-id`、Cookie 与其他官方身份头。

上游的“直连”是界面中的连接分类；使用 OpenAI 会话身份或协议兼容功能时，仍可能依赖本地代理。不能据此推断所有“官方登录 + API”配置都可以关闭本机服务。

## 2. 它如何完成 ChatGPT 登录

两种登录入口承担不同任务：

- **直连混合模式**：[`relay_status`](https://github.com/BigPizzaV3/CodexPlusPlus/blob/fb3926fac4f9738941c57703a1fb47b7b0072cee/apps/codex-plus-manager/src-tauri/src/commands.rs#L8981) 读取当前登录，没有凭据时明确提示先在 Codex / ChatGPT 正常登录；`chatgpt_auth_status_from_home` 读取该目录的 `auth.json`。这与 Dock 当前“原生 Codex 登录 → 重新读取官方登录”的方向一致。
- **网关供应商池的 ChatGPT 订阅登录**：[`login_gateway_chatgpt`](https://github.com/BigPizzaV3/CodexPlusPlus/blob/fb3926fac4f9738941c57703a1fb47b7b0072cee/apps/codex-plus-manager/src-tauri/src/gateway_commands.rs#L2723) 另走浏览器登录，将凭据保存为池内的官方推理供应商。源码明确说明它与 Codex 自己的登录独立。它用于让网关消费该账号的官方推理服务，不能把这个入口直接当成“模型始终走中转”的混合配置。

因此，本轮没有理由恢复已按用户要求移除的设备码、CLI 登录或失败后自动回退流程。正常官方登录后保留身份，再配置中转模型通道即可。登录、刷新和账号服务仍需要正常连接官方服务。

## 3. 混合模式的 Fast 与纯 API Fast 是两套机制

本机 26.1106 的桌面速度档位门控允许 `chatgpt` 身份；混合模式保留这个身份，因而避开纯 API Key 的认证门控。模型仍须声明对应档位，功能配置及账号条件也须允许。不能把 Superba 在纯 API Key 模式下的已知限制推广为所有混合连接都不可用。

CodexPlusPlus 另外提供默认关闭的纯 API 原生 Fast 增强：

- [`native_fast.rs`](https://github.com/BigPizzaV3/CodexPlusPlus/blob/fb3926fac4f9738941c57703a1fb47b7b0072cee/crates/codex-plus-core/src/native_fast.rs) 只向页面投影开关和本机 API 配置是否成立的布尔值。
- [`native-fast-unlock.js`](https://github.com/BigPizzaV3/CodexPlusPlus/blob/fb3926fac4f9738941c57703a1fb47b7b0072cee/assets/inject/native-fast-unlock.js) 与 [`native-fast-rspack.js`](https://github.com/BigPizzaV3/CodexPlusPlus/blob/fb3926fac4f9738941c57703a1fb47b7b0072cee/assets/inject/native-fast-rspack.js) 同时改写四处认证门控：界面档位访问、请求档位访问、队列默认档位及队列显式档位。只让图标出现并不够。
- [`bridge/native_fast.rs`](https://github.com/BigPizzaV3/CodexPlusPlus/blob/fb3926fac4f9738941c57703a1fb47b7b0072cee/crates/codex-plus-core/src/bridge/native_fast.rs#L97) 通过 CDP 在运行时处理模块；旧页面使用资源响应拦截，`app://` 页面使用文档模块替换。它不修改安装包，但需要跟随前端结构变化维护。
- 这些补丁不改写服务要求或模型的 `serviceTiers`，也不保证供应商实际提供加速或接受相应计费档位。

Dock 当前没有这套运行时增强。混合连接可以继续使用原生 `chatgpt` 身份通道；纯 API Fast 增强应作为独立功能评估，不能用一个 `features.fast_mode=true` 代替。

## 4. Dock 当前还需区分的模型目录条件

`NativeModelCatalog.Convert` 能保留完整 Codex 原生模型条目的能力；普通兼容接口只返回模型 ID 时，它会生成空的 `service_tiers`。缺少档位声明时，即使账号身份已正确分离，也不能保证原生 Fast 入口出现。当前转换器也不会凭模型名称自动授予 Fast 能力。

CodexPlusPlus 有独立的模型资料和托管目录流程，能够在已有目录及用户明确配置的模型元数据上生成客户端使用的目录。适合后续借鉴的是**模型能力的读取、来源、配置和应用流程**，而非给所有中转模型默认补 Fast 声明。

后续验收需要分别核对：

1. 原生账号仍为 ChatGPT，且当前文件中的刷新凭据没有被旧备份覆盖。
2. 模型目录包含所选模型及服务实际支持的档位。
3. 桌面选择器出现入口，普通发送与排队发送都保留所选档位。
4. 中转收到自己的 Key 及正确的 `service_tier`；服务端是否实际采用另行核对。

## 5. 本轮隔离验证

复用了既有测试，没有新增只复述实现的测试。选取 `132` 项中的 `4` 项，最终全部通过，日志为 `artifacts/codexplusplus-audit-2026-11-12/native-tests.log`：

- 混合配置保留官方身份、隔离模型 Key，并可还原原配置偏好。
- 网关转发新旧模型路径，替换上游鉴权并保留会话信息，不转发官方账号头。
- 使用本机原生 app-server，内置 `openai` 路由下 `account/read` 返回 `chatgpt`，模型目录及模拟推理完成，捕获 `priority` 档位。
- 同一套检查覆盖旧兼容 provider 路由。

测试使用虚构 OAuth 凭据、本机账号接口和模拟上游；外部请求被测试进程的代理设置阻断。初次沙箱运行因未加载当前用户 DPAPI 而失败，使用正常用户配置运行同一组隔离检查后通过。验证确认了原生内核与网关链路，没有操作真实账号、消耗模型额度，也没有验证真实桌面图标、真实账号资格或供应商实际加速。

## 建议

2026-11-12 后续补充了[相对旧 PAT 的能力复核](codexplusplus-capabilities-vs-pat-2026-11-12.md)：真实原生内核配合模拟上游，覆盖专用审核模型、主模型回退和模型不可用三种 auto-review 路径，并观察到审批事件及原生命令执行。该补充同时区分 Codex++ 已有审批兼容与 Dock 尚未实现的设置、路由和统计能力。

继续保留“API Key 直连”和“官方账号 + 独立模型入口”两个清楚的选择。有官方账号且需要原生身份能力时，使用混合连接；不恢复 PAT。当前重点应是把原生登录后的读取、模型目录能力和网关启动流程验证完整；纯 API 原生 Fast 注入单独评估。本轮继续暂停提交、推送与发版。
