# Codex++ 混合模式相对旧 PAT 的能力复核（2026-11-12）

**保留官方 ChatGPT 身份并将模型请求送往独立中转，不会必然失去 auto-review。** 实测当前原生内核能够在混合链路中完成自动审批，随后执行固定的无害命令。能力是否可用还取决于客户端版本、模型目录、审批模型、服务协议与账号下发的功能配置；不能仅凭登录成功或模型列表成功宣布所有能力通过。

本轮继续暂停提交、推送与发版。没有修改真实认证、当前服务或 Codex 安装文件，没有调用真实模型，也没有安装、运行 Codex++ 程序。Codex++ 部分是固定源码审查；动态验证使用原生 Codex 与 Dock 当前网关，验证双方共用的“官方身份与独立模型传输”机制，不等于 Codex++ 成品验收。

## 1. 比较对象

- 旧 PAT：此前由 Superba / Dock 将中转凭据写入 `auth.json.personal_access_token` 的兼容路径。它不是当前有效 ChatGPT OAuth 登录的同义词。上游已因桌面账号确认及相关操作问题迁移到 API Key，见 [Superba alpha.16](https://github.com/krill-lab/superba/releases/tag/v0.1.1-alpha.16)。
- 此次 Codex++ 路径：保留有效 ChatGPT OAuth 身份，独立设置模型地址与模型 Bearer。固定版本为 `2.6.6`、提交 `fb3926fac4f9738941c57703a1fb47b7b0072cee`，详见[上一轮评估](codexplusplus-mixed-review-2026-11-12.md)。
- 原生动态验证：本机 `codex-cli 0.167.0-alpha.4.1`，SHA-256 `83F5C2DD2AC2902E0D96BA5D8CCDD27D513F57DF4A9AB73E06A5A518028E9A2F`。公开 `rust-v0.167.0` 源码只作实现对照，不当成安装中 alpha 构建的完全相同源码。

纯 API Key 登录与这里的混合模式应分别判断。此前确认的“26.1106 纯 API Key 桌面不显示、也不发送 Fast 档位”，不直接适用于保持 `chatgpt` 身份的混合连接。

## 2. Auto-review：入口、模型选择和有效输出分别检查

这里的 auto-review 是“替我审批 / Approve for me”，负责需要权限审批的操作；它不同于代码审查 `/review`。官方 OpenAI documentation 说明应选择 `approvals_reviewer="auto_review"` 并保留能产生审批请求的交互策略，例如 `on-request`。`never` 不会触发审批，不能用“执行成功”证明审核成功。见[官方说明](https://learn.chatgpt.com/docs/sandboxing/auto-review)。

### Codex++ 已实现的兼容处理

1. **供应商审批模型映射**：`auto_review_model` 非空时，将请求中的 `codex-auto-review` 改写为用户为该供应商选择的模型；留空原样转发。见固定版本 [settings.rs](https://github.com/BigPizzaV3/CodexPlusPlus/blob/fb3926fac4f9738941c57703a1fb47b7b0072cee/crates/codex-plus-core/src/settings.rs#L294) 与 [protocol_proxy.rs](https://github.com/BigPizzaV3/CodexPlusPlus/blob/fb3926fac4f9738941c57703a1fb47b7b0072cee/crates/codex-plus-core/src/protocol_proxy.rs#L6041)。
2. **审批专用供应商**：聚合可以设置 `auto_review_relay_id`，优先交给指定成员；该成员不可用会报错，不静默切到其他成员。源码注释特别指出默认成员可能是官方登录，从而消耗官方推理额度。见 [aggregate_catalog.rs](https://github.com/BigPizzaV3/CodexPlusPlus/blob/fb3926fac4f9738941c57703a1fb47b7b0072cee/crates/codex-plus-core/src/aggregate_catalog.rs#L216) 与 [protocol_proxy.rs](https://github.com/BigPizzaV3/CodexPlusPlus/blob/fb3926fac4f9738941c57703a1fb47b7b0072cee/crates/codex-plus-core/src/protocol_proxy.rs#L3670)。
3. **远程开关无数据时补入口**：默认关闭的 `offlineAutoReview`，仅在 Statsig Ready 但完全没有真实远程数据时，补权限菜单的 `guardian_approval` 开关。已有真实数据时不覆盖。它只处理入口，不能提供模型能力或保证审批结果。见 [renderer-inject.js](https://github.com/BigPizzaV3/CodexPlusPlus/blob/fb3926fac4f9738941c57703a1fb47b7b0072cee/assets/inject/renderer-inject.js#L5361)。

以上机制说明 Codex++ 对自动审批有专门兼容，而非删除这项功能。但源码中“固定请求 `codex-auto-review`”的描述不能推广到所有新内核与目录组合。

### 当前原生内核三种实测

使用虚构 OAuth、模拟账号服务、模拟模型服务和独立 CODEX_HOME；由真实 app-server 经 Dock `ModelBridge` 发起请求。主模型输出固定 `exec_command`，参数为 `Write-Output 'guardian-probe-ok'`、`sandbox_permissions=require_escalated`；审批模拟服务只返回这条固定无害命令的评估 JSON。外部 HTTP 由进程代理指向关闭的回环端口阻断。

| 模型目录与上游行为 | 实际审批模型 | 原生审批与命令结果 |
| --- | --- | --- |
| 目录包含 `codex-auto-review`，模拟上游返回有效评估 JSON | `codex-auto-review` | `autoApprovalReview/completed=approved`，随后命令退出 0，输出 `guardian-probe-ok` |
| 目录只包含主模型，没有 `codex-auto-review` | 主模型 `gpt-6.1-sol` | 同样出现审批通过事件，随后命令退出 0，输出相同 |
| 目录包含 `codex-auto-review`，但模拟上游对审批请求返回 404 | 两次请求均为 `codex-auto-review` | 审批事件为 `denied`，原因为模型调用失败；命令 `declined`，没有执行 |

三项结果均通过独立结果断言：实际审批模型、审批事件、命令状态与输出，以及上游只收到模拟中转 Key、未收到官方账号头。第三项验证的是**服务不可用导致审核失败**，不是模型对命令风险作出实质拒绝。

请求同时携带 `x-openai-subagent=guardian` 头及相应 `client_metadata`；审批请求要求 JSON schema，至少返回 `outcome=allow|deny`。将其映射到只能聊天、不能稳定遵守评估格式的模型，不能保证审批成功。普通聊天可用不足以验收 auto-review。

模型目录缺少专用审核模型时回退主模型，与公开 [reviewer_config.rs](https://github.com/openai/codex/blob/rust-v0.167.0/codex-rs/core/src/guardian/reviewer_config.rs) 和 [model.rs](https://github.com/openai/codex/blob/rust-v0.167.0/codex-rs/ext/guardian-reviewer/src/model.rs) 的选择逻辑一致。这个回退不证明主模型具备官方专用审核模型相同的判定质量。不同版本应重新捕获实际模型名，不能只按固定别名检测审批流量。

原始结果保留在 `artifacts/codexplusplus-audit-2026-11-12/guardian-results-01/`，入口为 `verified-summary.json`，探针源码在相邻 `guardian-probe/`。模拟上游没有真实 WebSocket 服务；三项均记录了初始 WebSocket 重试错误，随后回退 HTTP 完成上述验证。这些记录不构成 WebSocket 成功验收，也没有隐藏重试过程。

## 3. 相对旧 PAT 的具体变化

| 能力 | 混合模式中的判断 | 条件及本轮证据边界 |
| --- | --- | --- |
| 自动审批 | 没有必然丢失；当前原生链路实测通过 | 菜单开关、审批模型选择、供应商有效输出都需要满足；映射到通用模型后的审核质量未评估 |
| Fast / Ultrafast | 保留 ChatGPT 身份可避开纯 API 的认证门控 | 还要模型目录声明服务档位、账号配置允许、上游接受并实际提供该档位；上一轮只验证 priority 经过网关 |
| 本地命令与函数工具 | 本轮首次补足原生命令执行证据 | 只覆盖固定无害命令与其自动审批；不能推广为任意 MCP、插件、浏览器工具全部通过 |
| 项目选择、排队、归档等账号相关界面 | 使用真实官方账号身份，不继续依赖旧 PAT 伪装账号 | 新桌面的旧 PAT 账号异常是上游迁移原因；本轮没有操作真实账号逐项回归 |
| 中转账号资料、额度、第三方工作区 | 账号服务走官方后，原来由 PAT 入口提供的数据不会自动成为官方账号数据 | 应区分官方账号信息与中转用量；中转用量如需显示，应使用它自己的已确认接口 |
| 压缩、WebSocket | 依赖中转协议能力，不由登录格式保证 | 此前真实入口 HTTP compact 成功、WS 曾返回 426；本轮没有复测真实入口，不能解释成换登录造成的新损失 |
| 搜索、图片、记忆、云端任务、远程连接器 | 没有足够证据逐项宣称保留或丢失 | 涉及模型能力、官方账号资格及专用服务；普通 Responses 回复成功不构成验收 |

最大的新增使用条件是：这条混合路径需要可用的官方账号及其登录、刷新、功能配置服务。以前只有中转 PAT、没有官方登录的用户，不能原样切到“保留官方身份”路径。Codex++ 另有纯 API 与独立增强，但需单独评估，不能将其配置与混合模式混为一谈。

## 4. 对 Dock 后续实现的影响

当前 Dock 已能透明转发此次原生审批请求，但没有 Codex++ 的审批模型映射、审批专用供应商和离线入口增强。不能把 Codex++ 已有的这些能力写成 Dock 已实现。

后续值得补的是：

- 显示所选模型目录实际声明的审批能力及来源，并提供明确的审批模型设置；不虚构不存在的 `codex-auto-review` 条目。
- 诊断按 guardian 元数据识别审核调用。当前 `ModelBridge.CountRequest` 仅统计 `model==codex-auto-review`，会漏计本轮已复现的主模型审核回退；这是统计缺口，不代表请求没有转发。
- 显示审核服务错误和是否发生模型映射；映射到通用模型时明确其来源，不声称具备官方专用模型同等质量。
- 如提供审批独立路由，应明确凭据与用量去向，不静默改用官方推理。

本轮只补研究文档和忽略目录中的隔离探针。后续按用户要求单独实施了[纯 API 的 Fast / Ultrafast 兼容](desktop-speed-compatibility.md)；其余尚未实施的功能仍以本节说明为准。

## 5. 追问补测：完全不登录官方账号，仅使用 API 中转

**可实现本地编程与自动审批，不必先有 ChatGPT OAuth 登录。** 官方文档也区分本地 API Key 工作流与需要 ChatGPT 登录的云端能力，见[认证说明](https://learn.chatgpt.com/docs/auth)。

2026-11-12 增补了纯 API 探针模式：独立 `auth.json` 只有 `auth_mode=apikey` 和虚构 API Key；自定义 Responses provider 使用本地中转地址、独立 Bearer、`requires_openai_auth=false`，显式加载本地模型目录。没有写入任何官方 OAuth tokens，也不调用前面的混合配置保存流程。沿用真实 app-server → 当前 ModelBridge → 模拟上游；用同一条固定无害命令触发审核。

| 模拟条件 | 实测 |
| --- | --- |
| 本地目录包含 `codex-auto-review`，上游正常 | 实际由主模型 `gpt-6.1-sol` 审核，通过后命令退出 0 |
| 本地目录只包含主模型，上游正常 | 同样由主模型审核，通过后命令退出 0 |
| 模拟上游拒绝所有 guardian 请求并返回 404 | 原生审核失败，命令被阻止 |

三项中 `account/read.account` 均为 `null`，本地账号探测端点的调用次数均为 0，只有模拟中转 Key 被发送给上游。API-only 自定义 provider 的模型选择与此前内置 openai 混合路由不同：即使目录列出了 `codex-auto-review`，本次仍使用主模型，不能根据目录里的名字推断实际请求模型。审核请求带 guardian 标识与评估 JSON schema；该路径允许审核器使用只读检查工具，本次模拟回复没有调用它们。

初次探针因错误假设自定义 provider 的 `account/read` 必须返回 `apiKey` 而提前结束；修正为明确禁止 ChatGPT 身份、允许无账号状态后，三项真实链路验证通过。失败记录保留在 `api-only-guardian-results-01/`；最终结果及独立断言摘要在 `api-only-guardian-results-02/verified-summary.json`。两者位于同一审计 artifacts 目录。

这证明当前原生内核具有无官方账号的 API 自动审批链路；没有启动真实桌面、运行 Codex++ 程序或验证真实中转模型的判定质量。桌面“替我审批”的入口仍需按版本核对；Fast 在此前检查的 26.1106 纯 API 桌面中受认证门控，Codex++ 的可选运行时增强处理该门控，但参数发送与上游实际加速仍应分别验收。官方账号、云端任务和依赖官方工作区 OAuth 的服务不会由普通模型 API 自动补齐。
