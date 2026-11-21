# 开发与构建

Codex Dock 默认界面使用 C#、.NET 8、WinUI 3 和 Tomlyn，保留 WPF 兼容源码；独立手机 worker 使用 Python 3.12、完整上游 Codex 模块及 certifi。面向使用者的安装与连接方式见 [README](../README.md)。

0.19.0 起，`scripts/package.ps1` 发布 WinUI 并使用 `InstalledBuild=true` 编译为 `CodexDock.exe`。正式安装包包含同次编译的 XBF、PRI 和完整运行库。正式构建排除 WinUI 冒烟入口和测试替身。Debug、界面预览构建或设置 `WinUiSmokeBuild=true` 的开发构建启用隔离冒烟入口。

发布验证使用两份报告。实际发布与安装后的 EXE 在独立配置目录验收页面加载、托盘与安装器请求退出，记录真实产物散列。完整界面回归使用独立测试构建，核对同版本和共享 Application 程序集散列。两份报告随便携包的 `verification/` 一起交付。

## 本地构建

需要 .NET 8 SDK；生成安装器另需 Inno Setup 6。构建手机 worker 需要 Windows x64 Python 3.12，首次创建隔离构建环境并安装锁定工具：

```powershell
pwsh -NoProfile -File scripts/build-mobile.ps1 -Python 'C:\path\to\Python312\python.exe' -InstallBuildTools
```

之后 `scripts/build-mobile.ps1` 复用 `artifacts/mobile-build-venv`。先构建 worker，再运行 Windows 测试。手机 worker 会复制到应用的 `mobile` 子目录。终端使用 PowerShell 7。

```powershell
dotnet build tests/CodexConfig.Tests/CodexConfig.Tests.csproj -c Release
& .\tests\CodexConfig.Tests\bin\Release\net8.0-windows\CodexConfig.Tests.exe
dotnet test tests/CodexConfig.UnitTests/CodexConfig.UnitTests.csproj -c Release --logger trx --results-directory artifacts/test-runs/unit
pwsh -NoProfile -File scripts/test-windows-ui.ps1
```

核心和 UI 检查使用虚构凭据、隔离配置目录及测试环境变量。UI 检查覆盖布局、草稿、模板、备份恢复、托盘、开机启动和更新入口；缩放使用布局变换模拟。

更新解析与校验、网关诊断投影及事件解析用例已迁入 xUnit。使用 `dotnet test --filter` 按测试名筛选，CI 保留 TRX 报告。原有运行器继续负责进程、系统与安装兼容性夹具。

### WPF 兼容版

WPF 的目标维护截止日为 **2027 年 2 月 28 日**。达到目标日期前，WinUI 稳定版需要覆盖受支持的 Windows 10 与 Windows 11 环境，确认安装、配置草稿、加密备份恢复、托盘退出和两个网关的完整路径。用连续两个稳定版本的回归及问题记录核对该条件，并为仍需兼容版的用户保留最近可用构建及回退说明。未满足条件时延后下线。

在兼容期内，Core 和 Application 的每次改动都通过 `Regression checks` 同时验证 WinUI 和 WPF，报告保存在同一次 CI 的 `windows-test-reports`。本地可在同一次 `scripts/test-windows-ui.ps1` 运行中依次跑两个界面。

Windows 界面和安装器的隔离输出集中在 `artifacts/test-runs/`。测试结束后，运行 `pwsh -NoProfile -File scripts/clear-test-runs.ps1 -WhatIf` 查看清理范围。移除 `-WhatIf` 后执行清理，加入 `-IncludeLegacy` 可一并清理 `legacy/` 中归档的旧报告。

手机 Python 回归：`py -3.12 -B -m unittest discover -s tests/mobile -p 'test_*.py' -v`。浏览器 QA：先在 `tests/mobile` 执行 `npm ci`，再从仓库根执行 `node tests/mobile/browser-smoke.cjs`；需要本机 Chrome，可用 `CODEX_DOCK_TEST_PYTHON` 指定 Python 绝对路径。测试创建独立浏览器资料和临时 Codex 数据目录；HTTPS 请求仅转到隔离 worker。上游浏览器用例通过 `tests/mobile/playwright.config.cjs` 复用，Windows 夹具使用生产服务相同的 HTTP/1.1。上游 Node / Python 原始用例保存在 `src/CodexMobile/upstream/tests`。回环套接字和测试子进程需要在环境许可下运行；不要禁用这些必要检查来生成发布包。

`node --test tests/mobile/upstream-contract.browser.cjs` 使用 Playwright 检查上游网页真实渲染的选择器，以及本机管理页依赖的账号面板 DOM 和方法。升级上游时，CI 会列出缺失或重复的选择器及改名的成员。可以用 `CODEX_DOCK_TEST_CHROME` 指定 Chrome，或在 `tests/mobile` 执行 `npx --no-install playwright-core install chromium` 使用 Playwright 配套浏览器。

Lovest Code 的 `MainActivity` 管理原生外壳和生命周期。电脑列表、聊天 WebView、通知收件箱、更新和下载使用独立控制器。聊天桥接沿用现有 prompt 协议，按 WebView 的随机令牌定位处理函数，并核对消息来源与当前页面的同源关系。焦点、页面和载荷限制由各动作保留。若采用 `WebMessagePort`，需要同步调整页面脚本，处理导航时的端口关闭、重新绑定和来源校验。

设置 `CODEX_CONFIG_TEST_CLI` 为原生 `codex.exe` 的绝对路径，可加入三项原生兼容检查：内置 `openai`、旧 provider，以及 `config/read` 项目来源层和 profile 支持边界。前两项只使用本机模拟上游与虚构官方登录；来源读取不创建模型任务，不消耗真实模型额度。网关进程用例使用测试程序自身的专用入口，界面用例使用实际 CodexDock 程序。Windows 用户加密、注册表和回环子进程检查需在正常用户上下文运行。

运行 `pwsh -NoProfile -File scripts/verify-superba-reuse.ps1` 可构建并运行核心和隔离 WPF 检查，结果保存在 `artifacts/test-runs/wpf-superba-*`。传入 `-CodexCli 'C:\path\to\codex.exe'` 可加入原生兼容检查。

定向检查可设置 `CODEX_CONFIG_TEST_FILTER='desktop'`（多个名称片段用分号分隔）；输出会注明筛选范围，不替代完整回归。`CODEX_CONFIG_UI_TEST_FILTER='desktop-restart'` 仅验证已保存结果页的重启操作和布局，使用已存在的明文夹具及模拟生命周期，可在无法使用用户加密的环境运行；完整 WPF 检查仍负责真实加密保存链路。两种筛选默认均关闭，完整验证脚本会暂时清除并在结束时恢复它们。

连接和网关专项使用 `CODEX_CONFIG_UI_TEST_FILTER='connection-mode'` 或 `'gateways'`。前者覆盖双向切换、暂存登录、确认写入、备份和布局；后者实际启动与停止两个隔离后台进程，覆盖独立自动启动、草稿保留、手动停止、失败隔离与窗口布局。`'login'` 验证官方文件读取、显式恢复加密备份、令牌优先级与草稿保留，不启动登录进程或设备码流程。启动设置按配置目录保存于 `%LOCALAPPDATA%\CodexConfig\gateways\<目录标识>\startup.json`，仅含两个布尔选项；测试改用输出目录中的 `gateway-preferences`，Windows 启动项使用内存替身。

`CODEX_CONFIG_UI_TEST_FILTER='api-key-migration'` 验证旧 PAT 读取、差异确认、API Key 写入、旧认证地址清理、加密恢复、外部冲突与最小窗口缩放。核心 `ApiKeyMigrationTests` 使用虚构认证和内存环境变量；新直连只生成 API Key，旧加密网关中服务类型的数字保持兼容。

`CODEX_CONFIG_UI_TEST_FILTER='runtime-conflicts'` 验证预览后管道轮换、确认保存、保留新注释、备份恢复，以及真实设置变化仍阻止覆盖。`ConfigWriteConcurrencyTests` 覆盖同账号令牌刷新、写入前重试、写入后复核和中断恢复。只对已核对的本机管道轮换及相同身份刷新合并，目录内容、索引和实际设置冲突继续拒绝。新加密备份携带写入前后快照，旧备份仅在摘要能够证明一致时恢复。

## 目录与字段维护

旧聊天接续专项：`node --test tests/desktop-connections.test.cjs`，核心测试程序的 `--connection-unit <输出目录>` 与 `--bridge-rebind-unit <输出目录>`，以及 `CODEX_CONFIG_UI_TEST_FILTER='connection-recovery'`。浏览器集成为 `--connection-browser <输出目录> <Chrome.exe>`；附加当前原版模块时用 `--connection-browser-native <输出目录> <Chrome.exe> <原版 async 资源目录>`。原生两向恢复通过 `CODEX_CONFIG_TEST_CLI` 加入完整核心回归，全部使用临时聊天和回环模拟上游。

桌面多实例专项：核心测试程序 `--desktop-unit <输出目录>` 验证实例分组、正常退出、显式强制回退、保留另一个实例、资料目录传递和新进程核对；包含独立原生双 GUI 测试。`--desktop-targets` 只读列出实例与资料目录，不关闭或启动 Codex。界面 `desktop-restart` 过滤器覆盖无默认选择、取消与所选目标传递，`connection-recovery` / `desktop-speed` 覆盖客户端增强共用入口、目录独立偏好、草稿保护和紧凑布局。

增强自动化专项：`--enhancement-unit <输出目录>` 核对默认关闭、目录隔离、摘要、身份匹配及工作区 `.lnk` 创建；界面过滤器 `automatic-enhancements` 核对单实例转交、明确启动、配置草稿和异常保护。自动 SDK 接回使用 `resumeOnly`，必须匹配之前实际应用的页面标记，不能调用 `Page.reload`。组合浏览器检查还验证页面和 manager 对象保持、不能夺取活跃租约、错误目录/进程创建时间拒绝及接回后单次发送。批量退出覆盖一个实例拒绝退出时不启动、部分退出后强制重试，以及原生双 GUI 最后只重开一个。全部在测试目录与回环环境执行。

应用状态层专项：测试程序 `--application-unit <输出目录>` 验证 `WorkspaceSession` 的加载、草稿、应用、等待用户和失败状态，以及 `WorkspaceViewModel` 的只读投影。它不启动 WPF、网关或 Codex，作为未来 WinUI 壳层的快速回归。

应用操作层专项：同一入口还验证 `WorkspaceOperationRunner` 的串行租约、重复进入拒绝、阶段报告、成功/等待用户/失败收口，以及 `WorkspaceApplyCoordinator` 的桌面应用入口。WPF 的 `RestartCodexFromReviewAsync` 通过共享应用服务持有一次租约，界面只提供平台执行适配与确认交互。

共享桌面应用服务：`DesktopApplicationServiceTests` 验证取消、未捕获目标拒绝、并发拒绝、确认后配置/目录/选项变化、取消令牌、同实例增强重试、退出拒绝保留旧连接、批量强制重试绑定、网关准备失败先于桌面退出，以及差异预览的凭据隐藏。WPF 与 WinUI 都通过 `DesktopApplicationService` 编排应用；`DesktopEnhancementRuntime` 统一持有手动与自动增强连接。包含 DPAPI 模拟混合连接的检查应在正常 Windows 用户上下文运行。

客户端增强状态专项：同一 `--application-unit` 还验证 `ClientEnhancementPreferencesService` 和 `ClientEnhancementsViewModel` 的统一读写、目录隔离、自动接回记录保留和开关变化；当前 WPF 页面已通过该边界读写三个增强偏好。

共享自动接回专项：`AutomaticEnhancementServiceTests` 验证默认关闭、首次绑定、重复检查合并、配置与进程/程序/资料目录/方案变化拒绝、异步核对后的上下文复核、20 秒节流、取消等待、已完成令牌清理、保留正在运行的旧选择和接入失败清理。实际运行适配器只有查找、检查、停止自身连接和 `resumeOnly` 接回接口，不提供启动或退出 Codex 的自动路径。WPF 与 WinUI 共用该服务；WinUI 隔离检查另验证原生托盘收起恢复、实际测试 `.lnk`、明确启动与当前用户单实例转交。

共享配置保存专项：同一 `--application-unit` 入口运行 `ConfigurationSaveServiceTests`，验证内存草稿、确认副本冻结、一次性确认、保存/桌面共用租约、可恢复的实际 DPAPI 备份、冲突和取消、运行管道与同账号刷新保留、自定义供应商和认证不变、PAT 明确迁移、HTTP 按站点确认、混合身份保留、差异脱敏、待恢复记录拒绝、无修改不备份及失败回滚。WPF 新过滤器 `CODEX_CONFIG_UI_TEST_FILTER=configuration-save` 独立运行既有配置确认与连接备注回归；`api-key-migration`、`connection-mode`、`model-behavior` 和 `runtime-conflicts` 继续核对保存后的边界。WinUI smoke 另渲染实际保存弹窗，检查取消、失效确认、草稿离开选择和保存后等待应用。

官方登录与模型专项：`CodexConfig.Tests.exe --login-model-unit <隔离目录>`（也包含在 `--application-unit` 与完整 Core 回归中）验证只读登录刷新、显式备份、取消与清零、外部变更、读取期间新登录、完整草稿保留、原生加密备份的确认保存、继承值、结构化审批和能力目录覆盖。模型发现测试使用替身响应，核对一次请求、ID 过滤、连接变化拒绝和主模型不变。WPF 过滤器 `login`、`connection-mode`、`model-behavior`、`configuration-save`、`api-key-migration` 及 `connection-editor` 覆盖兼容流程；后者还验证模型读取期间本地保存仍可完成。WinUI smoke 渲染模型、审批、登录备份和实际保存弹窗，验证取消请求、过期结果和浅深色紧凑布局。原生文件选择器与远端服务不在隔离测试范围。

全部配置、方案与恢复专项：`CodexConfig.Tests.exe --configuration-tools-unit <隔离目录>` 验证 20 项字段错误保留、分页、引号名称、父子路径、敏感值隐藏、方案默认选择/撤销/捕获、读取代数、方案库摘要冲突和损坏、恢复预览无写入、取消、操作租约、一次性确认、备份替换、中断事务、模型依赖以及确认后的令牌和运行管道刷新。两个壳层均使用共享方案库和恢复服务。WPF 完整 smoke 继续核对原字段、方案管理与恢复行为；WinUI 检查真实恢复差异弹窗、分页、密码框、固定方案保存区和未保存离开流程。测试不执行配置中的 MCP 命令，也不接触实际用户的方案库或恢复记录。

Windows 工具专项：`CodexConfig.Tests.exe --windows-tools-unit <隔离目录>` 覆盖启动项只读、替换确认后摘要复核、其他程序注册保护、路径长度和写入失败；配置来源读取磁盘值、敏感值隐藏、条件与文件变化、取消前不启动进程及不支持 Profile；独立 Profile 的普通字段投影、私密/权限字段保留、冻结确认、加密保存和恢复、外部冲突、共享操作锁与主草稿隔离。共 15 项，也包含于应用层和完整回归。WinUI 使用内存 Run 项与配置来源替身渲染真实 Profile 弹窗，验证独立草稿、页间保留、退出/目录/快捷方式保护、来源分页及取消。WPF 保留原字段 UI，通过同一服务完成启动设置、来源读取与 Profile 保存恢复，并运行原完整回归。测试不修改用户实际的启动注册或 Profile。

WebSocket 诊断专项：`CodexConfig.Tests.exe --websocket-diagnostics-unit <隔离目录>` 运行既有分段消息检查，并验证尾部正常关闭帧与确认握手。网关在正常对端关闭时先向另一端转发关闭帧，最多等待两秒确认后再清理，避免直接中止传输截断已转发尾部消息；超时、取消与连接替换仍走原清理路径。专项使用本机替身上游，不联系模型服务。

WinUI 壳层：`pwsh -NoProfile -File scripts/build-winui-preview.ps1 -Verify` 构建独立的 `CodexDock.WinUI.exe` 并执行隔离截图验收。`WorkspaceShellViewModel` 的无写入加载、地址敏感信息隐藏、目录切换、损坏设置和只读保存失败包含在 `--application-unit`。专项入口异常统一输出至标准错误并返回 1，测试初始化失败不再触发 Windows 应用程序错误弹窗。范围和环境要求见 [WinUI 开发预览](winui-preview.md)。

共享网关专项：`GatewayManagementServiceTests` 随 `--application-unit` 验证只读刷新、未知端口、目录隔离、并发租约、停止失败/中途配置变化拒绝重开、取消、仅一次启动和独立失败；另通过实际隔离的模型与手机后台核对启停、重启、健康与端口释放。WPF 过滤器 `gateways` 覆盖原网关页、草稿保留和自动启动；`gateway-lifecycle` 覆盖停止两类网关后退出与安装器调用顺序。网关页完整回归有多次真实进程启停，可能超过一分钟，需按进度日志等待结束。WinUI 还核对自带 `mobile` 文件夹的实际后台可运行。状态刷新使用 Core 的专用端口释放探测，不以短超时的连接拒绝代替已停止。

手机连接专项：`MobileConnectionServiceTests` 验证草稿与密码隐藏、输入验证、保存确认的一次性提交、操作租约、设置变更冲突、保存后启动失败、取消、管理链接范围、配对 PNG/有效期、设备成员校验及 Core 管理锁内摘要复核。实际测试后台验证端口变更保留测试登录、撤销设备后旧登录无法恢复。WPF 过滤器 `mobile-connection` 覆盖原手机页与托盘、共享保存确认和密码清理；WinUI smoke 覆盖独立模型/手机草稿、文本输入时序、保存与离开选择、真实配对图渲染、撤销与解除限制及本机 worker。全部使用隔离目录和虚构入口，不改安卓包或公网路由。

- `src/`：桌面应用与配置核心。
- `config/config-schema.json`：本地字段表，包含分类、类型、枚举和取值约束。
- `config/config-help.json`：独立维护的中文用途说明。
- `tests/`：核心测试。
- `scripts/`：打包、安装验证和隐私扫描。
- `docs/`：使用边界、版本记录与维护说明。
- `assets/`、`third_party/`：品牌资源与第三方许可。

字段表和帮助数据在构建时嵌入程序。维护方式见 [字段维护说明](schema-maintenance.md)。活动 profile、项目配置和托管策略可能覆盖本地文件，字段表不等同于运行会话的最终生效值。

## 打包与发布

先完成测试、审查并提交修改，再从干净工作区运行：

```powershell
pwsh -NoProfile -File scripts/package.ps1
pwsh -NoProfile -File scripts/verify-package.ps1
```

打包包含 .NET 运行时，记录源提交与文件散列，并对实际发布程序运行隔离 UI 检查。安装验证使用独立目录，核对发布文件、旧入口清理、卸载保留用户文件以及真实用户环境未改变。发布流程和更新附件约定见 [GitHub 发布与更新](github-releases.md)。

GitHub 的 `release.yml` 按平台独立构建和发布。`vX.Y.Z` 只构建 Windows，`android-vX.Y.Z` 只构建 Android；手动运行先选择 `platform`，默认只打包所选平台，明确勾选 `publish` 后才公开其独立 Release。安卓签名使用仓库 Secrets 中的既有密钥，不随 Windows 更新重新发布 APK。流水线工具的本地验证入口为 `python -B -m unittest discover -s tests/release -v`；发送补充专项见 [验证记录](review-steering-2026-10-31.md)。

个人 `auth.json`、`config.toml`、加密连接、备注、备份、测试运行目录、日志和构建缓存不得进入源码或发布包。打包脚本调用隐私扫描，在本机比较真实凭据与配置指纹，禁止打印其内容。仅上传明确的版本附件，不上传整个 artifacts 目录。

## 实现与兼容性

配置保存保留未编辑字段、注释、UTF-8 BOM 和原换行，采用外部修改检查、写锁、临时文件替换、加密备份与失败回滚。未设置的偏好不会自动写为默认值。已知嵌套表和数组元素按目录约束校验，跨字段校验只检查此次变更涉及的组；复杂表数组保留原文，未知结构不宣称完整验证。

2026-11-02 配置优化的目录来源、独立 profile 保存、原生配置层读取、模型能力和网关诊断边界见 [实施记录](configuration-implementation-2026-11-02.md)。`ConfigurationOptimizationTests.cs` 和 `ConfigurationOptimizationSmoke.cs` 覆盖对应回归。诊断只保留进程内计数和耗时；单个响应事件的检查缓存上限为 2 MiB，超限会显示检查不全并继续转发。

进程与主程序集使用 CodexDock；内部命名空间、用户数据目录和原有加密参数保留兼容。当前开发构建默认 API Key 直连，官方账号加独立模型入口仍为可选模式；旧 PAT 迁移需经过差异确认，见 [迁移记录](api-key-auth-migration-2026-11-12.md)。网关使用 ASP.NET Core 共享运行时并随自包含发布一起打包；每个配置目录一个独立 worker，管理端点与模型路由分别鉴权。原始实现与真实线路边界见 [0.12.0 审查](review-0.12.0.md)。

历史界面设计与审查记录保留在 docs 中，用于维护参考，不代表额外承诺的产品能力。

每次冻结前 `scripts/verify-mobile-upstream.py` 校验原始快照与显式覆盖，禁止构建时自动接受散列变化。`freeze-mobile.py` 校验微软官方 ConPTY 锁定摘要、构建并自检原生 Codex 退出助手，收集远端注入源码与 certifi 证书。相关许可随运行时提供。

`python -B tests/mobile/upstream_codex_regression.py` 按模块在独立解释器运行固定上游的 Codex 相关回归，保留原始断言。测试中的 WSL 发行版列表使用夹具，避免依赖本机 WSL 服务延迟；无 Windows 符号链接权限的用例明确跳过，目录 junction 边界另有原生测试。该入口不代替安卓真机、真实 SSH / WSL 或模型任务验收。

0.14.1 的草稿与轮询决策见 [审查记录](review-0.14.1.md)。内容版本忽略光标位置；身份尚未核验的发送只记录本次首次提升的范围，不迁移未知请求。后台状态读取与显式刷新共用读取逻辑，后台失败退避并去重提醒，显式操作仍即时反馈。定向验证入口为 `node --test tests/mobile/dock-drafts.test.cjs tests/mobile/client-status.test.cjs` 和 `node tests/mobile/composer-send.browser.cjs`；后者实际运行生产 JS、延迟确认、身份变化和应用管理重试界面。

0.15.0 的 Dock 展示模块位于 `src/CodexMobile/ui/`，通过现有 `/app.js`、`/style.css` 响应追加，冻结时复制到 `runtime/ui`；不新增公开资源路由或改变上游加载顺序。`node tests/mobile/dock-ui.browser.cjs` 覆盖实际 Dock 页面交互；`node tests/mobile/ui-performance.browser.cjs` 在同一隔离网关中移除精确的追加字节作为对照，记录合成长列表与长聊天的本机渲染耗时。前者包含合成文件/终端标签的状态保留，不代表真实终端已验收。见 [UI 实施记录](ui-optimization-plan.md) 和 [0.15.0 审查](review-0.15.0.md)。

0.15.1 的稳定设置分组、输入错误去重、会话菜单及网关状态语义见 [审查](review-0.15.1.md)。Dock 通过可选能力标志省略动态网络部署 UI 和读取，并在适配层拒绝无关更新 / 部署 API；不修改上游默认能力。独立安卓壳的源码、构建和隔离模拟器验证见 [安卓说明](android-dock-shell-plan.md)。

更新与聊天参数专项：`CodexConfig.Tests.exe --updates-diagnostics-unit <隔离目录>` 验证 16 项共享下载、固定来源与校验、取消/并发、安装前文件锁、自动接回租约交接、两个网关停止确认、安装器变化、目录/网关摘要变化、启动失败、明确聊天选择、损坏 JSON、敏感文本不返回、旧结果拒绝和固定尾部读取；也包含于应用层与完整回归。WinUI smoke 使用离线更新替身，渲染实际安装确认并通过专用窗口核对成功退出；真实安装器从不执行。WPF 沿用原更新与网关生命周期回归，下载和退出改走共享服务。WinUI 版本与 WPF 同为正式版比较基线，预览构建脚本验证两个源码版本及交付 DLL，不改变正式版默认入口。详情见 [更新与诊断迁移记录](winui-updates-2026-11-16.md)。

Windows 原生窗口回归应分别串行执行 WPF 和 WinUI，避免多个回归窗口同时争用焦点、模态确认与托盘菜单。某轮并行验收曾在旧版的原有保存区可见性断言失败，后续用相同产物单独完整复验；记录保留在更新与诊断阶段的工件目录。

首次配置与网关诊断专项：`CodexConfig.Tests.exe --workspace-readiness-unit <隔离目录>` 覆盖 18 项空目录无写入、既有认证保留、继承凭据、加载失败状态收口、损坏配置/认证/加密数据修复、首次保存确认与冲突、压缩触发/条目/成功区别、缺失与未知指标、诊断脱敏、停止与旧目录结果清除、停止前发出的迟到状态读取失效，以及实际本机网关的统计读取。也包含于应用层与完整回归。网关专项使用本机上游替身，不联系实际模型服务。WinUI 在真实表单中验证首次保存取消与确认、错误后重读、紧凑深色诊断、草稿保留与同步密码输入；两套密码框使用 `PasswordChanging`，防止后续回填抢先覆盖草稿。完整窗口验收与设备验收范围见 [本轮记录](winui-readiness-2026-11-16.md)。

`--bridge-rebind-unit <隔离目录>` 另核对旧 WebSocket 响应完整结束、正常关闭握手与空闲旧路由拒绝迟到请求。路由隔离后先进行最多两秒的关闭确认，再清理两端，避免发送关闭帧后立即 TCP 重置；迟到消息不转发或重放到旧路由。取消、异常和超时仍保留清理边界。所有上游均为本机替身。

文件预览与帮助专项：`CodexConfig.Tests.exe --workspace-information-unit <隔离目录>` 验证 14 项规范化 TOML、嵌套凭据和地址参数隐藏、认证白名单摘要、单项损坏、文件与显示上限、单个用户变量、原生只读/缺失目录、前后内容和目录复核、取消与迟到结果、初始字节冻结、目录完整路径、Explorer 独立参数及帮助检索。也包含于应用层与完整回归。WinUI 实际窗口覆盖草稿排除、三类预览、明确复制与目录动作、帮助导航、取消、损坏项保留、保存/恢复/重读/换目录清理快照及紧凑深色。目录打开与剪贴板在 smoke 模式使用记录替身，真实用户剪贴板不读写；WPF 原保存计划预览采用同一脱敏投影，仍保留草稿范围。详情见 [文件预览与帮助记录](winui-information-2026-11-16.md)。

WinUI 壳层交互专项：为 `--smoke-test <隔离目录>` 子进程设置 `CODEX_DOCK_WINUI_TEST_FILTER=workspace-usability`，单独运行 23 项键盘命令、保存范围、17 页尺寸矩阵、逻辑视口换行、原生最小尺寸消息和本窗口生命周期检查；无筛选时包含于完整回归。仅在 smoke 模式读取该筛选变量。`native-acceptance` 专项提供带明确隔离标题的实际窗口，使用真实 ContentDialog 和系统目录选择器，限制选择范围在本次输出目录，记录实际按键与选择器结果；最终标记前必须完成并核对这些结果，不能将只显示窗口算作通过。该模式不进入默认构建的自动 Verify 流程。详情见 [收尾核对](winui-final-audit-2026-11-16.md)。

`build-winui-preview.ps1 -Verify` 为回归子进程清除专项筛选，随后恢复调用方环境，保证始终核对完整窗口流程；以十秒间隔等待并设八分钟总上限。即使窗口被提前关闭、进程退出码为零，缺失报告也会阻止将构建标记为验证通过。

2026-11-17 的任务导航整合专项使用 `CODEX_DOCK_WINUI_TEST_FILTER=ui-consolidation`，仅与 `--smoke-test <隔离目录>` 配合使用。37 项检查覆盖所有旧目的地在五个主任务和两个底部入口中的归属、唯一模型编辑与服务目录、连接摘要、缺少模型时定位、保存后就地应用入口、手机服务控制的归属、只读页面 Ctrl+S 无写入、四份独立草稿汇总、Profile/方案切换及紧凑导航。无筛选时包含于完整 377 项窗口回归。原尺寸专项保留各页面及新手机二级页的三档窗口矩阵，检查当前实际可见的保存或应用动作。源码与结果见 [UI 整合记录](winui-consolidation-2026-11-17.md)。

后续任务流程复查使用 `CODEX_DOCK_WINUI_TEST_FILTER=task-flow` 单独运行 17 项，也已加入默认完整回归。专项先在实际窗口复现同页滚动复位，再验证明确字段焦点优先、工作台结果分类及无副作用跳转、独立草稿入口、关闭提示无空行、手机保存到配对的取消/成功/检测与窄窗口路径。手机状态读取允许获取设备列表，明确断言不因跳转执行公网检测或生成配对码；测试中不把只读控制调用误当成写操作。见 [任务流程复查](winui-task-flow-2026-11-17.md)。

审查修复专项使用 `CODEX_DOCK_WINUI_TEST_FILTER=review-fixes`，11 项检查也加入默认完整回归。两个手机运行时替身观察同一份虚构后台状态，验证启停/重启后配对入口同步、较早状态读取晚返回时重新核对、读取失败保持未知及独立草稿保留；配置恢复验证取消保留结果、成功清除旧来源行和再次明确读取。该专项不操作真实 Codex、用户手机服务或剪贴板。见 [审查修复记录](winui-review-fixes-2026-11-17.md)。

`CODEX_DOCK_WINUI_TEST_FILTER=environment-native` 是单独运行的原生环境专项，不包含在默认完整 smoke 中：按索引读取 Windows App SDK 显示区域，移动测试窗口并核对每屏 DPI、XAML 比例、工作区和草稿；打开的目录严格限制在该输出目录。实际复制只允许当前剪贴板为测试已知纯文本时执行，否则明确跳过；不得通过 OLE 回写先前任意数据对象。该错误路径曾造成恢复失败和 OLE 栈溢出，已删除并在 [环境验收](winui-environment-2026-11-16.md) 保留事故记录。报告不包含用户原剪贴板文本或对象。不同物理 DPI 仍需对应设备，不能以逻辑宽度压力测试替代。


本机验收更新：用户明确复制 `CODEX_DOCK_CLIPBOARD_TEST_20261117` 后，原生专项通过长度限制的 CF_UNICODETEXT 读取核对标记，只复制测试路径与脱敏摘要并恢复测试文本；不保留 OLE/WinRT 数据对象。最终 12 项通过且正常退出。`--installed-desktop-acceptance <新 artifacts 目录>` 为手工选择的真实已安装 Codex 空白资料验收，不加入默认测试；保护已存在的进程，验证关闭到托盘时不重复启动，以及明确确认后只重启测试实例。完整证据与验证安装器范围见 [本机剩余项验收](windows-local-acceptance-2026-11-17.md)。

复用专项：`CodexConfig.Tests.exe --upstream-reuse-unit <隔离目录>` 运行 17 项目录能力、加密等待设置、请求预算、空闲计时、取消、压缩透明转发、功能分页与草稿完整性检查。原生只读检查另用 `--native-feature-read <新隔离目录> <执行端绝对路径>`，只初始化并读取功能，不创建聊天。WinUI smoke 的 `CODEX_DOCK_WINUI_TEST_FILTER=upstream-reuse` 运行 18 项实际控件检查，也包含在完整回归中。完整原生窗口回归保持串行，使用自有空白替身；不与另一套窗口回归并行。压缩边界原型和产品范围见 [复用实施记录](codexplusplus-reuse-implementation-2026-11-17.md)。
