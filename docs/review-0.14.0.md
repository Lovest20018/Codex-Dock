# 0.14.0 Codex 工作台审查

## 范围与复用

按“仅补齐 Codex 的全部功能”接入固定上游 Codex Mobile Bridge 2.1.7 / Android build 40，提交 `0721aa7dafa6177751ba7e568e0ad0efee05655b`。完整 Codex 服务和 Web 直接复用，旧精简 service/web/bridge 移除。新建、项目、分支、整理、模型/权限、队列、审批/提问、富文本、附件、文件、Git、终端、CLI / WSL / SSH、账号、通知和安卓协助更新使用上游实现，仍受原生执行端能力限制。

保留 Dock 已有单一 HTTPS 域名、IPv4 监听、本机控制和 WPF 配置方式。其他 AI 提供方禁用；外部反代、证书、FRP 与其他网络部署仍由使用环境管理。程序不自动改路由器或真实账号，不为阅读历史启动执行端。

原始清单记录 1,265 个文件，省略不使用的公开 NAS `.env.example` 模板；1,264 个实际文件逐一核对。只有五个显式覆盖：HTTP 服务的可选 manager factory、可选提供方范围、保留已有授权的登录限制、旧草稿导入和导入触发点。原始/覆盖 SHA-256 在冻结前核对；暂存源码字节也与清单逐一核对，`.gitattributes` 阻止 Windows 改写快照换行。

## 管理及授权

电脑“管理 Codex 接入”复用上游 AccountsPanel、CliAccessPanel、WslAccountsPanel，仅在私有回环端口托管。60 秒一次性 fragment 在交换前从地址清除；成功后使用 30 分钟 HttpOnly / SameSite Cookie、同源和 CSRF 校验。只调度 accounts/account/cliAccess/clients 白名单。公网保留原上游登记限制，无通用桌面 RPC。

0.13.3 修复保留：五次错误限制新登录和配对，有效授权仍可使用；重启与解除限制不清除 Cookie，显式撤销、IP 规则及过期继续生效。DockAuth 适配明确忽略全部转发 IP 头，包括回环代理，避免上游默认信任本机代理改变现有来源语义。HTTP 回归用轮换伪造 X-Forwarded-For 验证限制不能绕过。

## 升级与运行时

worker 协议 4，桌面读取旧 1/2/3 设置并保留凭据与实例。旧 operations.json 以幂等方式迁移到上游 submissions.json，未知/已接受回执均保留，原文件不删，不触发任何发送。浏览器旧草稿只在已核验网关及账号下经用户确认导入，未知 UUID 保留，不能跨账号自动复制。

冻结版带 certifi 根证书、锁定微软 ConPTY 及三种 OpenConsole、经编译/自检的 Codex 退出助手、上游远端注入 Python 源码和完整 Web/vendor 许可。ConPTY 官方 NuGet v2 下载曾 TLS 超时，改从官方 v3 地址取得同一锁定包，SHA-256 与上游一致；没有修改依赖锁。

## 本地验证

- 25 项 Python 适配/迁移/认证回归通过，包括真实完整 HTTP 服务、Codex-only API、本机管理白名单、Host/Origin/CSRF、共享来源限制和重启授权。
- 81 项核心回归通过；实际冻结 EXE 验证完整资源、通知、仅 Codex 导航、管理入口、IPv4 映射、五次错误、重启、解除限制与撤销。
- 上游 Node 1,239 项和 Dock 草稿迁移 2 项通过。
- 原上游宽屏/手机屏浏览器 66 项通过。初次夹具 HTTP/1.0 在 Windows Chrome 出现连接重置，测试包装层使用与生产一致的 HTTP/1.1；未修改原场景和断言。
- 真实隔离 worker 的管理页、完整登录、原 APK 注入脚本、窄屏/暗色与 QR 共 5 组检查通过，独立 QR 解码核对原链接。
- 上游终端 WebSocket 4 项通过；原生 ConPTY HTTP 验收通过认证/CSRF、中文扩展路径、重开保持 cwd/环境、resize、输出、Ctrl+C 与关闭。
- 实际旧 0.13.3 EXE → 新 EXE 升级通过：旧安卓 Cookie、未知/已接受记录、再次重启幂等及原配置不变。
- 上游 Codex 相关 53 个模块共 836 项在独立解释器运行：711 项通过、125 项因平台或权限跳过、零失败，结果记录在 `artifacts/upstream-codex-tests.log`。无符号链接权限及 Unix 专有夹具明确跳过；Windows junction 边界另有原生用例验证。WSL 上传竞态夹具使用固定发行版列表，避免本机 WSL 服务等待超过夹具期限；原场景与断言保留。原完整多提供方 suite 含旧自动撤销断言和平台夹具，不宣称其全量通过。

核心和 WPF 编译零警告、零错误。冻结后与发布时重新核对来源，发布脚本对实际发布 EXE 强制运行桌面 UI 回归；隔离安装/卸载验证核对全部文件和用户文件保留。最终结果以包内 `verification/ui-report.json`、manifest 及包旁 installer/archive verification 为准。

全部测试使用虚构凭据与临时配置目录。未向当前真实聊天发送消息，也未切换真实账号或更改当前网关/反代。安卓真机、公网链路、实际 SSH / WSL 环境和真实模型任务仍需现场验收；本地协议和界面回归不替代这些条件。
