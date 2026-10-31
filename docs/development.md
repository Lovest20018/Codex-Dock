# 开发与构建

Codex Dock 使用 C#、.NET 8、WPF 和 Tomlyn；独立手机 worker 使用 Python 3.12、完整上游 Codex 模块及 certifi。面向使用者的安装与连接方式见 [README](../README.md)。

## 本地构建

需要 .NET 8 SDK；生成安装器另需 Inno Setup 6。构建手机 worker 需要 Windows x64 Python 3.12，首次创建隔离构建环境并安装锁定工具：

```powershell
pwsh -NoProfile -File scripts/build-mobile.ps1 -Python 'C:\path\to\Python312\python.exe' -InstallBuildTools
```

之后 `scripts/build-mobile.ps1` 复用 `artifacts/mobile-build-venv`。先构建 worker，再构建和测试 WPF；手机 worker 会复制到应用的 `mobile` 子目录。终端使用 PowerShell 7。

```powershell
dotnet build src/CodexConfig.App/CodexConfig.App.csproj -c Release
dotnet build tests/CodexConfig.Tests/CodexConfig.Tests.csproj -c Release
& .\tests\CodexConfig.Tests\bin\Release\net8.0-windows\CodexConfig.Tests.exe
dotnet run --project src/CodexConfig.App/CodexConfig.App.csproj -c Release -- --smoke-test .ui-test-local
```

核心和 UI 检查使用虚构凭据、隔离配置目录及测试环境变量。UI 检查覆盖布局、草稿、模板、备份恢复、托盘、开机启动和更新入口；缩放使用布局变换模拟。

手机 Python 回归：`py -3.12 -B -m unittest discover -s tests/mobile -p 'test_*.py' -v`。浏览器 QA：先在 `tests/mobile` 执行 `npm ci`，再从仓库根执行 `node tests/mobile/browser-smoke.cjs`；需要本机 Chrome，可用 `CODEX_DOCK_TEST_PYTHON` 指定 Python 绝对路径。测试创建独立浏览器资料和临时 Codex 数据目录；HTTPS 请求仅转到隔离 worker。上游浏览器用例通过 `tests/mobile/playwright.config.cjs` 复用，Windows 夹具使用生产服务相同的 HTTP/1.1。上游 Node / Python 原始用例保存在 `src/CodexMobile/upstream/tests`。回环套接字和测试子进程需要在环境许可下运行；不要禁用这些必要检查来生成发布包。

设置 `CODEX_CONFIG_TEST_CLI` 为原生 `codex.exe` 的绝对路径，可加入两项 app-server 兼容检查，分别验证内置 `openai` 与旧 provider。它们只使用本机模拟上游与虚构官方登录，不消耗真实模型额度。网关进程用例使用测试程序自身的专用入口，界面用例使用实际 CodexDock 程序。

## 目录与字段维护

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

个人 `auth.json`、`config.toml`、加密连接、备注、备份、测试运行目录、日志和构建缓存不得进入源码或发布包。打包脚本调用隐私扫描，在本机比较真实凭据与配置指纹，禁止打印其内容。仅上传明确的版本附件，不上传整个 artifacts 目录。

## 实现与兼容性

配置保存保留未编辑字段、注释、UTF-8 BOM 和原换行，采用外部修改检查、写锁、临时文件替换、加密备份与失败回滚。未设置的偏好不会自动写为默认值。复杂表数组保留原文，嵌套结构只作有限类型检查。

进程与主程序集使用 CodexDock；内部命名空间、用户数据目录和原有加密参数保留兼容。0.12.0 恢复可选混合连接，默认 PAT 编辑流程保持。网关使用 ASP.NET Core 共享运行时并随自包含发布一起打包；每个配置目录一个独立 worker，管理端点与模型路由分别鉴权。实现与真实线路边界见 [0.12.0 审查](review-0.12.0.md)。

历史界面设计与审查记录保留在 docs 中，用于维护参考，不代表额外承诺的产品能力。

每次冻结前 `scripts/verify-mobile-upstream.py` 校验原始快照与显式覆盖，禁止构建时自动接受散列变化。`freeze-mobile.py` 校验微软官方 ConPTY 锁定摘要、构建并自检原生 Codex 退出助手，收集远端注入源码与 certifi 证书。相关许可随运行时提供。

`python -B tests/mobile/upstream_codex_regression.py` 按模块在独立解释器运行固定上游的 Codex 相关回归，保留原始断言。测试中的 WSL 发行版列表使用夹具，避免依赖本机 WSL 服务延迟；无 Windows 符号链接权限的用例明确跳过，目录 junction 边界另有原生测试。该入口不代替安卓真机、真实 SSH / WSL 或模型任务验收。

0.14.1 的草稿与轮询决策见 [审查记录](review-0.14.1.md)。内容版本忽略光标位置；身份尚未核验的发送只记录本次首次提升的范围，不迁移未知请求。后台状态读取与显式刷新共用读取逻辑，后台失败退避并去重提醒，显式操作仍即时反馈。定向验证入口为 `node --test tests/mobile/dock-drafts.test.cjs tests/mobile/client-status.test.cjs` 和 `node tests/mobile/composer-send.browser.cjs`；后者实际运行生产 JS、延迟确认、身份变化和应用管理重试界面。

0.15.0 的 Dock 展示模块位于 `src/CodexMobile/ui/`，通过现有 `/app.js`、`/style.css` 响应追加，冻结时复制到 `runtime/ui`；不新增公开资源路由或改变上游加载顺序。`node tests/mobile/dock-ui.browser.cjs` 覆盖实际 Dock 页面交互；`node tests/mobile/ui-performance.browser.cjs` 在同一隔离网关中移除精确的追加字节作为对照，记录合成长列表与长聊天的本机渲染耗时。前者包含合成文件/终端标签的状态保留，不代表真实终端已验收。见 [UI 实施记录](ui-optimization-plan.md) 和 [0.15.0 审查](review-0.15.0.md)。
