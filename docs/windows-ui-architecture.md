# Windows UI 重构路线

2026-11-17 的 WinUI 壳层按任务整合导航：工作台、Codex 配置、运行与增强、手机访问、诊断与恢复，底部为设置和帮助。轻量页面路由负责目的地、任务、标题和保存范围；现有页面继续共用原草稿和应用服务。模型编辑、服务发现及手机服务入口已归并，独立草稿通过全局待处理菜单访问，底栏和键盘保存仅作用于当前对象。实现与验收见 [UI 整合实施](winui-consolidation-2026-11-17.md)。

当前阶段采用渐进式重构。现有 WPF 窗口继续作为交付壳层，`CodexConfig.Core` 保留已经验证的配置、网关、Codex 实例和增强逻辑；新增 `CodexConfig.Application` 作为 WPF 与未来 WinUI 壳层共用的状态层。当前窗口已经把配置加载、草稿、应用阶段、Codex 运行状态和增强开关同步到 `WorkspaceSession`，客户端增强页作为第一块迁移样板。客户端增强的三类偏好现在由 `ClientEnhancementPreferencesService` 和 `ClientEnhancementsViewModel` 统一读写，WPF 与未来 WinUI 不需要各自维护三套文件逻辑。

应用状态集中在 `WorkspaceSession`：工作区、配置快照、草稿、应用阶段、运行中的 Codex、客户端增强开关和最近一次应用结果。视图不再自行推断“是否正在应用”或“是否需要重启”，而是显示 `WorkspacePhase` 与 `WorkspaceOperationState`。

当前阶段又加入 `WorkspaceOperationRunner` 与 `WorkspaceApplyCoordinator`。它们在应用层提供一次性的操作租约：同一工作区的应用/重启不能重复进入，阶段和失败状态通过 `WorkspaceSession` 收口，WPF 只保留 Codex 进程、网关和窗口 API 的具体执行细节。未来 WinUI 可以复用同一套串行和状态规则，不需要复制 `desktopRestartBusy` 一类窗口字段。

已建立独立的 `CodexConfig.WinUI` 开发壳层，使用自适应 NavigationView 提供工作台与客户端增强页面。`WorkspaceShellViewModel` 在 Application 层组合既有工作区加载和增强偏好服务，负责可读状态、错误和保存结果。读取和回填不会保存偏好；文件损坏或写入失败时禁用修改并提供重新读取入口。

手动应用现已接入共享的 `DesktopApplicationService`：服务负责预检查、实例选择规则、确认信息、网关先行、进程重启、增强接入和重试状态。`IDesktopApplyInteraction` 只承载各壳层的对话与展示；`IDesktopApplyRuntime` 区分实际 Core 调用与隔离替身。`DesktopEnhancementRuntime` 统一持有调试连接与旧聊天后端，WPF 自动接回也复用此持有者。WinUI 使用 `NativeDesktopApplyRuntime`，同时提供无窗口的模型网关 worker 入口。共享服务未修改既有 Core 文件格式或进程关闭规则。

WPF 已移除原先窗口内的应用顺序和可写忙碌/重试字段，窗口从服务投影状态。WinUI 提供固定的应用区、无默认目标的实例选择、独立强制确认、增强重试及关闭保护；取消关闭不会先停用增强。自动接回的任务合并、取消、超时、节流、实例与摘要核对已移入 `AutomaticEnhancementService`，两个界面通过 `IAutomaticEnhancementRuntime` 复用规则，并与手动应用共享连接持有者。

窗口单实例与增强启动转交使用共享的 `DesktopInstance`，保留当前用户管道及原 WPF 作用域；WinUI 使用独立作用域，worker 与隔离测试绕过窗口限制。WinUI 原生托盘适配器只负责 `Shell_NotifyIcon`、菜单及窗口消息，窗口关闭收起到托盘，明确退出仍执行确认和连接清理。增强快捷方式复用 Core 的 `.lnk` 创建与参数校验。运行、构建和验证方式见 [WinUI 预览说明](winui-preview.md)。

配置保存已迁入 `ConfigurationSaveService`：WPF 的方案生成、结构化差异和最终写入均通过此服务，保留模板、模型目录、官方登录与运行状态合并规则；WinUI 通过同一服务确认和保存连接。`ConfigurationSaveReview` 冻结已预览的字节，对外只展示脱敏差异和写入目标，一份确认只能提交一次。保存与桌面应用共用 `WorkspaceApplyCoordinator`，备份、冲突检测与回滚仍由 Core 执行。保存后重新读取并核对，再进入等待应用阶段。

WinUI 的 `ConnectionDraftViewModel` 统一持有地址、密钥、协议、连接方式、默认模型、备注、字段覆盖、专用审核和能力目录草稿，页面切换不丢失修改。字段输入的语法错误单独保留，投影只使用已通过语法校验的值，错误存在时阻止任何配置保存。`ConfigurationCatalogService` 只查询同一份草稿和原生 schema，不管理文件写入。保存前后不自动重启；有草稿时不自动接回或应用旧配置，离开工作区与明确退出先处理未保存选择。

`TemplateLibraryService` 为 WPF 与 WinUI 共享的方案库入口。`TemplateRepository` 保留验证、敏感值过滤和原子替换，新增读取摘要与可选的预期摘要，在库锁内复核以拒绝并发覆盖。配置方案仍使用 Core 的差异与选择/撤销实现；WinUI 以草稿加载代数拒绝重新读取后的旧撤销，同时保留后来另行编辑的字段。方案编辑独立于配置草稿，两个保存入口各自明确作用范围。

`ConfigurationRecoveryService` 负责恢复确认的一次性消费、上下文和共享操作租约。Core 的 `PrepareRecovery` 从既有恢复计算提取只读投影，生成对外只含脱敏差异和元数据的不可修改计划；`ExecuteRecovery` 在写锁内复核来源和当前快照，沿用原备份、回滚、运行信息合并与依赖保护。正常恢复和中断恢复使用同一套已有目标计算，两个壳层不自行解密、拼接文件路径或实现恢复事务。未完成记录会阻止 WinUI 编辑、应用和自动接回，恢复后重新读取解锁。

`OfficialLoginService` 与 `ModelDiscoveryService` 为两套壳层共享的只读用例。官方登录服务只读取当前目录的文件身份或明确选择的加密备份；两次快照检查拒绝设置冲突或读取期间的新登录，备份字节只在草稿中持有，丢弃时清零。读取后更新已观察到的基线，保留全部草稿。模型发现只访问所选服务，过滤重复及无效 ID，并在完成时核对工作目录和连接摘要；主模型变化不使结果失效，地址、密钥、协议或认证方式变化则拒绝旧结果。WinUI 可取消模型读取，回填和导航不自动发请求。

WinUI 的模型与行为页复用 `ConfigSchema`、`ModelCapabilities` 和 `ManagedModelCatalogStore`。未设置值以空覆盖表示；原有未知值与结构化审批规则可保留。专用审核、导入和同步都生成同一保存方案，经确认后由 Core 同时备份并写入，未通过主模型和审核能力校验时保留草稿和原文件。

网关启停、自动启动与状态读取现已进入 `GatewayManagementService`。`IGatewayManagementRuntime` 只接收保存目录和不含密钥的定义摘要；原生适配器调用既有 `BridgeProcess`/`MobileGatewayManager`，保留认证控制与旧进程退出等待。WPF 的手动/自动网关入口、托盘状态与 WinUI 网关页共用此服务。刷新按目录合并，过期目录结果丢弃；自动启动每次 Dock 生命周期执行一次，两个服务错误独立。网关命令沿用保存/桌面应用的同一操作租约，原生端口释放探测抽为 Core 的 `GatewayPortState`，不将未知监听者判为停止。

手机设置、入口检测、配对、设备撤销和本机接入管理通过 `MobileConnectionService` 共享。原生适配器保留 `MobileGatewayManager` 的密码哈希、DPAPI 文件和后台控制协议；设置提交新增可选的确认摘要，在管理锁内和最终文件替换前复核，旧调用保持兼容。WinUI 的 `MobileSetupDraftViewModel` 独立于模型连接草稿；WPF 的保存与配对/设备写入口也已通过服务执行。配对结果只传本机 PNG 与有效期，接入管理链接严格绑定已保存的回环控制端口，设备写操作核对当前成员后才执行。

WinUI 文本字段在 `TextChanging` 同步记录草稿，避免另一控件或周期刷新抢先回填旧值；只在实际值不同的情况下回填控件。侧栏、窗口和托盘统一引用桌面版 Codex Dock 资源。按当前安排先完成 Windows 迁移，安卓标识与视觉优化留待后续单独处理。

`StartupRegistrationService` 通过已有 `IStartupRegistration` 读写同一 Windows 用户 Run 项。读取不写入，准备时绑定观察到的命令与目录，确认后再次比较；关闭拒绝删除其他程序或目录的启动项。开关与模型草稿分开保存，实际注册仍由 Core 处理。

`ConfigurationSourceService` 复用 Core 的短时 `ConfigReadClient`，两个壳层只传所选执行端、工作区、项目和可选 Profile。读取前后比较磁盘文件及读取条件，取消或上下文改变后不发布旧结果。执行端不支持 Profile 来源时保留明确的未知状态。界面结果分页且使用 Core 已隐藏凭据的值。

`ProfileApplicationService` 为两个壳层提供冻结差异、一次性确认、备份恢复来源复核和独立文件保存。Core 的 `ProfileStore` 保留原验证、文件锁、加密备份与原子写入，并将恢复准备拆成只读计划；确认后在锁内复核来源。WinUI 的 `ProfileDraftViewModel` 仅暴露通过普通字段验证的 TOML，其他字段留在源文件中保留，主配置与 Profile 的草稿、保存和恢复互不混用。WPF 保留原字段控件，并通过同一服务提交。

`UpdateDownloadService` 调用 Core 的 `GitHubUpdates`，两套壳层共享来源、重定向、大小与 SHA-256 校验，不在窗口中实现第二套下载协议。`UpdateInstallationService` 在保持已校验文件禁止写入/删除的句柄期间，先等待自动接回释放原操作租约，再占用共享安装租约，停止增强与已保存的两类网关，复核网关定义和停止状态，最后才启动安装器。WPF 保留原退出保护，WinUI 在确认正式版安装范围和处理全部草稿后进入同一服务；失败则保留窗口供核对与重试。

`SessionDiagnosticService` 共享当前目录列表、明确所选记录读取、取消和上下文复核。原生运行时调用 Core 的受限元数据读取，不返回对话或工具内容；Core 从打开时固定长度的文件尾部读取，忽略损坏行并支持取消。界面不预选最近聊天，旧目录或旧选择结果不能回填。两个壳层使用相同的参数和未知审核模型说明。

`WorkspaceSetupGuide` 将只读结果区分为需要连接、已有认证与无法读取。空目录不落盘，已有官方、环境、供应商和系统认证不强制转换；WinUI 在没有连接时禁用应用与自动接回。`WorkspaceCoordinator` 对存储、解密和解析失败统一结束加载，并在新加载开始时清除此前快照与运行状态；无论从目录还是已读取的快照加载，均使用同一条状态路径。错误呈现只保留文件类别和修复方向，修复后可在原窗口重读。

`ModelGatewayDiagnostics` 为 WPF 与 WinUI 提供同一套计数、协议与耗时呈现。原生网关运行时从已有认证健康入口读取，绑定保存配置的摘要与压缩偏好；`GatewayManagementService` 仅在当前模型网关确认运行时转交统计。投影只保留计数、有限耗时、已知协议/传输/结束标签及 HTTP 状态，不转交响应正文、请求 ID 或上游错误正文。统计不落盘，停止、未知和读取失败不会复用先前结果。

网关操作开始和结束时使此前的状态读取失效，新读取不会继续合并到旧任务；发布结果时在同一锁内核对读取代数，避免停止或重启后的旧统计晚到并回填。模型 WebSocket 的路由退休继续保持忙碌响应完整转发、空闲旧路由禁止新请求的规则，现与正常对端关闭一样给予有时限的关闭确认，再释放传输。

`ConfigurationPreviewService` 提供不含原始字节的配置、认证与环境快照。原生适配器限定两份文件和一个已知用户变量，以读取上限约束输入，前后复核内容和目录，取消或变更后不发布结果。配置递归复用 Core 敏感路径与地址脱敏规则，重新序列化以去掉注释；认证采用白名单摘要。WinUI 只传已保存文件，WPF 则保留保存计划的草稿预览，两者共享投影而不混淆数据范围。`WorkspaceDirectoryService` 校验当前完整路径，通过单独进程参数打开已有目录；复制由壳层在明确点击后执行。`DockHelpCatalog` 为两套壳层共享说明文本，WinUI 的七类主题仅提供页面跳转。隔离 UI 的目录和复制运行时不调用实际 Explorer 或剪贴板。

迁移与当前交接状态：

1. 功能迁移、入口整合、键盘、窗口、多份草稿和错误恢复已完成对应回归，证据见 [任务流程复查](winui-task-flow-2026-11-17.md)。
2. 当前 Windows 11 双屏 100%、系统目录入口、指定测试文字复制恢复、真实 Codex 空白独立资料生命周期及验证安装器已完成本机验收，见 [本机记录](windows-local-acceptance-2026-11-17.md)。
3. 可解压预览包与换机检查说明见 [预览交接](winui-preview-handoff-2026-11-17.md)。其他物理 DPI、Windows 10、手机公网设备等待对应环境，登录启动与已登录任务增强仍需安排实际操作窗口。
4. WPF 保持默认交付入口。默认入口切换与正式发布须结合剩余验收结果单独决定。

状态层与操作层的定向测试：`CodexConfig.Tests.exe --application-unit <输出目录>`，覆盖工作区生命周期、重复应用保护、失败收口、偏好服务与自动接回。当前 WPF 样板测试：`CODEX_CONFIG_UI_TEST_FILTER=automatic-enhancements` 与 `desktop-restart`。应用服务不引用 XAML 窗口类型；原生连接和命名管道由专用运行时持有，可供两个壳层共同使用。

Windows 端目标技术栈：WinUI 3、Windows App SDK、.NET 8+、CommunityToolkit.Mvvm、Microsoft.Extensions.Hosting/DependencyInjection。WebView2 只用于帮助、Markdown 和诊断预览，不承担核心配置表单、进程控制或增强注入。

不在本阶段引入跨平台 UI 框架、Electron 或完整 Web UI，也不重写已验证的核心库。这样可以先改善信息架构和交互状态，再承担 WinUI 壳层迁移的成本。
