# 安卓上游首批修复移植（2026-11-09）

本轮移植适合 Codex Dock 的第一批修复。最初在 Android 0.2.2 / Windows 0.16.1 开发状态生成 QA 包完成行为验证；审查后正式版本为 Windows 0.16.2、Android 0.2.3（versionCode 6），沿用原包名和签名。

## 来源与范围

- 原始快照：`try2love/codex-mobile-bridge` 2.1.7 / build 40，提交 `0721aa7dafa6177751ba7e568e0ad0efee05655b`。
- 移植来源：[v2.2.0 / build 58](https://github.com/try2love/codex-mobile-bridge/releases/tag/v2.2.0)，提交 [`76acf47b91ab3ae41f57fc7a8664da1c9224d206`](https://github.com/try2love/codex-mobile-bridge/commit/76acf47b91ab3ae41f57fc7a8664da1c9224d206)。发布于北京时间 2026-11-08 20:15。
- 核对完整标签源码，不依赖 GitHub compare 最多返回的文件列表。下载档案 SHA-256：`077bca779257f11c013c7982beaab119370f77f43ca8c1b4591a45be0afa0423`。
- `upstream-manifest.json` 保留原快照摘要；`upstream-overlay.json` 共 25 项显式修改，其中 7 项为原有适配。本轮 18 个生产文件与 1 个测试夹具登记来源提交及来源文件摘要；`httpd.py` 与原有适配重叠。
- 这次是选择性移植，基线没有改成完整 2.2.0。

## 已移植的行为

| 功能 | 本轮结果 | 适配决定 |
| --- | --- | --- |
| 独立通知 | 同一聊天不同事件各保留一张卡片；同一事件重试更新原卡片且不重复响铃；查看聊天保留事件卡片 | 用已认证 HTTPS origin、通知流和事件序号定位；保留单地址静音、通知权限、旧聊天卡片清理和用户通知渠道配置 |
| 冷启动布局 | DOM 或同源样式未就绪时等待加载事件，样式可读后补齐共享和 Lovest 布局 | 同步修复自有 `lovest-shell.js`，保留原生版本握手，加载重试不重复插入 CSS |
| 聊天阅读位置 | 刷新、追加、前插历史、重排及展开内容更新时保留展开状态和内外层滚动位置；切换聊天重置 | 复用共享活动节点函数，只移植当前启用的 Codex 时间线 |
| 首次复制 | 深度链接在首次捕获点击前带复制标记，原生确认成功后关闭菜单 | 保留 token、可信点击、当前 WebView、origin 与焦点检查；失败时保持菜单可重试 |
| 图片保存 | 预览底部提供“保存到本地”；图片下载取原图，工作区图片使用既有授权文件下载地址 | JS 与原生下载器要求同源及单个 `download=1`；后端仍走原鉴权、owner 和会话范围；下载响应使用 attachment 与 private/no-store |
| 模型兼容 | 无 summary 时展示有效的 reasoning content；原始思考与摘要增量可流式显示；中文等 Unicode 模型 ID 可读取与保存 | 摘要优先，完成事件覆盖临时增量；迟到的开始事件不清空增量；拒绝异常索引、空白、控制字符及超长 ID |

## 保留的项目契约

本轮保留三页原生导航、常驻 WebView、0.2.2 输入区常驻修复、账号范围草稿、最后聊天定位、固定 HTTPS 地址和 Codex-only 过滤。已有登录锁修复继续有效。发送、授权和停止的未知结果没有自动重放；被动读取不启动任务、不切换账号。

未引入上游多通道网关、UU 网络、其他 AI 客户端、聊天删除、完整旁聊持久化或独立 API profile 体系。模型创建选择器、缓存刷新与禁用客户端轮询、IPC 恢复列入下一批单独评估。

## 验证结果

| 验证 | 结果 |
| --- | --- |
| 上游逐文件校验 | 1264 个已有文件，25 项显式 overlay，全部通过 |
| Dock Python | 37 项通过，含新增原图 HTTP 下载、reasoning 事件与 Unicode 模型目录回归 |
| 受影响上游 Python | 258 项发现，254 项通过，4 项平台条件跳过；覆盖 CLI、上传、模型、账户、旁聊、模块及资源边界 |
| Node | 89 项通过；包含移动共享脚本、时间线、下载及新增下载 URL 边界 |
| Java 下载器 | 14 项基本下载和 47 项范围下载检查通过，实际本机 HTTPS 通信 |
| 浏览器 | 390/1280px 各 20 项阅读位置检查；原图保存、工作区图片、44px 触控入口、延迟 CSS、首次原生复制流程通过 |
| Dock 浏览器共同回归 | 22 项流程、五种宽度通过，包括草稿、IME、未知发送结果不重发和账号范围隔离 |
| Android 构建及独立 instrumentation | 已有签名 QA APK 构建通过；隔离 Android 14 模拟器 72 项通过，包括 3 张独立通知、重试去重/静音、首次真实剪贴板复制、草稿、导航、旋转、键盘、下载及离线返回 |
| 升级 | 隔离模拟器从既有 0.2.1 包升级，保留偏好与电脑列表通过 |
| 差异检查 | `git diff --check` 通过 |

4 项跳过分别为：POSIX 远端 shell、POSIX live symlink，以及当前 Windows 无创建符号链接权限的两个上传目录边界夹具。没有把这些跳过记作通过。iOS/Xcode、WebKit 和实体手机未验证；本轮目标是 Windows 网关与 Android。

旧时间线分页测试原来单独加载 `timeline.js`，新增共享依赖后补加载真实 `activity.js` 并提供空 DOM children，原断言全部保留。模拟器复制测试等待菜单进入 WebView 可视区后执行真实触摸，未放宽产品手势检查。原生下载器补充拒绝编码键名或空值构成的重复下载标记，与网页的重复参数规则一致。

## 本地 QA 产物与复跑

QA 目录为 `artifacts/mobile-backport-2026-11-09/`，包含浏览器、Python、Node、Java、构建日志和 Android 报告。安装包为 `LovestCode-Android-0.2.2-qa.apk`，SHA-256：`0e34a22cb4a34e6db73a875820805a6436ac6c24798509c1912fcacb3d305c90`。

上述 0.2.2 QA 包是最初未提交源码的验证产物。正式交付由同一个干净提交重新生成 Windows 0.16.2 与 Android 0.2.3，分别使用正式文件名、源码档案与 manifest；正式下载见各自 Release，不能用上述 QA 摘要代替正式校验清单。聊天网页、图片 HTTP 下载和模型兼容性位于 Windows 网关，完整功能须配套更新。正式发布说明见 [Windows](release-0.16.2.md) 和 [Android](release-android-0.2.3.md)。

常用复跑入口（使用本机 Python 3.12、Node、Chrome、JDK 17 和 Android SDK）：

```powershell
python -B scripts/verify-mobile-upstream.py
python -B -m unittest discover -s tests/mobile -p 'test_*.py'
node --test tests/mobile/backports.test.cjs
node tests/mobile/backports.browser.cjs
node tests/mobile/dock-ui.browser.cjs
python -B tests/android/helper-regression.py --jdk $env:JAVA_HOME
python -B scripts/build-android.py --jdk $env:JAVA_HOME --sdk $env:ANDROID_HOME --allow-dirty --output-dir artifacts/mobile-backport-2026-11-09
python -B tests/android/run-smoke.py --jdk $env:JAVA_HOME --sdk $env:ANDROID_HOME --apk artifacts/mobile-backport-2026-11-09/LovestCode-Android-0.2.2-qa.apk
```

`run-smoke.py` 仅允许模拟器，默认使用隔离的 `emulator-5562`。构建和模拟器回归须能访问既有私有签名目录；HTTP 夹具须允许本机回环连接。上游 Python 模块通过 `tests/mobile/upstream_codex_regression.py --module <模块名>` 在独立解释器执行。
