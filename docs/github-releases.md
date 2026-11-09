# GitHub 按平台发布

仓库：[Lovest20018/Codex-Dock](https://github.com/Lovest20018/Codex-Dock)。Windows Codex Dock 与 Android Lovest Code 独立更新、独立构建、独立 Release。某次功能需要两端配合时，在各自版本说明中明确对应版本。历史合并版本保留。

## 选择平台

| 平台 | 版本来源 | 发布标签 | 附件 |
| --- | --- | --- | --- |
| Windows | `src/CodexConfig.App/CodexConfig.App.csproj` | `vX.Y.Z`，例如 `v0.16.2` | 安装器 EXE、便携 ZIP、源码、SHA-256、安装验证报告、release manifest。 |
| Android | `src/CodexAndroid/android.json` | `android-vX.Y.Z`，例如 `android-v0.2.3` | APK、源码、签名 manifest、SHA-256、release manifest。 |

Web 页面、手机后台和模型网关属于 Windows 包。只有安卓原生客户端改动时才递增 Android 的 versionName 与 versionCode，保留原包名及证书。本轮两端均有修改，完整使用原图保存、独立通知及模型兼容性需要 Windows 0.16.2 与 Android 0.2.3。

Windows 的 [稳定版入口](https://github.com/Lovest20018/Codex-Dock/releases/latest) 指向 Windows Release；Android 从 README 中独立的 [Android 发布页](https://github.com/Lovest20018/Codex-Dock/releases/tag/android-v0.2.3) 下载。Android 发布不会替换 Windows 的 latest，已有 Windows 自更新继续可用。

## 触发

工作流为 [Platform release](../.github/workflows/release.yml)。完成所选平台的测试、版本说明与提交后，推送对应标签：

```powershell
# 仅发布 Windows
git push origin HEAD:main
git tag v0.16.2
git push origin v0.16.2

# Android 有原生更新时，独立提交版本后，仅发布 Android
git tag android-v0.2.3
git push origin android-v0.2.3
```

两个示例是各自的入口，不要求一起执行。Windows 标签跳过 Android 构建；Android 标签跳过 Windows 构建。

手动运行时选择 `platform = windows` 或 `android`，默认仅打包所选平台，不公开 Release。产物在成功运行的 Artifacts 中，名称为 `windows-release` 或 `android-release`。需要公开时明确勾选 `publish`；对应版本标签必须已经指向本次源码提交，否则拒绝上传。

标签与所选平台版本不匹配会在构建前失败。已公开的 Release 不覆盖；再次发布应递增发生更新的平台版本并维护其说明。另一个平台无需改版本或重新发布。

## 独立验证与附件

- Windows：核心与网关回归、锁定 Python 构建工具、冻结手机后台、自包含 WPF 发布、实际 EXE 界面检查，以及运行中手机网关的覆盖安装、不同安装目录隔离、逐文件比较、卸载和用户设置保持。
- Android：固定 SDK 和依赖、既有密钥签名，校验非调试 APK 的 v2 / v3 签名、固定证书及对应源码输入。
- 发布：只下载所选平台产物，核对其源码提交、校验和与验证报告。单独生成 `release-manifest.json`，包含 `platform`、`version`、`sourceCommit` 以及该平台附件的大小和 SHA-256。

Windows Release 共 6 个附件，Android Release 共 5 个附件。上传后核对 GitHub 返回的附件大小与摘要、文件清单，才公开。一个平台的发布不等待另一个平台，也不会附上另一个平台的旧包。

构建失败不会公开稳定版；发布失败可能保留该版本临时草稿。同一提交可重跑，只有当前版本草稿中的附件会被替换，不清理其他版本。Actions 产物保留 14 天，稳定 Release 附件持续保留。

## 安卓签名

| Actions Secret | 内容 |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | 既有 `codex-dock.p12` 的 Base64。 |
| `ANDROID_KEYSTORE_PASSWORD` | 同一签名库与 alias 的密码。 |

alias 为 `codex-dock`，证书 SHA-256 固定在 `android.json`。CI 不生成新签名，私钥只恢复到 runner 临时目录，构建结束清理，不进入产物或源码。Windows 构建不读取安卓签名秘密。

普通构建使用 `contents: read`，最终发布 job 单独使用 `contents: write`，使用 GitHub 工作流令牌，无需个人 Token。

## Windows 自更新

Windows“检查更新”读取 `/releases/latest`，只接受 `v主版本.次版本.修订号` 正式版本。Windows 发布设置为 latest；Android 使用独立 `android-v` 标签，发布时不改 latest，因此不会干扰旧版 Windows 的更新检查。

REST 403/429 时改读同仓库 `/releases/latest` 跳转。只接受受支持的 GitHub 下载域名，检查安装器与 `SHA256SUMS-X.Y.Z.txt`，下载后和安装前分别核对 SHA-256。

本地入口仍为 `scripts/package.ps1`、`scripts/verify-package.ps1` 和 `scripts/build-android.py`。禁止从脏工作区制作正式包；隐私扫描排除个人配置、认证、备份、签名、测试运行目录与缓存。不要上传整个 artifacts 目录。
