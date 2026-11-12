# 安卓上游与签名核对（2026-11-12）

本地已选择性移植上游 v2.2.0 的部分修复，未全量升级快照。现有 Lovest Code 0.2.4 APK 签名有效，沿用本项目登记的发布证书。本轮只核查，未合并新代码、构建、安装或发布安卓版本。

## 上游版本与本地合入情况

- 当日重新读取公开仓库页面和提交 Atom，主分支为 [`41e833ab12c240aca1574dd7518f37c63a3fe5b1`](https://github.com/try2love/codex-mobile-bridge/commit/41e833ab12c240aca1574dd7518f37c63a3fe5b1)，时间为北京时间 2026-11-08 20:55；该提交仅同步 README 和网站。
- 最新正式版仍为 [`v2.2.0`](https://github.com/try2love/codex-mobile-bridge/releases/tag/v2.2.0)，目标提交 `76acf47b91ab3ae41f57fc7a8664da1c9224d206`。
- 本地 `upstream-manifest.json` 基线仍是 **2.1.7 / build 40**、`0721aa7dafa6177751ba7e568e0ad0efee05655b`。`upstream-overlay.json` 登记 25 项显式覆盖，其中包含来自 v2.2.0 的定向移植；详见[已移植内容与验证](mobile-upstream-backport-2026-11-09.md)。
- 已移植独立通知、冷启动样式就绪、阅读位置、首次复制、原图保存、reasoning 内容与 Unicode 模型 ID 等。没有引入完整多信道、ZCode、聊天永久删除、独立 API profile 等整套新功能。
- 重跑 `scripts/verify-mobile-upstream.py`：**1264 个文件、25 项覆盖全部通过**。Windows 速度兼容任务没有改动安卓或上游源码。

## 签名相关代码

使用既有 v2.2.0 完整源码归档（SHA-256 `077bca779257f11c013c7982beaab119370f77f43ca8c1b4591a45be0afa0423`），与本地 2.1.7 脚本逐行比较并归一换行：

| 文件 | 结果 |
| --- | --- |
| `scripts/build-mobile-android.py` | 仅增加 ZCode 图标打包；签名逻辑相同 |
| `scripts/ci-android-build.sh` | 相同 |
| `scripts/sign-update.cjs` | 相同；这是更新元数据签名，不是 Android APK 证书 |

因此，检查的脚本中没有发现这次从 2.1.7 到 2.2.0 的签名实现更新。上游本地预览脚本使用其预览证书；本项目正式构建使用自己的既有私钥并强制核对证书、APK v2/v3 签名。没有导入上游证书、更换本地私钥或运行签名初始化脚本。

这里没有下载并比较上游发布 APK 的实际证书，因此不将“源码签名逻辑相同”解释成对上游私钥或发布证书是否轮换的判断。

## 本地 APK 实测

对既有 `artifacts/LovestCode-Android-0.2.4.apk` 运行 SDK 35.0.0 的 `apksigner verify --verbose --print-certs`：

- 验证通过，APK Signature Scheme v2、v3 均有效。
- 包名 `io.github.lovest20018.codexdock`，版本 0.2.4 / versionCode 7（源码元数据及既有构建清单一致）。
- 证书 SHA-256：`0c31e8dc7e29b7e835343db7551df5a505498959e87d4462f1b6bd9b5e72098e`，与 `src/CodexAndroid/android.json` 登记值一致。

当日证据位于 `artifacts/android-upstream-audit-2026-11-12/`，包含公开仓库页面、提交 Atom、脚本比较结果和 APK 验签输出。两次 Git HTTPS 引用查询遇到连接重置，当前分支以直接仓库页面的 `currentOid` 和当日 Atom 交叉核对；没有把失败的 Git 查询记作验证成功。
