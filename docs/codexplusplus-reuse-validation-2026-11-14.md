# Codex++ 2.6.9 修复复用验证

后续已按本报告完成第一批开发实现：旧聊天接续、网关 WebSocket 空闲退役、Windows 文件身份核对和目录待重载提示，见 [使用方式与实施验收](desktop-connection-recovery.md)。以下保留实施前的隔离验证结论。

结论：**旧聊天接续、网关连接更新值得适配；Windows 文件身份核对可以独立实现。Fast 的“前四个入口”修补不命中 Dock，模型目录内容摘要检测则已经具备。**

本轮完成隔离实验，没有修改产品代码、真实配置、真实聊天或客户端安装，没有重启真实 Codex，也没有制作或发布新版本。所有模型响应来自本机模拟服务，身份和 API Key 均为虚构。

验证基线为 Dock 0.18.0 的 `de1991cb5dc914f5b1234c65e51a9ac9485aaf08`、Codex++ 2.6.9 的固定提交 `e548ad099d655b002c569cac22c6f91b8cd37e3b`，以及本机内核 `0.167.0-alpha.4.1`。当前只读核对的商店前端资源为 `26.1106.6628.0`。

## 复用判断

| 修复 | 实验结果 | 对 Dock 的处理建议 |
| --- | --- | --- |
| 旧聊天切换 provider 后无法继续 | 已配置的旧 provider 仍会产生原生 `modelProviderMismatch`；移除旧 provider 时返回 `modelProviderNotConfigured`。只准备会话索引，再恢复，可保留原聊天 ID 和历史，两向通过。 | 借鉴逐聊天准备流程，增加串行锁、活动状态保护、备份、冲突校验与失败恢复。 |
| 保存新入口后旧聊天仍使用旧地址 | 原生已加载聊天会保留旧入口。Dock 网关的新 HTTP 请求能读取新设置；已经建立的 WebSocket 仍走旧入口，新建 WebSocket 才切换。 | 增加连接代次和空闲重连，重点覆盖 WebSocket。 |
| 商店版跨盘路径不同导致误判 | 真实商店 EXE 的常规路径和卷 GUID 路径，旧字符串比较判为不同，文件身份原型能正确识别；替换文件和错误路径检查通过。 | 独立补文件身份判断，保留现有包身份、进程句柄、创建时间和正常关闭保护。跨盘商店安装端到端验收仍待补。 |
| 新版 Fast runtime 排在第 5 个入口 | Dock 在 runtime 放到第 5、12、16 个资源位置时均能找到，并识别五类门控。未知加载器结构仍拒绝应用。 | 无需套用上游扫描数量补丁。26.1111 原始资源仍需另验。 |
| 模型目录更新识别与热刷新 | 同长度、同修改时间的内容变化能被 Dock 检出；同步保留审核映射和全部模型元数据。当前原生内核运行中仍返回旧目录，新进程才返回新内容。 | 保留现有摘要与不可变快照机制，明确“已同步，等待重载”；实时刷新需单独接入。 |
| 重启时固定调试端口暂未释放 | Dock 当前按回环端口 `0` 分配新端口，已有就绪重试和监听者身份核对，与上游固定端口情形不同。 | 可补充占用者与等待阶段反馈；本轮仅源码核对。 |
| 清理已经安装版本的更新包 | 上游按安装包版本清理，保留较新版本；Dock 当前主要清理下载失败的 `.partial`。 | 可后续实现，只处理 Dock 自有目录、严格匹配名称和版本，保留更新版本及未知文件。本轮未执行产品缓存清理。 |

## 旧聊天：验证到哪一步

同一 `openai` provider 的 A → B → A 实验表明，`config/read` 已显示 B 时，直接对已加载聊天调用 `thread/resume`，下一次请求仍到 A。空闲后取消订阅、确认 `notLoaded`，再恢复，才会切到 B。携带模型、Max 和 Ultra Fast 设置后，恢复响应及模拟服务捕获均保留对应值，历史中的两端用户消息和回复也保留。

回复进行中，原生返回 `active`。实验中的准备保护延后操作，没有发出取消订阅或恢复；原请求完成后再切换成功。测试将临时内核的 `thread_unload_delay_secs` 设为 `0`，只用于加速隔离验证。OpenAI Docs 说明，默认取消订阅后的卸载有宽限期，且须最后一个订阅者离开；实际接入应等待原生状态通知。官方建议的跨 provider 选项还包括从历史 `thread/fork` 创建新聊天，该路径也通过了验证。[OpenAI Docs：App Server](https://learn.chatgpt.com/docs/app-server)

最终验证采用上游的**只准备 SQLite 索引**思路，保存变更前备份，按预期旧 provider 条件更新，保留所有 rollout 文件字节。`legacy → openai → legacy` 均能恢复原聊天 ID；中间增加的回复、冷重开和重新扫描历史列表也通过。旧 provider 已被外部改变时，准备操作拒绝覆盖。当前隔离内核没有桌面 UI 的独立 `local_thread_catalog` 数据库，其双库一致性仍需真实桌面接入时验收。[上游索引准备实现](https://github.com/BigPizzaV3/CodexPlusPlus/blob/e548ad099d655b002c569cac22c6f91b8cd37e3b/crates/codex-plus-data/src/provider_sync.rs#L634)

另外直接调用 Dock 当前 `ConfigStore` 和 `ModelBridge`，执行 API → 虚构官方身份的混合连接 → API。每次只重开测试内核，三次请求保留同一聊天 ID、两侧历史、Max 和 Fast；混合连接保留虚构 ChatGPT 身份，并使用配置的模型 Key 转发。当前内核的现有 `openai` 与兼容 provider 两项混合连接回归也通过。

这些结果支持复用恢复机制。**真实桌面的 IPC、前端缓存接入、同一进程中的官方身份热切换，以及真实 OAuth 登录和刷新尚未验收。** Dock 后续接入应按聊天串行准备，重验目标连接，保留模型、推理与速度选择，等待空闲状态，恢复成功后更新前端缓存，再发送一次用户输入；失败时保留草稿与历史。

## 网关：HTTP 与 WebSocket 的区别

本轮使用实际 `ModelBridge`、加密连接文件和两个回环上游验证：

1. 首个 HTTP 请求与 WebSocket 均到 A。
2. 保存 B 地址与 B 的模型 Key，保持本地端口和本地令牌不变。
3. 下一个 HTTP 请求到 B，使用 B 的 Key。
4. 原 WebSocket 后续消息仍到 A，沿用建立连接时的 A Key。
5. 新建 WebSocket 后到 B，使用 B 的 Key。

因此，上游的连接代次机制对 Dock 有实际价值。HTTP 已能逐请求读取变化；WebSocket 需要在空闲后建立新连接。连接更新反馈应区分“已保存入口”和“当前聊天已重新接入”，避免只根据配置文件显示成功。[上游连接恢复流程](https://github.com/BigPizzaV3/CodexPlusPlus/blob/e548ad099d655b002c569cac22c6f91b8cd37e3b/assets/inject/renderer-inject/30-service-tier.js)

## 路径、Fast 与目录的验证边界

路径原型使用卷序列号与 128 位文件 ID 比较打开的文件句柄。真实商店 EXE 的等价路径、大小写、分隔符、扩展路径、硬链接通过；内容相同的不同文件、缺失路径和文件替换被正确区分。Windows 文档说明该组合可用于比较文件身份，ReFS 应使用 128 位 ID。[FILE_ID_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info)、[GetFileInformationByHandle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfileinformationbyhandle)

临时 C/D 盘目录链接被 Windows 以“不受信任的装入点”拒绝遍历，测试保持系统保护。这个结果验证了无法核对身份时拒绝接受路径，**没有完成跨盘商店安装的停止、重启实测**。后续文件身份判断应绑定本次操作，并继续核对原进程句柄和创建时间。

Fast 样本是带有已知门控与加载器结构的合成 asar，用于证明 Dock 没有前四个或前十二个资源的截断问题。本机 26.1106 的原版资源也已只读匹配；这些结果不覆盖 26.1111 门控、编译器结构或真实菜单运行效果。[上游第五入口回归](https://github.com/BigPizzaV3/CodexPlusPlus/blob/e548ad099d655b002c569cac22c6f91b8cd37e3b/assets/inject/native-fast-rspack.test.cjs)

目录测试将内容从 A 改为 B，保持文件长度与修改时间相同。Dock 能提示更新，生成新的不可变目录，保留旧版本；除固定审核映射外，每个模型字段保持一致，包括推理更新能力、速度档位和未知嵌套字段。预览后再改来源会被拒绝。原生 `model/list` 在同一进程中仍为 A，新进程才为 B，因此仅同步文件不足以支持热更新。

## 证据与实施顺序

本地证据目录为 `artifacts/codexplusplus-update-2026-11-14/validation/`，不随产品打包。`verification-summary.json` 记录版本、哈希、各阶段结果和未通过的对照实验；上游源码哈希记录于同级 `source-index.json`。

| 阶段 | 结果文件 | 结果 |
| --- | --- | --- |
| 原生聊天生命周期与目录观察 | `native-1794616744759220400/result.json` | 28 项断言通过；最终索引方案以后一行实验为准。 |
| 只准备索引、两向恢复、冷重开和列表扫描 | `native-1794617632607895100/result.json` | 14 项通过。 |
| 产品保存逻辑与网关的 API／混合双向切换 | `native-1794617998443014900/result.json` | 14 项通过。 |
| 当前内核与现有两类混合 provider 回归 | `native-bridge-current/native-bridge-results.json` | 2 项通过。 |
| 路径、目录和 Fast 资源扫描 | `results/core-validation.json` | 16 项通过；跨盘保护拒绝属于边界检查，跨盘安装仍未实测。 |
| HTTP 与现有 WebSocket 更新行为 | `bridge-live-rebind/bridge-live-rebind.json` | 10 项断言通过，确认现有 WebSocket 需要重连。 |

简化的官方身份 HTTP 模拟曾出现超时；把工作区 origin 改为 HTTP 的对照被原生 HTTPS 要求拒绝。这些尝试保留为未通过，不计入验收。最终身份切换结论来自产品 `ConfigStore` 与 `ModelBridge` 路径。历史列表的初次检查遗漏 `appServer` 来源筛选，修正后冷重开检查通过。

建议先做连接代次与空闲恢复，再接入旧 provider 的逐聊天索引准备，同时补 Windows 文件身份判断；目录先补待重载状态，热刷新与更新包清理后续处理。继续保留当前普通用户的统一保存、正常重开和未保存任务保护。

本轮借鉴机制并独立编写验证原型，没有把上游 Rust／JavaScript 并入 Dock 产品。上游固定版本为 AGPLv3，Dock 当前为 MIT，后续代码合入需按实际来源处理。[上游许可证](https://github.com/BigPizzaV3/CodexPlusPlus/blob/e548ad099d655b002c569cac22c6f91b8cd37e3b/LICENSE)
