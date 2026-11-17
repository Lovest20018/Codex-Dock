# Codex++ 2.7.1 复用评估

建议优先补长推理的响应头等待设置、模型能力保留，再做实验功能的支持状态读取；压缩兼容先验证边界，手机设备分级放到安卓实机阶段。任务面板、聊天助手和桌面镜像暂不并入当前配置工具。

后续已按此方案实施三项 Windows 能力并验证压缩边界，详见 [实施与测试记录](codexplusplus-reuse-implementation-2026-11-17.md)。下文保留当时评估依据，不能据此把所有推荐项视为已实现。

本轮固定核对 Codex++ **2.7.1**，提交 [`d5dcad3b3b9d1ef1dc83ba76a3b8f4c4098d2abe`](https://github.com/BigPizzaV3/CodexPlusPlus/commit/d5dcad3b3b9d1ef1dc83ba76a3b8f4c4098d2abe)，覆盖上次 2.6.9 评估之后的 2.6.10、2.7.0、2.7.1。版本同时由发布页和固定源码的 Cargo.toml 核对，没有把后续 main 混入。Dock 对照当前未提交工作区，而非仅对照旧 0.18.0 提交。

这是源码评估与本机隔离观察，没有运行 Codex++、移植其运行时代码、改变真实模型请求或操作用户设备。上游链接均固定到同一提交。

## 值得适配的内容

| 顺序 | 内容 | Dock 当前情况 | 建议范围 |
| --- | --- | --- | --- |
| 优先 | 流式响应头等待可配置 | 模型网关固定等待 60 秒；收到数据后另按 5 分钟空闲超时处理。长推理上游可能在首字前被判超时。 | 增加有界的按连接设置，分别表示连接、响应头和流中空闲阶段，保留取消。 |
| 优先 | 模型图片与速度能力保留 | 原生格式目录保留元数据；普通 `data/id` 目录转换会丢弃明确声明的能力。 | 只保留经过类型校验的已知能力字段，缺失项保持保守默认；不凭模型名称补图片或 Fast 能力。 |
| 下一批 | 实验功能支持状态与保存后核对 | 已有完整配置目录、保存确认和配置来源，但没有查询目标内核的实验功能阶段与启用状态。 | 将支持状态接入现有高级配置/诊断，保留统一保存、备份和应用流程。 |
| 先做验证 | 不支持压缩触发项时的降级 | 已识别压缩请求并统计；请求仍透明转发，上游拒绝时返回错误，没有摘要降级。 | 先验证明确错误码、触发项、上下文与流式协议，再决定是否加入有条件兼容。 |
| 安卓阶段 | 配对设备权限分级 | 已有一次性配对、撤销、登录与访问规则；已登录设备没有逐操作的查看/消息/完全控制分级。 | 在现有手机后台增加设备能力与服务端校验，继续使用当前设备与会话体系。 |

### 响应头等待

上游为流式响应头提供单独设置，默认 120 秒，有界调整至 3600 秒；这与传输过程中持续收到数据的空闲计时不同。[实现](https://github.com/BigPizzaV3/CodexPlusPlus/blob/d5dcad3b3b9d1ef1dc83ba76a3b8f4c4098d2abe/crates/codex-plus-core/src/protocol_proxy.rs#L2048)

Dock 对应 `ModelBridge.ForwardHttpAsync` 的固定 60 秒。此项改动边界小，适合先做；应验证慢响应头、取消、流已开始、等待配置变化与错误阶段提示，而不是统一拉长所有超时。API Key 直连由 Codex 自己发请求，Dock 网关设置只对经过本网关的路径生效。

### 模型能力

上游将选择器需要的能力字段传到原生目录，图片能力在多成员路由时收窄为已声明能力的交集。[实现](https://github.com/BigPizzaV3/CodexPlusPlus/blob/d5dcad3b3b9d1ef1dc83ba76a3b8f4c4098d2abe/crates/codex-plus-core/src/gateway/apply.rs#L899)

本轮直接调用当前 Core 的 `NativeModelCatalog.Convert`：输入普通 `data/id` 对象并明确声明图片、1M 上下文及 priority 档位，结果只剩 text、没有上下文字段、档位数组为空。相同能力使用原生 `slug/supported_reasoning_levels` 格式时保留。这个结论针对转换分支，不代表所有目录都丢能力，也不证明任何真实模型具有这些能力。

建议补普通目录转换的能力白名单和类型校验。上下文等字段须依据服务明确返回的含义处理，不将未知字段或缺失值自动变为支持。现有受管目录的摘要、备份、审核映射和待重载机制继续保留。

### 实验功能状态

上游通过 `experimentalFeature/list` 分页读取阶段与开关，修改后再回读；单纯写入配置成功不能证明功能生效。[实现](https://github.com/BigPizzaV3/CodexPlusPlus/blob/d5dcad3b3b9d1ef1dc83ba76a3b8f4c4098d2abe/assets/inject/renderer-inject/96-feature-lab.js#L276)

值得复用的是支持状态与结果核对。Dock 应继续先暂存、看差异、确认保存，不照搬上游面板直接写配置的方式。实际桌面可能有进程内默认值和远程开关；单独启动的 CLI 内核快照不能冒充当前桌面实例。需明确执行端与实例，区分已保存、等待重启、当前观察值和未确认。上游的 138 项中文说明也不等于任意版本都支持 138 项功能。

### 压缩兼容

上游对带触发项的探测请求，识别特定 400/422 拒绝，包括 `subscription_sharing_unsupported_capability`，再走降级路径。[错误识别](https://github.com/BigPizzaV3/CodexPlusPlus/blob/d5dcad3b3b9d1ef1dc83ba76a3b8f4c4098d2abe/crates/codex-plus-core/src/protocol_proxy.rs#L2912)、[降级分支](https://github.com/BigPizzaV3/CodexPlusPlus/blob/d5dcad3b3b9d1ef1dc83ba76a3b8f4c4098d2abe/crates/codex-plus-core/src/protocol_proxy.rs#L4687)

Dock 当前只有观察和透明转发，不能直接把这段条件判断加进去就宣称兼容。完整能力还需要摘要生成、压缩结果结构、上下文保留、流式完成语义及失败后的原错误处理。建议先用本机上游替身验证明确拒绝、无关 400、原生成功、流中失败和工具历史，再确定实现范围；本轮没有发送真实压缩请求。

### 手机设备权限

上游定义查看、消息、完全控制三档，并在 HTTP 接口按操作检查权限；主令牌限定在本机回环。[设备定义](https://github.com/BigPizzaV3/CodexPlusPlus/blob/d5dcad3b3b9d1ef1dc83ba76a3b8f4c4098d2abe/crates/codex-plus-core/src/remote_control/devices.rs#L20)、[接口权限](https://github.com/BigPizzaV3/CodexPlusPlus/blob/d5dcad3b3b9d1ef1dc83ba76a3b8f4c4098d2abe/crates/codex-plus-core/src/remote_control/server.rs#L250)

适合在现有手机后台上独立实现，不替换整套远程控制。设计时应覆盖已建立连接的撤销和降权；该固定版本的持续授权辅助方法主要检查设备是否仍存在，不能据此认定现有连接会完整响应权限降级。安卓界面和实际设备需同步验收。

## 已具备或暂不需要重复移植

| 上游更新 | 当前判断 |
| --- | --- |
| 卸载旧聊天后有限轮询 | Dock 已按原生状态轮询，保留活动回复保护，最长约 110 秒；不需要套用上游 15 秒参数。 |
| 新建聊天准备读取从 2 秒延长至 8 秒 | Dock 使用另一套桥接：本机接续响应 12 秒、原生关键读取 10 秒，并非同一处硬编码 2 秒缺陷。值得借鉴按读取类型区分截止，但不直接抄数值。 |
| 浏览器 WebSocket Origin 拒绝、保留不支持握手状态 | 当前网关已拒绝 Origin 并保留 404/405/426/501。上游混合协议供应商池的判断不适用于 Dock 当前单入口 Responses 网关。 |
| 切换供应商时保留 `[[skills.config]]` | Dock 按拥有的键修改原文，不整段合并技能数组。本轮切换连接计划中，两条技能数组表及启停值逐字节保留，未写文件；无需引入上游的整套模板合并。 |
| 旧聊天接续、连接代次、Windows 文件身份、目录摘要 | 已在前轮适配并保留各自验证边界，不重复计入本轮新增能力。 |
| 2.7.1 主题、下拉和注入样式失效修复 | 主要修复其 Web 注入页面。Dock 新界面使用 WinUI 原生控件和主题资源，可参考布局，不能直接移植 CSS 或同名函数修补。 |

任务面板的 Markdown 存储和“运行完成进入待验收”规则可以保留为后续设计参考；它的状态更新会尊重用户手动变更。[任务状态实现](https://github.com/BigPizzaV3/CodexPlusPlus/blob/d5dcad3b3b9d1ef1dc83ba76a3b8f4c4098d2abe/crates/codex-plus-core/src/task_board.rs#L572) 当前 Dock 的主要任务是连接、配置、增强和手机访问，新增完整任务管理会明显扩大产品范围。聊天助手「加加」、桌面镜像、拓展市场和 Hooks 执行同样不列入这次优先复用清单。

## 证据

`artifacts/codexplusplus-update-2026-11-17/source-index.json` 记录固定源码与每个提取文本文件的摘要；源码来自官方 codeload，未执行其中脚本。`probes/probe-results.json` 记录普通/原生目录的能力保留差异和技能数组样例，均调用当前 Core，不保存真实配置。

评估阶段未运行上游运行时代码、真实 OAuth、模型服务的压缩行为、手机新权限与镜像；当时的产品改动为另行要求的两处 WinUI 审查修复，已完成 405 项完整窗口回归及 ZIP 解压后的 11 项专项，见 [修复记录](winui-review-fixes-2026-11-17.md)。之后获准实施的三项功能、原生协议原型与新增测试单独记录在 [实施与测试记录](codexplusplus-reuse-implementation-2026-11-17.md)。
