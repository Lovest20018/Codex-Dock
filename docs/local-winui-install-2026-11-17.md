# Windows 本地覆盖安装测试

本包安装当前 WinUI 界面与最新修复，沿用 Codex Dock 的应用身份、安装目录和 `CodexDock.exe` 入口。版本比较基线为 0.18.0，安装器文件名包含 `WinUI-Local-20261117`，属于未提交工作区的本地测试快照。

## 安装

1. 在旧版 Dock 中选择“停止网关并退出”；已打开的独立 WinUI 预览也从托盘退出。
2. 直接运行 `CodexDock-0.18.0-WinUI-Local-20261117-Setup-win-x64.exe`，选择原来的安装目录，无需先卸载。
3. 安装后仍通过原来的 Codex Dock 快捷方式启动，新入口打开 WinUI 界面。
4. 在“手机访问”确认配置目录和原入口，启动手机服务，再执行 HTTPS 检测与配对/登录测试。

安装器沿用已有安装位置；尚无安装记录时默认 `%LOCALAPPDATA%\Programs\Codex Config`。包内带 .NET 8、ASP.NET Core 8、Windows App SDK 和手机后台，无需另外安装 .NET。安装文件不包含用户 Codex 配置、认证、聊天或手机设置；安装规则不删除这些外部数据。

安装器会请求停止同一安装目录的手机后台，无法核验或未退出时阻止继续覆盖；其他目录的手机后台不会因此停止。模型网关和独立预览请按第 1 步先退出。

本包不包含安卓 APK。现有 Lovest Code 可连接更新后的手机网关；安卓正式覆盖升级仍需原发布签名。

## 验证范围

安装载荷重新发布为自包含 WinUI，安装入口直接编译为 `CodexDock.exe`。本轮修正发布清单，明确包含同次编译生成的 App/MainWindow XBF 和 PRI，避免普通发布漏带页面资源；独立开发预览仍使用 `CodexDock.WinUI.exe`。

打包检查包括最终 Core/Application DLL 的 364 项隔离回归、安装入口专项、同一 Setup EXE 的隔离覆盖安装、安装文件逐项摘要、安装目录内的完整窗口回归和卸载后用户哨兵文件保留。实际计数、安装器 SHA-256 和完成状态以同目录 `installer-verification.json` 为准。

前一轮手机与安卓测试见 [修复与手机连接验收](review-fixes-mobile-validation-2026-11-17.md)。本机测试不会替代实体手机公网 HTTPS、Windows 10、其他物理 DPI 或实际登录启动测试；这些可在本次覆盖安装后继续验收。

本轮不自动在用户正式目录执行安装、不提交或发布 Release。安装后软件界面的“预览”字样属于当前 WinUI 构建标识。
