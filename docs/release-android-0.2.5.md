# Lovest Code for Android 0.2.5

本版将安卓图标更新为与 Windows Codex Dock 一致的“接入 · 浅绿”：开口 D、接入箭头、浅绿底和深墨绿。

- 更新应用内图标与启动器图标，使用独立前景和自适应背景。
- 提供 Android 13+ 单色主题图标和透明底通知图标。
- 沿用 `io.github.lovest20018.codexdock` 包名与原发布证书，版本号 0.2.5 / versionCode 8。

直接安装 `LovestCode-Android-0.2.5.apk` 覆盖旧版，无需卸载，连接和草稿数据沿用。也可在“我的 → 版本与更新”检查并下载。电脑端建议配套 [Codex Dock 0.19.0](https://github.com/Lovest20018/Codex-Dock/releases/tag/v0.19.0)；已有固定 HTTPS 入口不需要因图标改动重新设置。

本版不增加新权限、网络信道或设备能力分级。聊天、配对、登录、通知与更新流程继续使用现有实现；Windows 手机网关的 v13 同步与重连修复随 Windows 更新。

正式 APK 在 CI 使用原签名构建，校验 v2/v3 签名、固定证书、非调试状态及源码输入摘要。图标已在 Android 14 模拟器通过 13 项资源检查；完整连接与活动任务追加已有 72+22 项隔离检查。实体手机相机、省电策略和公网链路仍需实际设备验收。

[本版审查](https://github.com/Lovest20018/Codex-Dock/blob/android-v0.2.5/docs/review-android-0.2.5.md)
