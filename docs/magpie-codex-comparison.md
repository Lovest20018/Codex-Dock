# Magpie、Codex++ 与 Codex Dock 混合连接对比及恢复方案

分析日期：2026-10-30。以下保留最初的代码对比、旧网关问题复现与恢复设计。随后按用户确认的顺序实施为 0.12.0，结果和真实验证边界见 [实现审查](review-0.12.0.md)；未改写用户当前 Codex 连接。

**结论**

建议恢复“官方账号 + PAT / API”这个可选模式，同时保留 0.11.0 的 PAT 直连和固定保存栏。恢复时应重新整理路由、模型目录、诊断与网关生命周期，不能只把 0.10.3 删除的代码放回来。

Magpie 值得参考的是：区分官方登录身份和模型请求认证，保留会话的 provider 关系，并按请求类型处理模型目录、Responses、WebSocket、压缩与后台调用。旧版 Dock 已经实现了凭据分离，但这些兼容处理较少。本次复现了请求头遗漏和鉴权错误丢失；它们能解释部分使用问题，尚不能证明是之前所有故障的原因。

**分析基线**

| 对象 | 固定版本 | 本次用途 |
| --- | --- | --- |
| Magpie | [`d99e213f5bd018edfc60152ee91989f82f0f2f9a`](https://github.com/yetone/magpie/tree/d99e213f5bd018edfc60152ee91989f82f0f2f9a)，提交时间 2026-10-30 13:01:09 UTC | 对照当前公开实现；不是对未来 main 的结论 |
| 上次参考的 Codex++ | [`14fec65206a2eb22570c17e52a8598052d679272`](https://github.com/BigPizzaV3/CodexPlusPlus/tree/14fec65206a2eb22570c17e52a8598052d679272) | 沿用上次研究快照，没有以今天的 Codex++ 替换对比对象 |
| Dock 旧版 | `d4c9e5336f9bc7f73b3e3bf7df7d5c4bb9d4eb9d`，0.10.3 | 对照实际网关、保存逻辑及发布程序集 |
| Dock 当前版 | `032e443`，0.11.0 | 保留当前 PAT 编辑、即时加载和保存修复作为恢复基线 |

Magpie 的相关源码下载在本地忽略目录 `artifacts/research/magpie-20261030/source/`，来源元数据为同目录的 `source.json`。Codex++ 为之前已有的研究快照。未将外部项目完整源码纳入本项目。

**三者实现差异**

| 方面 | Magpie | 上次参考的 Codex++ | Dock 0.10.3 |
| --- | --- | --- | --- |
| 官方身份与模型传输 | ChatGPT 登录时可保留内置 `openai`，通过根级 `openai_base_url` 接入；API 登录、接管等场景使用自己的 provider | 用 `RelaySessionProvider` 区分会话身份与传输 provider；OpenAI 身份可配合本地代理传输 | 总是切到 `codex_config_mixed`，同时设置根级地址；`requires_openai_auth=true` 保留官方账号 |
| 模型凭据 | 独立 provider Key、网关 Key 与账号池；官方账号请求另行签名 | 本地代理令牌、供应商 Key、官方 OAuth 分开处理；合并认证时优先保留 live 登录态 | 上游凭据 DPAPI 加密；TOML 中是本地令牌；发给上游时重建 Authorization |
| 官方登录 | 有自己的 OAuth 登录、保存账号及轮换相关逻辑；也读取原生登录存储 | 有官方登录管理和 profile/live 凭据合并 | 调用原生 Codex 登录；使用临时目录，保存时落到目标文件存储；刷新交给 Codex |
| 模型路由 | 识别官方模型、注册 provider/model、禁用项、账号和分组；未知命名空间不会直接落到官方 | 有 profile、协议代理、官方混合和模型映射分支 | 模型请求统一转到一个已选入口；没有同等的模型来源和能力路由层 |
| 会话请求头 | 显式处理连字符会话头、后台调用元数据；账号相关状态按路由隔离 | `with_client_session_headers` 转发 `session-id`、`thread-id` | 白名单只列旧的 `session_id`、`conversation_id`，漏了新名称和部分后台元数据 |
| 模型目录 | 区分标准 `/v1/models` 与 Codex 原生目录；有目录合并、缓存及体积限制 | 此次重点核对身份、认证与代理，不据此宣称其目录实现优于 Magpie | 基本转发 `/models`；获取列表成功不能证明 Codex 模型菜单和能力描述正确 |
| WebSocket、压缩 | 有专门的 WebSocket 通道、HTTP 回退及压缩来源处理 | 有较完整的协议代理；本次没有重新运行其集成测试 | 有 HTTP/SSE、WS、compact 转发；WS 和 HTTP 请求都受固定 15 分钟取消限制，保存配置却默认关闭 WS |
| 出错说明 | 有按路由处理的错误和上游 request ID 传递机制 | 不能仅凭静态代码判断实际线路表现 | 上游 401/403 统一变为通用 502，鉴权原因和 request ID 丢失 |
| 编辑器与网关关系 | 网关是其运行系统的一部分，包含配置同步和兼容逻辑 | 代理与应用启动、供应商管理配合 | 后台子进程监视所属编辑器；编辑器退出后停止，但已运行的 Codex 仍可能持有本地地址 |

Magpie 的入口分支见 [`internal/agent/codex.go`](https://github.com/yetone/magpie/blob/d99e213f5bd018edfc60152ee91989f82f0f2f9a/internal/agent/codex.go#L39)，Codex++ 的身份和传输分离见 [`relay_config.rs`](https://github.com/BigPizzaV3/CodexPlusPlus/blob/14fec65206a2eb22570c17e52a8598052d679272/crates/codex-plus-core/src/relay_config.rs#L6862)。二者并非两种完全相反的架构；Magpie 也会使用 `requires_openai_auth` 与独立 Bearer，区别在于何时启用、如何兼容会话和请求。

**PAT 与官方账号不能当成同一类凭据**

本次核对的 Magpie Codex 认证路径使用 `auth_mode` 区分 API 登录和 ChatGPT 登录。第三方 Responses 入口配置的是独立服务凭据；未在这些路径中发现专门实现本项目 Krill `personal_access_token` 登录语义的分支。因此，不能把“Magpie 支持 API Key”推导成“已支持该 PAT 的账号发现、额度、工作区和全部工具”。相关判断见 [`CodexAPIKeySignedIn`](https://github.com/yetone/magpie/blob/d99e213f5bd018edfc60152ee91989f82f0f2f9a/internal/provider/logins.go#L541)。

恢复混合模式时，PAT 应作为模型入口的独立 Bearer 保存；它是否可以这么使用，要通过该入口的真实 Responses 调用验证。不要把它重新写入当前官方登录的 `auth.json.personal_access_token`，否则可能改变客户端的登录模式。

Magpie 的官方账号签名则使用 OAuth Bearer 和账号 ID，并补充 Codex 客户端头，这是另外一条认证路径。见 [`codex_request.go`](https://github.com/yetone/magpie/blob/d99e213f5bd018edfc60152ee91989f82f0f2f9a/internal/provider/codex_request.go#L31)。

**旧网关的具体问题与证据**

1. **新会话头丢失，已复现。** 向真实 0.10.3 `ModelBridge` 程序集发送本机请求，模拟上游收到了 `session_id`，没有收到 `session-id`、`thread-id`。这会丢失上游可能使用的会话关联与缓存信息；是否导致某条线路失败或缓存下降，仍需该线路的响应证据。Magpie 会保留这些头；对自己构建的官方账号请求，还会根据 `prompt_cache_key` 设置会话头。不能把它的缓存实验数字直接当成 Dock 的收益。对照 [`codexHeader`](https://github.com/yetone/magpie/blob/d99e213f5bd018edfc60152ee91989f82f0f2f9a/internal/gateway/codex_backend.go#L1765) 和 [Codex++ 会话头](https://github.com/BigPizzaV3/CodexPlusPlus/blob/14fec65206a2eb22570c17e52a8598052d679272/crates/codex-plus-core/src/protocol_proxy.rs#L5376)。

2. **401 被隐藏为不带原因的 502，已复现。** 模拟上游返回 401、`invalid_api_key` 和 `x-request-id`；客户端只得到通用 502，无原始鉴权原因和 request ID。原设计是避免 API 的 401 触发官方 OAuth 刷新，出发点可以保留，但应返回结构化的“模型入口鉴权失败”，保留经过筛选的上游状态、错误码和 request ID。

3. **provider 切换会增加历史会话兼容成本，静态确认。** 旧版总是写入 `model_provider=codex_config_mixed`。Magpie 明确处理“会话保留创建时 provider”的情况，并保留已使用的 provider 表。恢复时只设置全局默认值不足以兼容旧会话，也不能直接删除旧表。见 [Magpie 的会话兼容说明与代码](https://github.com/yetone/magpie/blob/d99e213f5bd018edfc60152ee91989f82f0f2f9a/internal/agent/codex.go#L65)。

4. **网关依附编辑器生命周期，静态确认。** `BridgeProcess.RunAsync` 每 3 秒检查编辑器、连接文件和 provider；编辑器退出后释放端口。这是旧版主动采用的清理策略，但 Codex 不会因此自动重载 TOML，之后可能继续访问失效地址。它是可解释的故障条件，本次未用真实用户会话重演。

5. **模型列表与能力没有形成完整契约，静态确认。** 标准 OpenAI 模型列表通常只有 ID；Codex 原生目录还要表达推理档位、上下文与工具等信息。Magpie 为二者提供不同路径，见 [`codex_catalog.go`](https://github.com/yetone/magpie/blob/d99e213f5bd018edfc60152ee91989f82f0f2f9a/internal/gateway/codex_catalog.go#L17)。旧版列表能读不等于自动审核、Fast、图片、压缩等能力可用；历史真实验证也曾发现 `codex-auto-review` 在列表中但调用不可用。

6. **长连接与重试需要重新定义，静态确认。** 旧版有固定 15 分钟取消、64 MiB 请求体限制和 WS 配置/实现不一致。它们是明确边界，不是本次已复现的用户故障。恢复时应采用连接建立超时、空闲超时和取消传播，并单独验证大图长会话。Magpie 的 WS 回退只可作为参考，不能在已收到输出后盲目重放整个请求。见 [`codex_socket.go`](https://github.com/yetone/magpie/blob/d99e213f5bd018edfc60152ee91989f82f0f2f9a/internal/gateway/codex_socket.go#L274)。

本机复现没有使用真实账号、PAT、API Key 或额度。上游由内存中的 `HttpMessageHandler` 模拟，客户端只访问回环地址。发布包 `manifest.json` 的源提交与上述 0.10.3 基线一致。记录位置：

- `artifacts/research/old-bridge-observations.json`
- `artifacts/research/magpie-20261030/probe/Program.cs`
- `artifacts/research/magpie-20261030/probe/Probe.csproj`

复现同时确认：上游 Authorization 使用配置的模型凭据，Cookie 与官方账号 ID 没有被转发。这些隔离应保留。

**建议恢复后的使用方式**

连接页保留两个选项：默认“PAT / API 直连”，可选“保留官方账号 + 独立模型入口”。固定保存栏、`Ctrl+S`、离线保存、加载模型期间可保存均保留。账号信息和网关诊断在选择混合模式后展开。

混合模式按以下边界运行：

| 请求或数据 | 去向 / 规则 |
| --- | --- |
| 官方账号身份、工作区及客户端账号服务 | 保持原生官方登录路径 |
| 已选择独立入口的模型调用 | Codex → 本机网关 → 已选入口；替换 Authorization，隔离 OAuth、Cookie、官方项目头 |
| 模型列表 | 转换或提供 Codex 原生目录；保留来源，能力缺失时使用保守描述 |
| 标题、记忆、审核、压缩等后台调用 | 明确标记请求类型；按已验证能力处理，缺少能力时给出原因 |
| API 错误、额度不足 | 显示具体失败；第一版不自动消耗官方推理额度补位 |
| 账号 OAuth 刷新 | 第一版交给原生 Codex；不建立第二套 refresh-token 持有者 |
| 网关管理接口 | 本地专用令牌；官方 OAuth 不能用于停止、更新或配置操作 |

第一版只支持当前配置目录中的一个官方登录与一个模型入口。暂不引入 Magpie 的账号池、自动轮换、跨厂商协议转换和额度重置机制。

**路由选择与兼容验证**

新会话首选保留内置 `openai` 身份，仅通过根级 `openai_base_url` 接入本地 Codex 路径。这样减少主动切换 provider 带来的会话分组变化。旧 `codex_config_mixed` 表保留为历史会话兼容入口；只有确认仍指向本工具且有可用后端时才更新，不能自动重写无关 provider。

这需要先用安装中的原生 Codex 验证三个条件：账号仍返回 ChatGPT 身份；主调用和后台调用确实到本地；本地请求可以鉴权且上游只收到独立模型凭据。`openai_base_url` 本身不会添加本地管理令牌；不能把“监听回环地址”当作完成鉴权。可验证的当前 OAuth 只用于本地入口校验，发往模型入口前剥离，管理接口仍使用独立令牌。

如果当前客户端或凭据存储方式无法满足这些条件，应阻止启用该路由并说明原因；在验证之前，不把它写成产品默认，也不删除已知可工作的旧 provider 兼容方案。

OpenAI Docs 确认了 `openai_base_url`、自定义 provider、`requires_openai_auth` 和原生模型目录的配置含义；这些配置项并不保证官方账号工具能在任意第三方模型上完整工作。内置 provider ID 当前为保留项，不应通过新增 `[model_providers.openai]` 实现覆盖。参考 [官方配置文档](https://learn.chatgpt.com/docs/config-file/config-reference)。

**Windows 登录存储是单独的兼容项**

Magpie 的 [`codex_store.go`](https://github.com/yetone/magpie/blob/d99e213f5bd018edfc60152ee91989f82f0f2f9a/internal/provider/codex_store.go#L3) 明确说明：它的系统 keychain 读取仅在 macOS 实现，不读取 Codex 的 Windows encrypted auth storage。`keyring` 在其发现流程里可能是未知，`auto` 读取文件也不能证明文件就是当前活跃登录态。

因此不能照搬其 `auth.json` 发现方式并宣称支持所有 Windows 登录。恢复时优先从原生 Codex 查询身份；本地入口所需的凭据校验还需单独验证。区分“已登录”“未登录”“当前存储不可读取”，不要因发现失败而自动登出、清空凭据或全局切换到文件存储。继续使用文件模式时，也应清楚显示这一兼容条件。官方存储选项见 [认证文档](https://learn.chatgpt.com/docs/auth)。

**代码恢复顺序与验收条件**

| 阶段 | 具体工作 | 完成证据 |
| --- | --- | --- |
| 1：原生兼容验证 | 用隔离 CODEX_HOME、虚构登录和本机捕获服务验证保留 `openai` 的身份、请求路径、目录、后台调用及存储方式；对照旧 provider | `account/read` 身份、出站路径与凭据类别可核对；失败时不给用户写入半成品路由 |
| 2：恢复核心 | 从旧版借用 DPAPI 连接、写锁、备份回滚和原生登录隔离设计；新建路由/目录/错误模型，修复请求头；保留当前保存逻辑 | API 401/403/429/5xx、SSE、取消、请求 ID、凭据隔离均通过；不重放已输出的请求 |
| 3：独立网关进程 | 明确按用户启用的后台进程；编辑器关闭不结束；有健康检测、版本握手、单实例、异常恢复和显式停止 | 编辑器退出后仍能推理；重启和升级后旧会话恢复；禁用时只还原本工具拥有的配置 |
| 4：接回可选界面 | 混合模式按需展开账号和诊断；保存失败保留草稿；记录连接状态、上游错误和脱敏请求 ID | 离线可保存；模型发现不阻塞；外部 OAuth 更新不被草稿覆盖；PAT 直连体验保持 |
| 5：真实线路验证 | 在用户已选择的线路上做最小请求、连续多轮、压缩、WS 和工具验证 | App / CLI 各有结果；分项标明支持、不可用或未验证后再发布 |

阶段 1 的阻断项优先于堆叠 UI：必须解决账号身份、本地鉴权和实际模型路由同时成立的问题。以上是原恢复顺序，具体实施证据见 [0.12.0 审查](review-0.12.0.md)。

保存流程继续以最新 live 登录态为准。Codex 刷新 OAuth 后，旧草稿或旧备份中的令牌不能覆盖新值；应重新读取或提示冲突。Codex++ 对此有明确处理，见 [`auth_contents_with_proxy_key`](https://github.com/BigPizzaV3/CodexPlusPlus/blob/14fec65206a2eb22570c17e52a8598052d679272/crates/codex-plus-core/src/relay_config.rs#L4868)。本项目可复用已有外部修改检测，但要避免每次保存普通设置都重写认证。

压缩、加密 reasoning 和 sealed subagent 内容可能与生成它们的后端相关；第一版固定会话后端，不做出错后的跨后端自动续接。Magpie 为这类请求安排了专门分支，不能只靠改模型名解决，见 [`codex_backend.go`](https://github.com/yetone/magpie/blob/d99e213f5bd018edfc60152ee91989f82f0f2f9a/internal/gateway/codex_backend.go#L178)。

**可沿用与应重做的部分**

- 沿用：当前 0.11.0 的 PAT 编辑和保存体验，旧版的凭据隔离、DPAPI、备份回滚、原生登录隔离与系统代理解析。
- 重做：会话/provider 兼容策略、Codex 原生模型目录、后台请求分类、错误诊断、长连接策略、网关生命周期。
- 按需实现：单账号登录状态显示、显式检查连接、能力验证结果。
- 第一版不恢复：多账号池、自动官方推理兜底、跨厂商加密上下文转换、强制开启 Fast 或自动审核。

Magpie 固定快照声明 MIT；Codex++ 固定快照声明 AGPL-3.0。本方案只借鉴后者的行为与设计，不复制其实现。将来若复用 Magpie 代码，也应保留对应许可与来源。本次没有引入外部实现或新依赖。
