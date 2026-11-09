# Lovest Code for Android 0.2.4

“我的 → 版本与更新”现在可以在应用内检查、下载并安装安卓新版。

- 关联本项目的 `android-v*` 正式发布和 `LovestCode-Android-<版本>.apk`，忽略 Windows、预发布及较旧版本。
- 显示新版和更新说明，确认“下载并安装”后显示进度，支持暂停、继续、失败重试与取消。
- 下载校验文件大小和 SHA-256；安装前再次校验 APK 包名、版本与既有签名，通过后打开 Android 系统安装器。
- 首次使用按系统提示允许 Lovest Code 安装应用；返回后继续安装。拒绝授权或稍后安装时保留已验证的 APK，重开应用仍可继续。
- 网络检查失败会说明原因并允许重试，发布页作为手动下载入口保留。更新检查不依赖电脑连接，不会发送电脑登录 Cookie。

版本 **0.2.4 / versionCode 7**，沿用包名、Logo 与签名。0.2.3 及更早版本需要先从本页下载 `LovestCode-Android-0.2.4.apk`，核对 `SHA256SUMS-Android-0.2.4.txt` 后覆盖安装一次，不要卸载；以后可在应用内更新。连接、登录状态和聊天草稿保留。

本次只更新 Android，Windows 继续使用 [0.16.2](https://github.com/Lovest20018/Codex-Dock/releases/tag/v0.16.2)，无需重新安装。更新仍由用户确认下载和系统安装，不静默安装。审查及验证边界见 [0.2.4 审查](review-android-0.2.4.md)。
