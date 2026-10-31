# GitHub 双端自动打包与发布

仓库：[Lovest20018/Codex-Dock](https://github.com/Lovest20018/Codex-Dock)。工作流位于 [release.yml](../.github/workflows/release.yml)，Windows 与 Android 从同一提交构建，版本分别读取 WPF csproj 和 `src/CodexAndroid/android.json`。

## 触发

完成测试与提交后，在 Actions 的 **Windows and Android release → Run workflow** 手动触发，默认只生成双端构建产物，不创建或公开 Release。从成功运行的 Artifacts 下载 `windows-release` 和 `android-release`。

需要公开发布时，明确勾选 `publish`，或推送与 Windows 版本一致的 `vX.Y.Z` 发布标签。例如当前 Windows 0.15.1 / Android 0.2.2 使用 `v0.15.1`。普通 main 推送只更新源码；发布标签或勾选公开发布的手动运行才生成稳定 Release。

```powershell
git push origin HEAD:main
git tag v0.15.1
git push origin v0.15.1
```

版本与标签不匹配会直接失败。已公开版本不覆盖；再次发布须递增 Windows 版本并同步发布说明。Android 有实际客户端更新时同时递增 versionName 和 versionCode，保留原包名及证书。

## 构建与验证

- Windows：.NET 核心、网关与前端状态回归；锁定 Python 3.12 构建工具；冻结手机后台；生成自包含 WPF 便携包及 Inno Setup 安装器；对实际发布 EXE 执行隔离 UI 检查，再运行隔离安装、文件比较及卸载检查。
- Android：Java 17、SDK 35 / build-tools 35.0.0；校验固定 ZXing 依赖与上游完整性；签名构建实际 APK，要求 v2 / v3、固定证书指纹和非调试包；逐项核对源码包与构建输入。
- 发布：等待双端成功，核对同一源提交与各文件 SHA-256，建立临时草稿并上传明确列出的附件；核对 GitHub 返回的每个附件大小及 SHA-256 后，才转为最新稳定版本。

失败的构建不会公开为稳定版；发布阶段失败可能保留当前提交的临时草稿，再次运行同一提交可重新上传。工作流仅删除和替换其当前版本草稿内的附件，不清理其他版本。历史废弃草稿需要另行明确清理。

正式 Release 包含 Windows 安装器、便携包、源码、SHA-256 与安装验证 JSON，以及 Android APK、源码、manifest 和独立 SHA-256；另附 `release-manifest.json` 汇总版本、源提交、文件大小与散列。Actions 构建产物保留 14 天，稳定 Release 附件持续保留。

## 安卓签名

仓库 Actions Secrets 使用：

| 名称 | 内容 |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | 既有 `codex-dock.p12` 的 Base64。 |
| `ANDROID_KEYSTORE_PASSWORD` | 同一签名库与 alias 的密码。 |

alias 固定为 `codex-dock`，证书 SHA-256 固定在 `android.json`。不能在 CI 生成新签名替换旧签名。密钥只恢复到 runner 临时目录，构建结束无论成功失败均清理，不上传到构建产物或源码。请在仓库外妥善保存原签名备份；仓库 Secret 不替代备份。

普通构建仅需 `contents: read`，最终发布 job 单独使用 `contents: write`。使用 GitHub 提供的工作流令牌上传 Release，无须在仓库配置个人 GitHub Token。

## Windows 自更新

Windows“检查更新”读取 GitHub REST `/releases/latest`，只接受 `v主版本.次版本.修订号` 正式版本；同版与旧版不提示升级。双端采用 Windows 标签共用一个 Release，安卓自己的版本不干扰 Windows 更新判断。

REST 403/429 时改读同一仓库的 `/releases/latest` 页面跳转，再验证安装器地址及校验清单。安装器与 `SHA256SUMS-X.Y.Z.txt` 缺失时不会下载。下载只允许 GitHub 及 Release 资源域名，下载后和安装前分别核对 SHA-256。

本地打包入口仍为 `scripts/package.ps1`、`scripts/verify-package.ps1` 和 `scripts/build-android.py`。禁止从脏工作区制作正式包；隐私扫描排除个人配置、认证、备份、签名、测试运行目录与缓存。不要上传整个 artifacts 目录。

接口契约见 [GitHub Release API](https://docs.github.com/en/rest/releases/releases) 与 [Actions Secrets](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets)。
