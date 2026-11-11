# Superba 更新复用评估（2026-11-11）

建议先做三项：撤掉社区模板默认关闭统计的推荐、更新配置资料并补版本兼容提示、完善自定义 HTTP 模型连接的授权。模型目录的更新与应用状态适合第二批；PAT 项目恢复有价值，需要单独验证和收窄恢复范围。

本次为源码评估，未移植功能、修改用户配置或生成发行包。

后续第一批合并和保存后重启联动见 [实施与验收记录](superba-reuse-implementation-2026-11-11.md)。下文保留评估时的本地基线。

## 核对范围

- Dock 基线：Windows 0.16.3，提交 `beb90b0`。
- 本次查到的最新 Superba 发布：[v0.1.1-alpha.15](https://github.com/krill-lab/superba/releases/tag/v0.1.1-alpha.15)，提交 `7ba3591`，预发布版本。主要核对 alpha.13～alpha.15，并回看此前与现有功能重合的变更。
- 使用固定标签的[变更日志](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.15/CHANGELOG.md)及下列源码，避免把不同时间的 main 页面混为同一快照。
- 结论来自公开源码、发布记录和本地代码比对。没有在真实 PAT、桌面账号或生产网关上复现上游问题；没有取得完整提交差异或构建上游。

## 1. 第一批：修正社区模板中的统计选项

**已确认本地推荐与上游当前建议不同。** Dock 的 `src/CodexConfig.Core/ConfigTemplate.cs:85` 仍把 `analytics.enabled = false` 默认勾选在 Krill 社区方案中。

alpha.14 撤掉这项 PAT 默认推荐：按上游调查，桌面端关闭 analytics 时，远程功能开关客户端不再持久缓存，中文界面开关也受影响。固定标签的 [`codex.rs`](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.15/crates/superba-core/src/codex.rs) 已从 `PAT_RECOMMENDED_KEYS` 移除它，并解释了原因。

建议移植的是默认选择与作用说明：

- 新内置方案不再默认关闭 analytics；仍允许用户主动选择关闭。
- `config/config-help.json:129` 补充桌面端缓存的兼容说明，注明核对版本和来源。
- 不根据现有文件中恰好为 false 就认定它是旧推荐，不自动改回 true 或删除。`TemplateRepository.cs:108` 允许用户编辑后的内置方案覆盖新版内置值，旧本地方案也应保留用户选择。
- 模板来源应区分原历史配方与本轮调整，继续通过现有差异确认页保存。

改动小，但实际中文界面问题仍需对应桌面版本的隔离验收。

## 2. 第一批：配置资料、中文说明与版本兼容

Dock 当前目录为 `local-2026-11-02`，1003 条定义，核查内核 `0.166.0-alpha.5.1`。Superba alpha.13 的发布记录报告已覆盖 `0.168.0-alpha.4`，并更新中文说明；其[目录索引](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.15/catalog/dist/index.json) 同时保留多个版本。

逐项解析上游 [0.166.0 → 0.168.0-alpha.4 差异表](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.15/catalog/dist/changes/codex-0.168.0-alpha.4.md)，与本地 JSON 的 `entries.path` 做精确匹配：该表新增的 **61 条结构路径，本地均未收录**。

| 路径分组 | 未收录数量 |
| --- | ---: |
| `auto_review.*` | 1 |
| `features.*` | 22 |
| `model_providers.*` | 2 |
| `otel.*` | 1 |
| `plugins.*` | 17 |
| `tools.*` | 1 |
| `tui.*` | 17 |

这些包含父表和动态模板，并非 61 个独立开关，也不是两份完整目录的全部差异。本地已有未知字段编辑通道，未收录主要意味着缺少专门说明、枚举和校验。不能用上游报告的 1132 减本地 1003 来推断缺失数。

已核对的具体差异：

| 项目 | Dock 当前资料 | 上游新资料 |
| --- | --- | --- |
| `features.multi_agent` | 稳定，默认 true | 已弃用，默认仍 true |
| `features.respect_system_proxy` | 开发中 | 实验性 |
| `features.unified_image_budget`、`retain_client_tool_outputs`、`deferred_tool_world_state` | 开发中，默认 false | 稳定，默认 true |
| `tui.notification_method` | `auto` / `osc9` / `bel` | 新增 `osc777` |
| Guardian V2 同步预算与分区 | 未收录 | 新增结构及组合约束 |
| 供应商 capability 与 OAuth 凭据存储 | 缺少两个新子项 | 有对应定义与说明 |

建议沿用 `docs/schema-maintenance.md` 的维护方式：以上游差异和中文资料作线索，按目标 Codex 官方 schema/源码复核后写入自有 JSON，记录版本、来源与摘要。新版本默认值只作参考，不写入用户配置；旧版本仍需保留相应语义。

还值得借鉴 [`settings/plan.rs`](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.15/crates/superba-config/src/settings/plan.rs) 与 [`settings/mod.rs`](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.15/crates/superba-config/src/settings/mod.rs)：按读取同一配置目录的桌面内核、CLI 和远端分别显示支持情况，并以实际 `features list` 结果纠正目录差异。Dock 当前 `ConfigSchema` 只有整份目录的核查版本，尚无逐执行端支持矩阵。

可以先补资料和版本标记，再扩展兼容展示；无需引入它的 Rust crate、生成器或在线目录服务。Guardian V2 新组合约束应接入现有 `ConfigConstraints`，实施时以目标内核验证。

## 3. 第一批：补齐 HTTP 模型连接的授权

alpha.15 的新能力是受约束的自定义 HTTP 连接。其[设计说明](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.15/ARCHITECTURE.md) 将授权绑定到具体站点，并覆盖探测、转发与撤销后的运行状态。

Dock 的 `ConnectionSettings.cs:28` 已接受 HTTP / HTTPS；PAT 本身要求 HTTPS，但 HTTP 可转为 API Key。因此可复用的是授权机制，不能把“支持 HTTP”作为新增功能重复开发。

本地代码尚无按 origin 保存的 HTTP 确认。`ModelCatalogClient.cs:61` 和 `ModelBridge.cs:156` / `:219` 的代理选择也未对 HTTP 单独约束。

建议在现有连接表单增加具体站点的明确确认和持续可见的 HTTP 标记；获取模型与发送请求共同校验授权；外部明文 HTTP 使用直连；撤销确认时同步停止相应转发。Dock 自有 `127.0.0.1` 模型网关与手机反代入口应维持各自的本地连接契约。

这部分逻辑需要以 C# 重写并覆盖目录读取、HTTP 与 WebSocket 三条路径。

## 4. 第二批：模型目录的“已获取、待应用、已采用”状态

alpha.14 增加模型隐藏操作，并将服务端后来新增、尚未应用到 Codex 的模型标为待处理。

Dock 的 `MainWindow.xaml.cs:436` 获取模型后，只更新本工具的下拉列表与耗时提示。`ModelCapabilities.cs` 能读已有目录的能力；`NativeModelCatalog.cs` 会转换网关返回值。这些尚未形成对直接连接的完整目录管理流程。

可借鉴 [`superba-models`](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.15/crates/superba-models/src/lib.rs) 的以下做法：

- 保存目录来源与内容摘要，区分获取结果、待应用目录和已写入配置的目录；实际运行是否采用另行确认。
- 支持按模型隐藏，区分用户隐藏和上游隐藏，并保留原生元数据。
- 为目标内核生成目录，用该内核预检格式后再写配置；不凭一份固定模板宣称兼容所有版本。
- 继续显示上游声明的推理和服务档位，不根据名称推断 Fast / Ultrafast 支持。

这需要模型目录存储与应用流程，工作量中等，适合放在资料更新之后。

## 5. 专项：PAT / API Key 下的桌面项目保留与恢复

alpha.14 针对桌面 app 26.1103 的账号状态变化增加修复：曾用 ChatGPT 登录后，转为 token 登录可能丢失项目显示；它在启动前处理账号状态，并提供项目恢复选择。

[`account_state.rs`](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.15/crates/superba-appctl/src/account_state.rs) 会把 `electron-account-renderer-state-v1.inactive` 中多个 owner 的状态合入顶层，然后移除该分区键。范围包括项目之外的原子状态和置顶等内容。**不宜整体照搬这个合并范围。**

Dock Windows 当前主要负责配置保存并提示重新打开 Codex，没有 Superba 的统一桌面启动前处理链路。保留真实官方登录的混合连接，也不应按 token 登录去合并状态。

建议先做只读诊断，确认 app 版本、实际 `CODEX_HOME` 和登录类型；需要恢复时仅列出用户可识别的候选项目。参考 [`project_restore.rs`](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.15/crates/superba-appctl/src/project_restore.rs) 的选择、去重、复用项目 ID、备份和原子替换机制，但限制在所选项目与账号范围。写入前需确认桌面程序退出并检测外部修改。

该项收益可能很大，实施前应先用隔离账号状态复现，单独审查恢复范围。

## 已有能力与暂缓事项

| 上游能力 | Dock 的判断 |
| --- | --- |
| 官方登录保留、模型流量转 API | 已有混合连接，无需重建 |
| 新旧 Krill 域名识别 | `KrillLines.cs` 已包含新域名及三个旧域名别名 |
| 写入前完整差异确认 | 0.16.3 已完成，可保留本项目流程 |
| WebSocket 请求逐帧统计 | `ModelBridge.cs:262` 已逐完整消息计数；没有上游曾用握手预热标记排除后续轮次的同类逻辑。此判断不等于已具备其完整用量报表 |
| 桌面热切换 | 依赖 CDP、受控启动和版本相关 IPC；需要新增生命周期管理，暂缓 |
| 多工具中心、跨协议转换、自动故障切换与自有压缩 | 会扩大当前 Codex 专用网关的职责，暂缓 |
| egui / wgpu / glibc 内存优化 | 与现有 WPF 技术栈不同，不能直接套用 |

alpha.13 曾把 `auth.json.lock` 兼容列为已知问题。本地 `ConfigStore.cs:316` 也只是 Dock 自己的写锁；这值得后续按目标 Codex 实现核查，但不能作为上游已经提供的修复来移植。

## 落地顺序与验证边界

第一批建议为模板与帮助修正、配置资料更新、HTTP 连接授权；随后做模型目录生命周期；项目恢复单独推进。配置资料可复用经复核的数据和中文说明，核心行为按现有 C# / WPF 架构实现。继续保留已有 Superba MIT 声明，并为实际导入内容记录固定来源。

本次完成了本地 JSON 解析、61 条新增路径精确比对及相关调用路径检查。未改生产代码，因此未运行构建和功能回归。真正移植时，分别验证模板不覆盖用户选择、不同内核的配置兼容、HTTP 三条请求路径、模型目录预检，以及项目恢复的隔离、冲突和回滚行为。
