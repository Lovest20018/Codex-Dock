# Lovest Code for Android 0.2.3

- 同一聊天的不同事件分别保留系统通知卡片；重试同一事件更新原卡片且不重复提醒；打开聊天保留这些事件通知。
- 修复冷启动时样式尚未就绪导致原生布局缺失的问题；延迟加载后补齐布局且不重复插入样式。
- 支持图片预览中的原图下载，保留当前电脑、鉴权、重定向和文件完整性校验。
- 保留文字输入区常驻、聊天／电脑／我的导航、草稿、上次聊天和独立设置。

版本为 **0.2.3 / versionCode 6**，应用 ID、Logo 和签名与 0.2.2 一致，可直接覆盖安装。支持 Android 8 / API 26 及以上。

请核对 `SHA256SUMS-Android-0.2.3.txt` 后安装 `LovestCode-Android-0.2.3.apk`。保留已保存电脑与登录，更新后重新打开连接检查通知和图片保存；通知权限及各电脑的静音选择继续生效。

完整使用本批功能请配套更新 [Windows 0.16.2](https://github.com/Lovest20018/Codex-Dock/releases/tag/v0.16.2)。聊天网页、首次复制标记、图片 HTTP 下载及模型兼容性由 Windows 网关提供，仅替换 APK 不会更新网关。

本页只包含安卓 APK、对应源码、签名 manifest、SHA-256 与发布 manifest。Windows 使用独立发布页，并继续作为仓库 latest。行为验证及限制见 [Android 0.2.3 审查](review-android-0.2.3.md)。
