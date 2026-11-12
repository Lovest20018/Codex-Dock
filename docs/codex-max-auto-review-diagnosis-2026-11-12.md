# Max 与专用自动审核模型核对

2026-11-12，桌面版本 `26.1106.6628.0`，实际内核 `codex-cli 0.167.0-alpha.4.1`。用户最终确认后台已看到 Max，并要求自动审核固定调用 `codex-auto-review`，不得回退到 `gpt-6.1-sol`。本轮只应用审核模型设置。

## Max：已更正初次判断

初次捕获只检查了请求顶层 `reasoning.effort`，没有检查 `input` 中的 `configuration_update`，据此判断 Max 被降为 xhigh 不成立。原生代码支持保留当前上下文窗口最初的请求档位，以便复用缓存，并在输入中追加可信的档位更新。

补测使用同一份已安装 CLI、独立配置目录、虚构凭据和 loopback 模拟 Responses 服务：

| 操作 | 顶层 reasoning.effort | 输入中最后一条 configuration_update |
| --- | --- | --- |
| 新聊天第一轮选 Max | `max` | `max` |
| 同一聊天从 Max 切到 xhigh | 仍为 `max` | `xhigh` |
| 同一聊天再切回 Max | 仍为 `max` | `max` |

因此，中转只记录顶层字段可能显示旧档位，不能据此判断新的档位没有传递。用户随后确认真实后台已看到 Max；没有修改其推理参数、关闭 `supports_reasoning_effort_updates` 或替换 CLI。API 支持与 mid-turn 档位更新参见官方 [Reasoning models](https://developers.openai.com/api/docs/guides/reasoning) 与 [deployment checklist](https://developers.openai.com/api/docs/guides/deployment-checklist)。真实中转是否在所有路径正确处理历史更新项，仍以其实现为准。

## 自动审核：已固定专用模型

此前内置 `openai` provider 的 API Key 测试中，即使目录列出 `codex-auto-review`，审核器仍可能回退到主模型。这是可用性回退行为，未满足用户指定专用审核模型的要求。

当前原生模型元数据支持：

```json
{
  "slug": "gpt-6.1-sol",
  "auto_review_model_override": "codex-auto-review"
}
```

本次从本机现有模型缓存生成独立的原生目录快照，保留全部 8 个模型；对其中 7 个非审核模型设置上述覆盖值，审核模型自身的元数据不变。逐字段比较确认其他模型属性均保持原值，包括 Max、Ultra、Fast、工具能力与推理档位更新支持。没有虚构新的审核模型条目；现有源目录已包含 `codex-auto-review` 且声明支持 API。

本机 `config.toml` 已通过现有 ConfigStore 的冲突检测、加密备份和原子保存流程写入以下三项。原生目录路径为 `C:/Users/com/.codex/codex-dock-model-catalog-aa6b6662d8654d48.json`：

```toml
approval_policy = "on-request"
approvals_reviewer = "auto_review"
model_catalog_json = "C:/Users/com/.codex/codex-dock-model-catalog-aa6b6662d8654d48.json"
```

实际保存时保留原文件的排版与其余内容；上面的正斜杠用于展示路径。认证文件、连接入口和推理设置未改动。已创建恢复备份：

```text
C:\Users\com\.codex\codex-config-backups\20261112-082007-4462805-fe577e709c3f46f6871c300806ec1fa1.backup
```

写入当时，正在运行的客户端没有被中断或重启。随后需要完全退出并重新打开 Codex，使新的模型目录进入内核的内存目录与后续审核会话；仍使用“帮我批准”。后续实际重启与后台验收结果见文末。

## 已完成的验证

原生 app-server → loopback 模拟服务，两条审核路径均通过：

| 条件 | 实际审核请求 | 原生命令结果 |
| --- | --- | --- |
| 专用审核模型返回有效的允许结果 | `codex-auto-review`，effort 为 `low`，带 guardian 标识与评估 JSON schema | 自动审核 approved；固定无害命令退出 0 |
| 本地目录中没有专用模型，模拟该模型返回 404 | 仍请求 `codex-auto-review`；没有请求主模型来替代审核 | 自动审核 denied；命令 declined，没有执行 |

主任务继续使用所选主模型；判断是否回退时必须检查 guardian 请求，不能把主任务本身的 `gpt-6.1-sol` 请求当成审核回退。模拟上游的允许结果只用于验证路由和执行控制，没有评估真实模型审核质量。所有模型调用均在本机、只携带虚构凭据，未消耗真实中转额度。

配置保存另在独立假凭据目录验证了注释、换行、Fast、推理设置、认证文件保持不变，且创建了加密备份。真实配置保存后也核对了文件与目录哈希。

## 维护边界与复现材料

这是当前缓存生成的显式目录快照。以后更新或新增模型时，应重新生成目录并保留 `auto_review_model_override`；直接移除目录设置会恢复原生模型选择行为，可能再次回退主模型。该设置只覆盖目录中列出的模型，不为未知模型推断能力。

官方 [Auto-review](https://learn.chatgpt.com/docs/sandboxing/auto-review) 说明，审核只在动作需要审批时触发，已获许可的动作不会额外产生审核调用。只检查模型后台是否出现调用时，应选取确实需要审批的操作。

忽略目录 `artifacts/fast-diagnosis-2026-11-12/` 保留：

- `native-effort-source/`：匹配版本的原生档位更新源码，用于纠正初次判断。
- `probe-effort-review.py`：隔离原生链路与模拟服务。
- `offline-result-apikey-max-first.json`：从 Max 开始、随后切换档位的对照。
- `offline-result-apikey--dedicated-review.json`：保留原生 Max 机制，仅固定审核模型的成功路径。
- `offline-result-apikey--dedicated-review--review-absent--reject-review.json`：拒绝专用模型后不回退的失败路径。
- `review-policy-tool/`、`dedicated-review-plan/`：生成、保存工具和已应用的元数据摘要。目录内容未包含真实认证凭据。

早期未检查 configuration_update 的捕获以及未完成的虚构 ChatGPT 身份对照均保留为过程记录，不作为 Max 不支持或认证模式差异的结论。

## 后续 Ultrafast 验证

用户在同轮追加真实后台核对请求。只读运行时检查显示，尚未重启的桌面进程仍选中 `priority`，内存模型列表只提供普通和 Fast。磁盘上的原始模型缓存及本次保存的独立目录，均已经包含原有的 `service_tiers` 中 `ultrafast` 条目；本次审核配置生成没有删除或新增它。

补测用新目录元数据启动隔离内核，原生 `model/list` 同时返回 `priority` 和 `ultrafast`；提交 `turn/start.serviceTier=ultrafast` 后，捕获的 5 次主请求均为 `service_tier=ultrafast`。同次审核仍调用 `codex-auto-review`，审核请求没有继承主请求的速度档位，审核 approved。原始记录为 `offline-result-apikey--dedicated-review--ultrafast.json`。

这仍是本机模拟服务验证，不是当前真实中转的 Ultrafast 验收。需要先完全退出并重新打开桌面端，加载已保存的目录，再在聊天下方选择 Ultra Fast 后发起新的测试消息，核对真实后台的 `service_tier`。当前进程的 Fast 请求不能作为 Ultrafast 成功证据。

## 重启后真实会话进展

用户完全重启后反馈 auto-review 后台已经正常；据此记录专用审核模型的真实使用验收，不再只有隔离夹具证据。

北京时间 2026-11-12 16:28:47 与 16:29:03，当前聊天原生 `thread_settings_applied.thread_settings` 已记录 `model=gpt-6.1-sol`、`service_tier=ultrafast`、`reasoning_effort=ultra`。此前 16:27:24 的新进程首轮还是 `service_tier=default`；不能把重启本身当作已选 Ultra Fast。当前轮已按用户要求继续生成真实聊天请求，速度档位的客户端提交已确认；中转后台实际接收、执行及计费仍待用户对照记录确认。

## 用户最终验收与产品化

用户后续明确反馈“现在测试都正常了”，完成当前真实中转的速度档位与专用审核模型使用流程核对。此次用户反馈是实机验收依据，区别于前面的本地模拟捕获；没有进行延迟倍速或费用核账。

本机修复已通过，但专用审核模型和受管目录仍由临时工具生成，尚未集成为 Dock 界面功能。后续改动、维护方式与回归计划见[今日进度与 Dock 收敛方案](progress-and-dock-plan-2026-11-12.md)。

按该方案继续实施后，Dock 本地开发版现已提供正式的目录纳管与专用审核入口。此处的“临时工具”说明对应较早的手工修复阶段；新流程、备份和更新方式见[模型与专用审核](model-behavior-managed-catalog.md)。未在后台静默纳管或改变已经验证的真实用户配置。
