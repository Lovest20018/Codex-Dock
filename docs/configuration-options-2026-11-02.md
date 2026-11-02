# 项目配置选项全量清单（2026-11-02）

覆盖 `config/config-schema.json` 的全部 983 条定义，包含 841 条取值项与 142 条父表/容器；动态 `<name>` 是结构模板，实际实例数量不限。配置目录本身没有逐项展开旧 profile。

原目录中文说明属于项目现有资料，未逐条做真实模型调用验证。983 条路径均能在本次官方 JSON schema 结构中找到；这只证明结构收录，不保证当前客户端、模型、认证或服务端一定使用。

“目录默认”来自旧快照；“当前内核特性”仅对 `features list` 公布的规范开关有效，采用隔离空配置下的 `codex-cli 0.166.0-alpha.5.1` 输出，不是用户会话最终值。未标注生命周期不等于稳定。

优化建议是基于类别与明确发现的初步分流，不代表每条设置均有独立收益测试。详细优先级见 [审查报告](configuration-audit-2026-11-02.md)。

来源：[官方配置 schema](https://learn.chatgpt.com/docs/config-schema.json)、[官方配置参考](https://learn.chatgpt.com/docs/config-file/config-reference)、项目配置目录。

## 模型与速度（12 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `features.fast_mode` | Fast 模式：允许使用 Fast 服务档位：配置中的 `service_tier = "fast"`（请求中发送 `priority`）只有开启时才生效，终端界面也会提供切换 Fast 模式的选项和快捷键。关闭后 Fast 档位设置会被忽略；Ultra Fast 档位由 `ultrafast_mode` 单独控制。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 这是允许选择 Fast；单独开启不能证明请求已走快速档位。 |
| `features.ultrafast_mode` | Ultra Fast 模式：允许使用 Ultra Fast 服务档位，独立于 Fast 模式：配置中的 `service_tier = "ultrafast"` 只有开启时才生效，终端界面也会提供相应选项。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 按模型能力与任务选择；档位应显示实际值。 |
| `model` | 模型：新会话默认使用的模型名称。未设置时使用当前模型供应商和内置模型目录给出的默认模型；会话中切换模型或命令行 `-m` 参数会覆盖此值。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按模型能力与任务选择；档位应显示实际值。 |
| `model_catalog_json` | 自定义模型目录文件：指向一个 JSON 模型目录文件（绝对路径），启动时加载并替换本进程内置的模型目录，文件中至少要有一个模型。只在启动时生效：按会话下发的 `config` 覆盖虽然被接受，但不会重新加载它。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示目录来源、更新时间与能力；软刷新，不阻断离线保存。 |
| `model_reasoning_effort` | 推理强度：发给模型的推理强度（Responses API `reasoning.effort`），常见取值为 `none`、`minimal`、`low`、`medium`、`high`、`xhigh`，部分模型还支持 `max`、`ultra` 等由模型目录声明的值。未设置时使用模型的默认推理强度；强度越高通常越慢、消耗的 token 越多。 | string；min_length=1 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按当前模型支持档位生成选项；日常可选 high，复杂任务保留 xhigh。 |
| `model_reasoning_summary` | 推理摘要：请求模型返回推理摘要的方式（Responses API `reasoning.summary`）：`auto`、`concise`（简要）、`detailed`（详细）或 `none`（不要摘要）。未设置时使用模型目录中的默认值。 | enum：auto / concise / detailed / none | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按模型能力与任务选择；档位应显示实际值。 |
| `model_verbosity` | 输出详细度：控制 GPT-5 系列模型回复的详细程度（Responses API `text.verbosity`），可选 `low`、`medium`、`high`。只对模型目录标明支持该参数的模型生效，其他模型会忽略它并在日志中警告；未设置时使用模型的默认值。 | enum：low / medium / high | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按模型能力与任务选择；档位应显示实际值。 |
| `oss_provider` | 本地模型供应商：用 `--oss` 运行本地开源模型时默认采用的供应商，只能是 `lmstudio` 或 `ollama`。仅影响 CLI（终端界面和 `codex exec`），命令行指定的 `--local-provider` 优先。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按模型能力与任务选择；档位应显示实际值。 |
| `personality` | 回复风格（已弃用）：已弃用：`friendly` 和 `pragmatic` 不再切换回复风格。设为 `none` 时仍会从模型内置指令中去掉 `# Personality` 一节。 | enum：none / friendly / pragmatic | 未提供 | 未标注 / 弃用提示 / 未限定平台 | 未单独实测 | 保留已有值；核实替代写法后提供显式迁移。 |
| `plan_mode_reasoning_effort` | 计划模式推理强度：终端界面（TUI）中计划（Plan）模式专用的推理强度。未设置时使用 Plan 预设的默认值（目前为 `medium`）；设置后（包括 `none`）覆盖该预设，其中 `none` 表示不推理，而不是沿用全局设置。 | string；min_length=1 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按模型能力与任务选择；档位应显示实际值。 |
| `review_model` | 代码审查模型：`/review` 代码审查使用的模型。未设置时使用当前会话正在用的模型。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按模型能力与任务选择；档位应显示实际值。 |
| `service_tier` | 服务档位：新轮次请求使用的服务档位（Responses API `service_tier`）：`default` 表示标准路由，`priority`（旧写法 `fast`）为快速档，`flex` 为弹性档，`ultrafast` 为超快档，其他值原样发送。`priority`/`fast` 需开启 `features.fast_mode`，`ultrafast` 需开启 `features.ultrafast_mode`（两者默认开启），否则该设置被忽略。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 默认/fast 与允许 Fast 分开；显示实际请求档位及服务支持。 |

## 供应商与认证（61 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `chatgpt_base_url` | ChatGPT 后端地址：ChatGPT 后端接口（而非 OpenAI API）的基础地址，默认 `https://chatgpt.com/backend-api/`。账号额度与用量、插件和应用连接器、云任务、使用统计等请求发往这里，模型推理请求则走 `openai_base_url` 或模型供应商的 `base_url`；项目内的 `.codex/config.toml` 不能设置此项。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `cli_auth_credentials_store` | 登录凭据存储方式：CLI 登录凭据（API key、ChatGPT 令牌等）的存放位置：`file`（默认，`$CODEX_HOME/auth.json`）、`keyring`（系统钥匙串）、`auto`（有钥匙串就用钥匙串，否则用文件）或 `ephemeral`（只留在当前进程内存中）。 | enum：file / keyring / auto / ephemeral | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 混合模型路由当前要求 file；勿为通用建议改成 keyring/auto。 |
| `forced_chatgpt_workspace_id` | 限定 ChatGPT 工作区：设置后只允许登录指定的 ChatGPT 工作区，可写单个 ID 或 ID 数组（不支持逗号分隔的字符串）。当前 ChatGPT 类凭据（含个人访问令牌 PAT）属于其他工作区时 Codex 会自动登出；单独设置此项时 API key 登录不受影响。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `forced_login_method` | 限定登录方式：限制可用的登录方式：`chatgpt` 只允许 ChatGPT 账号类登录（含个人访问令牌 PAT），`api` 只允许 API key。若当前凭据与要求不符，Codex 会直接登出。 | enum：chatgpt / api | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_provider` | 当前模型供应商：使用 `model_providers` 中的哪个模型供应商 ID，默认 `openai`。内置 ID 有 `openai`、`ollama`、`lmstudio`、`amazon-bedrock` 和 `amazon-bedrock-runtime`，其他 ID 需在 `[model_providers.<ID>]` 中自定义；项目内的 `.codex/config.toml` 不能设置此项。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers` | 自定义模型供应商：用户自定义的模型供应商表，用来扩展内置列表。与内置 ID 同名的条目会被忽略（只有 `amazon-bedrock` 两个内置供应商允许改 `base_url`、`auth`、`http_headers` 和 `aws`）；项目内的 `.codex/config.toml` 不能设置此项。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `model_providers.<name>` | 单个模型供应商：一个自定义模型供应商，表名就是供 `model_provider` 引用的 ID，例如 `[model_providers.mysite]`。通常需要设置 `base_url` 和一种凭据来源（`env_key`、`experimental_bearer_token`、`auth` 等）。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `model_providers.<name>.auth` | 命令获取令牌：通过运行本地命令获取 Bearer 令牌：命令标准输出去掉首尾空白后作为 `Authorization: Bearer` 的值，并缓存复用。不能与 `env_key`、`experimental_bearer_token`、`requires_openai_auth` 同时使用。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `model_providers.<name>.auth.args` | 令牌命令参数：传给令牌命令的参数列表（字符串数组），直接传给程序，不经过 shell 解析。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.auth.command` | 令牌命令：要执行的程序，必填且不能为空。只写程序名时从 `PATH` 中查找，写路径时相对 `cwd` 解析；不经过 shell 执行。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.auth.cwd` | 令牌命令工作目录：运行令牌命令时的工作目录，写成相对路径的 `command` 也据此解析。默认是 `.`，按配置文件所在目录解析（依据源码推断）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.auth.refresh_interval_ms` | 令牌刷新间隔：缓存令牌的最长使用时间（毫秒），到期后重新运行命令，默认 300000（5 分钟）。设为 `0` 表示不主动刷新，只在请求返回 401 后重新获取。 | integer；minimum=0.0 | 300000 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.auth.timeout_ms` | 令牌命令超时：等待令牌命令成功退出的最长时间（毫秒），默认 5000；超时即视为获取令牌失败。 | integer；minimum=1.0 | 5000 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.aws` | AWS SigV4 认证：用 AWS SigV4 签名来认证发往该模型供应商的请求，主要用于内置的 `amazon-bedrock` 供应商。设置后不能再使用 `env_key`、`experimental_bearer_token`、`auth`、`requires_openai_auth`，也不能开启 `supports_websockets`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `model_providers.<name>.aws.auth_refresh` | AWS 重新认证命令：AWS 认证失败且可以刷新（如 SSO 会话过期）时运行的重新登录命令，例如 `aws sso login`。`command` 只能是 `aws`，在终端中运行时可交互输入。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `model_providers.<name>.aws.auth_refresh.args` | 重新认证参数：传给 `aws` 的参数列表，例如 `["sso", "login"]`。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.aws.auth_refresh.command` | 重新认证程序：直接执行的程序（不经过 shell），目前只允许填 `aws`。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.aws.auth_refresh.timeout_ms` | 重新认证超时：等待重新认证命令完成的最长时间（毫秒），默认 300000（5 分钟）。 | integer；minimum=1.0 | 300000 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.aws.credential_export` | AWS 凭据导出命令：运行一条命令获取 AWS 临时凭据，取代 AWS SDK 默认的凭据链。命令需在标准输出打印 JSON（`{"Credentials": {…}}` 或 `credential_process` 格式），凭据会在过期前约 5 分钟重新获取；不能与 `aws.profile` 同时设置。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `model_providers.<name>.aws.credential_export.args` | 导出命令参数：传给凭据导出命令的参数列表，直接传给程序，不经过 shell。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.aws.credential_export.command` | 导出命令程序：直接执行的程序（不经过 shell），必须是绝对路径或不带目录的程序名。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.aws.credential_export.timeout_ms` | 导出命令超时：等待凭据导出命令完成的最长时间（毫秒），默认 30000（30 秒）。 | integer；minimum=1.0 | 30000 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.aws.profile` | AWS 配置档：要使用的 AWS profile 名称（AWS CLI 配置中的配置档）；不设置时由 AWS SDK 的默认凭据链决定。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.aws.region` | AWS 区域：用于确定供应商区域端点（如 Bedrock 的区域地址）的 AWS 区域，例如 `us-east-1`。不设置时按 AWS 的常规来源（如 `AWS_REGION`、`AWS_DEFAULT_REGION` 环境变量）确定。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.base_url` | API 基础地址：该模型供应商 OpenAI 兼容 API 的基础地址，例如 `https://example.com/v1`；Codex 会请求 `<base_url>/responses`、`<base_url>/models` 等路径。不设置时回落到 OpenAI 官方地址（API key 登录为 `https://api.openai.com/v1`）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.capabilities` | 供应商能力覆盖：为自定义的 Responses 兼容模型供应商声明可选的 API 能力：联网搜索能否访问实时网络、远程压缩协议。没写的能力保持该供应商原有的默认值，模型和轮次设置还可能进一步收紧。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `model_providers.<name>.capabilities.external_web_access` | 搜索访问实时网络：托管的联网搜索能否访问实时网络，默认 `true`。设为 `false` 时 `live`、`indexed` 联网搜索模式不可用，Codex 会退回到 `cached`（只用缓存结果）。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.capabilities.remote_compaction` | 远程压缩协议：自动压缩上下文时使用的远程压缩协议。不写时保持默认：OpenAI 和 Azure Responses 供应商为 `v2`，其他自定义模型供应商为 `unsupported`（本地压缩）。 | enum：unsupported / v2 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 由供应商能力控制；验证请求 trigger 与响应 compaction，再标记成功。 |
| `model_providers.<name>.env_http_headers` | 环境变量请求头：从环境变量取值的额外请求头：键是请求头名称，值是环境变量的名字（不是值本身）。变量未设置或为空时不发送该请求头；内置 `openai` 供应商就用它发送 `OpenAI-Organization` 和 `OpenAI-Project`。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `model_providers.<name>.env_http_headers.<name>` | 单个环境变量请求头：一条映射：键为请求头名称，值为提供该请求头内容的环境变量名。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.env_key` | API key 环境变量：存放该模型供应商 API key 的环境变量名，例如 `MY_SITE_API_KEY`。Codex 从该变量读取 key 作为 Bearer 令牌；变量不存在或为空时请求会报错，并显示 `env_key_instructions` 中的说明。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.env_key_instructions` | 获取 key 的说明：`env_key` 指定的环境变量缺失时显示给用户的提示文本，用来说明如何获取并设置这个 key。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.experimental_bearer_token` | 明文 Bearer 令牌：直接写在配置文件里、用于 `Authorization: Bearer <token>` 请求头的令牌。出于安全考虑官方建议改用 `env_key`，此项主要用于程序化生成配置的场景。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.gateway_oauth` | 网关 OAuth 凭据：模型供应商前置网关要求的第二套 OAuth 凭据，与主认证一起发送。Codex 通过浏览器授权码流程（PKCE）登录并自动刷新令牌，再按 `delivery` 以请求头或 Cookie 附加；必须设置 `base_url`，不能与 AWS 认证同时使用。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `model_providers.<name>.gateway_oauth.authorization_url` | 网关授权地址：OAuth 授权端点，用于在浏览器中发起登录。必须是 HTTPS（本机回环地址可用 HTTP），且不能包含用户名、密码或片段（`#`）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.gateway_oauth.client_id` | 网关客户端 ID：在网关的 OAuth 服务中注册的客户端 ID，不能为空。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.gateway_oauth.delivery` | 网关令牌附加方式：网关令牌如何附加到请求上：`kind = "header"` 时放进指定的请求头，`kind = "cookie"` 时放进指定名称的 Cookie。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.gateway_oauth.delivery.kind` | 附加位置：`header`（请求头）或 `cookie`（Cookie）。 | enum：header / cookie | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.gateway_oauth.delivery.name` | 请求头或 Cookie 名：承载网关令牌的请求头名称或 Cookie 名称。不能使用 `Authorization`、`Cookie`、`Host` 等保留请求头或 `x-codex-`、`x-openai-` 前缀，也不能与 `http_headers`、`env_http_headers` 中已有的请求头重名。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.gateway_oauth.delivery.scheme` | 认证方案前缀：仅 `kind = "header"` 时使用：写在令牌前面的认证方案，默认 `Bearer`，即发送 `Bearer <token>`。 | string | "Bearer" | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.gateway_oauth.redirect_port` | 登录回调端口：登录回调使用的本机端口，Codex 在 `127.0.0.1` 上监听它接收授权结果；不设置时随机选择空闲端口，不能设为 `0`。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.gateway_oauth.resource` | 资源标识：可选的 OAuth `resource` 参数（资源指示符），在授权、换取令牌和刷新令牌时一并发送。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.gateway_oauth.scopes` | 授权范围：申请的 OAuth scope 列表，发送时以空格连接。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.gateway_oauth.token_url` | 网关令牌地址：OAuth 令牌端点，用于以授权码换取令牌和刷新令牌。要求与 `authorization_url` 相同：HTTPS（本机回环地址可用 HTTP）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.http_headers` | 固定请求头：发往该模型供应商的每个请求都附带的固定请求头，键是名称，值是内容。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `model_providers.<name>.http_headers.<name>` | 单个固定请求头：一条请求头：键为请求头名称，值为发送的内容。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.model_catalog_url` | 模型目录地址：Codex 原生格式模型目录的完整 URL。不设置时模型列表从 `<base_url>/models` 获取（OpenAI 内置供应商未改 `base_url` 时走 Codex 后端）；设置后改从此地址获取，且不跟随重定向。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.name` | 显示名称：该模型供应商在界面中显示的友好名称。注意：名称恰好是 `OpenAI` 时，Codex 会把它当作 OpenAI 供应商对待（依据源码）。 | string | "" | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.query_params` | URL 查询参数：追加到基础地址后的 URL 查询参数（键值对），对发往该模型供应商的每个请求生效。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `model_providers.<name>.query_params.<name>` | 单个查询参数：一条查询参数：键为参数名，值为参数值。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.request_max_retries` | 请求最大重试次数：发往该模型供应商的 HTTP 请求失败后的最大重试次数，默认 4，上限 100。只重试 5xx 和网络传输错误，不重试 429。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.requires_openai_auth` | 需要 OpenAI 登录：该模型供应商是否使用 OpenAI 的登录凭据（API key 或 ChatGPT 令牌）。为 `true` 时首次运行会显示登录界面，凭据按 `cli_auth_credentials_store` 保存；为 `false`（默认）时跳过登录，key 从 `env_key` 指定的环境变量读取。不能与 `auth`、`aws` 同时使用。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.stream_idle_timeout_ms` | 流式空闲超时：流式响应多久（毫秒）没有任何数据就视为连接已断开，默认 300000（5 分钟）。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.stream_max_retries` | 流式重连次数：流式响应中途断开后重新连接的最大次数，用完后本次请求失败；默认 5，上限 100。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.supports_standalone_web_search` | 支持独立搜索接口：该模型供应商是否提供独立的联网搜索接口（`POST <base_url>/alpha/search`）。自定义模型供应商默认 `false`，OpenAI 内置供应商默认支持。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.supports_websockets` | 支持 WebSocket 传输：该模型供应商是否支持通过 WebSocket 传输 Responses API。为 `true` 时 Codex 可以经 WebSocket 发送请求；自定义模型供应商默认 `false`，OpenAI 内置供应商默认支持。不能与 `aws` 同时使用。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.websocket_connect_timeout_ms` | WebSocket 连接超时：建立 WebSocket 连接的最长等待时间（毫秒），超时视为连接失败，默认 15000（15 秒）。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `model_providers.<name>.wire_api` | 接口协议：该模型供应商使用的接口协议。目前只支持 `responses`（默认）；旧的 `chat`（Chat Completions）已移除，配置后会报错。 | enum：responses | "responses" | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `openai_base_url` | OpenAI 接口地址：覆盖内置 `openai` 模型供应商的基础地址，`model_provider` 仍保持 `openai`。不设置时按登录方式选择：ChatGPT 账号类登录（含 PAT）用 `https://chatgpt.com/backend-api/codex`，API key 用 `https://api.openai.com/v1`；项目内的 `.codex/config.toml` 不能设置此项。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `profile` | 默认配置档（旧版）：旧版写法：在 `config.toml` 中用 `profile = "名称"` 选择 `profiles` 里的配置档。Codex 已不再支持，写了会报错，并提示改用 `--profile 名称` 加 `$CODEX_HOME/名称.config.toml`。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持认证与路由配套；改动前检查依赖。 |
| `profiles` | 配置档表（旧版）：旧版写法：在 `[profiles.名称]` 下定义一组可切换的设置。当前 Codex 仍能解析该表但不再应用其中的设置；新写法是把差异项放进 `$CODEX_HOME/名称.config.toml`，再用 `--profile 名称` 启动（同名旧表仍存在时会报错）。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 旧表仅作历史说明；新 profile 使用独立 .config.toml 文件。 |

## 上下文与指令（46 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `compact_prompt` | 压缩提示词：压缩上下文（生成历史摘要）时发给模型的提示词，替换内置的摘要提示词，优先于 `experimental_compact_prompt_file`。只在本地压缩时使用：内置 `openai` 供应商和 Azure Responses 默认走服务端远程压缩，此时不会用到它。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `developer_instructions` | 开发者指令：以 `developer` 角色消息加入上下文的额外指令。与 `instructions` 不同，它不会替换模型的内置指令。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `experimental_compact_prompt_file` | 压缩提示词文件（实验性）：实验性：从该文件读取压缩提示词，文件无法读取或内容为空时配置加载会报错。已设置 `compact_prompt` 时以后者为准；同样只在本地压缩时使用。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `features.context_management` | 实验性上下文管理：启用实验性上下文管理：对支持的模型自动开启 `token_budget`，并提供历史与笔记扩展，让模型在上下文窗口轮换前记录笔记。只对使用 ChatGPT 登录、Plus 或 Pro 套餐且走官方 Codex 后端的会话生效，API key 或自定义认证无效；以 TOML 表的形式开启时写作 `[features.context_management]` 中的 `experimental_mode = true`。开发中，默认关闭。 | bool_or_table | false | 开发中 / 未限定平台 | 开发中 / false | 能被内核识别的开发中开关；不等价于远程压缩，建议单独实验验证。 |
| `features.context_management.experimental_mode` | 实验性上下文管理：开发中：开启实验性上下文管理（`context_management` 功能）。仅在 ChatGPT 登录且为 Plus 或 Pro 订阅、使用内置 OpenAI 供应商和支持该能力的模型时生效，生效后会自动启用 `token_budget` 及其历史与笔记扩展。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.memories` | 记忆：启用记忆：每次启动根会话时，后台调用模型从以往会话中提取记忆，并整理成 Codex 主目录下的记忆文件供之后的会话使用，这会产生额外的模型请求和用量。还需要 `[memories].use_memories` 为 true（默认即是），可用 `/memories` 配置。默认关闭；旧键名 `memory_tool` 仍被接受。 | boolean | false | 稳定 / 未限定平台 | 稳定 / false | 通常跟随模型默认；压缩收益需运行证据。 |
| `features.token_budget` | 上下文用量提示：把当前上下文窗口的用量信息加入模型可见的上下文，并在接近自动压缩时提醒模型收尾。可在 `[features.token_budget]` 中设置提醒阈值、提醒模板和附加指引等。开发中，默认关闭。 | bool_or_table | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.token_budget.auto_compact_fallback_buffer_tokens` | 兜底缓冲 token 数：在自动压缩阈值之后额外预留的 token 数，供模型在压缩前按兜底提示词记笔记。只有设置了 `auto_compact_fallback_prompt` 时才生效，且此时必须填写正整数。 | integer；minimum=1.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.token_budget.auto_compact_fallback_prompt` | 压缩前兜底提示词：上下文窗口即将自动轮换（压缩）前插入的一条开发者消息，让模型先整理要保留的信息。最长 2000 字节，需同时设置 `auto_compact_fallback_buffer_tokens`。 | string；max_length=2000 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.token_budget.enabled` | 启用上下文预算：开发中：开启 `token_budget` 功能，把当前上下文窗口的用量信息加入模型可见的上下文。本节其他设置只在开启后生效；部分模型的目录信息会自带默认值。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 能加载为启用；尚无本线路成本/效果证据，不作为默认优化承诺。 |
| `features.token_budget.guidance_message` | 上下文用量说明：附加在上下文窗口用量信息之后的说明文字（以开发者消息发送），用来指导模型如何对待剩余预算。最长 2000 字节，留空表示不附加。 | string；max_length=2000 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.token_budget.reminder_message_template` | 收尾提醒模板：收尾提醒的文字模板，其中 `{n_remaining}` 会替换为距离自动压缩还剩的 token 数。不能为空，最长 2000 字节；未设置时使用内置模板。 | string；min_length=1，max_length=2000 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.token_budget.reminder_threshold_tokens` | 收尾提醒阈值：距离自动压缩还剩多少 token 时向模型发出一次收尾提醒（正整数）。未设置时不发提醒，除非模型目录提供了默认值。 | integer；minimum=1.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.token_budget.use_history_notes_extension` | 历史与笔记扩展：是否向模型提供内置的历史与笔记扩展：可浏览、搜索压缩前的历史窗口，并读写笔记文件。仅在使用 OpenAI 供应商并通过 Codex 后端认证（ChatGPT 登录）时可用；开启实验性上下文管理时会被自动打开。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 历史/开发中能力，需专门验证，不等价于远程压缩。 |
| `goals` | 目标设置：`[goals]` 表：会话目标相关设置。目标功能（`features.goals`，默认开启）可以为会话保存目标并自动继续推进。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `goals.max_goal_token_budget` | 目标 token 预算上限：单个目标允许的最大 token 预算，同时作为新建目标的默认预算（正整数）；超过上限的预算会被拒绝。未设置时不限制。 | integer；minimum=1.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `include_apps_instructions` | 注入应用指令块：是否在开发者消息中加入 `<apps_instructions>` 块，向模型说明可用的应用连接器（Apps）。默认 `true`；只有启用了 Apps 且存在可用连接器时才会加入。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `include_collaboration_mode_instructions` | 注入协作模式指令块：是否在开发者消息中加入 `<collaboration_mode>` 块，告诉模型当前的协作模式（如 Plan 计划模式或默认模式）及其要求。默认 `true`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `include_environment_context` | 注入环境信息块：是否以用户消息加入 `<environment_context>` 块，向模型提供工作目录、Shell、日期等运行环境信息。默认 `true`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `include_environment_context_time` | 环境信息含日期：环境信息块中是否包含当前日期和时区。默认 `true`；关闭后模型无法从该块得知今天的日期。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `include_permissions_instructions` | 注入权限说明块：是否在开发者消息中加入 `<permissions instructions>` 块，向模型说明当前的沙箱权限、审批策略和命令执行规则。默认 `true`；关闭后只保留精简的命令规则信息。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `instructions` | 系统指令：替换模型内置的基础系统指令（system prompt）。`model_instructions_file` 指定的文件优先于此值；偏离内置指令通常会降低模型表现。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories` | 记忆设置：`[memories]` 表：记忆功能的设置。需先开启 `[features].memories`（默认关闭）；开启后 Codex 会在启动时从过往会话中提炼记忆，并在新会话中提供给模型。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `memories.consolidation_model` | 记忆整合模型：第二阶段把零散记忆整合为全局记忆时使用的模型。未设置时使用当前模型供应商推荐的模型。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.dedicated_tools` | 专用记忆工具：设为 `true` 时，通过扩展工具向模型提供专用的记忆工具。默认 `false`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.disable_on_external_context` | 外部内容不生成记忆：设为 `true` 时，会话一旦用到外部上下文来源（如 MCP 工具、联网搜索、工具搜索），就被标记为 `memory_mode = polluted`，不再用于生成记忆。默认 `false`；旧名称 `no_memories_if_mcp_or_web_search` 仍可用。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.dual_write` | 双版本同时生成：`memories.version` 为 `v1` 时同时运行 `v1` 和 `v2` 两套记忆生成流程，为切换到 `v2` 预热，但只有 `v1` 提供给模型；切换到 `v2` 后只运行 `v2`。默认 `false`；显式设为 `false` 时，新配置不会自动选用 `v2`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.extract_model` | 记忆提炼模型：第一阶段从单个会话中提炼记忆（会话摘要）时使用的模型。未设置时使用当前模型供应商推荐的模型。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.generate_memories` | 从新会话生成记忆：设为 `false` 时，新建会话在状态数据库中记为 `memory_mode = disabled`，不会被用来生成记忆；已有记忆仍可使用。默认 `true`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.max_raw_memories_for_consolidation` | 整合用原始记忆上限：全局整合时最多保留的近期原始记忆条数，范围 1–4096，默认 256。 | integer；minimum=1.0，maximum=4096.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.max_rollout_age_days` | 参与记忆的会话天数：只有最近多少天内的会话会被用来生成记忆，范围 0–90，默认 10 天。 | integer | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.max_rollouts_per_startup` | 每次处理的会话数：每轮最多处理多少个候选会话记录（rollout 文件），范围 1–128，默认 2。 | integer；minimum=1.0，maximum=128.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.max_unused_days` | 记忆闲置天数上限：一条记忆最近一次被使用后超过多少天，就不再参与第二阶段（整合）的筛选，范围 0–365，默认 30 天。 | integer | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.min_rate_limit_remaining_percent` | 运行所需剩余额度：只有 Codex 用量限额窗口的剩余百分比不低于该值时，启动时才会运行记忆流程，范围 0–100，默认 25。 | integer；minimum=0.0，maximum=100.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.min_rollout_idle_hours` | 会话最短闲置时长：会话最后一次活动后至少闲置多少小时才会被用来生成记忆，范围 1–48，默认 6；官方建议大于 12 小时。 | integer | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.use_memories` | 在会话中使用记忆：设为 `false` 时不在开发者提示词中注入记忆使用说明，模型不会使用已有记忆；记忆仍会照常生成。默认 `true`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `memories.version` | 记忆流程版本：选择记忆流程版本 `v1` 或 `v2`，两者分别存放在 Codex 主目录下的 `memories` 和 `memories_v2` 目录。未设置时，启用记忆且主目录下还没有 `memories` 目录的新配置默认用 `v2`（显式设置 `dual_write = false` 时除外），已有 `v1` 记忆的配置默认用 `v1`。 | enum：v1 / v2 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `model_auto_compact_token_limit` | 自动压缩阈值：上下文用量达到该 token 数时自动压缩会话历史。未设置时为模型上下文窗口的 90%；按 `total` 方式统计时，设置值也不会超过这个 90% 上限。 | integer | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 当前未覆盖时保留模型默认；测量后再调整。 |
| `model_auto_compact_token_limit_scope` | 自动压缩计数范围：决定自动压缩阈值按哪部分 token 计算：`total`（默认）统计全部活动上下文，`body_after_prefix` 只统计当前压缩窗口中沿用前缀之后新增的部分。无论哪种方式，用量达到模型完整上下文窗口时都会强制压缩。 | enum：total / body_after_prefix | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `model_context_window` | 上下文窗口大小：模型的上下文窗口大小（token 数），覆盖模型目录中的值；若目录声明了最大窗口，则不会超过该上限。Codex 实际可用的通常是窗口的 95%，自动压缩阈值默认按窗口的 90% 计算。 | integer | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保持模型目录值；不通过手填更大数字宣称扩大真实容量。 |
| `model_instructions_file` | 模型指令文件：指定一个文件，用其内容替换所选模型的内置指令，优先于 `instructions`；文件无法读取或为空时配置加载会报错。官方强烈不建议使用：偏离 Codex 认可的指令很可能降低模型表现。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `model_post_turn_compact_threshold_percent` | 轮次结束压缩阈值：一轮最终回复结束后，若上下文用量达到可用上下文窗口的该百分比，就在轮次结束时压缩。原有的自动压缩阈值仍然有效；未设置或为 0 时关闭，取值 0–100。 | integer；minimum=0.0，maximum=100.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `project_doc_fallback_filenames` | 备用项目说明文件名：目录中没有 `AGENTS.override.md` 或 `AGENTS.md` 时，按顺序尝试的备用文件名。只能写文件名，不能包含路径；默认为空列表。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `project_doc_max_bytes` | 项目说明最大字节数：所有 `AGENTS.md` 等项目说明合计最多读入的字节数，默认 32768（32 KiB），超出部分会被直接截断；设为 0 则不读入项目说明。 | integer；minimum=0.0 | 32768 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `project_root_markers` | 项目根目录标记：向上查找父目录时用来识别项目根目录的标记，未设置时为 `.git`。项目根决定从哪一级开始读取 `.codex` 配置目录和 `AGENTS.md`；设为空列表则不再向上查找。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |
| `tool_output_token_limit` | 工具输出 token 上限：工具或函数调用的输出写入上下文时的截断预算（token 数），覆盖模型目录中的默认截断设置；对按字节截断的模型会换算成大致等量的字节数。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 通常跟随模型默认；压缩收益需运行证据。 |

## 审批与沙箱（37 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `allow_login_shell` | 允许登录 Shell：是否允许 shell 类工具以登录 shell 方式运行命令（会加载 `~/.bash_profile`、`~/.zprofile` 等），默认 `true`，模型未指定时也默认使用登录 shell。设为 `false` 时，模型的 `login = true` 请求会被拒绝，未指定时改用非登录 shell。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `approval_policy` | 审批策略：执行命令等操作何时需要用户审批：`on-request`（由模型决定何时请求，旧值 `on-failure` 同义）、`never`（从不请求），或用 `granular` 表按类别控制。未设置时默认 `on-request`，被标记为不受信任的项目改用内部的严格策略；显式写 `untrusted` 已不再支持，会导致配置报错。 | enum：on-request / never | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 支持枚举及 granular 表单；与沙箱、reviewer 一起检查。 |
| `approval_policy.granular` | 按类别审批：按类别控制审批提示的写法（`approval_policy = { granular = { ... } }`）。某项为 `true` 时该类请求照常交给用户审批，为 `false` 时直接自动拒绝、不打扰用户；其中 `sandbox_approval`、`rules`、`mcp_elicitations` 三项必须填写。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `approval_policy.granular.mcp_elicitations` | MCP 信息请求：是否允许 MCP 服务器向用户发起信息请求（elicitation）提示；`false` 时这类请求会被自动拒绝。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `approval_policy.granular.request_permissions` | 申请权限提示：是否允许由 `request_permissions` 工具触发的权限申请提示，默认 `false`（自动拒绝）。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `approval_policy.granular.rules` | 规则触发的审批：是否允许由执行策略（execpolicy）中 `prompt` 规则触发的审批提示；`false` 时这类请求会被自动拒绝。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `approval_policy.granular.sandbox_approval` | Shell 命令审批：是否允许 shell 命令的审批请求，包括在命令中直接申请 `with_additional_permissions` 或 `require_escalated` 的提权请求；`false` 时这些请求会被自动拒绝。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `approval_policy.granular.skill_approval` | 技能脚本审批：是否允许运行技能脚本时触发的审批提示，默认 `false`（自动拒绝）。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `approvals_reviewer` | 审批审核者：已升级的审批请求（如越出沙箱、被拦截的网络访问、MCP 审批提示）交给谁审核：`user`（默认，由用户决定）或 `auto_review`（由专门的自动审核子代理收集上下文、按风险批准或拒绝）。旧值 `guardian_subagent` 等同于 `auto_review`；ARC 等独立安全检查不受此项影响。 | enum：user / auto_review / guardian_subagent | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `auto_review` | 自动审核策略：审批交给 `auto_review` 时，自动审核子代理（Guardian）的策略设置：可以追加或替换审核策略文本、调整读取会话历史的预算和连续拒绝时的熔断方式。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `auto_review.circuit_break_action` | 熔断方式：同一轮次中自动审核拒绝过多（连续 3 次，或最近 50 次中达到 10 次）而中断该轮次时，是否附带结构化错误：`default`（默认，只发警告并中断）或 `strict`（另附结构化错误）。严格模式写入的错误可能无法被读取共享历史的旧客户端识别。 | enum：default / strict | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `auto_review.conversation_history_max_output_tokens` | 历史工具输出上限：自动审核读取会话历史的工具每次返回的最大估算 token 数（不含标准序列化余量），默认 4,000；上级工具更严格的限制仍然生效。 | integer；minimum=1.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `auto_review.experimental_conversation_history_prompt` | 历史检索提示词（实验）：实验性：启用历史工具和应用（Apps）时，用来替换自动审核内置的历史检索说明；留空或不设置则使用内置提示词。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `auto_review.experimental_policy_template` | 审核模板（实验）：实验性：完整替换自动审核的提示词模板。模板中应包含 `{{ tenant_policy_config }}` 占位符，运行时会替换为生效的审核策略。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `auto_review.extra_policy` | 附加审核策略：插入到自动审核模板 `{{ extra_policy }}` 位置的附加策略文本，与基础策略一起生效；管理员 requirements 中的同类设置优先。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `auto_review.policy` | 审核策略：写入自动审核提示词策略配置部分的自定义策略，设置后取代模型自带的默认策略；管理员 requirements 中的同类设置优先。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `default_permissions` | 默认权限配置档：默认使用的权限配置档名称。以 `:` 开头的是内置配置档（`:read-only`、`:workspace`、`:danger-full-access`），其他名称从 `[permissions]` 表中查找。在同一配置层中它优先于 `sandbox_mode`。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 与 sandbox_mode/sandbox_workspace_write 做组合校验；不同时盲写。 |
| `permissions` | 权限配置档：自定义的命名权限配置档，以 `[permissions.<名称>]` 定义，可设置 `description`、`extends`（继承另一个配置档）、`workspace_roots`、`filesystem` 和 `network`，由 `default_permissions` 选用。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `sandbox_mode` | 沙箱模式：模型执行命令时的沙箱级别：`read-only`（只读）、`workspace-write`（可写工作区）或 `danger-full-access`（不使用沙箱）。未设置时默认只读，已对当前目录做过信任选择时默认 `workspace-write`；Windows 未启用沙箱时 `workspace-write` 会降为只读。 | enum：read-only / workspace-write / danger-full-access | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `sandbox_workspace_write` | 工作区可写设置：沙箱模式为 `workspace-write` 时的补充设置：额外可写目录、是否允许联网，以及是否把 `$TMPDIR` 和 `/tmp` 排除在可写范围之外。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `sandbox_workspace_write.exclude_slash_tmp` | 排除 /tmp：设为 `true` 时 `/tmp` 不再默认可写；默认 `false`。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `sandbox_workspace_write.exclude_tmpdir_env_var` | 排除 $TMPDIR：设为 `true` 时环境变量 `$TMPDIR` 指向的目录不再默认可写；默认 `false`。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `sandbox_workspace_write.network_access` | 允许联网：是否允许 `workspace-write` 沙箱中的命令访问网络，默认 `false`（禁止联网）。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `sandbox_workspace_write.writable_roots` | 额外可写目录：除当前工作区外额外允许写入的目录列表，须为绝对路径。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `shell_environment_policy` | Shell 环境变量策略：决定 shell 等工具启动子进程时传入哪些环境变量：先按 `inherit` 取初始变量，再按需移除名字含 `KEY`、`SECRET`、`TOKEN` 的变量，然后依次应用 `exclude`、`set` 和 `include_only`（或新写法 `filters`）。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `shell_environment_policy.exclude` | 排除变量（旧）：旧写法：要从环境中移除的变量名模式列表。模式实际按通配符匹配（支持 `*` 和 `?`，不区分大小写），不是正则表达式；不能与 `filters` 同时使用。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `shell_environment_policy.experimental_use_profile` | 使用 Shell 配置文件（实验）：实验性：是否通过 shell 的 profile 来运行命令，默认 `false`。依据源码，Codex 0.163 中该值会被解析，但未见实际使用之处。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `shell_environment_policy.filters` | 变量筛选规则：新写法的筛选规则表，键为变量名模式（支持 `*`、`?` 通配，不区分大小写），值为 `include` 或 `exclude`。不能与旧的 `exclude`、`include_only` 混用；`include` 不会加回被 `exclude` 移除的变量。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `shell_environment_policy.filters.<name>` | 单条筛选规则：一条变量筛选规则：键为变量名模式，值为 `include` 时只保留匹配的变量，为 `exclude` 时移除匹配的变量。 | enum：include / exclude | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `shell_environment_policy.ignore_default_excludes` | 不排除敏感变量：是否跳过默认排除规则（移除名字含 `KEY`、`SECRET`、`TOKEN` 的变量），默认 `true`，即这些变量默认会传给命令；设为 `false` 可自动移除它们。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `shell_environment_policy.include_only` | 仅保留变量（旧）：旧写法：非空时只保留名称匹配这些模式的变量（`*`、`?` 通配，不区分大小写），在应用 `set` 之后执行；不能与 `filters` 同时使用。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `shell_environment_policy.inherit` | 继承范围：子进程环境变量的起点：`all`（默认，继承 Codex 进程的全部环境变量）、`core`（只继承 `HOME`、`PATH`、`USER` 等核心变量）或 `none`（不继承）。 | enum：core / all / none | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `shell_environment_policy.set` | 设置变量：强制写入子进程环境的变量表（键为变量名，值为字符串），在排除规则之后、`include_only` 之前应用。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `shell_environment_policy.set.<name>` | 单个变量：要设置的一个环境变量，键为变量名，值为字符串。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `windows` | Windows 设置：仅在 Windows 上生效的设置，用于选择沙箱实现（`sandbox`）以及是否允许 MXC 沙箱（`allow_mxc`）。 | table | 未提供 | 未标注 / windows | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `windows.allow_mxc` | 允许 MXC 沙箱：设为 `false` 时禁止使用 MXC 沙箱：显式设置 `windows.sandbox` 为 `mxc` 会报错，也不会自动选用 MXC（Codex 0.163 起）。 | boolean | 未提供 | 未标注 / windows | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |
| `windows.sandbox` | Windows 沙箱实现：Windows 上使用的沙箱实现：`elevated`（提升模式，设置时需要管理员权限）、`unelevated`（无需管理员的受限令牌模式）或 `mxc`（系统原生 MXC 沙箱，Codex 0.156 起）。未设置时参考旧的 `[features]` 开关，都没有时 Windows 上不启用沙箱。 | enum：elevated / unelevated / mxc | 未提供 | 未标注 / windows | 未单独实测 | 显示作用层级；校验与权限配置的组合。 |

## 工具（66 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `background_terminal_max_timeout` | 后台终端最长等待：模型用 `write_stdin` 以空输入轮询后台终端输出时，单次最多等待的毫秒数。默认 `300000`（5 分钟），小于 `5000` 时按 `5000` 处理；向终端写入内容的调用不受此值影响。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `browser_use` | 浏览器操作权限：桌面 app 浏览器操作（Browser Use）的用户级权限：是否允许读取浏览历史，以及按网站设置的访问、下载、上传和完整 CDP 权限。Codex 只读取并转交这些值，由桌面 app 执行；管理员下发的 requirements 可以进一步收紧。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `browser_use.allow_history_access` | 允许读取浏览历史：是否允许浏览器操作读取浏览器的历史记录。未设置时视为允许，设为 `false` 则禁止；桌面 app 的浏览器设置页会写入此项。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `browser_use.default_origin_policy` | 默认网站权限：未在 `browser_use.origins` 中单独设置的网站所用的默认权限，包含 `access`、`downloads`、`uploads`、`full_cdp_access` 四项，每项取 `allow` 或 `deny`；未设置的项视为允许。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `browser_use.default_origin_policy.access` | 默认允许访问网站：是否默认允许浏览器操作打开并操作网站，取 `allow` 或 `deny`，未设置视为允许。设为 `deny` 后，只有在 `browser_use.origins` 中明确允许的网站才能访问。 | enum：allow / deny | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `browser_use.default_origin_policy.downloads` | 默认允许下载：是否默认允许浏览器操作在网站上下载文件，取 `allow` 或 `deny`，未设置视为允许；可被 `browser_use.origins` 中的单站设置覆盖。 | enum：allow / deny | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `browser_use.default_origin_policy.full_cdp_access` | 默认完整 CDP 权限：是否默认允许浏览器操作使用完整的 Chrome DevTools Protocol（CDP）能力，取 `allow` 或 `deny`，未设置视为允许。功能开关 `browser_use_full_cdp_access` 关闭时始终不允许。 | enum：allow / deny | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `browser_use.default_origin_policy.uploads` | 默认允许上传：是否默认允许浏览器操作向网站上传文件，取 `allow` 或 `deny`，未设置视为允许；可被 `browser_use.origins` 中的单站设置覆盖。 | enum：allow / deny | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `browser_use.origins` | 按网站设置权限：按网站来源单独设置浏览器操作权限的表，键为来源模式（如 `https://example.com`），值的字段与 `default_origin_policy` 相同。匹配到的单站 `deny` 总是生效，单站 `allow` 可以覆盖默认的禁止。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `browser_use.origins.<name>` | 单个网站权限：某个网站来源（键名，如 `https://example.com`）的浏览器操作权限，可设置 `access`、`downloads`、`uploads`、`full_cdp_access`；未设置的字段沿用 `default_origin_policy`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `browser_use.origins.<name>.access` | 允许访问此网站：是否允许浏览器操作打开并操作该网站，取 `allow` 或 `deny`。`deny` 时即使默认允许也会拦截，`allow` 时即使默认禁止也会放行。 | enum：allow / deny | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `browser_use.origins.<name>.downloads` | 允许在此网站下载：是否允许浏览器操作在该网站下载文件，取 `allow` 或 `deny`；未设置时使用 `default_origin_policy.downloads`。 | enum：allow / deny | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `browser_use.origins.<name>.full_cdp_access` | 此网站完整 CDP 权限：是否允许浏览器操作在该网站使用完整的 Chrome DevTools Protocol（CDP）能力，取 `allow` 或 `deny`；未设置时使用 `default_origin_policy.full_cdp_access`。 | enum：allow / deny | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `browser_use.origins.<name>.uploads` | 允许向此网站上传：是否允许浏览器操作向该网站上传文件，取 `allow` 或 `deny`；未设置时使用 `default_origin_policy.uploads`。 | enum：allow / deny | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `computer_use` | 电脑操作权限：桌面 app 电脑操作（Computer Use）可以控制哪些本机应用的用户级设置，可按 macOS bundle ID 或 Windows 应用标识逐个允许或禁止。Codex 只读取并转交这些值，由桌面 app（macOS / Windows）执行。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `computer_use.default_app_access` | 默认应用访问权限：未单独列出的应用是否默认允许被电脑操作控制，取 `allow` 或 `deny`。未设置时由桌面 app 的默认行为决定。 | enum：allow / deny | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `computer_use.macos` | macOS 应用权限：电脑操作在 macOS 上的按应用权限设置，目前只有 `bundle_ids` 一项。 | table | 未提供 | 未标注 / macos | 未单独实测 | 本项目 Windows 客户端不适用；资料保留。 |
| `computer_use.macos.bundle_ids` | 按 bundle ID 设置：以 macOS 应用的 bundle ID（如 `com.apple.Safari`）为键、`allow` 或 `deny` 为值的表，用来单独允许或禁止电脑操作控制该应用。 | map | 未提供 | 未标注 / macos | 未单独实测 | 本项目 Windows 客户端不适用；资料保留。 |
| `computer_use.macos.bundle_ids.<name>` | 单个 macOS 应用：该 bundle ID 对应应用的电脑操作权限：`allow` 允许控制，`deny` 禁止控制。 | enum：allow / deny | 未提供 | 未标注 / macos | 未单独实测 | 本项目 Windows 客户端不适用；资料保留。 |
| `computer_use.windows` | Windows 应用权限：电脑操作在 Windows 上的按应用权限设置，可按应用用户模型 ID（`aumids`）或可执行文件（`exes`）指定。 | table | 未提供 | 未标注 / windows | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `computer_use.windows.aumids` | 按 AUMID 设置：以 Windows 应用用户模型 ID（AppUserModelID，常见于商店应用）为键、`allow` 或 `deny` 为值的表，用来单独允许或禁止电脑操作控制该应用。 | map | 未提供 | 未标注 / windows | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `computer_use.windows.aumids.<name>` | 单个 AUMID 应用：该 AppUserModelID 对应应用的电脑操作权限：`allow` 允许控制，`deny` 禁止控制。 | enum：allow / deny | 未提供 | 未标注 / windows | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `computer_use.windows.exes` | 按可执行文件设置：按可执行文件识别 Windows 应用的权限列表，每项包含 `publisher_name`（发布者）、`product_name`（产品名）、可选的 `binary_name`（文件名）和 `access`（`allow` 或 `deny`）。 | array | 未提供 | 未标注 / windows | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks` | 生命周期钩子：直接写在 `config.toml` 中的钩子：按事件名（如 `PreToolUse`、`Stop`）列出匹配组，在对应时机运行命令（`type` 为 `command`）或调用 MCP 工具（`type` 为 `mcp_tool`）。`hooks.state` 记录每个钩子的启用与信任状态，非托管钩子要经用户确认信任后才会运行。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `hooks.Interrupt` | 中断钩子：用户中断正在进行的轮次时运行的钩子，不支持 `matcher`。超时默认 1 秒、最多 3 秒，只用于通知或记录，不能改变中断结果。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.PermissionRequest` | 审批请求钩子：Codex 即将向用户请求审批时运行的钩子，`matcher` 按工具名匹配。钩子可以直接返回允许或拒绝，代替弹出审批提示。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.PostCompact` | 压缩后钩子：上下文压缩完成后运行的钩子，`matcher` 匹配触发方式 `manual`（手动压缩）或 `auto`（自动压缩）。钩子要求停止时，当前轮次会被中止。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.PostModelRequest` | 模型请求后钩子：每次模型请求结束后运行的钩子，不支持 `matcher`；输入包含结果状态（`completed`、`failed` 或 `cancelled`）、错误信息和 token 用量。只用于观察，输出仅记入日志，不能改变后续流程。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.PostToolUse` | 工具调用后钩子：工具执行完成后运行的钩子，`matcher` 按工具名做正则匹配（如 `apply_patch&#124;Edit`）。钩子可以向模型追加上下文，或返回阻止结果把反馈交给模型。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.PreCompact` | 压缩前钩子：即将压缩上下文时运行的钩子，`matcher` 匹配 `manual` 或 `auto`。钩子要求停止时不进行本次压缩，并中止当前轮次。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.PreModelRequest` | 模型请求前钩子：每次向模型发送请求前运行的钩子，不支持 `matcher`。只用于观察，输出仅记入日志，不能阻止或修改请求。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.PreToolUse` | 工具调用前钩子：工具执行前运行的钩子，`matcher` 按工具名做正则匹配。钩子可以阻止本次调用并给出原因、改写工具输入，或向模型追加上下文。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.SessionEnd` | 会话结束钩子：会话结束时运行的钩子，适合做清理或记录。超时默认 1 秒、最多 3 秒。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.SessionStart` | 会话开始钩子：会话开始时运行的钩子，`matcher` 匹配来源：`startup`、`resume`、`clear`、`compact` 或 `fork`。钩子可以向模型追加初始上下文，或要求停止。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.Stop` | 轮次结束钩子：主代理即将结束一个轮次时运行的钩子，不支持 `matcher`。钩子可以阻止结束并给出继续工作的提示，让代理接着完成任务。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.SubagentStart` | 子代理启动钩子：子代理启动时运行的钩子，`matcher` 匹配子代理类型（角色名，如 `explorer`）。钩子可以向子代理追加上下文。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.SubagentStop` | 子代理结束钩子：子代理即将结束一个轮次时运行的钩子，`matcher` 匹配子代理类型。与 `Stop` 类似，钩子可以阻止其结束并要求继续。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.UserPromptSubmit` | 提交提示词钩子：用户提交提示词后、模型处理前运行的钩子，不支持 `matcher`。钩子可以拦截这条提示词，或向模型追加上下文。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.state` | 钩子状态：按钩子键记录每个钩子的启用与信任状态，键形如 `<来源文件路径>:pre_tool_use:0:0`（事件、匹配组序号、处理器序号）。只从用户级配置读取，通常由 TUI 或桌面 app 的钩子管理界面写入。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `hooks.state.<name>` | 单个钩子状态：某个钩子的状态记录，包含 `enabled` 和 `trusted_hash` 两项。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `hooks.state.<name>.enabled` | 启用此钩子：设为 `false` 可停用该钩子，未设置时视为启用。管理员托管的钩子和内置钩子不受此项影响。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.state.<name>.trusted_content_hash` | 已信任的插件内容哈希：用户批准插件钩子时记录的插件内容哈希，与只覆盖钩子定义的 `trusted_hash` 分开保存，以免只认识钩子定义的客户端覆盖它。每次运行插件钩子前都会重新计算插件内容的哈希，不一致时需要重新批准。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `hooks.state.<name>.trusted_hash` | 已信任的钩子哈希：用户确认信任该钩子时记录的定义哈希。只有与当前钩子定义的哈希一致时钩子才会运行；钩子内容改动后哈希不再匹配，需要重新确认。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `skills` | 技能设置：技能（skills）的用户级设置：是否加载内置技能、是否向模型注入可用技能清单及其 token 预算，以及按 SKILL.md 路径或名称启用、停用单个技能。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `skills.bundled` | 内置技能：Codex 自带的系统技能的设置，目前只有 `enabled` 一项。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `skills.bundled.enabled` | 启用内置技能：是否安装并加载 Codex 自带的系统技能，默认 `true`。设为 `false` 后不再加载系统技能，只保留用户、项目和插件提供的技能。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `skills.config` | 单个技能开关：按顺序排列的技能开关规则，每项用 `path`（SKILL.md 的绝对路径）或 `name` 选中技能，再用 `enabled` 启用或停用，后面的规则覆盖前面的。在界面中开关技能时由 Codex 写入。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `skills.include_instructions` | 注入技能清单：是否在每个轮次向模型注入可用技能清单及其使用说明，默认 `true`。设为 `false` 后不再注入。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `skills.max_context_tokens` | 技能清单 token 上限：可用技能清单最多占用的 token 数。未设置时取模型上下文窗口的 2%（窗口未知时按 8,000 字符计）；手动设置时最大按 10,000 计。超出预算时会缩短技能描述或省略部分技能。 | integer；minimum=1.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `tool_suggest` | 工具安装建议：工具建议功能的设置：模型可以在需要时建议用户安装尚未启用的应用连接器或插件。`discoverables` 增加可被建议的项目，`disabled_tools` 记录不再建议的项目；功能本身由功能开关 `tool_suggest` 控制（默认开启）。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tool_suggest.disabled_tools` | 不再建议的工具：不再建议安装的应用连接器或插件列表，每项包含 `type`（`connector` 或 `plugin`）和 `id`。用户在建议提示中选择不再提示时由 Codex 追加，各配置层的列表会合并。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `tool_suggest.discoverables` | 额外可建议的工具：额外允许模型建议安装的应用连接器或插件列表，每项包含 `type`（`connector` 或 `plugin`）和 `id`；同时出现在 `disabled_tools` 中的项目不会被建议。 | array | [] | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `tools` | 工具设置：内置工具的细项设置：联网搜索参数（`tools.web_search`）、向用户提问工具 `request_user_input` 和计划工具 `update_plan` 的开关。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tools.experimental_request_user_input` | 向用户提问工具：`request_user_input` 工具的设置。该工具让模型在执行中向用户提出带选项的问题并等待回答，主要在计划（Plan）模式下可用。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tools.experimental_request_user_input.enabled` | 启用提问工具：是否向模型提供 `request_user_input` 提问工具，默认 `true`。设为 `false` 后模型无法在轮次中途向用户提问。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `tools.update_plan` | 计划工具：`update_plan` 工具的设置。该工具让模型维护一份分步骤的任务计划（待办清单），并在界面中显示进度。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tools.update_plan.enabled` | 启用计划工具：是否向模型提供 `update_plan` 计划工具，默认 `false`。未启用时，Codex 还会从模型指令中去掉与该工具相关的说明。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `tools.web_search` | 联网搜索参数：联网搜索请求的附加参数：搜索上下文大小、限定域名和大致位置。是否启用联网搜索由顶层 `web_search` 决定；旧写法 `tools.web_search = true` 或 `false` 仍能解析，但会被忽略。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tools.web_search.allowed_domains` | 限定搜索域名：只在这些域名内搜索的域名列表（如 `docs.python.org`），作为过滤条件随搜索请求发送；未设置时不限制。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `tools.web_search.context_size` | 搜索上下文大小：联网搜索为模型取回的上下文量，取 `low`、`medium` 或 `high`，对应 API 参数 `search_context_size`；越大结果越充分，通常耗时和费用也更高。未设置时由服务端决定。 | enum：low / medium / high | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `tools.web_search.location` | 搜索大致位置：提供给联网搜索的用户大致位置，用于让结果更贴近当地，包含 `country`、`region`、`city`、`timezone` 四个可选字段。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tools.web_search.location.city` | 城市：大致位置中的城市名称，自由文本（如 `Shanghai`）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `tools.web_search.location.country` | 国家：大致位置中的国家，使用两位 ISO 国家代码（如 `CN`、`US`）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `tools.web_search.location.region` | 地区：大致位置中的省、州等地区名称，自由文本。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `tools.web_search.location.timezone` | 时区：大致位置中的时区，使用 IANA 时区名（如 `Asia/Shanghai`）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按工作需要选择；不以全部开启作为优化。 |
| `web_search` | 联网搜索模式：联网搜索工具的模式：`cached`（默认，只用 OpenAI 缓存的结果，不实时访问网页）、`indexed`（只用搜索索引中的结果）、`live`（可实时访问网页）或 `disabled`（关闭）。使用完全访问权限（不使用沙箱）时，除 `disabled` 和 `indexed` 外会优先改用 `live`；当前服务商不支持外部网页访问时，`live` 和 `indexed` 会退回 `cached`。 | enum：disabled / cached / indexed / live | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 当前 live 适合查最新资料；一般任务可按需求选 cached。 |

## 应用与插件（50 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `apps` | 应用连接器设置：ChatGPT 应用连接器（Apps，如 Google Drive、GitHub）在 Codex 中的启用、工具筛选和审批设置。`[apps._default]` 设置默认值，`[apps.<应用 ID>]` 单独设置某个应用。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `apps._default` | 应用默认设置：对所有应用生效的默认设置，可被单个应用的设置覆盖。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `apps._default.approvals_reviewer` | 默认审批审核者：应用审批请求默认交给谁审核（`user` 或 `auto_review`），可被单个应用的设置覆盖。 | enum：user / auto_review / guardian_subagent | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps._default.default_tools_approval_mode` | 默认工具审批模式：所有应用工具的默认审批模式（`auto`、`prompt`、`writes` 或 `approve`），可被单个应用或工具的设置覆盖；未设置时为 `auto`。 | enum：auto / prompt / writes / approve | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps._default.destructive_enabled` | 默认允许破坏性工具：是否默认允许可能有破坏性的应用工具（`destructive_hint = true` 或未标注），默认 `true`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps._default.enabled` | 默认启用应用：设为 `false` 时所有应用默认禁用，只有单独设置了 `enabled = true` 的应用可用；默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps._default.open_world_enabled` | 默认允许开放世界工具：是否默认允许会与外部世界交互的应用工具（`open_world_hint = true` 或未标注），默认 `true`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps.<name>` | 单个应用：某个应用连接器的设置，表名为应用 ID（如 `google_drive`）。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `apps.<name>.approvals_reviewer` | 审批审核者：该应用发起的审批请求交给谁审核，覆盖会话默认值：`user`（用户）或 `auto_review`（自动审核子代理）；旧值 `guardian_subagent` 等同于 `auto_review`。 | enum：user / auto_review / guardian_subagent | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps.<name>.default_tools_approval_mode` | 工具默认审批模式：该应用工具的默认审批模式，账号级和单个工具的设置优先：`auto` 按工具注解判断，`prompt` 每次都询问，`writes` 除标为只读的工具外都询问，`approve` 直接批准。 | enum：auto / prompt / writes / approve | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps.<name>.default_tools_enabled` | 默认启用工具：该应用的工具是否默认启用。设置后会取代 `destructive_enabled` 和 `open_world_enabled` 的筛选；单个工具的 `enabled` 仍然优先。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps.<name>.destructive_enabled` | 允许破坏性工具：是否允许使用该应用中可能有破坏性的工具（`destructive_hint = true`，未标注的也按破坏性处理）。未设置时沿用 `apps._default.destructive_enabled`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps.<name>.enabled` | 启用此应用：设为 `false` 时 Codex 不再提供该应用及其工具；默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps.<name>.links` | 按账号设置：以已连接账号的 link ID 为键的审批设置，用于同一应用连接了多个账号时分别设置。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `apps.<name>.links.<name>` | 单个连接账号：该应用某个已连接账号的审批设置，表名为 link ID。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `apps.<name>.links.<name>.approvals_reviewer` | 审批审核者：来自该账号的审批请求交给谁审核（`user` 或 `auto_review`），覆盖应用级设置。 | enum：user / auto_review / guardian_subagent | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps.<name>.links.<name>.default_tools_approval_mode` | 工具默认审批模式：通过该账号调用工具时的默认审批模式（`auto`、`prompt`、`writes` 或 `approve`），覆盖应用级设置；单个工具的设置仍然优先。 | enum：auto / prompt / writes / approve | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps.<name>.omit_tools_from` | 隐藏工具的位置：该应用的工具不出现在哪些面向模型的位置（`direct`、`deferred`、`code_mode`），在服务器级设置之外额外生效。未设置时沿用较低优先级的配置，空列表表示清除应用级的隐藏设置。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps.<name>.open_world_enabled` | 允许开放世界工具：是否允许使用该应用中会与外部世界交互的工具（`open_world_hint = true`，未标注的也按此处理）。未设置时沿用 `apps._default.open_world_enabled`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps.<name>.tools` | 按工具设置：该应用中以工具名（如 `repos/list`）为键的单个工具设置。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `apps.<name>.tools.<name>` | 单个工具：应用中某个工具的设置，表名为工具名或工具标题，可设置 `enabled` 和 `approval_mode`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `apps.<name>.tools.<name>.approval_mode` | 审批模式：该工具的审批模式（`auto`、`prompt`、`writes` 或 `approve`），优先于账号级和应用级默认值；管理员下发的设置除外。 | enum：auto / prompt / writes / approve | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps.<name>.tools.<name>.enabled` | 启用此工具：是否启用该工具；`true` 表示明确放行，即使应用默认值或破坏性筛选本会禁用它。应用本身被禁用时此项不起作用。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `apps_mcp_product_sku` | 应用 MCP 产品 SKU：访问内置 Codex Apps MCP 服务器时通过 `X-OpenAI-Product-Sku` 请求头发送的产品标识，默认 `codex`，一般由宿主程序设置。项目内 `.codex/config.toml` 中的此项会被忽略。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `marketplaces` | 插件市场：已添加的插件市场，以市场名称为键。条目通常由 Codex 在添加或刷新市场时写入，记录来源和最近一次同步的状态。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `marketplaces.<name>` | 单个插件市场：某个插件市场的记录，表名为市场名称，包含来源类型、来源地址、Git 引用和同步状态。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `marketplaces.<name>.last_revision` | 最近同步版本：Codex 最近一次成功启用的该市场 Git 提交版本，由 Codex 自动维护。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `marketplaces.<name>.last_updated` | 最近更新时间：Codex 最近一次成功添加或刷新该市场的时间，由 Codex 自动维护。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `marketplaces.<name>.ref` | Git 引用：`source_type` 为 `git` 时要检出的分支、标签或提交。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `marketplaces.<name>.source` | 市场来源：添加该市场时使用的来源位置，例如 Git 仓库地址或本地目录路径。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `marketplaces.<name>.source_type` | 来源类型：安装该市场时使用的来源类型：`git`（Git 仓库）或 `local`（本地目录）。 | enum：git / local | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `marketplaces.<name>.sparse_paths` | 稀疏检出路径：`source_type` 为 `git` 时只检出的仓库子路径列表（稀疏检出）。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `plugins` | 插件设置：插件的启用设置和插件自带 MCP 服务器的策略，以 `<插件>@<市场>` 形式的完整插件 ID 为键；`[plugins._default]` 设置已安装插件的默认启用状态。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `plugins._default` | 插件默认启用状态：已安装插件的默认启用设置（Codex 0.163 起）。未设置时保持插件来源和账号原有的启用状态；单个插件的 `enabled` 优先，但不能启用被安装来源停用的插件。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `plugins._default.enabled` | 默认启用插件：已安装插件是否默认启用，默认 `true`；设为 `false` 时只有单独设置了 `enabled = true` 的插件才会生效。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `plugins.<name>` | 单个插件：某个插件的设置，表名为完整的 `<插件>@<市场>` ID。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `plugins.<name>.auto_provision` | 请求自动开通：远程插件的开通元数据：不设置时不请求开通。只在同时设置 `remote_plugin_id` 且插件服务（Plugin Service）在开通时对该用户可用时适用，并仍受实际启用状态、可信的开通策略以及认证和授权限制约束；此值本身不是可信授权，也不会触发开通。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `plugins.<name>.enabled` | 启用此插件：明确启用或停用该插件；未设置时沿用 `plugins._default` 或插件安装来源的默认状态。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `plugins.<name>.mcp_servers` | 插件的 MCP 服务器：针对该插件自带 MCP 服务器的策略覆盖，以服务器名为键。只能调整启用、审批和工具筛选，启动方式仍由插件清单决定。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `plugins.<name>.mcp_servers.<name>` | 单个插件 MCP 服务器：插件中某个 MCP 服务器的策略设置，表名为服务器名。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `plugins.<name>.mcp_servers.<name>.default_tools_approval_mode` | 工具默认审批模式：该服务器工具的默认审批模式（`auto`、`prompt`、`writes` 或 `approve`），单个工具的设置优先。 | enum：auto / prompt / writes / approve | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `plugins.<name>.mcp_servers.<name>.disabled_tools` | 禁用的工具：不向模型提供的工具名列表（黑名单），在 `enabled_tools` 筛选之后再移除。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `plugins.<name>.mcp_servers.<name>.enabled` | 启用此服务器：设为 `false` 时 Codex 不初始化该插件 MCP 服务器；默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `plugins.<name>.mcp_servers.<name>.enabled_tools` | 允许的工具：只向模型提供这些工具（白名单）；未设置时提供该服务器的全部工具。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `plugins.<name>.mcp_servers.<name>.tools` | 按工具设置：以工具名为键的单个工具策略设置。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `plugins.<name>.mcp_servers.<name>.tools.<name>` | 单个工具：插件 MCP 服务器中某个工具的设置，可指定 `approval_mode` 和 `output_token_limit`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `plugins.<name>.mcp_servers.<name>.tools.<name>.approval_mode` | 审批模式：该工具的审批模式（`auto`、`prompt`、`writes` 或 `approve`），优先于服务器的默认审批模式。 | enum：auto / prompt / writes / approve | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `plugins.<name>.mcp_servers.<name>.tools.<name>.output_token_limit` | 输出 token 上限：该工具输出最多保留给模型的 token 数，超出部分会被截断；实际允许量另加约 20% 的序列化余量。 | integer；minimum=1.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `plugins.<name>.release_id` | 远程插件版本：把远程插件固定到某个发布版本的标识；不设置时不改变版本选择。Codex 核心只保存这项元数据，自身不会选择或下载版本。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |
| `plugins.<name>.remote_plugin_id` | 远程插件 ID：ChatGPT 托管的远程插件的稳定唯一标识。该插件对当前用户是否可用，由插件服务（Plugin Service）在开通时判定。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按应用/工具组织；保留明确的权限范围。 |

## MCP 服务（53 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `mcp_enterprise_managed_auth` | MCP 企业托管授权：为启用企业托管授权（EMA）的 MCP 服务器和插件统一指定可信的企业身份提供方（IdP）。Codex 用该 IdP 的刷新令牌为各服务器换取专用授权，与 Codex 账号本身的登录无关。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `mcp_enterprise_managed_auth.idp` | 企业身份提供方：企业 IdP 的 OAuth 设置，包含 `issuer` 和 `client_id`。它是共享的企业授权来源，与 Codex 账号凭据相互独立。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `mcp_enterprise_managed_auth.idp.client_id` | IdP 客户端 ID：在该企业 IdP 中注册的 OAuth 客户端 ID，Codex 以此身份向 IdP 登录和交换令牌。公开客户端只需此项，机密客户端还要设置 `client_secret`。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_enterprise_managed_auth.idp.client_secret` | IdP 客户端密钥：机密（confidential）客户端在 IdP 登录和令牌交换时使用的预配客户端密钥，公开客户端不设置。设置时不能为空。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_enterprise_managed_auth.idp.issuer` | IdP 签发方：企业 IdP 的 issuer 地址，用于 OAuth 元数据发现和校验身份令牌的签发方。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_enterprise_managed_auth.idp.token_endpoint_auth_method` | 令牌端点认证方式：企业托管授权（EMA）客户端在令牌端点注册的认证方式：`client_secret_basic`（设置了密钥时的默认值，用 HTTP Basic 认证发送）或 `client_secret_post`（在请求体中发送）。必须同时设置 `client_secret`。 | enum：client_secret_basic / client_secret_post | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_oauth_callback_port` | MCP 登录回调端口：MCP OAuth 登录时本地回调服务器监听的固定端口；未设置时由操作系统随机分配。单个服务器的 `oauth.callback_port` 优先于此项。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_oauth_callback_url` | MCP 登录回调地址：MCP OAuth 登录时写入授权请求的重定向 URI，用来代替本地监听地址（例如经过端口转发时）。本地回调服务器仍监听 `127.0.0.1`（端口见 `mcp_oauth_callback_port`）；单个服务器的 `oauth.callback_url` 优先于此项。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_oauth_credentials_store` | MCP 凭据存储位置：MCP OAuth 凭据的保存方式：`auto`（默认，优先系统密钥链，不可用时用文件）、`keyring`（只用系统密钥链）或 `file`（保存到 Codex 主目录下的文件）。 | enum：auto / file / keyring | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_optional_startup_grace_ms` | 可选 MCP 启动宽限：构建初始工具列表时，等待非必需 MCP 服务器启动的毫秒数，默认 `1000`。到时仍未就绪的服务器暂不提供工具；设为 `0` 则不使用共享宽限，改为按各服务器的 `startup_timeout_sec` 等待。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers` | MCP 服务器：Codex 可以调用其工具的 MCP 服务器，每个以 `[mcp_servers.<名称>]` 表定义：设置 `command` 时启动本地 stdio 进程，设置 `url` 时连接 Streamable HTTP 服务。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `mcp_servers.<name>` | 单个 MCP 服务器：一个 MCP 服务器的定义，表名即服务器名称。设置 `command` 时为 stdio 服务器，设置 `url` 时为 HTTP 服务器；两类只接受各自的字段，混用会导致配置报错。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `mcp_servers.<name>.args` | 启动参数：启动 stdio MCP 服务器时传给 `command` 的参数列表；仅 stdio 服务器可用。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.auth` | 认证方式：HTTP MCP 服务器的认证流程：`oauth`（默认，使用已保存的 OAuth 凭据）、`chatgpt`（对受信任的 ChatGPT 官方来源使用当前 ChatGPT 登录）或 `ema_auth`（企业托管授权）。stdio 服务器不能设置此项。 | enum：oauth / chatgpt / ema_auth | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.bearer_token_env_var` | Bearer 令牌环境变量：存放 HTTP 访问令牌的环境变量名。设置后请求会带上 `Authorization: Bearer <令牌>`，令牌本身必须由该环境变量提供，不写在配置中；仅 HTTP 服务器可用。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.command` | 启动命令：启动 stdio MCP 服务器的程序或命令（如 `npx`），Codex 以子进程方式运行它，并通过标准输入输出通信。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.cwd` | 工作目录：启动 stdio MCP 服务器进程时使用的工作目录；仅 stdio 服务器可用。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.default_tools_approval_mode` | 工具默认审批模式：该服务器所有工具的默认审批模式，单个工具的 `tools.<工具名>.approval_mode` 优先：`auto` 按工具注解判断（标为只读的不询问，可能有破坏性或访问外部的会询问），`prompt` 每次都询问，`writes` 除标为只读的工具外都询问，`approve` 直接批准不询问。 | enum：auto / prompt / writes / approve | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.disabled_tools` | 禁用的工具：不向模型提供的工具名列表（黑名单），在 `enabled_tools` 筛选之后再移除。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.enabled` | 启用此服务器：设为 `false` 时 Codex 跳过该 MCP 服务器，不启动它也不提供其工具；默认 `true`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.enabled_tools` | 允许的工具：只向模型提供这些工具（白名单）；未设置时提供该服务器的全部工具。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.env` | 环境变量：启动 stdio MCP 服务器时额外设置的环境变量表。服务器进程默认只继承 `HOME`、`PATH`、`USER`、`LANG` 等少数基础变量，其他变量需要在这里写明或用 `env_vars` 转发。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `mcp_servers.<name>.env.<name>` | 单个环境变量：传给 MCP 服务器进程的一个环境变量，键为变量名，值为字符串。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.env_http_headers` | 环境变量请求头：值从环境变量读取的 HTTP 请求头表，键为请求头名，值为环境变量名；变量不存在或为空时不发送该请求头。仅 HTTP 服务器可用。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `mcp_servers.<name>.env_http_headers.<name>` | 单个环境变量请求头：一个请求头：键为请求头名称，值为提供其内容的环境变量名。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.env_vars` | 转发的环境变量：从 Codex 自身环境转发给 stdio MCP 服务器的环境变量名列表。每项可以是变量名，也可以是带 `name` 和 `source` 的表，`source` 为 `local`（默认，从本机读取）或 `remote`（在远程执行环境中读取，仅远程 stdio 服务器可用）。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.environment_id` | 运行环境：在哪个执行环境中启动该 MCP 服务器，默认 `local`（本机）；其他值表示在对应的远程执行环境中启动。`http_headers_helper` 只支持本机服务器。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.http_headers` | 固定请求头：每次请求 HTTP MCP 服务器时附加的固定请求头表；仅 HTTP 服务器可用。密钥类的值建议改用 `env_http_headers` 或 `bearer_token_env_var`。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `mcp_servers.<name>.http_headers.<name>` | 单个固定请求头：一个固定请求头：键为请求头名称，值为请求头内容。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.http_headers_helper` | 动态请求头命令：仅本机 HTTP 服务器可用的 shell 命令，运行后应输出一个 JSON 对象作为动态请求头。请求被服务器以 401 或 403 拒绝时会重新运行一次以刷新请求头；命令行可能被本机其他进程看到，不要在其中写入凭据。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.name` | 显示名称（旧）：旧版的显示名称字段，为兼容旧配置仍被接受，但不再起作用；服务器名称以表名为准。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.oauth` | OAuth 客户端设置：该服务器 MCP 登录或企业令牌交换时使用的 OAuth 客户端设置：`client_id`、`client_secret`、`callback_url`、`callback_port`、`authorization_server_issuer`。仅 HTTP 服务器可用。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `mcp_servers.<name>.oauth.authorization_server_issuer` | 授权服务器签发方：企业托管授权令牌交换时期望的资源授权服务器 issuer。只能在 `auth` 为 `ema_auth` 时使用，否则配置报错。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.oauth.callback_port` | 回调端口：该服务器 OAuth 登录时本地回调使用的固定端口，优先于全局的 `mcp_oauth_callback_port`。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.oauth.callback_url` | 回调地址：为该 OAuth 客户端注册的回调地址，优先于全局的 `mcp_oauth_callback_url`。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.oauth.client_id` | 客户端 ID：授权和令牌交换时使用的 OAuth 客户端 ID，用于服务器要求使用预先注册的客户端时。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.oauth.client_secret` | 客户端密钥：预注册 OAuth 客户端的密钥，用于令牌交换；设置时不能为空，且必须同时设置 `client_id`。该值以明文保存在配置文件中。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.oauth.token_endpoint_auth_method` | 令牌端点认证方式：企业托管授权（EMA）客户端在令牌端点注册的认证方式：`client_secret_basic`（设置了 `client_secret` 时的默认值）或 `client_secret_post`。只能在 `auth` 为 `ema_auth` 时使用，且必须同时设置 `oauth.client_secret`，否则配置报错。 | enum：client_secret_basic / client_secret_post | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.oauth_resource` | OAuth 资源参数：MCP 登录或企业令牌交换时附带的 `resource` 参数（RFC 8707），指明令牌要访问的资源服务器。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.omit_tools_from` | 隐藏工具的位置：该服务器的工具不出现在哪些面向模型的位置：`direct`（初始工具列表）、`deferred`（通过工具搜索按需发现）、`code_mode`（Code Mode 脚本内可调用的工具）。空列表表示清除较低优先级配置中的此项设置。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.required` | 必须启动成功：设为 `true` 时，该服务器初始化失败会让 `codex exec` 报错退出，构建工具列表时也会等待它就绪。若同时设置 `startup_readiness` 为 `catalog`，有效的缓存工具列表也算启动成功，连接失败会在调用工具时才报告。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.scopes` | OAuth 权限范围：MCP 登录或企业令牌交换时请求的 OAuth scope 列表。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.startup_readiness` | 启动就绪条件：启动时是否必须建立实时连接才算就绪：`connection`（默认，需要实时连接）或 `catalog`（可以先使用有效的缓存工具列表）。 | enum：connection / catalog | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.startup_timeout_ms` | 启动超时（毫秒）：以毫秒表示的启动超时，即初始化服务器并首次列出工具的最长时间，默认 30 秒；同时设置 `startup_timeout_sec` 时以后者为准。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.startup_timeout_sec` | 启动超时（秒）：初始化 MCP 服务器并首次列出工具的最长等待秒数，可以是小数，默认 30 秒；优先于 `startup_timeout_ms`。 | number | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.supports_parallel_tool_calls` | 支持并行调用：设为 `true` 时把该服务器的全部工具标记为可安全并行调用，模型可以同时发起多个调用；默认 `false`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.tool_input_schema_max_bytes` | 工具参数结构上限：单个工具输入参数结构（JSON Schema）的 UTF-8 字节阈值，超过时会被压缩以节省上下文，默认 5,000 字节；调大可以保留更多参数说明。显式设置时，Code Mode 渲染工具参数类型也使用该值。 | integer；minimum=1.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.tool_timeout_sec` | 工具调用超时：调用该服务器工具的默认超时秒数，可以是小数，默认 300 秒（5 分钟）。 | number | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.tools` | 按工具设置：以工具名为键的单个工具设置表，可为每个工具指定 `approval_mode` 和 `output_token_limit`。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `mcp_servers.<name>.tools.<name>` | 单个工具：该服务器中某个工具的设置，表名为工具名。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `mcp_servers.<name>.tools.<name>.approval_mode` | 审批模式：该工具的审批模式，优先于服务器的 `default_tools_approval_mode`：`auto` 按工具注解判断（标为只读的不询问，可能有破坏性或访问外部的会询问），`prompt` 每次都询问，`writes` 除标为只读的工具外都询问，`approve` 直接批准不询问。 | enum：auto / prompt / writes / approve | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.tools.<name>.output_token_limit` | 输出 token 上限：该工具输出最多保留给模型的 token 数，超出部分会被截断；实际允许量另加约 20% 的序列化余量。 | integer；minimum=1.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |
| `mcp_servers.<name>.url` | 服务器地址：Streamable HTTP MCP 服务器的地址（如 `https://example.com/mcp`）。设置后按 HTTP 方式连接，不能与 `command` 同时设置。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按实际服务实例组织；校验传输、认证和超时。 |

## 多代理（16 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `agents` | 子代理设置：多代理（子代理）相关设置：是否启用、同时运行的子代理上限、嵌套深度、子代理默认模型和推理强度，以及以 `[agents.<角色名>]` 定义的自定义角色。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `agents.default_subagent_model` | 子代理默认模型：派生子代理时调用没有指定模型，就使用这个模型；未设置时沿用父会话的模型。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按任务控制模型、并发和嵌套；通常跟随默认。 |
| `agents.default_subagent_reasoning_effort` | 子代理默认推理强度：派生子代理时调用没有指定推理强度，就使用这个值（如 `low`、`medium`、`high`）；须是所选模型支持的级别。 | string；min_length=1 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按任务控制模型、并发和嵌套；通常跟随默认。 |
| `agents.enabled` | 启用多代理：是否向模型提供派生和管理子代理的多代理工具，默认 `true`。开启功能开关 `multi_agent_v2` 时以该开关为准。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按任务控制模型、并发和嵌套；通常跟随默认。 |
| `agents.interrupt_message` | 记录中断提示：轮次被中断时，是否在历史中写入一条模型可见的中断说明，让模型知道上一轮被打断，默认 `true`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按任务控制模型、并发和嵌套；通常跟随默认。 |
| `agents.max_concurrent_threads_per_session` | 子代理并发上限：每个会话中可同时存在的子代理数量上限（旧名 `max_threads`），至少为 1。未设置时由所用多代理版本决定：V1 默认 6 个，V2 默认 3 个。 | integer；minimum=1.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按任务控制模型、并发和嵌套；通常跟随默认。 |
| `agents.max_depth` | 子代理嵌套深度：V1 多代理中子代理可以逐层派生的最大层数，默认 `1`，即子代理不能再派生子代理；V2 忽略此项。 | integer | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按任务控制模型、并发和嵌套；通常跟随默认。 |
| `agents.<name>` | 自定义代理角色：一个自定义子代理角色，表名为角色名，主代理派生子代理时可以选用。内置角色有 `default`、`explorer` 和 `worker`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `agents.<name>.config_file` | 角色配置文件：该角色专用的配置层文件路径，相对路径以定义它的 `config.toml` 所在目录为基准。角色配置可以定制或收紧子代理的设置，但不能扩大父会话的权限。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按任务控制模型、并发和嵌套；通常跟随默认。 |
| `agents.<name>.description` | 角色说明：面向人的角色说明，会写进派生子代理工具的说明中，帮助模型选择角色。除非引用的角色文件中已提供，否则必须填写。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按任务控制模型、并发和嵌套；通常跟随默认。 |
| `agents.<name>.nickname_candidates` | 昵称候选：使用该角色派生的子代理可选用的昵称列表，用于在界面中区分各个子代理。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按任务控制模型、并发和嵌套；通常跟随默认。 |
| `orchestrator` | 编排层设置：由编排层（orchestrator）控制的功能设置，目前用于决定是否向模型提供内置应用连接器（Apps）的 MCP 工具；`skills` 子项已弃用。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `orchestrator.mcp` | 编排层 MCP：编排层自有 MCP 工具（内置的 Codex Apps 应用连接器服务器）的开关设置。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `orchestrator.mcp.enabled` | 启用编排层 MCP：是否向模型提供内置 Codex Apps MCP 服务器的工具和资源，默认 `true`；设为 `false` 时应用连接器（Apps）在 Codex 中不可用。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按任务控制模型、并发和嵌套；通常跟随默认。 |
| `orchestrator.skills` | 编排层技能（已弃用）：已弃用、不再起作用的旧设置，仅为兼容旧配置而保留；云端技能请改用 `cloud.skills` 配置。 | table | 未提供 | 未标注 / 弃用提示 / 未限定平台 | 未单独实测 | 保留已有值；核实替代写法后提供显式迁移。 |
| `orchestrator.skills.enabled` | 编排层技能开关（已弃用）：已弃用的旧开关，设置后没有任何效果；请改用 `cloud.skills.enabled`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按任务控制模型、并发和嵌套；通常跟随默认。 |

## 实时与音频（15 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `audio` | 实时语音音频设备：实时语音使用的本机音频设备偏好：麦克风、扬声器和麦克风声道。终端界面（TUI）的实时语音设置会写到这里，对下一次语音对话生效。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `audio.microphone` | 麦克风设备：实时语音使用的输入设备名称，需与系统列出的设备名完全一致；不设置时使用系统默认麦克风。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 仅使用实时/音频功能时调整；区分设备支持。 |
| `audio.microphone_channel` | 麦克风声道：要混合采集的麦克风声道编号，从 1 开始，可写单个数字或数组（如 `[1, 2]`）；不设置时混合全部输入声道。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 补充当前官方 schema 的 minimum=1；避免接受 0。 |
| `audio.speaker` | 扬声器设备：实时语音播放所用的输出设备名称；不设置时使用系统默认输出设备。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 仅使用实时/音频功能时调整；区分设备支持。 |
| `experimental_realtime_start_instructions` | 实时语音开始指令（实验）：实验性，官方注明请勿使用：替换实时语音开启时插入开发者消息里的内置“开始”指令。客户端为本次会话单独提供的开始指令优先于此项。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 仅使用实时/音频功能时调整；区分设备支持。 |
| `experimental_realtime_webrtc_call_base_url` | WebRTC 通话地址（实验）：实验性，官方注明请勿使用：只覆盖创建 WebRTC 实时通话的 HTTP 请求所用的基础 URL。它与 `experimental_realtime_ws_base_url` 分开，因为后者管的是 WebSocket 旁路控制连接；项目内的 `.codex/config.toml` 不能设置此项。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 仅使用实时/音频功能时调整；区分设备支持。 |
| `experimental_realtime_ws_backend_prompt` | 实时语音后端提示词（实验）：实验性，官方注明请勿使用：替换实时语音 WebSocket 会话 `session.update` 中的指令，不影响普通对话的提示词。非空时优先于客户端传入的提示词和内置提示词。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 仅使用实时/音频功能时调整；区分设备支持。 |
| `experimental_realtime_ws_base_url` | 实时语音 WebSocket 地址（实验）：实验性，官方注明请勿使用：只覆盖实时语音 WebSocket 连接（`/v1/realtime`）的基础 URL，普通模型请求的地址不变；WebRTC 通话的旁路控制连接也使用这个地址。项目内的 `.codex/config.toml` 不能设置此项。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 仅使用实时/音频功能时调整；区分设备支持。 |
| `experimental_realtime_ws_model` | 实时语音模型（实验）：实验性，官方注明请勿使用：指定实时语音 WebSocket 连接使用的模型或快照。客户端请求里指定的模型优先；都未指定时按协议版本取内置默认值（v1/v2 为 `gpt-realtime-1.5`，v3 为 `gpt-live-1-codex`）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 仅使用实时/音频功能时调整；区分设备支持。 |
| `experimental_realtime_ws_startup_context` | 实时语音启动上下文（实验）：实验性，官方注明请勿使用：替换 Codex 自动合成、附加在实时语音会话指令之后的启动上下文。设为空字符串则完全不注入启动上下文。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 仅使用实时/音频功能时调整；区分设备支持。 |
| `realtime` | 实时语音会话设置：实验性，官方注明请勿使用：选择实时语音会话的协议版本 `version`、会话类型 `type`、传输方式 `transport` 和音色 `voice`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `realtime.transport` | 实时语音传输方式：实时语音的传输方式：`webrtc`（默认）或 `websocket`。依据 0.163 源码，实际使用的传输由客户端发起语音会话时决定（未指定时用 WebSocket），此项目前主要供客户端读取。 | enum：webrtc / websocket | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 仅使用实时/音频功能时调整；区分设备支持。 |
| `realtime.type` | 实时语音会话类型：`conversational`（默认）为语音对话，`transcription` 只做语音转写。WebRTC 通话只支持 `conversational`。 | enum：conversational / transcription | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 仅使用实时/音频功能时调整；区分设备支持。 |
| `realtime.version` | 实时语音协议版本：实时语音协议版本：`v1`、`v2`（默认）或 `v3`（无分帧的双向协议，默认模型 `gpt-live-1-codex`）。只对 WebSocket 传输生效；WebRTC 通话未指定版本时用 `v1`，且不支持 `v2`。 | enum：v1 / v2 / v3 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 仅使用实时/音频功能时调整；区分设备支持。 |
| `realtime.voice` | 实时语音音色：实时语音回复使用的音色，如 `alloy`、`cedar`、`marin`。每个协议版本只支持其中一部分，选了当前版本不支持的音色会导致语音会话启动失败；不设置时用该版本的默认音色，客户端指定的音色优先。 | enum：alloy / arbor / ash / aube / ballad / breeze / bubbie / cedar / coral / cove / echo / ember / haetsal / hanul / himari / juniper / leher / maple / marin / miko / neer / porto / rindo / rio / rivage / sage / selva / shimmer / sol / sonna / spruce / tinta / vale / verse / viola | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 仅使用实时/音频功能时调整；区分设备支持。 |

## 界面与通知（243 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `check_for_update_on_startup` | 启动时检查更新：为 `true`（默认）时，Codex CLI 的终端界面（TUI）启动时会检查是否有新版本并提示升级；从源码构建的版本不做此检查。只有在 Codex 版本由组织统一管理时才建议设为 `false`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `desktop` | 桌面 app 设置：桌面 app 保存在 `config.toml` 中的 `[desktop]` 表，内容由桌面 app 自行维护，Codex 只把它当作不透明数据保存，一般无需手动编辑。其中的工作树根目录 `git-worktree-root`（须为绝对路径，默认 `$CODEX_HOME/worktrees`）也会被 CLI 沿用。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `disable_paste_burst` | 禁用粘贴突发检测（旧）：`tui.disable_paste_burst` 的旧位置，仅在 `[tui]` 下没有设置该项时作为后备生效。建议改用 `[tui]` 下的同名设置。 | boolean | 未提供 | 未标注 / 弃用提示 / 未限定平台 | 未单独实测 | 保留已有值；核实替代写法后提供显式迁移。 |
| `file_opener` | 文件引用打开方式：模型回复中引用的文件会按这里选择的 URI 方案生成可点击的超链接，可选 `vscode`、`vscode-insiders`、`windsurf`、`cursor` 或 `none`。未设置时按 `vscode` 处理；设为 `none` 则不生成链接。 | enum：vscode / vscode-insiders / windsurf / cursor / none | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `hide_agent_reasoning` | 隐藏推理过程：设为 `true` 时不显示模型的推理（`AgentReasoning`）事件，默认 `false`。依据 0.163 源码，目前只有 `codex exec` 的文本输出读取此项（不再向 stderr 打印推理摘要），终端界面（TUI）不受影响。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notice` | 产品内提示状态：记录各类产品内提示（警告、新功能引导、确认框等）是否已被确认或隐藏的表，与桌面通知（notifications）无关。这些值通常由 Codex 在用户选择“不再提示”等操作后自动写入，一般无需手动修改。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `notice.external_config_migration_prompts` | 外部配置迁移提示：记录在哪些范围内不再弹出“从其他代理工具（如 Claude Code）导入配置”的迁移提示，分为用户主目录级和各项目级，并记录上次提示的时间。由 Codex 自动维护。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `notice.external_config_migration_prompts.home` | 隐藏主目录级迁移提示：为 `true` 时，不再提示把用户主目录下其他代理工具（如 Claude Code）的配置迁移到 Codex。由 Codex 在用户选择不再提示后写入。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notice.external_config_migration_prompts.home_last_prompted_at` | 主目录级上次提示时间：上一次显示主目录级外部配置迁移提示的时间，以整数时间戳保存。由 Codex 自动写入。 | integer | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notice.external_config_migration_prompts.project_last_prompted_at` | 项目级上次提示时间：按项目路径记录上一次显示项目级外部配置迁移提示的时间（整数时间戳）。由 Codex 自动写入。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `notice.external_config_migration_prompts.project_last_prompted_at.<name>` | 某项目上次提示时间：以项目路径为键、整数时间戳为值的一条记录：该项目上一次显示外部配置迁移提示的时间。 | integer | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notice.external_config_migration_prompts.projects` | 已关闭迁移提示的项目：按项目路径记录是否已关闭项目级外部配置迁移提示（例如导入该项目中 Claude Code 配置的提示）。由 Codex 自动维护。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `notice.external_config_migration_prompts.projects.<name>` | 某项目的迁移提示开关：以项目路径为键的一条记录；值为 `true` 表示该项目不再弹出外部配置迁移提示。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notice.fast_default_opt_out` | 不使用默认 Fast 档：记录用户是否退出了 Codex 管理的 Fast 默认档。为 `true` 且没有显式设置 `service_tier` 时，TUI 以普通服务档（`default`）发送请求，不再自动采用模型默认的 Fast 档。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notice.hide_full_access_warning` | 隐藏完全访问警告：记录用户是否已确认“完全访问”（Full Access）权限模式的警告；为 `true` 时切换到该模式不再弹出警告。由 Codex 自动写入。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notice.hide_gov_cloud_guidance` | 隐藏 GovCloud 指引：记录用户是否已确认 AWS GovCloud 使用指引；为 `true` 后不再显示。TUI 只在使用 Amazon Bedrock 模型供应商或连接外部（非内嵌）app-server 时于启动阶段检查此指引。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notice.hide_gpt-5.1-codex-max_migration_prompt` | 隐藏 codex-max 迁移提示：记录用户是否已看过“迁移到 `gpt-5.1-codex-max` 模型”的启动提示；为 `true` 时，引用此键的模型升级提示不再显示。由 Codex 自动写入。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notice.hide_gpt5_1_migration_prompt` | 隐藏 GPT-5.1 迁移提示：记录用户是否已看过启动时的模型迁移（升级）提示，键名源自 GPT-5.1 迁移；为 `true` 时，引用此键的模型升级提示不再显示。由 Codex 自动写入。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notice.hide_rate_limit_model_nudge` | 隐藏限额换模型提醒：记录用户是否关闭了“接近用量限额时建议切换到更省额度的模型”的提醒；为 `true` 时不再弹出。由 Codex 在用户选择不再提醒后写入。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notice.hide_world_writable_warning` | 隐藏可写目录警告：记录用户是否已确认 Windows 上关于“所有人可写（world-writable）目录”的沙箱警告；为 `true` 时不再显示。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notice.model_migrations` | 已确认的模型迁移：以“旧模型名 → 新模型名”的映射记录用户已确认过的模型迁移，避免重复弹出同一个迁移提示。由 Codex 自动写入。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `notice.model_migrations.<name>` | 单条模型迁移记录：以旧模型名为键、新模型名为值的一条已确认迁移记录。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `notify` | 外部通知命令：每个轮次完成（代理处理完一次用户提交）后要执行的外部命令，以字符串数组写出命令及其参数；Codex 执行时会在末尾再追加一个描述该事件的 JSON 字符串参数（事件类型如 `agent-turn-complete`）。未设置则不启用；它与 TUI 的桌面通知 `tui.notifications` 是两套独立机制。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `show_raw_agent_reasoning` | 显示原始推理内容：设为 `true` 时显示模型的原始推理内容（`AgentReasoningRawContentEvent`），而不只是推理摘要；只有模型或服务商实际返回原始推理时才有内容。默认 `false`；使用 `--oss` 本地模型且未设置此项时默认开启。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `suppress_unstable_features_warning` | 隐藏开发中功能警告：启用了开发中（不稳定）的功能时，Codex 默认会给出警告；设为 `true` 可不再显示该警告，默认 `false`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 排查实验能力时可取消覆盖以看见告警；不影响实际能力开关。 |
| `tui` | 终端界面设置：只对 Codex CLI 终端界面（TUI）生效的设置集合，包括通知、动画、快捷键、状态栏、主题等；不影响桌面 app 和 `codex exec`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.agents_overview_grouping` | 代理总览分组方式：代理总览（Agent Command Center）上次选择的任务分组方式：`project`（默认，按项目）、`status`（按状态）或 `model`（按模型）。由 TUI 自动记住，也可手动设置。 | enum：project / status / model | "project" | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.alternate_screen` | 备用屏幕模式：控制 TUI 是否使用终端的备用屏幕（alternate screen）。`auto`（默认）通常使用备用屏幕，但在 macOS 自带终端 Terminal.app 经 SSH 运行时改用原生回滚；`always` 始终使用；`never` 始终以内联模式运行并保留终端回滚历史。命令行参数 `--no-alt-screen` 优先于此设置。 | enum：auto / always / never | "auto" | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.animations` | 界面动画：TUI 动画总开关（欢迎画面、流光文字、加载转圈等），默认 `true`；关闭后 `tui.effects` 下的各项效果也都不生效。首次启动时若检测到屏幕阅读器且未设置此项，Codex 会自动写入 `false`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.auto_recap` | 自动会话回顾：终端失去焦点后自动生成会话回顾（总结进展和下一步），默认 `true`。依据源码，至少完成 3 个轮次且失焦约 30 分钟才会触发，每次生成会额外发起一次临时模型请求；关闭后仍可用 `/recap` 手动生成。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.copy_on_select` | 选中即复制：松开鼠标时自动复制所选的对话记录、底栏或输入框文字。默认 `auto`：直接运行在已知会转发原生复制快捷键的终端（Ghostty 1.2+、macOS 上的 Kitty、Windows Terminal、Windows 上的 VS Code）中时关闭，其余情况（包括未识别的终端、无法识别版本的 Ghostty 以及 tmux/Zellij 内）开启。 | enum：auto / always / never | "auto" | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.copy_on_select_clear_selection` | 复制后取消选中：“选中即复制”确认内容已写入剪贴板后，自动取消只读的对话记录或底栏文字的选中状态；可编辑的输入框中的选中保持不变。默认 `false`；“选中即复制”关闭时无效。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.disable_paste_burst` | 禁用粘贴突发检测：在不支持括号粘贴（bracketed paste）的终端里，Codex 会把短时间内连续到达的按键识别为一次粘贴，避免粘贴内容中的回车直接提交。设为 `true` 则完全关闭该检测，字符按收到的顺序逐个插入。默认 `false`，优先于顶层旧设置 `disable_paste_burst`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.effects` | 视觉效果：逐项开关 TUI 的视觉效果，默认全部开启；每项都还要求 `tui.animations` 为 `true`。关闭某项只去掉动画，不影响其背后的状态显示。 | table | {"effort":true,"progress":true,"shimmer":true,"starfield":true,"title":true,"welcome":true} | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.effects.effort` | 推理强度切换动画：切换推理强度时，在输入框和底栏播放动画。默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.effects.progress` | 进度动画：为活动条目的圆点和加载转圈播放动画，默认 `true`；关闭后进度仍会显示，只是不再动。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.effects.shimmer` | 文字流光效果：在状态文字和加载文字上显示流光（shimmer）效果，默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.effects.starfield` | 输入框星空动画：输入框里的星空（starfield）动画，默认 `true`。依据源码，只在使用名称含 `astra` 的模型开始新任务时出现。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.effects.title` | 标题提醒闪烁：需要用户操作（如审批）时，让终端窗口/标签标题里的指示符闪烁，默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.effects.welcome` | 欢迎画面动画：播放欢迎画面的图案动画，默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.fullscreen_transcript` | 全屏对话记录：是否由 Codex 自己接管全屏对话记录，在 Codex 的全屏视图里滚动、选择和搜索。默认 `true`；设为 `false` 则改用终端原生的复制、粘贴和回滚。`tui.alternate_screen` 为 `never` 时总是使用终端原生回滚；也可用 `/tui` 命令选择，修改在下次启动时生效。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap` | 快捷键自定义：自定义 TUI 的快捷键：可在 `global` 中全局重绑，也可按上下文（如 `composer`、`pager`、`approval`）重绑，上下文中的绑定优先于 `global`。值可以是 `ctrl-a` 这样的单个按键、最多两段的组合键（如 `ctrl-x ctrl-s`）或它们的列表，空列表表示解除绑定；也可用 `/keymap` 命令交互设置。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.agents` | 代理总览快捷键：代理总览（Agent Command Center）里的快捷键，用于恢复、搜索、新建、分叉、重命名、停止、归档、删除、隐藏和置顶任务。每项可设为单个按键、按键列表，或用空列表解除绑定。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.agents.archive` | 归档任务：确认后归档选中的任务及其子代理。默认 `a`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.agents.delete` | 删除任务：确认后永久删除选中的任务及其子代理。默认 `backspace`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.agents.fork` | 分叉会话：分叉（fork）选中的对话并打开得到的新会话。默认 `f`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.agents.hide` | 隐藏任务：在总览中隐藏选中的任务，直到再次显式恢复它或重启 TUI。默认 `h`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.agents.new_task` | 新建任务：在选中任务所在的代码检出目录中新开一个会话。默认 `n`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.agents.new_worktree` | 在新工作树中新建：从项目默认分支新建一个 Git 工作树（worktree），并在其中打开新会话。默认 `w`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.agents.rename` | 重命名任务：重命名选中的任务。默认 `r`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.agents.resume` | 打开会话恢复选择器：打开会话恢复选择器，挑选要继续的历史会话。默认 `o`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.agents.search` | 搜索任务：在可用的代理任务中搜索。默认 `f3` 或 `/`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.agents.stop` | 停止任务：停止选中的、正在运行的任务。默认 `x`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.agents.toggle_grouping` | 切换任务分组：切换任务的分组方式（如按状态或按项目），所选分组会保存到 `tui.agents_overview_grouping`。默认 `g`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.agents.toggle_pin` | 置顶任务：在共享侧边栏区域中置顶或取消置顶选中的任务。默认 `p`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.approval` | 审批浮层快捷键：命令、文件修改、权限和 MCP 信息征询等审批浮层里的快捷键，例如批准、本会话内批准、拒绝和打开详情。每项可设为单个按键、按键列表，或用空列表解除绑定。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.approval.approve` | 批准：选择主选项（“是，继续”），批准本次请求。默认 `y`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.approval.approve_for_prefix` | 按命令前缀批准：存在该选项时，批准并把命令前缀写入执行策略（exec policy），以后以相同前缀开头的命令不再询问；网络访问审批中对应“以后允许此主机”。默认 `p`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.approval.approve_for_session` | 本会话内批准：存在该选项时，在本会话内批准：同一命令、同一批文件或同一组权限在本会话中不再询问。默认 `a`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.approval.cancel` | 取消信息征询：取消 MCP 服务器发起的信息征询（elicitation）请求；无论如何设置，`esc` 始终也能取消。默认 `c`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.approval.decline` | 拒绝并给出指示：拒绝请求并停下来，让用户告诉 Codex 应该怎么换种做法（“否，并告诉 Codex 怎么做”）。默认 `esc` 或 `n`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.approval.deny` | 拒绝并继续：拒绝请求但不给出后续指示，代理跳过该操作继续工作（如“否，不运行并继续”）。默认 `d`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.approval.open_fullscreen` | 打开审批详情：打开全屏的审批详情视图，查看完整的命令或改动。默认 `ctrl-a`，也接受 `ctrl-shift-a`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.approval.open_thread` | 跳转到发起会话：当审批请求来自另一个会话（如子代理）时，跳转到发起该请求的会话。默认 `o`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.chat` | 聊天界面快捷键：主聊天界面里的快捷键，例如中断轮次、调整推理强度、切换权限模式、处理异步问题和实时语音。每项可设为单个按键、按键列表，或用空列表解除绑定。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.chat.decrease_reasoning_effort` | 降低推理强度：把当前推理强度调低一档。默认 `alt-,` 或 `shift-down`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.chat.edit_queued_message` | 编辑排队消息：先向上浏览待回答的异步问题，再编辑最近一条排队待发送的消息。默认 `shift-left` 或 `alt-up`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.chat.increase_reasoning_effort` | 提高推理强度：把当前推理强度调高一档。默认 `alt-.` 或 `shift-up`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.chat.interrupt_turn` | 中断当前轮次：中断正在进行的轮次，让代理停止当前工作。默认 `esc`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.chat.next_permission_mode` | 下一个权限模式：切换到下一个可用的权限模式（审批与沙箱的组合预设）。默认未绑定按键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.chat.open_presets` | 打开预设输入：打开已保存的提示词和斜杠命令（`[tui.presets]`）菜单。默认 `leader p`（按前导键后再按 `p`），该组合已被其他绑定占用时不设默认。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.chat.previous_permission_mode` | 上一个权限模式：切换到上一个可用的权限模式（审批与沙箱的组合预设）。默认未绑定按键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.chat.prompt_stack_back` | 返回输入框：在待回答的异步问题之间往回移动，直到回到输入框。默认 `shift-right` 或 `alt-down`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.chat.skip_question` | 跳过问题：跳过当前聚焦的问题。默认 `ctrl-]`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.chat.toggle_voice` | 开始/结束语音对话：开始或结束实时语音对话。默认 `f8`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.chat.toggle_voice_mute` | 语音静音：在进行中的实时语音对话里开关麦克风。默认 `f9`；若 `f9` 已被其他快捷键占用，则不使用默认值。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.composer` | 输入框快捷键：输入框（composer）里的快捷键：提交、排队发送、快捷键提示和历史搜索。这里的设置优先于 `global` 中的同名动作。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.composer.history_search_next` | 历史搜索下一项：在反向历史搜索中跳到下一个匹配项。默认 `ctrl-s`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.composer.history_search_previous` | 反向历史搜索：打开对已发送提示词的反向历史搜索，或跳到上一个匹配项。默认 `ctrl-r`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.composer.queue` | 排队发送：任务运行期间把当前草稿加入待发送队列，等当前轮次结束后再发出。默认 `tab`，优先于 `global.queue`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.composer.submit` | 提交草稿：提交输入框中的当前草稿。默认 `enter`，优先于 `global.submit`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.composer.toggle_shortcuts` | 快捷键提示浮层：显示或隐藏输入框的快捷键提示浮层。默认 `?`，优先于 `global.toggle_shortcuts`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor` | 文本编辑快捷键：输入框等文本区域里的编辑快捷键：光标移动、删除、换行，以及 Emacs 风格的剪切（kill）与粘回（yank）。每项可设为单个按键、按键列表，或用空列表解除绑定。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.editor.delete_backward` | 向左删除字符：删除光标左侧的一个字符（字形簇）。默认 `backspace`、`shift-backspace` 或 `ctrl-h`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.delete_backward_word` | 删除前一个词：删除光标前的一个词。默认包括 `alt-backspace`、`ctrl-backspace`、`ctrl-w` 等。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.delete_forward` | 向右删除字符：删除光标右侧的一个字符（字形簇）。默认 `delete`、`shift-delete` 或 `ctrl-d`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.delete_forward_word` | 删除后一个词：删除光标后的一个词。默认包括 `alt-delete`、`ctrl-delete`、`alt-d` 等。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.insert_newline` | 插入换行：在编辑区中插入换行而不提交。默认包括 `ctrl-j`、`shift-enter`、`alt-enter` 等。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.kill_line_end` | 剪切到行尾：把光标到行尾的文本剪切到剪切缓冲区（kill buffer），之后可用 `yank` 粘回。默认 `ctrl-k`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.kill_line_start` | 剪切到行首：把光标到行首的文本剪切到剪切缓冲区。默认 `ctrl-u`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.kill_whole_line` | 剪切整行：把当前整行剪切到剪切缓冲区。默认未绑定按键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.move_down` | 光标下移一行：把光标下移一个显示行（自动换行后的视觉行）。默认 `down` 或 `ctrl-n`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.move_left` | 光标左移：把光标左移一个字符（字形簇）。默认 `left` 或 `ctrl-b`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.move_line_end` | 移到行尾：把光标移到行尾。默认 `end` 或 `ctrl-e`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.move_line_start` | 移到行首：把光标移到行首。默认 `home` 或 `ctrl-a`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.move_right` | 光标右移：把光标右移一个字符（字形簇）。默认 `right` 或 `ctrl-f`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.move_up` | 光标上移一行：把光标上移一个显示行（自动换行后的视觉行）。默认 `up` 或 `ctrl-p`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.move_word_left` | 移到前一个词首：把光标移到前一个词的开头。默认 `alt-b`、`alt-left` 或 `ctrl-left`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.move_word_right` | 移到后一个词尾：把光标移到下一个词的末尾。默认 `alt-f`、`alt-right` 或 `ctrl-right`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.editor.yank` | 粘回剪切内容：把剪切缓冲区（kill buffer）中的文本粘贴到光标处。默认 `ctrl-y`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global` | 全局快捷键：在任何界面都可用的快捷键；具体上下文（如 `composer`）没有覆盖同名动作时使用这里的绑定。每项可设为单个按键、按键列表，或用空列表解除绑定。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.global.clear_terminal` | 清屏：清空并重绘终端界面。默认 `ctrl-l`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.copy` | 复制最近回复：把代理的最近一条回复复制到剪贴板。默认 `ctrl-o`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.find_transcript` | 在对话记录中查找：在完整的对话记录中搜索文本。默认 `f3`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.focus_activity` | 聚焦活动分组：在 Codex 接管的全屏对话记录中聚焦到活动分组，以便查看其详情。默认 `f4`；该键已被自定义绑定占用时不使用默认值。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.leader` | 前导键：前导键（leader）：按下后弹出随上下文变化的快捷菜单，其他动作可绑定成 `leader c` 这样的形式。默认 `ctrl-x`（`ctrl-x` 已被其他绑定占用时不设默认）；每个绑定只能是单个按键（不能是按键序列，可列出多个备选），且须带 `ctrl`/`alt` 或为非字符键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.open_agents` | 打开代理总览：打开共享的代理会话总览（Agent Command Center），在其中查看和管理各个任务。默认未绑定按键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.open_external_editor` | 用外部编辑器编辑：用外部编辑器（优先 `$VISUAL`，其次 `$EDITOR`）编辑当前草稿，保存退出后内容回填到输入框。默认 `ctrl-g`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.open_transcript` | 打开对话记录：打开对话记录浮层，浏览完整对话。默认 `ctrl-t`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.queue` | 排队发送（全局）：任务运行期间把当前草稿加入待发送队列。这是全局后备绑定，设置了 `composer.queue` 时以后者为准；内置默认 `tab`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.submit` | 提交草稿（全局）：提交输入框中的当前草稿。这是全局后备绑定，设置了 `composer.submit` 时以后者为准；内置默认 `enter`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.toggle_fast_mode` | 切换 Fast 模式：开关 Fast 模式（Fast 服务档）。默认未绑定按键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.toggle_raw_output` | 切换原始输出模式：开关原始滚动输出模式，让对话记录便于在终端里直接选择复制，作用同 `/raw`。默认 `alt-r`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.toggle_shortcuts` | 快捷键提示（全局）：显示或隐藏输入框的快捷键提示浮层。这是全局后备绑定，设置了 `composer.toggle_shortcuts` 时以后者为准；内置默认 `?`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.toggle_side_conversation` | 切换旁支对话：在旁支对话（用 `/side` 开启的临时分叉会话）与其主会话之间来回切换，两者都不会关闭。默认 `ctrl-/`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.global.toggle_vim_mode` | 切换 Vim 模式：开关输入框的 Vim 编辑模式，作用同 `/vim`。默认未绑定按键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.list` | 列表选择快捷键：弹出式选择列表（如模型、主题选择器）里的快捷键：移动、翻页、跳到首尾、确认和取消。每项可设为单个按键、按键列表，或用空列表解除绑定。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.list.accept` | 确认选择：确认当前选中的项。默认 `enter`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.list.cancel` | 取消并关闭：取消并关闭选择视图。默认 `esc`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.list.jump_bottom` | 跳到最后一项：跳到列表的最后一项。默认 `end`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.list.jump_top` | 跳到第一项：跳到列表的第一项。默认 `home`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.list.move_down` | 向下选择：把选中项下移一项。默认包括 `down`、`ctrl-n`、`ctrl-j` 和 `j`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.list.move_left` | 向左移动：在支持横向操作的列表选择器中向左移动。默认 `left` 或 `ctrl-h`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.list.move_right` | 向右移动：在支持横向操作的列表选择器中向右移动。默认 `right` 或 `ctrl-l`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.list.move_up` | 向上选择：把选中项上移一项。默认包括 `up`、`ctrl-p`、`ctrl-k` 和 `k`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.list.page_down` | 向下翻页：把选中项向下移动一页。默认 `page-down` 或 `ctrl-f`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.list.page_up` | 向上翻页：把选中项向上移动一页。默认 `page-up` 或 `ctrl-b`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.pager` | 翻页浮层快捷键：对话记录浮层和其他静态浮层（分页查看器）里的快捷键：滚动、翻页、跳到首尾、查找和关闭。每项可设为单个按键、按键列表，或用空列表解除绑定。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.pager.close` | 关闭浮层：关闭分页浮层。默认 `q` 或 `ctrl-c`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.pager.close_transcript` | 关闭对话记录：用对话记录专用的切换键关闭对话记录浮层。默认 `ctrl-t`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.pager.find` | 在浮层中查找：在对话记录分页浮层中查找文本。默认 `f3` 或 `/`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.pager.half_page_down` | 向下半页：向下滚动半页。默认 `ctrl-d`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.pager.half_page_up` | 向上半页：向上滚动半页。默认 `ctrl-u`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.pager.jump_bottom` | 跳到末尾：跳到内容末尾。默认 `end`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.pager.jump_top` | 跳到开头：跳到内容开头。默认 `home`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.pager.page_down` | 向下翻页：向下滚动一页。默认 `page-down`、`space` 或 `ctrl-f`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.pager.page_up` | 向上翻页：向上滚动一页。默认 `page-up`、`shift-space` 或 `ctrl-b`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.pager.scroll_down` | 向下滚动一行：向下滚动一行。默认 `down` 或 `j`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.pager.scroll_up` | 向上滚动一行：向上滚动一行。默认 `up` 或 `k`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal` | Vim 普通模式快捷键：输入框处于 Vim 普通模式（Normal）时的按键。大写字母的动作（如 `A`）在配置中要写成 `shift-a`，不同终端对 Shift 的上报差异由 Codex 处理。每项可设为单个按键、按键列表，或用空列表解除绑定。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.vim_normal.append_after_cursor` | 在光标后插入：在光标之后进入插入模式（Vim 的 `a`）。默认 `a`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.append_line_end` | 在行尾插入：在行尾进入插入模式（Vim 的 `A`）。默认 `shift-a`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.cancel_operator` | 取消待定操作符：取消尚未完成的操作符（如已按下 `d` 还没选动作），回到普通模式。默认 `esc`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.change_to_line_end` | 修改到行尾：删除从光标到行尾的内容并进入插入模式（Vim 的 `C`）。默认 `shift-c`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.delete_char` | 删除光标处字符：删除光标下的字符（Vim 的 `x`）。默认 `x`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.delete_to_line_end` | 删除到行尾：删除从光标到行尾的内容（Vim 的 `D`）。默认 `shift-d`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.enter_insert` | 进入插入模式：在光标处进入插入模式（Vim 的 `i`）。默认 `i`，也可用 Insert 键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.enter_replace_mode` | 进入替换模式：进入替换模式，输入的字符逐个覆盖光标下的字符（Vim 的 `R`）。默认 `shift-r`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.find_backward` | 行内向左查找字符：在当前行向左查找下一个输入的字符并跳到该处（Vim 的 `F`）。默认 `shift-f`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.find_forward` | 行内向右查找字符：在当前行向右查找下一个输入的字符并跳到该处（Vim 的 `f`）。默认 `f`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.insert_line_start` | 在行首插入：在本行第一个非空白字符处进入插入模式（Vim 的 `I`）。默认 `shift-i`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.jump_bottom` | 跳到最后一行：跳到缓冲区的最后一行（Vim 的 `G`）。默认 `shift-g`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.jump_top` | 跳到第一行：跳到缓冲区的第一行。默认是两段组合键 `g g`（即 Vim 的 `gg`），也可改绑为单个按键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.move_down` | 光标下移：光标下移一行（Vim 的 `j`）；已在边界时改为调出较新的输入历史。默认 `j` 或 `down`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.move_left` | 光标左移：光标左移一个字符（Vim 的 `h`）。默认 `h` 或 `left`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.move_line_end` | 移到行尾：把光标移到行尾（Vim 的 `$`）。默认 `$`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.move_line_start` | 移到行首：把光标移到行首（Vim 的 `0`）。默认 `0`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.move_right` | 光标右移：光标右移一个字符（Vim 的 `l`）。默认 `l` 或 `right`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.move_up` | 光标上移：光标上移一行（Vim 的 `k`）；已在边界时改为调出较早的输入历史。默认 `k` 或 `up`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.move_word_backward` | 移到前一个词首：把光标移到前一个词的开头（Vim 的 `b`）。默认 `b`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.move_word_end` | 移到词尾：把光标移到当前或下一个词的末尾（Vim 的 `e`）。默认 `e`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.move_word_forward` | 移到下一个词首：把光标移到下一个词的开头（Vim 的 `w`）。默认 `w`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.open_line_above` | 在上方新开一行：在当前行上方新开一行并进入插入模式（Vim 的 `O`）。默认 `shift-o`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.open_line_below` | 在下方新开一行：在当前行下方新开一行并进入插入模式（Vim 的 `o`）。默认 `o`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.paste_after` | 在光标后粘贴：把已复制（yank）的内容粘贴到光标之后（Vim 的 `p`）。默认 `p`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.redo` | 重做：重做上一次被撤销的编辑（Vim 的 `ctrl-r`）。默认 `ctrl-r`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.repeat_last_change` | 重复上次修改：重复上一次完整的编辑操作（Vim 的 `.`）。默认 `.`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.replace_char` | 替换单个字符：用下一个输入的字符替换光标下的字符（Vim 的 `r`）。默认 `r`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.start_change_operator` | 修改操作符：开始修改（change）操作符，随后的按键选择文本对象（如 `ciw`），所选内容被删除后进入插入模式（Vim 的 `c`）。默认 `c`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.start_delete_operator` | 删除操作符：开始删除操作符，下一个按键选择动作范围（Vim 的 `d`）。默认 `d`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.start_yank_operator` | 复制操作符：开始复制（yank）操作符，下一个按键选择动作范围（Vim 的 `y`）。默认 `y`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.substitute_char` | 替换字符并插入：删除光标下的字符并进入插入模式（Vim 的 `s`）。默认 `s`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.till_backward` | 向左移到字符后：在当前行向左查找下一个输入的字符，停在它的后一位（Vim 的 `T`）。默认 `shift-t`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.till_forward` | 向右移到字符前：在当前行向右查找下一个输入的字符，停在它的前一位（Vim 的 `t`）。默认 `t`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.undo` | 撤销：撤销上一次完整的编辑（Vim 的 `u`）。默认 `u`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_normal.yank_line` | 复制整行：复制（yank）整行（Vim 的 `Y`）。默认 `shift-y`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator` | Vim 操作符等待快捷键：按下 `d` 或 `y` 后等待动作时生效的按键：重复操作符键（`dd`、`yy`）作用于整行，按 `esc` 取消且不修改文本。每项可设为单个按键、按键列表，或用空列表解除绑定。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.vim_operator.cancel` | 取消操作符：取消待定的操作符并回到普通模式。默认 `esc`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.delete_line` | 删除整行：再按一次删除操作符键以删除整行（Vim 的 `dd`）。默认 `d`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_down` | 动作：下移一行：作为操作符的动作范围：向下一行（Vim 的 `j`）。默认 `j`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_find_backward` | 动作：向左查找字符：作为操作符的动作范围：到当前行左侧的下一个指定字符（Vim 的 `F`）。默认 `shift-f`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_find_forward` | 动作：向右查找字符：作为操作符的动作范围：到当前行右侧的下一个指定字符（Vim 的 `f`）。默认 `f`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_jump_bottom` | 动作：到最后一行：作为操作符的动作范围：到缓冲区最后一行（Vim 的 `G`）。默认 `shift-g`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_jump_top` | 动作：到第一行：作为操作符的动作范围：到缓冲区第一行。默认是两段组合键 `g g`（即 Vim 的 `gg`），也可改绑为单个按键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_left` | 动作：左移：作为操作符的动作范围：向左一个字符（Vim 的 `h`）。默认 `h`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_line_end` | 动作：到行尾：作为操作符的动作范围：到行尾（Vim 的 `$`）。默认 `$`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_line_start` | 动作：到行首：作为操作符的动作范围：到行首（Vim 的 `0`）。默认 `0`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_right` | 动作：右移：作为操作符的动作范围：向右一个字符（Vim 的 `l`）。默认 `l`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_till_backward` | 动作：向左到字符后：作为操作符的动作范围：到当前行左侧指定字符的后一位（Vim 的 `T`）。默认 `shift-t`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_till_forward` | 动作：向右到字符前：作为操作符的动作范围：到当前行右侧指定字符的前一位（Vim 的 `t`）。默认 `t`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_up` | 动作：上移一行：作为操作符的动作范围：向上一行（Vim 的 `k`）。默认 `k`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_word_backward` | 动作：到前一个词首：作为操作符的动作范围：到前一个词的开头（Vim 的 `b`）。默认 `b`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_word_end` | 动作：到词尾：作为操作符的动作范围：到当前或下一个词的末尾（Vim 的 `e`）。默认 `e`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.motion_word_forward` | 动作：到下一个词首：作为操作符的动作范围：到下一个词的开头（Vim 的 `w`）。默认 `w`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.select_around_text_object` | 选择外围文本对象：在操作符之后选择包含边界的文本对象（Vim 的 `a`，如 `daw`、`da(`）。默认 `a`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.select_inner_text_object` | 选择内部文本对象：在操作符之后选择不含边界的文本对象（Vim 的 `i`，如 `diw`、`ci(`）。默认 `i`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_operator.yank_line` | 复制整行：再按一次复制操作符键以复制整行（Vim 的 `yy`）。默认 `y`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_search` | Vim 搜索快捷键：Vim 普通模式和操作符等待模式共用的搜索按键：正向/反向搜索，以及重复上次搜索。每项可设为单个按键、按键列表，或用空列表解除绑定。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.vim_search.backward` | 反向搜索：在当前缓冲区中朝文本开头方向搜索（Vim 的 `?`）。默认 `?`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_search.forward` | 正向搜索：在当前缓冲区中朝文本末尾方向搜索（Vim 的 `/`）。默认 `/`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_search.next` | 下一个匹配：按原方向重复上一次已确认的搜索（Vim 的 `n`）。默认 `n`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_search.previous` | 上一个匹配：按相反方向重复上一次搜索（Vim 的 `N`）。默认 `shift-n`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_text_object` | Vim 文本对象快捷键：在操作符后接 `i`（内部）或 `a`（外围）时，用来指定文本对象种类的按键，如词、括号、引号。每项可设为单个按键、按键列表，或用空列表解除绑定。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.keymap.vim_text_object.backtick` | 反引号对象：文本对象：一对反引号包围的内容。默认为反引号键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_text_object.big_word` | WORD 对象：文本对象：以空白分隔的 WORD（可含标点）。默认 `shift-w`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_text_object.braces` | 花括号对象：文本对象：一对花括号 `{}` 包围的内容。默认 `{`、`}` 或 `shift-b`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_text_object.brackets` | 方括号对象：文本对象：一对方括号 `[]` 包围的内容。默认 `[` 或 `]`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_text_object.cancel` | 取消文本对象：取消尚未完成的文本对象命令。默认 `esc`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_text_object.double_quote` | 双引号对象：文本对象：一对英文双引号包围的内容。默认为双引号键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_text_object.parentheses` | 圆括号对象：文本对象：一对圆括号 `()` 包围的内容。默认 `(`、`)` 或 `b`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_text_object.single_quote` | 单引号对象：文本对象：一对英文单引号包围的内容。默认为单引号键。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.keymap.vim_text_object.word` | 词对象：文本对象：一个词（word）。默认 `w`。 | union | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.model_availability_nux` | 模型可用性提示状态：TUI 保存的启动提示（新模型可用性介绍）状态：按模型名记录该提示已显示的次数。由 TUI 自动维护，一般无需修改。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.model_availability_nux.<name>` | 某模型的提示次数：以模型名（slug）为键、已显示次数为值的一条记录。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.mouse_scroll_speed` | 鼠标滚动速度：滚动对话记录时鼠标滚轮的速度倍数，以每个滚动事件移动一行为基准，默认 `1.0`。小于 `1.0` 的正数减慢滚动，大于 `1.0` 加快；必须是有限的正数。 | number | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.notification_condition` | 通知触发条件：TUI 桌面通知的发送时机：`unfocused`（默认）仅在终端失去焦点时发送，`always` 不论焦点都发送。 | enum：unfocused / always | "unfocused" | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.notification_method` | 终端通知方式：发送终端通知的方式：`auto`（默认）在 Ghostty、iTerm2、Kitty、Warp、WezTerm 中使用 OSC 9 桌面通知，其他终端使用 BEL 响铃；也可固定为 `osc9` 或 `bel`。 | enum：auto / osc9 / bel | "auto" | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.notifications` | 桌面通知：是否从 TUI 发送桌面通知，默认 `true`。也可写成事件类型列表，只为列出的事件发通知：`agent-turn-complete`（轮次完成）、`approval-requested`（请求审批）、`plan-mode-prompt`（计划模式提问）、`async-question`（异步问题）。 | union | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.pet` | 终端宠物：终端宠物选择器中预选的宠物 ID，设置后 TUI 中会显示该宠物，设为 `disabled` 则隐藏。自定义宠物从 `CODEX_HOME/pets/<pet-id>/pet.json` 加载；也可用 `/pets` 命令选择。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.pet_anchor` | 宠物停靠位置：终端宠物在垂直方向上的停靠位置：`composer`（默认）跟随当前输入框区域，`screen-bottom` 固定在终端屏幕最底部。 | enum：composer / screen-bottom | "composer" | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.presets` | 预设输入：终端界面（TUI）中保存的可复用输入（提示词或斜杠命令），键为单个数字 `0`–`9`。用 `chat.open_presets` 快捷键打开预设菜单后按对应数字即可调出，也可在菜单中新建、编辑或删除；不支持以 `!` 开头的 Shell 命令。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.presets.<name>` | 单个预设：一个编号预设，表名是 `0`–`9` 中的一个数字，包含必填的 `input` 和可选的 `label`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.presets.<name>.input` | 预设内容：预设的纯文本输入，可以是提示词或斜杠命令，不能为空，最长 8,000 个 UTF-8 字节。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.presets.<name>.label` | 预设名称：可选的简短名称，显示在预设菜单和快捷键提示中。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.question_esc_back` | Esc 返回输入框：回答代理提出的异步问题时，按 `esc` 收起问题并回到输入框，同时保留已写的回答草稿，默认 `true`。设为 `false` 时 `esc` 按其他快捷键规则处理（例如中断当前轮次）。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.raw_output_mode` | 启动时原始输出模式：以原始滚动输出模式启动 TUI，让对话记录便于在终端里直接选择复制，默认 `false`。运行中可用 `/raw` 或 `alt-r` 切换。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.rendering` | 富内容渲染：逐项开关 TUI 中的富内容渲染，默认全部开启，与动画和视觉效果设置无关；关闭某项后显示原始源码文本。 | table | {"lists":true,"math":true,"mermaid":true,"tables":true} | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `tui.rendering.lists` | 列表符号渲染：把 Markdown 列表符号和任务列表标记渲染成 Unicode 符号，默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.rendering.math` | 数学公式渲染：用 Unicode 记号渲染数学表达式，默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.rendering.mermaid` | Mermaid 图表渲染：把 Mermaid 代码块渲染成图表，默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.rendering.tables` | 表格渲染：渲染竖线分隔（pipe）的表格，包括 Markdown 代码围栏中的表格，默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.resume_cwd` | 恢复会话时的目录：恢复或分叉会话时使用的工作目录：`current` 为启动 Codex 的目录，`session` 为会话中记录的最近工作目录。未设置时，如果两者不同会询问用户。 | enum：current / session | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.right_click_paste` | 右键粘贴：全屏 TUI 在没有选中文字时的右键粘贴后备功能。默认 `auto`（仅 Windows/WSL/Linux）；`on` 还会在 macOS 上启用；两种模式在 SSH 中或已识别的 VS Code 终端里都不读取剪贴板。它只控制 Codex 的后备行为，不影响终端自身的粘贴快捷键。 | enum：auto / on / off | "auto" | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.screen_reader_detection_done` | 屏幕阅读器检测标记：记录是否已做过一次性的屏幕阅读器检测；只要此键存在（不论 `true` 还是 `false`）就跳过检测。检测发现屏幕阅读器且未设置 `tui.animations` 时，Codex 会自动关闭动画。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.session_picker_view` | 会话选择器布局：恢复/分叉会话选择器的结果列表布局：`dense`（默认，紧凑）或 `comfortable`（宽松）。 | enum：comfortable / dense | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.show_server_version_notice` | 服务端版本差异提示：TUI 连接的 app-server 版本与自身不同时，是否显示说明性提示，默认 `true`；不影响兼容性错误和版本状态的显示。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.show_tooltips` | 启动小提示：在 TUI 欢迎屏幕上显示使用小提示，默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.status_line` | 状态栏项目：状态栏要显示的项目 ID 列表（按顺序）。未设置时默认为 `model-with-reasoning`、`current-dir` 和 `thread-name`；可选项目还有 Git 分支、上下文用量、限额剩余等，可用 `/statusline` 命令交互配置。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.status_line_use_colors` | 状态栏着色：用当前语法高亮主题派生的颜色给状态栏项目着色，默认 `true`。 | boolean | true | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.terminal_resize_reflow_max_rows` | 缩放重排行数上限：终端窗口尺寸变化时，TUI 会重新排版并重放已渲染的对话记录；记录超过此行数时只重放最近的这些行。不设置时使用按终端类型决定的默认值（无法识别终端时为 1000 行），设为 `0` 则保留全部行。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.terminal_title` | 终端标题项目：写入终端窗口/标签标题的项目 ID 列表（按顺序）。未设置时默认为 `activity`、`thread-name` 和 `project-name`；其中 `activity` 在工作时显示转动指示，等待用户操作时显示提醒。可用 `/title` 命令交互配置。 | array | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.theme` | 语法高亮主题：语法高亮主题名（kebab-case），设置后不再自动按终端亮色/暗色选择主题。可在 TUI 中用 `/theme` 选择，自定义主题放在 `$CODEX_HOME/themes`。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |
| `tui.vim_mode_default` | 默认 Vim 模式：输入框启动时默认进入 Vim 模式（普通模式 `Normal`），默认 `false`。运行中可用 `/vim` 切换。 | boolean | false | 未标注 / 未限定平台 | 未单独实测 | 区分 Desktop、CLI 和本工具；个人偏好按需改。 |

## 历史与存储（16 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `allow_symlinked_codex_home` | 允许 CODEX_HOME 符号链接：仅 macOS 有效：允许沙箱信任 `CODEX_HOME`（通常是 `~/.codex`）之内或之下可写目录中的符号链接，并按链接当前指向的目标授予写权限；默认 `false`，遇到这类路径时沙箱直接报错。只读取本机 `~/.codex/config.toml` 顶层的值（配置档和项目配置中的同名项无效），需重启 Codex 生效；开启后即使链接指向 `CODEX_HOME` 之外或在两次命令之间被改动也会被信任。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按历史与保留需求调整；先展示影响范围。 |
| `experimental_thread_store` | 会话存储后端（实验）：实验性，官方注明请勿使用：选择会话的持久化实现。不设置时就是本地存储（会话记录写入 rollout JSONL 文件，元数据写入 SQLite）；旧的 `experimental_thread_store_endpoint` 已移除，写了会直接报错。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `experimental_thread_store.type` | 会话存储类型：会话存储的实现类型，配置中唯一可用的取值是 `local`（本地 rollout 文件 + SQLite 元数据，与不设置时相同）。源码里另有仅供测试使用的内存存储，不对外开放。 | enum：local | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按历史与保留需求调整；先展示影响范围。 |
| `ghost_snapshot` | 幽灵快照（旧版兼容）：已弃用，仅为兼容保留：旧版 Codex 会在轮次开始前用 Git 给工作区拍“幽灵快照”以便撤销改动，现在已不再生成快照。保留这个表只是为了让含 `[ghost_snapshot]` 的旧配置仍能正常加载，其中各项都不再起作用。 | table | 未提供 | 未标注 / 弃用提示 / 未限定平台 | 未单独实测 | 保留已有值；核实替代写法后提供显式迁移。 |
| `ghost_snapshot.disable_warnings` | 关闭快照警告（已失效）：旧版用于关闭幽灵快照相关的警告提示。快照功能已移除，此项现在不起任何作用，仅为兼容旧配置保留。 | boolean | 未提供 | 未标注 / 弃用提示 / 未限定平台 | 未单独实测 | 保留已有值；核实替代写法后提供显式迁移。 |
| `ghost_snapshot.ignore_large_untracked_dirs` | 忽略大型未跟踪目录（已失效）：旧版幽灵快照的阈值：含大量未跟踪文件的目录不纳入快照（旧名 `large_untracked_dir_warning_threshold`，默认 200）。快照功能已移除，此项现在不起作用，仅为兼容旧配置保留。 | integer | 未提供 | 未标注 / 弃用提示 / 未限定平台 | 未单独实测 | 保留已有值；核实替代写法后提供显式迁移。 |
| `ghost_snapshot.ignore_large_untracked_files` | 忽略大型未跟踪文件（已失效）：旧版幽灵快照的阈值：超过该字节数的未跟踪文件不纳入快照（旧名 `ignore_untracked_files_over_bytes`，默认 10 MiB）。快照功能已移除，此项现在不起作用，仅为兼容旧配置保留。 | integer | 未提供 | 未标注 / 弃用提示 / 未限定平台 | 未单独实测 | 保留已有值；核实替代写法后提供显式迁移。 |
| `history` | 输入历史记录：控制是否以及如何把提交过的消息写入 `$CODEX_HOME/history.jsonl`（默认 `~/.codex/history.jsonl`），每行记录会话 ID、时间戳和消息文本，供终端界面（TUI）在输入框里翻找和搜索历史输入。子项 `persistence` 决定是否落盘，`max_bytes` 限制文件大小。 | table | {"persistence":"save-all"} | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `history.max_bytes` | 历史文件大小上限：`history.jsonl` 的最大字节数；超过后删除最旧的记录，把文件裁剪到该上限的约 80%。不设置或设为 `0` 表示不限制。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按历史与保留需求调整；先展示影响范围。 |
| `history.persistence` | 历史保存方式：是否把输入历史写入磁盘：`save-all`（默认）保存全部记录，`none` 不写入。官方英文说明写成了布尔值，实际取值是这两个字符串。 | enum：save-all / none | "save-all" | 未标注 / 未限定平台 | 未单独实测 | 按历史与保留需求调整；先展示影响范围。 |
| `log_dir` | 日志目录：Codex 写日志文件的目录，默认 `$CODEX_HOME/log`（通常是 `~/.codex/log`）。显式设置后，SQLite 日志数据库也改存到这里，终端界面（TUI）还会在该目录写一份文本日志 `codex-tui.log`；未设置时 SQLite 日志放在 `sqlite_home`。已有的 SQLite 日志不会自动迁移，管理员要求了 `sqlite_home` 时 SQLite 日志仍留在该目录。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按历史与保留需求调整；先展示影响范围。 |
| `projects` | 项目信任列表：按项目目录记录的设置，目前只有信任级别 `trust_level`。键是项目目录或 Git 仓库根目录的绝对路径，通常在首次进入某个目录、确认是否信任后由 Codex 自动写入。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `projects.<name>` | 单个项目设置：某一个项目的设置，表名是该项目目录的绝对路径，例如 `[projects."/home/me/repo"]`。查找时依次匹配当前目录、项目根目录（含 `.git` 等标记的目录）和 Git 仓库根目录。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `projects.<name>.trust_level` | 项目信任级别：`trusted` 或 `untrusted`。只有 `trusted` 的项目才会加载项目内的 `.codex/config.toml`、钩子和执行策略，且默认审批策略为 `on-request`；`untrusted` 时使用内部的 `untrusted` 审批策略（命令都需审批，除非执行策略规则明确放行）。两者在未设置 `sandbox_mode` 时都默认使用工作区可写（`workspace-write`）沙箱。 | enum：trusted / untrusted | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按历史与保留需求调整；先展示影响范围。 |
| `sqlite_home` | SQLite 数据目录：Codex 存放 SQLite 状态数据库（如保存会话索引的 `state_5.sqlite`、保存日志的 `logs_2.sqlite`）的目录。优先级为：此配置 > 环境变量 `CODEX_SQLITE_HOME` > `$CODEX_HOME`。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按历史与保留需求调整；先展示影响范围。 |
| `thread_unload_delay_secs` | 空闲会话卸载延迟：一个会话既没有订阅者也没有任何活动多少秒后，app-server 把它从内存中卸载，`0` 表示立即卸载。默认 60（1 分钟），0.164 及更早版本为 1800（30 分钟）。每个会话加载时读取此值，请求中的配置覆盖按会话生效。 | integer；minimum=0.0 | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按历史与保留需求调整；先展示影响范围。 |

## 遥测与反馈（72 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `analytics` | 使用统计：控制 OpenAI 的使用统计（analytics）：`enabled = false` 时在本机所有 Codex 产品界面关闭。自定义 OTLP 指标导出器不受影响，由 `otel.metrics_exporter` 单独控制；未设置时默认值取决于客户端（终端界面（TUI）和 `codex exec` 默认开启，`codex app-server` 默认关闭，除非以 `--analytics-default-enabled` 启动）。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `analytics.enabled` | 启用使用统计：设为 `false` 时关闭 OpenAI 使用统计，包括发往 ChatGPT 后端的使用事件和内置 `statsig` 通道的指标。自定义 OTLP 指标导出器仍会工作，要一并关闭需设置 `otel.metrics_exporter = "none"`；未设置时按客户端的默认值。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 保留用户偏好；不要表述成关闭全部诊断、反馈和自定义遥测。 |
| `feedback` | 反馈功能：控制各 Codex 产品界面的反馈功能，例如终端界面（TUI）的 `/feedback` 命令和 app-server 的反馈上传。默认开启，`enabled = false` 时关闭。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `feedback.enabled` | 启用反馈：设为 `false` 时关闭反馈上传和 Guardian 审查记录的后台留存：终端界面（TUI）的 `/feedback` 只提示已禁用，app-server 拒绝反馈上传请求；显式的本地导出仍可使用。默认 `true`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel` | OpenTelemetry 遥测：OpenTelemetry（OTEL）遥测设置：日志、追踪和指标分别由 `exporter`、`trace_exporter`、`metrics_exporter` 决定导出到哪里。默认不导出日志和追踪，指标走内置的 `statsig` 通道（受 `analytics.enabled` 控制）；项目内的 `.codex/config.toml` 不能设置此表。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.environment` | 遥测环境标签：写入导出数据的环境标签（如 `dev`、`staging`、`prod`、`test`），用于在收集端区分数据来源。默认 `dev`。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.exporter` | 日志导出器：OTEL 日志事件（如用户提示、工具结果、API 请求）的导出方式：`none`（默认，不导出）、`statsig`（发往 OpenAI 内置的收集端点），或用 `{ otlp-http = { … } }`、`{ otlp-grpc = { … } }` 指向自己的 OTLP 收集器。 | enum：none / statsig | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.exporter.otlp-http` | 日志 OTLP HTTP 导出：通过 OTLP/HTTP 把日志事件发送到自己的收集器。必填 `endpoint` 和 `protocol`，可选 `headers` 和 `tls`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.exporter.otlp-http.endpoint` | 日志 HTTP 端点：接收日志的完整 URL，例如 `https://otel.example.com/v1/logs`。Codex 的 OTLP HTTP 端点按信号区分，不会自动补路径，日志、追踪、指标要分别填写；必填。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.exporter.otlp-http.headers` | 日志 HTTP 请求头：导出日志时附带的额外 HTTP 请求头，常用于向收集器认证。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.exporter.otlp-http.headers.<name>` | 日志 HTTP 请求头项：一条请求头：键为名称，值为发送的内容。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.exporter.otlp-http.protocol` | 日志 HTTP 编码格式：导出日志时 OTLP/HTTP 请求体的编码：`binary`（Protobuf 二进制）或 `json`。必填。 | enum：binary / json | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.exporter.otlp-http.tls` | 日志 HTTP TLS：连接日志收集器时的 TLS 设置：可以指定自定义 CA 证书，或同时提供客户端证书和私钥启用双向 TLS（mTLS）。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.exporter.otlp-http.tls.ca-certificate` | 日志 HTTP CA 证书：用于校验日志收集器服务端证书的 CA 证书文件路径（PEM 格式）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.exporter.otlp-http.tls.client-certificate` | 日志 HTTP 客户端证书：双向 TLS 使用的客户端证书文件路径（PEM 格式），必须与 `client-private-key` 一起设置；设置后只允许 HTTPS 连接。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.exporter.otlp-http.tls.client-private-key` | 日志 HTTP 客户端私钥：双向 TLS 使用的客户端私钥文件路径（PEM 格式），必须与 `client-certificate` 一起设置。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.exporter.otlp-grpc` | 日志 OTLP gRPC 导出：通过 OTLP/gRPC 把日志事件发送到自己的收集器。必填 `endpoint`，可选 `headers` 和 `tls`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.exporter.otlp-grpc.endpoint` | 日志 gRPC 端点：接收日志的 OTLP gRPC 收集器地址，例如 `https://otel.example.com:4317`。必填。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.exporter.otlp-grpc.headers` | 日志 gRPC 请求头：导出日志时附带的额外请求头（gRPC 元数据），常用于向收集器认证。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.exporter.otlp-grpc.headers.<name>` | 日志 gRPC 请求头项：一条请求头：键为名称，值为发送的内容。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.exporter.otlp-grpc.tls` | 日志 gRPC TLS：连接日志收集器时的 TLS 设置：可以指定自定义 CA 证书，或同时提供客户端证书和私钥启用双向 TLS（mTLS）。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.exporter.otlp-grpc.tls.ca-certificate` | 日志 gRPC CA 证书：用于校验日志收集器服务端证书的 CA 证书文件路径（PEM 格式）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.exporter.otlp-grpc.tls.client-certificate` | 日志 gRPC 客户端证书：双向 TLS 使用的客户端证书文件路径（PEM 格式），必须与 `client-private-key` 一起设置。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.exporter.otlp-grpc.tls.client-private-key` | 日志 gRPC 客户端私钥：双向 TLS 使用的客户端私钥文件路径（PEM 格式），必须与 `client-certificate` 一起设置。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.log_agent_responses` | 记录代理最终回复：是否把主代理和子代理的最终回复文本写入 OTLP 日志。默认 `false`，且只有 `otel.exporter` 设为 `otlp-http` 或 `otlp-grpc` 时才生效；回复可能包含敏感内容，每条最多记录 64 KiB。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.log_guardian_assessments` | 记录 Guardian 评估：是否把已完成的 Guardian（自动审批审查）评估写入 OTLP 日志。默认 `false`，且只有 `otel.exporter` 设为 `otlp-http` 或 `otlp-grpc` 时才生效；评估理由可能包含敏感内容，每条最多记录 64 KiB。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.log_user_prompt` | 记录用户提示词：是否在 OTEL 日志的 `codex.user_prompt` 事件里记录用户提示词原文。默认 `false`，此时正文以 `[REDACTED]` 代替，只保留长度；追踪数据始终只含长度和输入数量（依据 0.163 源码，官方英文写的是“traces”）。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.metrics_exporter` | 指标导出器：指标的导出方式。默认 `statsig`，即 OpenAI 内置通道，是否发送跟随 `analytics.enabled`；自定义的 `otlp-http`、`otlp-grpc` 导出器不受 `analytics.enabled` 影响；设为 `none` 关闭指标导出。 | enum：none / statsig | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.metrics_exporter.otlp-http` | 指标 OTLP HTTP 导出：通过 OTLP/HTTP 把指标发送到自己的收集器，不受 `analytics.enabled` 影响。必填 `endpoint` 和 `protocol`，可选 `headers` 和 `tls`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.metrics_exporter.otlp-http.endpoint` | 指标 HTTP 端点：接收指标的完整 URL，例如 `https://otel.example.com/v1/metrics`。不会自动补路径，需与日志、追踪的地址分别填写；必填。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.metrics_exporter.otlp-http.headers` | 指标 HTTP 请求头：导出指标时附带的额外 HTTP 请求头，常用于向收集器认证。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.metrics_exporter.otlp-http.headers.<name>` | 指标 HTTP 请求头项：一条请求头：键为名称，值为发送的内容。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.metrics_exporter.otlp-http.protocol` | 指标 HTTP 编码格式：导出指标时 OTLP/HTTP 请求体的编码：`binary`（Protobuf 二进制）或 `json`。必填。 | enum：binary / json | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.metrics_exporter.otlp-http.tls` | 指标 HTTP TLS：连接指标收集器时的 TLS 设置：可以指定自定义 CA 证书，或同时提供客户端证书和私钥启用双向 TLS（mTLS）。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.metrics_exporter.otlp-http.tls.ca-certificate` | 指标 HTTP CA 证书：用于校验指标收集器服务端证书的 CA 证书文件路径（PEM 格式）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.metrics_exporter.otlp-http.tls.client-certificate` | 指标 HTTP 客户端证书：双向 TLS 使用的客户端证书文件路径（PEM 格式），必须与 `client-private-key` 一起设置；设置后只允许 HTTPS 连接。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.metrics_exporter.otlp-http.tls.client-private-key` | 指标 HTTP 客户端私钥：双向 TLS 使用的客户端私钥文件路径（PEM 格式），必须与 `client-certificate` 一起设置。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.metrics_exporter.otlp-grpc` | 指标 OTLP gRPC 导出：通过 OTLP/gRPC 把指标发送到自己的收集器，不受 `analytics.enabled` 影响。必填 `endpoint`，可选 `headers` 和 `tls`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.metrics_exporter.otlp-grpc.endpoint` | 指标 gRPC 端点：接收指标的 OTLP gRPC 收集器地址，例如 `https://otel.example.com:4317`。必填。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.metrics_exporter.otlp-grpc.headers` | 指标 gRPC 请求头：导出指标时附带的额外请求头（gRPC 元数据），常用于向收集器认证。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.metrics_exporter.otlp-grpc.headers.<name>` | 指标 gRPC 请求头项：一条请求头：键为名称，值为发送的内容。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.metrics_exporter.otlp-grpc.tls` | 指标 gRPC TLS：连接指标收集器时的 TLS 设置：可以指定自定义 CA 证书，或同时提供客户端证书和私钥启用双向 TLS（mTLS）。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.metrics_exporter.otlp-grpc.tls.ca-certificate` | 指标 gRPC CA 证书：用于校验指标收集器服务端证书的 CA 证书文件路径（PEM 格式）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.metrics_exporter.otlp-grpc.tls.client-certificate` | 指标 gRPC 客户端证书：双向 TLS 使用的客户端证书文件路径（PEM 格式），必须与 `client-private-key` 一起设置。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.metrics_exporter.otlp-grpc.tls.client-private-key` | 指标 gRPC 客户端私钥：双向 TLS 使用的客户端私钥文件路径（PEM 格式），必须与 `client-certificate` 一起设置。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.span_attributes` | 追踪附加属性：附加到每个导出的追踪 span 上的键值属性，例如 `{ team = "infra" }`。只在配置了追踪导出器时使用；键不能为空，无效的项会在启动时给出警告并被忽略。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.span_attributes.<name>` | 单个追踪属性：一条 span 属性：键是属性名（不能为空），值是字符串。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.tool_result` | 工具结果日志限制：控制遥测日志的 `codex.tool_result` 事件里最多记录多少工具输出；只影响日志，不影响模型看到的内容。 | table | {"max_bytes":2048} | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.tool_result.max_bytes` | 工具结果日志上限：工具输出写入日志前允许的最大 UTF-8 字节数，超出部分截断并附上截断提示。默认 2048；调大会让更多工具数据进入日志。 | integer；minimum=0.0 | 2048 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.trace_exporter` | 追踪导出器：追踪（trace span）的导出方式：`none`（默认，不导出）、`statsig`（发往 OpenAI 内置的收集端点），或 `otlp-http`、`otlp-grpc` 自己的收集器。它与日志导出器相互独立，开启日志导出不会顺带导出追踪。 | enum：none / statsig | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.trace_exporter.otlp-http` | 追踪 OTLP HTTP 导出：通过 OTLP/HTTP 把追踪 span 发送到自己的收集器。必填 `endpoint` 和 `protocol`，可选 `headers` 和 `tls`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.trace_exporter.otlp-http.endpoint` | 追踪 HTTP 端点：接收追踪的完整 URL，例如 `https://otel.example.com/v1/traces`。不会自动补路径，需与日志、指标的地址分别填写；必填。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.trace_exporter.otlp-http.headers` | 追踪 HTTP 请求头：导出追踪时附带的额外 HTTP 请求头，常用于向收集器认证。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.trace_exporter.otlp-http.headers.<name>` | 追踪 HTTP 请求头项：一条请求头：键为名称，值为发送的内容。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.trace_exporter.otlp-http.protocol` | 追踪 HTTP 编码格式：导出追踪时 OTLP/HTTP 请求体的编码：`binary`（Protobuf 二进制）或 `json`。必填。 | enum：binary / json | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.trace_exporter.otlp-http.tls` | 追踪 HTTP TLS：连接追踪收集器时的 TLS 设置：可以指定自定义 CA 证书，或同时提供客户端证书和私钥启用双向 TLS（mTLS）。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.trace_exporter.otlp-http.tls.ca-certificate` | 追踪 HTTP CA 证书：用于校验追踪收集器服务端证书的 CA 证书文件路径（PEM 格式）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.trace_exporter.otlp-http.tls.client-certificate` | 追踪 HTTP 客户端证书：双向 TLS 使用的客户端证书文件路径（PEM 格式），必须与 `client-private-key` 一起设置；设置后只允许 HTTPS 连接。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.trace_exporter.otlp-http.tls.client-private-key` | 追踪 HTTP 客户端私钥：双向 TLS 使用的客户端私钥文件路径（PEM 格式），必须与 `client-certificate` 一起设置。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.trace_exporter.otlp-grpc` | 追踪 OTLP gRPC 导出：通过 OTLP/gRPC 把追踪 span 发送到自己的收集器。必填 `endpoint`，可选 `headers` 和 `tls`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.trace_exporter.otlp-grpc.endpoint` | 追踪 gRPC 端点：接收追踪的 OTLP gRPC 收集器地址，例如 `https://otel.example.com:4317`。必填。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.trace_exporter.otlp-grpc.headers` | 追踪 gRPC 请求头：导出追踪时附带的额外请求头（gRPC 元数据），常用于向收集器认证。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.trace_exporter.otlp-grpc.headers.<name>` | 追踪 gRPC 请求头项：一条请求头：键为名称，值为发送的内容。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.trace_exporter.otlp-grpc.tls` | 追踪 gRPC TLS：连接追踪收集器时的 TLS 设置：可以指定自定义 CA 证书，或同时提供客户端证书和私钥启用双向 TLS（mTLS）。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.trace_exporter.otlp-grpc.tls.ca-certificate` | 追踪 gRPC CA 证书：用于校验追踪收集器服务端证书的 CA 证书文件路径（PEM 格式）。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.trace_exporter.otlp-grpc.tls.client-certificate` | 追踪 gRPC 客户端证书：双向 TLS 使用的客户端证书文件路径（PEM 格式），必须与 `client-private-key` 一起设置。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.trace_exporter.otlp-grpc.tls.client-private-key` | 追踪 gRPC 客户端私钥：双向 TLS 使用的客户端私钥文件路径（PEM 格式），必须与 `client-certificate` 一起设置。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `otel.tracestate` | W3C tracestate 字段：写入向外传播的 W3C `tracestate` 请求头的字段，写法为 `[otel.tracestate.<成员>]` 下的 `字段 = "值"`。Codex 把它编码成 `成员=字段:值;字段:值`，只更新这些字段、保留同一成员中的其他字段；格式无效的项会在启动时警告并忽略。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.tracestate.<name>` | tracestate 成员：一个 tracestate 成员（列表项的键），其下的字段会以分号分隔的 `字段:值` 形式合并成该成员的值。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `otel.tracestate.<name>.<name>` | tracestate 字段：成员中的一个字段：字段名不能含 `:`、`;`、`,`、`=`，值不能含 `,`、`=`、`;`。同名字段会覆盖上游传来的旧值。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |
| `responses_api_metadata` | Responses 请求元数据：随每个 Responses API 请求一起发送的自定义元数据（写入请求的 `client_metadata`）。最多 16 项；键须以字母开头、只含字母数字和 `_`、`.`、`-`、不超过 64 字节，值不超过 128 字节，Codex 保留的键会被拒绝；项目内的 `.codex/config.toml` 不能设置此项。 | map | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `responses_api_metadata.<name>` | 单个元数据项：一条元数据：键以字母开头，只含字母、数字、`_`、`.`、`-`（最长 64 字节），值为不超过 128 字节的字符串。 | string | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 分别说明分析、反馈、OTLP；按需要选择。 |

## 功能开关（290 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `features.agent_message_board` | 代理留言板：为同一代理树中的多个代理提供共享留言板工具（频道、主题帖、发帖、阅读与搜索），供子代理之间交流。仅在同时启用 `multi_agent_v2` 且会话会持久保存时生效；临时会话需另行配置内存或远程留言板。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.analytics_plan_history` | 分析页套餐历史：在终端界面（TUI）的 `/analytics` 页面中预览 ChatGPT 个人账号的 5 小时额度与每周额度使用历史。实验性功能，默认关闭，只影响 TUI 中的显示。 | boolean | false | 实验 / 未限定平台 | 实验 / false | 实验能力；保留按需选择并显示版本要求。 |
| `features.api_key_cyber_access_programs` | API key 网络安全计划：允许使用 OpenAI API key 登录的会话在请求中携带显式指定的网络安全访问计划（cyber access program，如 Daybreak），是否有权使用仍由服务端判定；关闭时，API key 会话显式指定计划会直接报错。ChatGPT 登录不受此开关影响，且只对内置的 `openai` 模型供应商生效；0.163 起默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.api_key_model_discovery` | API key 模型发现：使用 API key 认证时，从服务端拉取可用模型目录，用于模型选择等处；关闭后不使用拉取到的目录。只对未自定义 base URL 的官方 OpenAI 接口，或配置了 `model_catalog_url` 的模型供应商生效。0.161 起为稳定功能并默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.app_server_websocket_compression` | app-server 连接压缩：让 app-server 的 WebSocket 和 Unix 套接字（UDS）连接与客户端协商使用 zstd 压缩；未开启时不压缩。只影响 app-server 与客户端之间的传输。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.apply_patch_freeform` | 自由格式 apply_patch：已移除。早期版本用它向模型提供自由格式（freeform）的 `apply_patch` 编辑工具，该实现已删除；保留此键只为兼容旧配置，设置后被忽略。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.apply_patch_preserve_line_endings` | 补丁保留换行符：已移除。曾用于让 `apply_patch` 修改文件时保留原有的换行符（LF / CRLF）；现在补丁总是保留原换行符，此开关不再起作用。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.apply_patch_streaming_events` | apply_patch 流式进度：模型还在生成 `apply_patch` 参数时，就边解析补丁边向客户端推送结构化的进度事件，界面可以提前显示将要修改的文件。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.apps` | 应用连接器：启用应用连接器（Apps，即 ChatGPT 账号中的连接器），通过内置的 Codex Apps MCP 服务器把它们作为工具提供给模型，可用 `/apps` 管理。只在使用 ChatGPT 账号登录时生效，API key 登录无效。默认开启；旧键名 `connectors` 仍被接受。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.apps_mcp_path_override` | Apps MCP 路径覆盖：已移除。曾用于让内置的 Apps MCP 服务器改用新的访问路径；该过渡开关已删除，设置后被忽略。 | bool_or_table | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.apps_mcp_path_override.enabled` | Apps MCP 路径覆盖（已移除）：已移除：旧版用于覆盖 Apps MCP 路径的兼容开关，现在会被忽略，无需设置。 | boolean | null | 已移除 / 未限定平台 | 未单独实测 | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.apps_mcp_path_override.path` | Apps MCP 覆盖路径（已移除）：已移除：旧版 Apps MCP 路径覆盖所用的路径，现在会被忽略，无需设置。 | string | null | 已移除 / 未限定平台 | 未单独实测 | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.async_compaction` | 后台预压缩：在上下文用量达到前台自动压缩阈值之前，提前在后台准备远程压缩结果，到时直接采用，减少压缩上下文时的等待。默认在用量达到上下文窗口的 65% 时开始，可用 `[features.async_compaction]` 中的 `model_async_compact_threshold_percent`（1–100）调整；只在模型供应商支持远程压缩 v2 且未启用 `token_budget` 时生效。开发中，默认关闭。 | bool_or_table | false | 开发中 / 未限定平台 | 开发中 / false | 当前内核开发中且默认关闭；保持可选，不默认启用。 |
| `features.async_compaction.enabled` | 后台预压缩：开发中：开启 `async_compaction` 功能，在前台达到压缩阈值之前就在后台准备远程压缩，减少等待。仅对支持远程压缩的供应商（如内置 OpenAI）生效，开启 `token_budget` 时不生效。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.async_compaction.model_async_compact_threshold_percent` | 后台压缩触发百分比：上下文用量达到模型完整上下文窗口的该百分比时开始后台压缩，默认 65；取值 1–100，即使功能未开启也会校验该范围。 | integer；minimum=1.0，maximum=100.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.auth_elicitation` | 连接器授权提示：Codex Apps 应用连接器因未授权或授权失效而调用失败时，通过 MCP URL elicitation 向用户弹出授权或安装链接，而不是把错误直接交给模型；同时向 MCP 服务器声明客户端支持表单和 URL 两种 elicitation。审批策略为 `never` 时不会弹出。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.background_paginated_rollout_migration` | 会话记录后台迁移：在后台把旧格式的本地会话记录（rollout 文件）逐个迁移为分页历史格式，并把历史写入本地 SQLite。迁移过程保证任何时候要么保留原文件，要么留下可恢复的新文件。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.bedrock_setup_wizard` | Bedrock 设置向导：在终端界面（TUI）的登录引导中提供 Amazon Bedrock 设置选项。只在使用内置 `openai` 模型供应商、未显式设置 `model_provider` 且允许 API 登录方式时出现。0.164 起为稳定功能并默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.browser_annotation_api` | 网页标注接口：桌面 app 的管理开关：是否允许网页在桌面 app 中打开并定制标注工具，用户自己发起的普通标注不受影响。应由管理员在托管配置（requirements）中设置，而不是写在用户 config.toml 里；Codex CLI 本身不读取它。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.browser_use` | Browser Use 集成：桌面 app 的管理开关：是否允许在桌面 app 中使用 Browser Use 代理集成，让代理操作浏览器。应由管理员在托管配置（requirements）中设置，而不是写在用户 config.toml 里；Codex CLI 本身不读取它。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.browser_use_external` | 外部浏览器 Browser Use：桌面 app 的管理开关：是否允许 Browser Use 集成操作外部浏览器，而不只是桌面 app 内置的浏览器。应由管理员在托管配置（requirements）中设置；Codex CLI 本身不读取它。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.browser_use_full_cdp_access` | Browser Use 完整 CDP：桌面 app 的管理开关：是否允许 Browser Use 集成使用完整的 Chrome DevTools Protocol（CDP）能力。应由管理员在托管配置（requirements）中设置；Codex CLI 本身不读取它。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.chronicle` | Chronicle 屏幕记忆：启用 Chronicle 旁路进程，被动采集屏幕上下文并生成记忆。Codex 0.163 中除了接受此开关（旧键名 `telepathy`）外没有其他代码读取它，实际功能应由桌面 app 提供（依据源码推断）。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.cli_daybreak` | CLI Daybreak 控制：在 Codex CLI（终端界面和 `codex exec`）中启用 Daybreak：提供 `/daybreak` 开关，并在每个轮次按模型公布的访问计划自动选择标准计划或 Daybreak 网络安全计划（cyber access program）。Daybreak 需要 ChatGPT 账号或 OpenAI API key，且只能配合 OpenAI 模型供应商使用。0.163 起为稳定功能并默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.code_mode` | 代码模式：启用 JavaScript 代码模式：模型不再逐个直接调用工具，而是编写 JavaScript，在独立的代码模式宿主进程中通过 `exec` / `wait` 组合调用工具。模型元数据声明了工具模式时以模型为准，模型未声明支持时会警告可能降低效果；超时等细项在 `[features.code_mode]` 下配置。开发中，默认关闭。 | bool_or_table | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.code_mode.default_exec_yield_time_ms` | 代码模式让出时间：代码模式中 `exec` 调用的默认让出时间（毫秒）：脚本运行超过该时间仍未结束时，先把已有输出交给模型，脚本继续运行，之后可用 `wait` 取回结果。默认 30000（30 秒）。 | integer；minimum=0.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.code_mode.direct_only_tool_namespaces` | 仅直接暴露的命名空间：只作为模型直接工具提供的工具命名空间（需完全匹配）。这些工具不延迟加载，在仅代码模式的会话中也保留在顶层，并且不出现在代码模式的嵌套工具中。 | array | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.code_mode.enabled` | 启用代码模式：开发中：开启 `code_mode` 功能，让模型编写 JavaScript，在独立宿主进程的 V8 隔离环境中编排和组合工具调用（通过 `exec`/`wait` 工具）。本节其他设置只在开启后生效。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.code_mode.excluded_tool_namespaces` | 排除的工具命名空间：不在代码模式嵌套工具中提供的工具命名空间（需完全匹配）。 | array | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.code_mode.experimental_show_cell_overhead` | 显示单元耗时开销：实验性：在每个代码模式单元的返回结果中附上处理器耗时、代码模式宿主耗时和框架开销。该选项和返回格式随时可能变化或移除。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.code_mode.tool_input_schema_max_bytes` | 工具输入类型字节上限：代码模式中每个工具输入类型（渲染为 TypeScript 声明）允许的最大 UTF-8 字节数，最小值和默认值都是 16000；普通 MCP 工具至少取其服务器显式配置的输入上限。 | integer；minimum=1.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.code_mode_buffered_exec` | 代码模式缓冲执行：已移除。曾用于让代码模式 `exec` 调用的让出（yield）超时可配置；该开关已删除，设置后被忽略，默认让出时间现由 `[features.code_mode].default_exec_yield_time_ms` 设置。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.code_mode_host` | 代码模式宿主进程：在独立的宿主进程（`codex-code-mode-host`）中运行 JavaScript 代码模式；连接远程代码模式宿主时也必须开启。关闭后代码模式不可用，工具改为直接调用，除非 `[features.code_mode_host]` 中设置了 `disable_in_process_fallback = true`。默认开启。 | bool_or_table | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.code_mode_host.disable_in_process_fallback` | 禁用进程内回退：独立宿主进程不可用时，不回退到进程内运行代码模式，而是直接失败（fail-closed）。默认 `false`。 | boolean | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.code_mode_host.enabled` | 独立代码模式宿主：在独立宿主进程中运行 JavaScript 代码模式（`code_mode_host` 功能，稳定，默认开启）。 | boolean | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.code_mode_host_grpc` | 代码模式宿主 gRPC：Codex 与自己启动的代码模式宿主进程之间改用基于标准输入输出的 gRPC 通信，替代默认的通信方式。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.code_mode_interrupt` | 中断时终止代码单元：用户中断当前轮次时，同时终止代码模式中仍在运行的代码单元（cell），避免它们在后台继续执行。0.163 起为稳定功能并默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.code_mode_only` | 仅代码模式：把模型可见的工具限制为代码模式入口（`exec`、`wait`），其他工具只能在 JavaScript 中间接调用；开启它会自动同时开启 `code_mode`。模型元数据声明了工具模式时以模型为准。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.code_mode_only_strict_3p_tools` | 严格延迟第三方工具：只在“仅代码模式”下生效：符合条件的 MCP 工具、应用连接器工具和动态工具一律延迟加载，只能在 `exec` 中使用，并忽略 `deferLoading`、`omit_tools_from` 和命名空间跳过等设置。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.code_mode_prewarm` | 代码模式预热：在会话启动时就预先建立与代码模式宿主进程的连接，减少第一次执行代码时的等待。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.code_mode_tool_description_first` | 工具描述优先：代码模式中呈现工具定义时，把每个工具自身的描述放在其命名空间描述之前；工具搜索结果的呈现也随之改变。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.code_mode_tool_search` | 代码模式工具搜索：在 JavaScript 代码模式内部提供按相关度排序的工具搜索（`tool_search`），让模型在代码中查找延迟加载的工具。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.codex_apps_mcp_2026_07_28` | Apps MCP 新协议：让 Codex 自带的 Codex Apps MCP 服务器（应用连接器）使用 MCP 协议版本 2026-07-28，关闭时使用旧版协议；其他 MCP 服务器由 `mcp_2026_07_28` 控制。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.codex_git_commit` | Git 提交署名指引：已移除。早期版本通过模型指令加入 Git 提交署名（attribution）指引；该开关已删除，设置后被忽略。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.codex_hooks` | 生命周期钩子（旧名）：`[features].hooks` 的旧名称：启用从 `hooks.json` 加载的生命周期钩子（类似 Claude Code 的钩子）。该功能已稳定且默认开启，使用旧名会提示改用 `hooks`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.collab` | 子代理协作（旧名）：`[features].multi_agent` 的旧名称：启用多代理协作工具（终端界面中称为 Subagents），让模型可以派生子代理。该功能已稳定且默认开启，建议改用 `multi_agent`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.collaboration_modes` | 协作模式：已移除。曾用于启用协作模式（Plan 计划模式与 Default 默认模式）；现在协作模式总是开启，保留此键只为兼容旧配置。 | boolean | true | 已移除 / 未限定平台 | 已移除 / true | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.compaction_image_budget` | 压缩计入图片预算：远程压缩上下文时，把压缩后仍保留在历史中的图片计入上下文预算。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.computer_use` | Computer Use 电脑操作：桌面 app 的管理开关：是否允许使用 Codex Computer Use，让代理操作电脑界面。应由管理员在托管配置（requirements）中设置，而不是写在用户 config.toml 里；Codex CLI 本身不读取它。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.concurrent_reasoning_summaries` | 推理摘要顺序截断：请求推理摘要时，要求 OpenAI 以顺序截断（`SequentialCutoff`）方式交付推理摘要。只在内置 OpenAI 模型供应商且请求了推理摘要时生效。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.connectors` | 应用连接器（旧名）：`[features].apps` 的旧名称：启用 Apps 应用连接器。该功能已稳定且默认开启，但只在 ChatGPT 登录时可用；建议改用 `apps`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.content_item_kinds` | 内容条目分类：在发往 Responses API 的内部元数据中附带每个内容条目的分类；关闭时发送前清除这些分类。属于请求层面的内部开关，对使用体验没有直接影响。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.credential_masking` | 凭据遮蔽：沙箱内的命令只拿到遮蔽后的凭据占位值，真实凭据由已启用的网络代理在出站请求时注入，凭据来源在 `[features.network_proxy].credentials` 中配置。需要同时启用 `network_proxy` 并使用沙箱，关闭沙箱时不生效并给出警告。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.current_time_reminder` | 当前时间提醒：定期在模型可见的上下文中插入当前时间，并提供查询当前时间的工具。可在 `[features.current_time_reminder]` 中设置提醒间隔（`reminder_interval_seconds`，默认 1 秒）、时钟来源（`system` / `external`）、投递时机，以及是否提供可被打断的 `clock.sleep` 工具。开发中，默认关闭。 | bool_or_table | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.current_time_reminder.clock_source` | 时钟来源：当前时间提醒使用的时钟：`system`（默认，本机系统时间）或 `external`（由宿主提供的外部时钟，宿主未提供时会报错）。 | enum：system / external | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.current_time_reminder.delivery_mode` | 时间提醒发送时机：控制当前时间提醒可以在哪些推理请求前插入：`any_inference`（默认）或 `after_user_or_tool_output`。 | enum：any_inference / after_user_or_tool_output | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.current_time_reminder.enabled` | 启用当前时间提醒：开发中：开启 `current_time_reminder` 功能，定期把当前时间加入模型可见的上下文。本节其他设置只在开启后生效。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.current_time_reminder.reminder_interval_seconds` | 时间提醒间隔：两次当前时间提醒之间至少间隔的秒数，默认 1。 | integer；minimum=0.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.current_time_reminder.sleep_tool` | 提供 clock.sleep 工具：向模型提供可被用户输入打断的 `clock.sleep` 等待工具。默认 `false`。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.cwd_relative_turn_diffs` | 按工作目录显示差异：显示每个轮次的文件改动（diff）时，路径相对当前工作目录，而不是先探测 Git 仓库根目录再相对仓库根显示。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.daemon_auto_start` | 自动启动后台服务：交互式启动 `codex` 时自动启动或连接本机共享的后台服务（app-server 守护进程），多个终端会话共用它，可用 `/daemon` 管理。单次启动加 `--no-daemon` 可不使用；远程工作区不会自动启动。0.157 起为稳定功能并默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.default_mode_request_user_input` | 默认模式允许提问：允许模型在默认协作模式下也使用 `request_user_input` 工具向用户提出带选项的问题；不开启时只有计划模式等特定模式可用。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.defer_mailbox_preemption` | 延迟投递代理消息：多代理协作时，其他代理发来的消息不再打断当前正在生成的回复，模型可以继续完成推理和说明，消息改在下一个正常输入边界投递。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.deferred_executor` | 执行环境延迟就绪：允许在所选执行环境尚未启动完成时就开始轮次，并向模型提供 `wait_for_environment` 工具，用来等待该环境就绪。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.deferred_tool_world_state` | 延迟工具状态说明：在模型可见的“世界状态”上下文中列出延迟加载的工具命名空间（例如需要通过工具搜索才会加载的 MCP 工具），工具搜索本身的描述中则不再列出这些来源。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.elevated_windows_sandbox` | Windows 提权沙箱（旧）：已移除。曾用于启用 Windows 提权沙箱流程（安装 + 运行器）。未设置 `[windows].sandbox` 时，旧配置中的 `true` 仍会被解释为 `sandbox = "elevated"`；请改用 `[windows].sandbox`。 | boolean | false | 已移除 / windows | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.enable_experimental_windows_sandbox` | Windows 沙箱（旧名）：旧开关：未设置 `[windows].sandbox` 时，设为 `true` 等同于把 `[windows].sandbox` 设为 `unelevated`（受限令牌沙箱）。请直接使用 `[windows].sandbox`；仅 Windows 有效。 | boolean | 未提供 | 未标注 / windows | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.enable_fanout` | CSV 批量代理任务：已移除。曾用于启用基于 CSV 的批量代理任务（agent job）工具；这些工具已删除，设置后无效果。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.enable_mcp_apps` | MCP Apps 界面：启用 MCP Apps，即由 MCP 工具返回、在客户端中渲染的交互界面。Codex 核心只把它作为可在运行中刷新的 MCP 开关传递，实际界面由桌面 app 渲染（依据源码推断）。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.enable_request_compression` | 请求体压缩：向 Codex 后端发送流式请求时，用 zstd 压缩请求体以减少上传数据量。只在使用 ChatGPT 登录（走 Codex 后端）且模型供应商为内置 OpenAI 时生效。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 混合模型路由当前要求 false；按连接模式处理，勿统一打开。 |
| `features.exec_permission_approvals` | 命令申请额外权限：允许执行命令的工具在保持沙箱的前提下，为单条命令申请额外权限并交由审批。旧键名 `request_permissions` 仍被接受。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.executed_tool_call_metadata` | 工具调用元数据：在发往 Responses API 的内部元数据中记录模型尝试过的工具调用。属于请求层面的内部开关。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.executor_capability_discovery` | 执行端能力发现：通过一次高层的 exec-server RPC 发现所选根目录中的插件与技能清单（manifest），代替逐项扫描；文件系统受限的沙箱环境无论是否开启都会使用这种方式。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.executor_capability_discovery_v2` | 执行器能力发现 v2：通过 V2 发现协议获取并缓存所选执行环境（执行器）的能力清单，但不发布、也不启用其中的能力；开启后每个轮次开始时，MCP 相关组件会按新清单重新计算。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.experimental_use_unified_exec_tool` | 统一执行工具（旧名）：`[features].unified_exec` 的旧名称：使用单一的、基于 PTY 的统一命令执行工具。该功能已稳定且默认开启，建议改用 `unified_exec`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.experimental_windows_sandbox` | Windows 沙箱（旧）：已移除。曾用于在 Windows 上启用受限令牌沙箱。未设置 `[windows].sandbox` 时，旧配置中的 `true`（以及旧键名 `enable_experimental_windows_sandbox`）仍会被解释为 `sandbox = "unelevated"`；请改用 `[windows].sandbox`。 | boolean | false | 已移除 / windows | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.external_agent_memory_import` | 导入外部代理记忆：迁移外部代理配置时，允许从外部编程代理（如 Claude Code）的项目记忆目录导入 Markdown 记忆文件。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.external_migration` | 外部配置迁移提示：已移除。曾用于在启动时提示把外部代理的配置迁移到 Codex；现在保留为无效键，设置后被忽略，可改用 `/import` 从 Claude Code 导入。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.goals` | 会话目标：启用持久保存的会话目标：可用 `/goal` 为长时间任务设定目标，Codex 会在轮次结束后自动继续推进，直到目标完成。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.guardian_approval` | 自动审查审批：启用自动审查（Auto-review，“替我批准”）：审批请求交给自动审查子代理按风险评估后批准或拒绝，而不是询问用户。实际是否启用由 `approvals_reviewer = "auto_review"` 决定，在终端界面打开此开关会同时设置该项；托管配置中的 `auto_review` 也对应此开关。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.guardian_conversation_history_tools` | 审查读取对话历史：让自动审查（Guardian）可以使用根会话的消息历史工具，查阅主对话内容来判断审批请求。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardian_enhanced_node_repl_transcripts` | 审查包含 REPL 记录：在自动审查（Guardian）的审查材料中加入已完成的 `node_repl` 或 `cua_repl` 代码模式响应文本。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardian_ext` | Guardian 扩展原型：已移除。曾用于启用由扩展实现的同步 Guardian 审查器原型；该原型未被采用并已删除，设置后被忽略。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.guardian_node_repl_transcript_images` | 审查包含 REPL 图片：在自动审查（Guardian）的审查材料中同时加入已完成的 `node_repl` 或 `cua_repl` 代码模式响应里的图片；需要与 `guardian_enhanced_node_repl_transcripts` 一起开启。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardian_reuse_parent_compaction` | 审查复用父会话压缩：重新启动自动审查（Guardian）会话时，复用父会话加密的压缩结果；关闭时，父会话压缩后仍保留一份独立的审查记录。0.156 起为稳定功能并默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.guardian_root_handoff_context` | 审查限定交接上下文：多代理 v2 中子代理请求审批时，自动审查只把根会话中此前的沟通窗口作为根会话证据，而不是整段根会话。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardian_trust_orchestrator_connectors` | 信任编排器连接器：在 Guardian V2 中信任由插件服务编排器（plugin service orchestrator）提供的应用连接器身份，把这类连接器工具视为可信工具。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2` | Guardian V2 自动审查：启用第二代 Guardian 自动审批审查（包括异步评分与分类）。可在 `[features.guardianv2]` 下配置审查记录格式、审查范围等细项。开发中，默认关闭。 | bool_or_table | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.async_classifier_conversation_token_limit` | 分类器会话 token 上限：Guardian v2 后台分类器以 `conversation` 模式运行时会保留历史；下一次请求的估算 token 数超过该值时，清空保留的历史重新开始。默认 100000；单次请求仍受模型输入上限约束。 | integer；minimum=1.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.async_classifier_mode` | 后台分类器模式（已弃用）：已弃用，设置后被忽略并在启动时给出警告：Guardian v2 的后台风险分类现在总是使用持续的分类会话（相当于原来的 `conversation` 模式）。早期版本用它在 `snapshot` 和 `conversation` 两种模式之间切换。 | enum：snapshot / conversation | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.classifier_instructions` | 分类器提示词：自定义 Guardian v2 后台风险分类器的提示词，替换模型目录或内置的默认提示词。设置后若未同时设置 `review_threshold`，阈值默认改为 0.8（沿用旧的校准）。 | string | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.enabled` | 启用 Guardian v2：开发中：开启 `guardianv2` 功能。审批交给自动审查（`approvals_reviewer = auto_review`）时，后台持续给会话中的动作打风险分，低风险动作可被快速批准，其余仍走同步的 Guardian 审查。本节其他设置只在开启后生效。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.free_guardian` | 免费 Guardian（已弃用）：已弃用：仅为兼容旧配置保留，Guardian 的计费现由后端决定，此值不起作用。 | boolean | null | 开发中 / 弃用提示 / 未限定平台 | 未单独实测 | 保留已有值；核实替代写法后提供显式迁移。 |
| `features.guardianv2.max_action_tokens` | 待审动作 token 上限：提交给分类器的单个待审动作（工具名和参数）允许的最大 token 数；超过时该动作无法后台评分，会改走同步审查。范围 100–100000，默认 10000。 | integer；minimum=100.0，maximum=100000.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.max_classifier_instruction_tokens` | 分类器提示词 token 上限：分类器提示词（不含固定的记录来源说明）最多保留的 token 数，范围 100–100000；默认不截断，除非模型目录给出上限。 | integer；minimum=100.0，maximum=100000.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.max_parent_compaction_tokens` | 复用压缩结果 token 上限：复用主会话压缩结果时允许的最大 token 估算值，超过则视为无法复用；范围 100–100000，默认 25000。 | integer；minimum=100.0，maximum=100000.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.max_tool_call_lag` | 风险分允许滞后数：快速批准时，复用的风险分最多可以落后当前动作几个工具调用；超过则视为分数过期，改走同步审查。默认 2。 | integer；minimum=0.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.persist_scores` | 保存风险评分：把审查过的动作和风险分写入会话记录（rollout 文件），用于调试。默认 `false`。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.reasoning_effort` | 分类器推理强度：Guardian v2 后台分类器使用的推理强度，默认 `low`（模型目录可另给默认值）。 | string；min_length=1 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.reuse_parent_compaction` | 复用主会话压缩结果：是否把主会话最近一次（加密的）压缩结果作为分类器的上下文，默认 `true`。关闭后，主会话发生过压缩时会改走同步审查。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.review_scope` | 审查范围：`review_scope` 表：决定哪些类型的工具调用参与 Guardian v2 后台分类和快速批准。模型目录自带审查策略时以目录为准。 | table | null | 开发中 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `features.guardianv2.review_scope.computer_use_only` | 仅限浏览器和电脑操作：只对浏览器和电脑操作（computer use）类工具做后台分类和快速批准，其他工具仍走同步审查。默认 `true`。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.review_scope.sandboxed_exec_commands` | 包含沙箱内命令：把在沙箱中执行的 Shell 命令也纳入 Guardian v2 分类。默认 `false`，且只有 `computer_use_only = false` 时才生效。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.review_threshold` | 风险分阈值：风险分低于该值的动作可被快速批准，达到或超过则改走同步审查；取值 0.0–1.0，默认 0.5（自定义了分类器提示词时默认 0.8）。 | number；minimum=0.0，maximum=1.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.thread_context` | Guardian 会话上下文：已移除。曾用于让 Guardian V2 使用会话自有的上下文；现在该行为总是开启，`[features.guardianv2]` 中的 `thread_context` 会被忽略并提示删除。 | boolean | false | 已移除 / 弃用提示 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.guardianv2.transcript` | 分类器对话记录：`transcript` 表：控制提供给 Guardian v2 后台分类器的对话记录包含哪些内容，以及各部分的 token 上限。 | table | null | 开发中 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `features.guardianv2.transcript.include_images` | 包含截图：在对话记录中附上消息和指定工具输出里的近期截图。默认 `true`。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.transcript.max_message_entry_tokens` | 单条消息 token 上限：对话记录中每条消息最多保留的 token 数，范围 100–100000，默认 2000；不能大于 `max_message_transcript_tokens`。 | integer；minimum=100.0，maximum=100000.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.transcript.max_message_transcript_tokens` | 消息总 token 上限：对话记录中所有消息合计最多保留的 token 数，范围 100–100000，默认 10000。 | integer；minimum=100.0，maximum=100000.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.transcript.max_recent_non_user_entries` | 最近非用户条目数：对话记录中最多保留的最近非用户条目（助手消息、工具调用等）数量，正整数，默认 40。 | integer；minimum=1.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.transcript.max_tool_entry_tokens` | 单条工具记录 token 上限：每条工具调用或工具输出最多保留的 token 数，范围 100–100000，默认 1000；不能大于 `max_tool_transcript_tokens`。 | integer；minimum=100.0，maximum=100000.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.transcript.max_tool_transcript_tokens` | 工具记录总 token 上限：所有工具调用和工具输出合计最多保留的 token 数，范围 100–100000，默认 10000。 | integer；minimum=100.0，maximum=100000.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.transcript.sources` | 对话记录来源：除用户和助手消息外还要放入对话记录的内容：`tool_calls`（工具调用）、`tool_outputs`（工具输出）、`reasoning`（推理内容）。默认是前两项。 | array | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2.transcript_mode` | 对话记录格式：同步 Guardian 审查和后台评分共用的对话记录编码格式：`line`（默认）或 `json`。 | enum：line / json | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.guardianv2_decisions_comparison` | Decisions 对比测量：在 Guardian V2 旁同时运行 Decisions 分类，仅用于对比测量，不改变实际的审批结果。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.hooks` | 生命周期钩子：启用类似 Claude Code 的生命周期钩子：从 `hooks.json` 文件加载钩子，在会话、工具调用等生命周期事件发生时运行，可用 `/hooks` 查看和管理。默认开启；旧键名 `codex_hooks` 仍被接受。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.image_detail_original` | 原始分辨率图片：已移除。曾允许模型在支持的模型上以 `detail: "original"` 请求原始分辨率图片；现在保留为无效键，只为兼容仍传 `--enable image_detail_original` 的旧脚本。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.image_generation` | 图片生成：启用由扩展提供的图片生成工具，让模型可以生成图片。需要模型供应商支持图片生成，ChatGPT 免费套餐不可用。默认开启；旧键名 `imagegenext` 仍被接受。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.image_resize_notice` | 图片缩放提示：提示词中的图片被缩小后，告诉模型图片已被缩放并附上尺寸信息。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.imagegenext` | 图片生成（旧名）：`[features].image_generation` 的旧名称：启用基于扩展的图片生成。该功能已稳定且默认开启；同时写了 `image_generation` 时以后者为准，此键被忽略。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.in_app_browser` | 应用内浏览器：桌面 app 的管理开关：是否允许使用桌面 app 的内置浏览器面板。应由管理员在托管配置（requirements）中设置，而不是写在用户 config.toml 里；Codex CLI 本身不读取它。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.in_app_chat` | 应用内聊天：桌面 app 的管理开关：是否允许使用桌面 app 的内置聊天面板（ChatGPT 对话）。应由管理员在托管配置（requirements）中设置；Codex CLI 本身不读取它。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.in_app_dictation` | 应用内语音听写：桌面 app 的管理开关：是否允许在桌面 app 中使用语音听写输入。应由管理员在托管配置（requirements）中设置；Codex CLI 本身不读取它。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.in_app_local_automation` | 本地自动化任务：桌面 app 的管理开关：是否允许桌面 app 运行本地自动化任务（automations）。应由管理员在托管配置（requirements）中设置；Codex CLI 本身不读取它。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.in_app_updates` | 应用内更新：桌面 app 的管理开关：是否允许桌面 app 在应用内自行更新。应由管理员在托管配置（requirements）中设置；Codex CLI 本身不读取它。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.in_app_voice` | 应用内语音对话：桌面 app 的管理开关：是否允许在桌面 app 中使用语音（Voice）对话；允许并不代表一定可用，还取决于账号和服务端支持。应由管理员在托管配置（requirements）中设置；Codex CLI 本身不读取它。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.incremental_tools` | 增量工具定义：在世界状态中跟踪顶层工具定义，工具变化时只向模型发送增量的上下文更新，而不是整份工具列表。只对使用 Responses Lite 格式的模型生效。0.164 起为稳定功能并默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.instant_interrupt` | 即时打断：回复生成过程中用户发送新输入时，立即打断当前响应，并让前台的代码模式观察结果尽快让出，以便马上处理新输入。虽处于开发中，0.162 起的正式版默认开启（桌面 app 自带的 0.162.0-alpha.17.2 中默认关闭）。 | boolean | true | 开发中 / 未限定平台 | 开发中 / true | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.item_ids` | 响应条目 ID：已移除。曾用于开启响应条目 ID；现在条目 ID 总是开启，保留此键只为兼容旧配置。 | boolean | true | 已移除 / 未限定平台 | 已移除 / true | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.js_repl` | JavaScript REPL：已移除。曾提供基于常驻 Node 内核的 JavaScript REPL 工具；该功能已删除，设置后被忽略。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.js_repl_tools_only` | 仅 JS REPL 工具：已移除。曾用于只向模型直接暴露 `js_repl` 工具；随 JavaScript REPL 一起删除，设置后被忽略。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.local_thread_store_compression` | 会话记录压缩：压缩本地较冷（不再活跃）的会话记录（rollout 文件），包括共享历史，以节省磁盘空间。要求所有读取同一 Codex 主目录的程序都支持压缩格式；若同一个 `~/.codex` 还会被较旧的 Codex 读取（例如桌面 app 自带的旧版本），不要开启。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.local_thread_store_shared_compression` | 共享历史压缩：已移除。原先单独控制共享历史的压缩；现在统一由 `local_thread_store_compression` 控制所有会话记录文件。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.login_shell_package_path` | 登录 Shell 保留内置工具：登录 Shell 的启动脚本重置了 `PATH` 时，在该 Shell 内重新把 Codex 自带工具（如 ripgrep）所在目录加回 `PATH`，让命令仍能找到它们。只对 POSIX 登录 Shell 且未显式覆盖 `PATH` 时生效。实验性功能，默认关闭。 | boolean | false | 实验 / 未限定平台 | 实验 / false | 实验能力；保留按需选择并显示版本要求。 |
| `features.mcp_2026_07_28` | MCP 新协议版本：Codex 与 MCP 服务器通信时使用 MCP 协议版本 2026-07-28，关闭时使用旧版协议；内置的 Codex Apps 服务器另由 `codex_apps_mcp_2026_07_28` 控制。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.mcp_oauth_refresh_coordination` | MCP OAuth 协调刷新：MCP 服务器的 OAuth 令牌改由 MCP 客户端（RMCP）通过 Codex 配置的凭据存储来协调刷新，而不是沿用原有的刷新与保存流程。0.164 起为稳定功能并默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.mcp_tool_list_changed` | 响应 MCP 工具列表变更：MCP 服务器发出工具列表变更通知（`notifications/tools/list_changed`）时，在当前轮次内就刷新该服务器提供给模型的工具，而不是一直沿用启动时的列表；开启后也不再复用未跟踪变更的工具目录缓存。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.memory_tool` | 记忆功能（旧名）：`[features].memories` 的旧名称：启用启动时的记忆提炼和基于文件的记忆整合。默认关闭；建议改用 `memories`，具体参数见 `[memories]`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.mentions_v2` | 统一提及菜单：在终端界面（TUI）中启用统一的提及菜单：输入 `@` 时在同一个列表中选择插件、文件和技能。关闭后 `@` 只搜索文件，插件改回在 `$` 中列出。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.model_catalog_in_context` | 模型目录写入上下文：派生子代理时可选的模型列表改为写入只追加的上下文，而不是放在派生工具的描述里。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.multi_agent` | 子代理：启用子代理（协作工具）：模型可以派生子代理并行处理任务、向它们发送消息并等待结果，终端界面中可用 `/agents` 等命令管理。如果模型元数据或 `multi_agent_v2` 指定了多代理版本，以它们为准。默认开启；旧键名 `collab` 仍被接受。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_mode` | 多代理模式（旧）：已移除。保留为无效键只为兼容旧配置，设置后被忽略；多代理功能由 `multi_agent` 和 `multi_agent_v2` 控制。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.multi_agent_v2` | 多代理 v2：启用按任务路径（task path）路由的第二代多代理运行时，开启后不论模型如何声明都使用 v2。可在 `[features.multi_agent_v2]` 下配置并发数、等待超时、子代理指令等细项。稳定功能，默认关闭。 | bool_or_table | false | 稳定 / 未限定平台 | 稳定 / false | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.default_wait_timeout_ms` | 默认等待超时：调用 `wait_agent` 等待子代理而未指定超时时使用的默认值（毫秒），默认 30000；范围 0–3600000。 | integer；minimum=0.0，maximum=3600000.0 | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.disable_direct_message` | 禁用直接消息：不向模型提供多代理的直接消息工具（`send_message`、`followup_task`）；派生子代理和子代理结果的自动回传仍可用。默认 `false`。 | boolean | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.disable_native_tools` | 隐藏原生多代理工具：隐藏 Codex 自带的多代理工具，但保留多代理 v2 运行时，供宿主提供的工具使用。默认 `false`。 | boolean | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.enabled` | 启用多代理 v2：开启 `multi_agent_v2` 功能（稳定，默认关闭）：基于任务路径的新版多代理路由，开启后优先于 `[agents]` 中的 v1 设置。本节其他设置只在开启后生效。 | boolean | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.expose_spawn_agent_model_overrides` | 允许指定子代理模型：在多代理 v2 的 `spawn_agent` 工具上提供 `model` 和 `reasoning_effort` 参数，并在主代理和子代理的使用提示中加入相应说明。默认 `true`。 | boolean | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.hide_spawn_agent_metadata` | 隐藏子代理元数据：派生子代理时隐藏代理元数据：`spawn_agent` 的结果只返回任务名而不含昵称，工具说明中也不带继承模型的提示。默认 `true`。 | boolean | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.max_concurrent_threads_per_session` | 并发代理会话上限：每个会话同时打开的代理会话数上限，默认 4；未设置时若配置了 `agents.max_concurrent_threads_per_session`，则取该值加 1。 | integer；minimum=1.0 | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.max_wait_timeout_ms` | 最长等待超时：`wait_agent` 允许的最长等待时间（毫秒），默认 3600000（1 小时），范围 0–3600000。 | integer；minimum=0.0，maximum=3600000.0 | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.message_board_in_memory` | 内存留言板：把代理树共享的留言板保存在内存中（用于训练会话），临时会话也能使用。默认 `false`，即保存在本地状态数据库中。 | boolean | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.message_board_remote` | 远程留言板：改用按会话划分的远程留言板代替本地存储，需要填写 `url` 和凭据。留言板本身需开启 `agent_message_board` 功能。 | table | null | 稳定 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `features.multi_agent_v2.message_board_remote.bearer_token` | 留言板凭据：由运行时配置覆盖直接传入的远程留言板凭据（Bearer token）；也可改用 `bearer_token_env_var` 从环境变量读取。 | string | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.message_board_remote.bearer_token_env_var` | 凭据环境变量：从该环境变量读取远程留言板凭据，代替 `bearer_token`；变量不存在时会话启动会报错。 | string | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.message_board_remote.url` | 留言板地址：远程留言板服务的 URL（必填）。 | string | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.min_wait_timeout_ms` | 最短等待超时：`wait_agent` 允许的最短等待时间（毫秒），默认 10000，范围 0–3600000。 | integer；minimum=0.0，maximum=3600000.0 | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.multi_agent_mode_hint_text` | 多代理模式提示：自定义告诉模型何时使用多代理的模式提示，替换内置提示（内置提示默认只在用户明确要求时派生子代理，推理强度为 `ultra` 时改为主动派生）。 | string | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.non_code_mode_only` | 不放入代码模式：多代理工具只作为模型直接工具提供，不出现在代码模式的嵌套工具中。默认 `true`。 | boolean | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.preserve_fork_prefix` | 分叉时保留父会话前缀：完整分叉子代理时保留父会话中模型可见的历史，并在其后追加子代理指令。0.164 起默认 `true`；没有可复用上下文基线的旧式压缩仍会重建历史。 | boolean | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.root_agent_usage_hint_text` | 主代理使用提示：替换注入给主代理的多代理使用说明；设为空字符串则不注入。 | string | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.subagent_developer_instructions` | 子代理开发者指令：对没有角色专属指令的子代理，用这段开发者指令替换从父会话继承来的开发者指令。 | string | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.subagent_usage_hint_text` | 子代理使用提示：替换注入给子代理的多代理使用说明；设为空字符串则不注入。 | string | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.tool_namespace` | 工具命名空间：多代理 v2 工具所在的命名空间，默认 `collaboration`；只能使用字母、数字、`_` 和 `-`，长度 1–64。 | string；min_length=1，max_length=64 | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.usage_hint_enabled` | 使用提示开关（已弃用）：已弃用：仅为兼容旧配置保留，值会被忽略。 | boolean | null | 稳定 / 弃用提示 / 未限定平台 | 未单独实测 | 保留已有值；核实替代写法后提供显式迁移。 |
| `features.multi_agent_v2.usage_hint_text` | spawn_agent 使用提示：追加到 `spawn_agent` 工具说明末尾的使用提示，替换默认的代理角色说明。 | string | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2.wait_agent_enabled` | 提供 wait_agent 工具：向模型提供多代理 v2 的 `wait_agent` 工具，用于等待子代理的结果。默认 `true`。 | boolean | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.multi_agent_v2_dynamic_tools` | 子代理继承动态工具：多代理 v2 中新派生的子代理继承客户端定义的动态工具。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.multi_env_copy_file` | 跨环境复制文件：向模型提供 `multi_env_copy_file` 工具，在两个不同的执行环境之间按绝对路径复制单个文件，内容不经过模型上下文。两端各按自身权限读写，目标目录必须已存在，中途中断可能留下不完整的文件。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.network_proxy` | 网络代理：为已经拥有网络访问权限的沙箱会话启动受管网络代理，沙箱内命令的网络流量经它转发，并受 `[features.network_proxy]` 中域名允许 / 拒绝等规则的限制。实验性功能，默认关闭；开启后需要重启 Codex。 | bool_or_table | false | 实验 / 未限定平台 | 实验 / false | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.allow_local_binding` | 允许本地连接：允许在沙箱内启动本地服务并直接连接本机回环地址，同时跳过代理额外的内网目标检查；代理的域名规则仍然有效。在 MXC（Windows 原生沙箱）下默认 `true` 且无法强制为 `false`，其他情况默认 `false`。 | boolean | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.allow_upstream_proxy` | 允许上游代理：托管代理转发出站流量时，是否经由环境中配置的上游代理（如 `HTTPS_PROXY`）。默认 `true`。 | boolean | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.credential_broker` | 凭据代管：开启凭据代管：沙箱内命令只拿到同格式的占位凭据，代理在请求发往允许的地址时再换成真实凭据（会自动启用 MITM 解密）。默认 `false`；开启 `credential_masking` 功能也会启用它。 | boolean | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.credentials` | 代管凭据列表：按名称定义需要代管的凭据（`credentials.<name>`），每项说明凭据来自哪些环境变量、格式如何、可注入到哪些地址以及注入方式。 | map | null | 实验 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `features.network_proxy.credentials.<name>` | 单个代管凭据：一类由环境变量提供的凭据的定义，至少需要 `env`、`patterns` 和目标地址（`url_prefixes` 或 `url_prefix_from_env`）。 | table | null | 实验 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `features.network_proxy.credentials.<name>.auth` | 注入方式：凭据注入的方式，可多选：`bearer`（`Authorization: Bearer`）、`token`（`Authorization: token`）、`basic`（`Authorization: Basic`）、`header`（写入 `header` 指定的请求头）。未填写时为 `bearer`。 | array | [] | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.credentials.<name>.env` | 凭据环境变量：保存真实凭据的环境变量名列表，沙箱内看到的是替换后的占位值；不能与内置凭据来源重名。 | array | [] | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.credentials.<name>.header` | 自定义请求头：注入方式包含 `header` 时写入凭据的请求头名称；使用 `header` 方式时必填，否则不能填写。 | string | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.credentials.<name>.patterns` | 凭据格式：描述凭据格式的正则表达式列表，用于识别凭据并生成同格式的占位值；至少一项。 | array | [] | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.credentials.<name>.prefix` | 请求头值前缀：注入方式为 `header` 时放在凭据前面的前缀，只能与 `header` 一起使用。 | string | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.credentials.<name>.url_prefix_from_env` | 目标地址环境变量：从该环境变量读取一个额外允许注入的 URL 前缀或主机名；不能与 `env` 中的变量相同。 | string | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.credentials.<name>.url_prefixes` | 允许注入的地址：允许注入真实凭据的 URL 前缀列表。只写主机名时，回环地址按 HTTP 处理，其他主机按 HTTPS 处理。 | array | [] | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.dangerously_allow_all_unix_sockets` | 允许所有 Unix 套接字：危险：允许经代理访问任意本地 Unix 套接字（如 `docker.sock`），不再限于 `unix_sockets` 中允许的路径；此时代理只会监听回环地址。执行时默认 `false`。 | boolean | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.dangerously_allow_non_loopback_proxy` | 允许非回环监听：危险：允许代理监听非回环地址，使其他机器也能访问。默认 `false`，此时非回环地址会被改回 `127.0.0.1`；启用 Unix 套接字转发时此项无效。 | boolean | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.domains` | 域名规则：按域名模式设置访问规则，值为 `allow` 或 `deny`；同一模式规则冲突时 `deny` 优先。 | map | null | 实验 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `features.network_proxy.domains.<name>` | 单条域名规则：该域名模式的规则：`allow` 允许访问，`deny` 禁止访问。 | enum：allow / deny | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.enable_socks5` | 启用 SOCKS5：是否同时提供 SOCKS5 代理（监听 `socks_url`）。默认 `true`。 | boolean | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.enable_socks5_udp` | 启用 SOCKS5 UDP：是否允许通过 SOCKS5 转发 UDP。默认 `true`；`limited` 模式下 UDP 始终被阻止。 | boolean | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.enabled` | 启用托管网络代理：实验性：开启 `network_proxy` 功能，为沙箱会话启动托管网络代理，按本节规则控制命令的出站网络访问。本节其他设置只在开启后生效。 | boolean | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.mode` | 代理模式：`full`（默认）允许所有 HTTP 方法，HTTPS 直接建立隧道；`limited` 只允许 `GET`、`HEAD`、`OPTIONS`，并阻止 HTTPS CONNECT（除非启用 MITM）、SOCKS5 UDP 和非 HTTPS 的 SOCKS5 TCP。 | enum：limited / full | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.proxy_url` | HTTP 代理地址：托管 HTTP 代理的监听地址，默认 `http://127.0.0.1:3128`。 | string | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.socks_url` | SOCKS5 代理地址：托管 SOCKS5 代理的监听地址，默认监听 `127.0.0.1:8081`。 | string | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.network_proxy.unix_sockets` | Unix 套接字规则：按路径设置允许或禁止经代理访问的本地 Unix 套接字，值为 `allow` 或 `deny`；`allow` 的路径必须是绝对路径。 | map | null | 实验 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `features.network_proxy.unix_sockets.<name>` | 单条套接字规则：该 Unix 套接字路径的规则：`allow` 允许，`deny` 禁止。 | enum：allow / deny | null | 实验 / 未限定平台 | 未单独实测 | 实验能力；保留按需选择并显示版本要求。 |
| `features.non_prefixed_mcp_tool_names` | MCP 工具名去前缀：模型看到的 MCP 工具命名空间不再带旧的 `mcp__` 前缀。如果在 `[features.non_prefixed_mcp_tool_names]` 的 `server_names` 中列出服务器，则只有这些服务器去掉前缀。开发中，默认关闭。 | bool_or_table | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.non_prefixed_mcp_tool_names.enabled` | MCP 工具名不加前缀：开发中：开启 `non_prefixed_mcp_tool_names` 功能，模型看到的 MCP 工具名不再带旧的 `mcp__` 命名空间前缀；未填写 `server_names` 时对所有 MCP 服务器生效。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.non_prefixed_mcp_tool_names.server_names` | 不加前缀的服务器：只对这些 MCP 服务器的工具省略旧的 `mcp__` 前缀，其他服务器保持原样。 | array | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.nonfatal_clock_read_errors` | 时钟读取失败不中断：读取当前时间失败时（时间提醒、查询时间工具或 sleep 工具），把错误告诉模型，而不是让整个轮次失败。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.omit_app_server_notification_media` | 通知中省略媒体：app-server 发给客户端的条目通知中省略内联的图片和音频内容。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.output_token_replay` | 输出 token 重放：请求 OpenAI 同时返回加密的采样输出（`output.encrypted_content`），以便之后保留原 token 重放这些输出。只对内置 OpenAI 模型供应商生效。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.personality` | 人格选择：已移除。曾用于在终端界面中选择模型人格（personality）；现在保留为无效键，设置后被忽略，托管配置中的该项也会被跳过。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.plugin_hooks` | 插件钩子：已移除。曾用于单独启用插件自带的生命周期钩子；现在插件钩子随 `hooks` 与 `plugins` 一起生效，此键设置后被忽略。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.plugin_sharing` | 插件共享：启用远程插件的共享流程，例如共享插件、查看别人共享给自己的插件。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.plugins` | 插件：启用插件：可浏览、安装和管理插件，插件可以打包技能、MCP 服务器、应用连接器和钩子等，可用 `/plugins` 浏览。关闭后插件及其提供的 MCP 服务器都不会加载。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.powershell_shell_version` | PowerShell 版本信息：在提供给模型的环境信息中加入所选 PowerShell 执行宿主的主、次版本号。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.prefer_mxc` | 优先 MXC 沙箱：在 Windows 上，如果系统提供原生 MXC 沙箱且配置允许（`[windows].allow_mxc` 未设为 false），优先使用它，否则沿用原有的 Windows 沙箱。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.prevent_idle_sleep` | 运行时防止休眠：Codex 正在执行轮次时阻止电脑进入空闲休眠，轮次结束后恢复。实验性功能，默认关闭；由终端界面（TUI）实现。 | boolean | false | 实验 / 未限定平台 | 实验 / false | 实验能力；保留按需选择并显示版本要求。 |
| `features.psp` | PSP 路由：为发往 ChatGPT 的第一方请求附加 `oai-chat-psp=true` Cookie，让这些请求经 PSP 路由；PSP 的具体含义源码中没有说明。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.realtime_conversation` | 实时语音对话：已移除（0.164 起）。曾用于在终端界面（TUI）中开关实时语音对话（`/voice`）；现在实时语音对话是否可用不再取决于此开关，设置后被忽略。 | boolean | true | 已移除 / 未限定平台 | 已移除 / true | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.reasoning_effort_override` | 推理强度变更提示：会话中途切换推理强度时，在请求输入中追加一条可信的配置更新条目，把新的推理强度告知模型。只对内置 OpenAI 模型供应商且模型支持推理强度更新时生效。0.166 起为稳定功能并默认开启。 | boolean | false | 开发中 / 未限定平台 | 稳定 / true | 优先纠正目录：当前内核稳定、默认 true；无需显式开启。 |
| `features.recommended_plugins` | 推荐插件：在模型可见的上下文中加入推荐插件。Codex 0.163 中只有一个辅助判断函数引用它，未发现直接读取的代码；桌面 app 会按服务端实验下发此开关（依据源码推断）。稳定功能，默认关闭。 | boolean | false | 稳定 / 未限定平台 | 稳定 / false | 稳定项通常跟随默认；实验项按需验证。 |
| `features.remote_compaction_v2` | 远程压缩 v2：已移除。保留此键只为兼容旧配置，设置后被忽略；Codex 总会在 `x-codex-beta-features` 请求头中向 Responses API 声明支持远程压缩 v2。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.remote_control` | 远程控制：已移除。旧版本用它把 app-server 连接到 ChatGPT 远程控制服务；这个开关对应的旧实现已删除，设置后被忽略。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.remote_models` | 远程模型（旧）：已移除。旧的远程模型列表开关，保留只为兼容旧配置，设置后被忽略。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.remote_plugin` | 远程插件目录：启用由插件服务（PS）提供的远程插件目录，可以浏览和安装远程插件。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.request_permissions` | 申请额外权限（旧名）：`[features].exec_permission_approvals` 的旧名称（开发中，默认关闭）：允许命令执行工具在保持沙箱的前提下申请额外权限。建议改用新名称。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.request_permissions_tool` | 权限申请工具：向模型提供内置的 `request_permissions` 工具，让它主动申请额外权限；获批的权限可用于之后的命令。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.request_rule` | 申请执行规则：已移除。曾允许模型在请求审批时一并提议命令执行规则（exec rule）；现在不再被读取，设置后无效果。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.resize_all_images` | 统一缩放图片：已移除。曾用于在记录进历史前缩放所有内联图片；现在图片总是经过统一的预处理，保留此键只为兼容旧配置。 | boolean | true | 已移除 / 未限定平台 | 已移除 / true | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.respect_system_proxy` | 遵循系统代理：Codex 自己的网络客户端改为遵循系统代理设置：依次使用系统 / PAC / WPAD 配置、环境变量中的代理，最后直连。关闭时使用 HTTP 库（reqwest）的默认代理行为。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.responses_websockets` | Responses WebSocket（旧）：已移除。旧的 Responses API WebSocket 传输试验开关，设置后被忽略；现在是否使用 WebSocket 由模型供应商的 `supports_websockets` 决定。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.responses_websockets_v2` | Responses WebSocket v2（旧）：已移除。旧的 Responses API WebSocket 传输 v2 试验开关，设置后被忽略；现在是否使用 WebSocket 由模型供应商的 `supports_websockets` 决定。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.retain_client_developer_messages` | 保留客户端开发者消息：压缩上下文后，在新的上下文窗口中继续保留由客户端写入的开发者消息（developer message）。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.retain_client_tool_outputs` | 保留客户端工具输出：跨上下文窗口（例如压缩之后）时，按客户端的要求保留指定的工具输出。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.rollout_budget` | 会话 token 预算：在一个会话的所有代理会话（主代理与子代理）之间共享并跟踪 token 预算，剩余量越过设定阈值时提醒模型。可在 `[features.rollout_budget]` 中设置总额 `limit_tokens`、提醒阈值以及采样 / 预填 token 的权重。开发中，默认关闭。 | bool_or_table | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.rollout_budget.enabled` | 启用会话总预算：开发中：开启 `rollout_budget` 功能，为一个会话及其所有子代理统计共享的加权 token 预算，用完后会话以超出预算的错误结束（`enforce_limit = false` 时只提醒、不停止）。开启时必须同时设置 `limit_tokens` 和 `reminder_at_remaining_tokens`。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.rollout_budget.limit_tokens` | 预算总量：共享预算的加权 token 总量（正整数），开启该功能时必填。 | integer；minimum=1.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.rollout_budget.prefill_token_weight` | 输入 token 权重：计算预算时输入（预填充）token 的权重，非负数，默认 1.0。 | number；minimum=0.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.rollout_budget.reminder_at_remaining_tokens` | 剩余预算提醒点：剩余加权 token 降到这些值时各提醒模型一次；每个值都必须为正且小于 `limit_tokens`，开启该功能时必填。 | array | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.rollout_budget.sampling_token_weight` | 输出 token 权重：计算预算时模型输出（采样）token 的权重，非负数，默认 1.0。 | number；minimum=0.0 | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.runtime_agent_instances` | 运行时代理实例认证：为 OpenAI 请求认证注册一个临时运行时（runtime agent instance），请求时附带由它签名的运行时断言，`mode` 决定只发断言还是与原凭据一起发。依据源码推断：0.166.0-alpha.1 中这条认证路径尚未接入请求流程。开发中，默认关闭。 | bool_or_table | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.runtime_agent_instances.enabled` | 启用运行时代理实例：开发中：开启 `runtime_agent_instances` 功能，与在 `[features]` 中写 `runtime_agent_instances = true` 等效；断言的发送方式由同一表中的 `mode` 选择。 | boolean | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.runtime_agent_instances.mode` | 运行时断言发送方式：开启运行时代理实例后，请求如何携带运行时断言：`dual_header`（默认，原凭据与断言一起发送，用于迁移期）或 `assertion_only`（只发送断言作为请求凭据）。 | enum：dual_header / assertion_only | null | 开发中 / 未限定平台 | 未单独实测 | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.runtime_metrics` | 运行时指标：启用运行时指标快照（通过手动读取器采集），随 OpenTelemetry 遥测上报。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.search_tool` | 搜索工具（旧）：已移除。旧的搜索工具开关，保留只为兼容旧配置，设置后无效果。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.secret_auth_storage` | 加密保存登录凭据：登录凭据选择保存到系统钥匙串（keyring）时，改为把凭据加密保存在本地密钥文件中，只把该文件的密钥放进系统钥匙串。对以文件（`auth.json`）保存的凭据没有影响。Windows 上默认开启，其他平台默认关闭。 | boolean | false | 稳定 / 未限定平台 | 稳定 / true | 优先纠正目录：当前内核默认 true；按版本显示。 |
| `features.send_async_message` | 异步用户消息（旧）：已移除。曾按模型能力启用异步向用户发送消息；该开关已删除，类似能力见 `send_message_to_user_async`。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.send_message_to_user_async` | 异步向用户发消息：即使模型目录未声明支持，也为根代理提供 `send_message_to_user_async` 工具，让它在执行任务过程中异步向用户发送消息；子代理不提供。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.shell_snapshot` | Shell 环境快照：对用户登录 Shell 的环境做一次快照，之后执行命令时直接加载快照，而不必每次完整运行登录 Shell 的启动脚本，从而加快命令执行。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.shell_snapshot_v2` | Shell 快照 v2：把按策略过滤后的 Shell 快照完全保存在执行端内存中，不再写入快照文件。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.shell_tool` | Shell 工具：启用默认的 Shell 命令执行工具；关闭后模型不能再通过 Shell 工具执行命令。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.shell_zsh_fork` | zsh 执行桥（旧）：已移除。曾把 Shell 工具的执行改走打过补丁的 zsh 执行桥；该后端已删除，现在使用标准 Shell 执行器，设置后会提示删除此项。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.skill_env_var_dependency_prompt` | 技能环境变量提示：已移除。曾在技能缺少所需的环境变量时提示用户；该功能已删除，设置后被忽略。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.skill_mcp_dependency_install` | 技能 MCP 依赖安装：提及的技能依赖尚未安装的 MCP 服务器时，提示用户选择“安装”或“仍然继续”，并可直接完成安装。只在官方客户端中生效。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.skill_search` | 技能搜索实验：以影子模式（shadow mode）运行低开销的技能搜索方法并上报实验指标，不影响实际使用的技能。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.skip_host_skill_discovery` | 跳过主机技能发现：没有任何已注册组件需要时，跳过对主机技能的快照扫描。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.sleep_tool` | 等待（sleep）工具：允许注册内置的 sleep 工具，让模型可以等待一段时间（可被新输入打断）。默认由模型能力或 `current_time_reminder` 的设置决定是否提供，在 `[features.sleep_tool]` 中设 `mode = "always_on"` 则始终提供。默认开启。 | bool_or_table | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.sleep_tool.enabled` | 启用等待工具：是否允许注册内置的 sleep 等待工具（`sleep_tool` 功能，稳定，默认开启）。 | boolean | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.sleep_tool.mode` | 等待工具注册方式：`model_driven`（默认）按模型和旧的时钟配置决定是否提供等待工具；`always_on` 始终提供。 | enum：model_driven / always_on | null | 稳定 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.sqlite` | SQLite 元数据：已移除。曾用于把会话记录的元数据保存到本地 SQLite 数据库；此开关已不再被读取，设置后无效果。 | boolean | true | 已移除 / 未限定平台 | 已移除 / true | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.stable_environment_tools` | 提前声明环境工具：已移除（0.164 起）。曾用于在执行环境就绪之前就向模型声明环境类工具；现在 Codex 总是这样做，此开关不再起作用。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.standalone_web_search` | 独立联网搜索工具：提供由扩展实现的独立联网搜索工具。需要模型供应商支持联网搜索；使用 Responses Lite 格式的模型不论此开关都会使用它。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.steer` | 回复中插话：已移除。曾控制回复进行中按 Enter 是立即提交（插话引导当前轮次）还是排队等待；现在总是立即提交，保留此键只为兼容旧配置。 | boolean | true | 已移除 / 未限定平台 | 已移除 / true | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.step_model_switching` | 轮次中途切换模型：允许在轮次进行中修改模型、推理强度、推理摘要或服务档位，并从后续步骤开始生效；未开启时这类中途更新会被拒绝。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.subagent_default_context_limits` | 子代理默认上下文限制：子代理改用各自模型默认的上下文窗口和自动压缩阈值，不再继承父会话中设置的 `model_context_window` 与 `model_auto_compact_token_limit`。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.subagent_thread_environments` | 子代理继承会话环境：创建子代理时，让它继承父会话配置的执行环境，而不是父会话当前轮次临时选择的环境。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.system_proxy_fallback` | 系统代理兜底重试：未开启 `respect_system_proxy` 时，启动阶段的引导请求（如加载云端配置、登录时交换令牌）如果正常路由失败，就改经系统代理重试。只用于可安全重试的这些请求，不改变普通请求的路由。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.telepathy` | Chronicle（旧名）：`[features].chronicle` 的旧名称（开发中，默认关闭）：启用 Chronicle 旁路进程，被动采集屏幕上下文形成记忆。建议改用 `chronicle`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.terminal_resize_reflow` | 窗口缩放重排：已移除。曾控制终端窗口大小变化时是否重排对话滚动记录；现在总是重排，保留此键只为兼容旧配置。 | boolean | true | 已移除 / 未限定平台 | 已移除 / true | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.terminal_visualization_instructions` | 终端可视化指引：在终端界面（TUI）的开发者指令中加入终端专用的可视化指引，要求模型需要图示时使用紧凑的 ASCII 图、树、时间线或表格。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.tool_call_mcp_elicitation` | MCP 工具审批弹窗：MCP 工具调用需要审批时，通过 MCP elicitation 请求向客户端发起审批，而不是改用 `request_user_input` 提问（后者只能在根会话中使用）。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.tool_registry` | 工具注册设置：`[features.tool_registry]` 表：工具注册相关的设置。它不是功能开关，不能写成 `true`/`false`。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `features.tool_registry.error_on_tool_collisions` | 工具重名时报错：多个工具的实际名称相同时，让该轮次直接失败。默认 `false`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.tool_registry.turn_metadata_includes_tool_info` | 轮次元数据含工具信息：在每轮请求的元数据中附上权威的工具信息。默认 `false`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.tool_search` | 工具搜索（旧）：已移除。现在工具搜索（`tool_search`）总是开启，保留此键只为兼容旧配置。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.tool_search_always_defer_mcp_tools` | MCP 工具总是延迟：已移除。曾让 MCP 工具总是通过工具搜索延迟加载，而不是直接暴露少量工具；现在只要工具搜索可用，MCP 工具总是延迟加载。 | boolean | true | 已移除 / 未限定平台 | 已移除 / true | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.tool_suggest` | 工具推荐：启用可发现的工具建议：模型可以列出尚未安装的插件或应用连接器，并请求用户安装。需要同时开启 `apps` 与 `plugins`。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.transcript_v2` | 全屏对话记录（旧）：已移除。曾用于开启全屏对话记录，设置后无效果并会给出提示；请改用 `[tui].fullscreen_transcript`。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.tui_app_server` | TUI 使用 app-server：已移除。终端界面（TUI）现在总是基于 app-server 实现，保留此键只为兼容旧配置。 | boolean | true | 已移除 / 未限定平台 | 已移除 / true | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.unavailable_dummy_tools` | 不可用工具占位：已移除。曾为历史记录中已不可用的工具调用补充占位工具；该功能已删除，设置后被忽略。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.unbounded_connection_retries` | 断网持续重连：采样请求因网络连接失败时，不在重试次数用尽后报错，而是保留当前轮次并持续等待网络恢复后重连（界面显示 Reconnecting... waiting for network）。只针对连接失败，不针对服务端返回的错误状态，也不适用于 Amazon Bedrock。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.undo` | 撤销（幽灵提交）：已移除。曾在每个轮次创建一个幽灵提交（ghost commit）以支持撤销；现在保留为无效键，只为让旧配置中的 `undo` 能被解析。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.unified_exec` | 统一执行工具：使用基于 PTY 的统一命令执行工具（`exec_command` 与 `write_stdin`），支持长时间运行和交互式进程。用户配置无法关闭它，旧的关闭写法会被忽略，只有托管配置（requirements）能禁用，禁用后命令改为一次性执行。默认开启；旧键名 `experimental_use_unified_exec_tool` 仍被接受。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.unified_exec_tty` | 交互式终端（TTY）：允许统一执行工具为命令分配交互式终端（TTY），以运行需要终端交互的程序；关闭后请求 TTY 的命令会被拒绝。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.unified_exec_zsh_fork` | 统一执行 zsh 桥（旧）：已移除。曾允许统一执行工具与打过补丁的 zsh 执行桥组合使用；该后端已删除，设置后会提示删除此项。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.unified_image_budget` | 统一图片预算：对每张图片使用统一的像素与 token 预算进行预处理，不再依据旧的 detail 提示。只对使用 Responses Lite 格式或支持原始图片细节的模型生效。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.use_agent_identity` | 代理身份认证：使用 ChatGPT 登录的会话改用 Agent Identity（代理身份）认证策略，而不是只用 JWT。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.use_legacy_landlock` | 旧版 Landlock 沙箱：已弃用。在 Linux 上改用旧的 Landlock 沙箱回退方案，而不是默认的 bubblewrap 沙箱；即将移除，使用时会给出提示。默认关闭。 | boolean | false | 弃用 / linux | 弃用 / false | 本项目 Windows 客户端不适用；资料保留。 |
| `features.use_linux_sandbox_bwrap` | bubblewrap 沙箱（旧）：已移除。曾用于在 Linux 上选择启用 bubblewrap 沙箱；现在 bubblewrap 已是默认方案，此键保留为无效项，只为兼容旧脚本和配置。 | boolean | false | 已移除 / linux | 已移除 / false | 本项目 Windows 客户端不适用；资料保留。 |
| `features.use_xaa` | MCP 企业令牌授权：对配置了企业托管认证（`mcp_enterprise_managed_auth`）的 MCP 资源启用企业刷新令牌授权（XAA）。必须在用户级配置或托管配置中开启，项目级 `.codex/config.toml` 中的设置无效。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.view_image` | 本地图片查看：启用内置的本地图片查看工具，让模型可以把本地图片文件加载进上下文查看。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.wake_on_agent_messages` | 代理消息唤醒会话：根会话的上一轮次结束、处于空闲时，收到其他代理（如子代理）发来的消息也会自动开始新的轮次，而不必等待用户输入。只对根会话生效，且当前协作模式须允许自动开始轮次。开发中，默认关闭。 | boolean | false | 开发中 / 未限定平台 | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.web_search` | 联网搜索（旧开关）：已弃用：旧的联网搜索开关，联网搜索现已默认开启（`cached` 模式）。设为 `true` 且未设置顶层 `web_search` 时改用 `live` 实时搜索，设为 `false` 并不会关闭搜索；请改用顶层 `web_search`（`live`、`indexed`、`cached` 或 `disabled`）。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 稳定项通常跟随默认；实验项按需验证。 |
| `features.web_search_cached` | 缓存联网搜索（旧）：已弃用。曾允许模型发起读取缓存内容的联网搜索；未设置顶层 `web_search` 时仍会被解释为 `web_search = "cached"`（优先于 `web_search_request`）。请改用顶层 `web_search`（`live`、`indexed`、`cached` 或 `disabled`）。 | boolean | false | 弃用 / 未限定平台 | 弃用 / false | 保留已有值；核实替代写法后提供显式迁移。 |
| `features.web_search_request` | 实时联网搜索（旧）：已弃用。曾允许模型发起读取实时内容的联网搜索；未设置顶层 `web_search` 时仍会被解释为 `web_search = "live"`，`[features]` 中的旧键名 `web_search` 也对应此项。请改用顶层 `web_search`。 | boolean | false | 弃用 / 未限定平台 | 弃用 / false | 保留已有值；核实替代写法后提供显式迁移。 |
| `features.windows_sandbox_service` | Windows 沙箱服务：在 Windows 上设置提权沙箱时，尝试通过已安装的系统服务完成提权配置。开发中，默认关闭。 | boolean | false | 开发中 / windows | 开发中 / false | 开发中；默认不推荐开启，明确需要时隔离验证。 |
| `features.workspace_dependencies` | 工作区依赖：启用工作区依赖支持：桌面 app 为本地会话准备内置的运行时依赖，并提供 `load_workspace_dependencies` 工具。此开关由桌面 app 读取，Codex CLI 本身不使用。默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |
| `features.workspace_owner_usage_nudge` | 工作区所有者用量提醒：已移除。曾在终端界面中显示面向工作区所有者的用量提醒文案；现在该提醒总是开启，保留此键只为兼容旧配置。 | boolean | false | 已移除 / 未限定平台 | 已移除 / false | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.worktrees` | 托管工作树：已移除（0.164 起）。曾用于开关托管的 Git worktree（`/worktree`）；现在该功能是否可用不再取决于此开关，设置后被忽略。 | boolean | true | 已移除 / 未限定平台 | 已移除 / true | 历史兼容资料；不作为新配置或启用功能的依据。 |
| `features.write_stdin_approval` | 写入终端需审批：向以提权（沙箱外）方式启动的统一执行终端写入新的输入前，需要再次审批，避免借已获批的终端执行未经审批的命令。0.158 起为稳定功能并默认开启。 | boolean | true | 稳定 / 未限定平台 | 稳定 / true | 稳定项通常跟随默认；实验项按需验证。 |

## 其他（6 条）

| 路径 | 名称 / 原目录说明 | 类型 / 取值 | 目录默认 | 目录状态 / 平台 | 当前内核特性 | 优化建议 |
| --- | --- | --- | --- | --- | --- | --- |
| `cloud` | 云端功能设置：`[cloud]` 表：由云端提供的功能的设置，目前包含 `cloud.skills`（云端技能）。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `cloud.skills` | 云端技能：控制是否发现云端技能并提供给模型。默认允许，但只有运行 Codex 的宿主程序提供了云端来源时才会实际出现。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |
| `cloud.skills.enabled` | 启用云端技能：设为 `false` 可禁止发现和使用云端技能；未设置时默认允许（`true`）。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 明确用途与适用客户端后再写入。 |
| `daybreak` | 默认启用 Daybreak：新会话和非交互轮次（如 `codex exec`）默认是否选用 Daybreak，即 OpenAI 面向网络安全工作的模型访问计划（cyber access program）。默认 `false`；启用后要求使用 `openai` 模型供应商、已登录 ChatGPT 或使用 OpenAI API Key，且所选模型提供 Daybreak，否则会报错；该选项本身不授予访问资格。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 明确用途与适用客户端后再写入。 |
| `experimental_use_unified_exec_tool` | 统一执行工具（旧开关）：旧版顶层开关，等同于 `[features].unified_exec`：启用或关闭单一的、基于 PTY 的统一命令执行工具。该功能已稳定且默认开启，使用此键会提示已弃用，建议改用 `[features].unified_exec`。 | boolean | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 明确用途与适用客户端后再写入。 |
| `features` | 功能开关表：`[features]` 表：集中管理各项功能开关，键为功能名，值为 `true`/`false`，部分功能可写成带参数的子表。推荐用它代替各处单独的旧开关；未知的键会被忽略并记录警告。 | table | 未提供 | 未标注 / 未限定平台 | 未单独实测 | 按子项或实际命名实例编辑；不直接写父表值。 |

## 本次官方 schema 新增待评估字段（20 条）

以下是结构差异，不建议一次性全部开放或启用。旧 `profiles.<name>.*` 的 368 条兼容展开路径单独排除；新 profile 应按独立文件支持。

- `auto_review.timeout_ms`
- `features.critical_path_timing`
- `features.guardian_native_multi_agent_history`
- `features.guardian_recorded_items`
- `features.guardianv3`
- `features.guardianv3.enabled`
- `features.mcp_connection_sharing`
- `features.omit_skill_descriptions`
- `features.plugin_discovery_prefetch`
- `features.prediction_preserve_parent_tools`
- `features.rollout_budget.enforce_limit`
- `features.simplified_selection_hints`
- `features.skill_catalog_deltas`
- `features.tui_multi_backend`
- `features.untrusted_as_tool_output`
- `memories.consolidation_service_tier`
- `model_providers.<name>.credential_file`
- `model_providers.<name>.location`
- `model_providers.<name>.project_id`
- `tui.keymap.global.select_transcript`

## 当前内核另有未收录的特性（13 个）

这组与上表重叠；其中 `features.artifact` 仅见当前内核特性列表，未见本次公开 schema，先显示为诊断信息。

- `features.artifact`
- `features.critical_path_timing`
- `features.guardian_native_multi_agent_history`
- `features.guardian_recorded_items`
- `features.guardianv3`
- `features.mcp_connection_sharing`
- `features.omit_skill_descriptions`
- `features.plugin_discovery_prefetch`
- `features.prediction_preserve_parent_tools`
- `features.simplified_selection_hints`
- `features.skill_catalog_deltas`
- `features.tui_multi_backend`
- `features.untrusted_as_tool_output`

## Dock、手机与安卓设置

它们与 Codex TOML 属于不同保存范围，见 [运行设置清单](runtime-settings-inventory-2026-11-02.md)。
