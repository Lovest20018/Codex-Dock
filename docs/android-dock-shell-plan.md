# Lovest Code 安卓客户端

Lovest Code 0.2.0 是独立安装的安卓原生壳，连接 Windows Dock 0.15.1 的共享 Codex 工作台。聊天、审批、项目、Git、文件、终端、通知及 WSL / SSH 沿用电脑侧能力；电脑配置、账号登记和网关生命周期继续由 Windows Dock 管理。

## 安装与连接

安装 `LovestCode-Android-0.2.0.apk`（Android 8 / API 26 及以上）。它的 applicationId 为 `io.github.lovest20018.codexdock`，沿用 Codex Dock 安卓 0.1.0 的包名、签名与 Logo，可直接覆盖升级并保留本机连接和登录；与旧上游 App 并存。旧 App 的私有电脑列表和 Cookie 不跨应用读取，首次使用需重新扫码或添加网关并登录；电脑上的聊天记录不受影响。

在 Windows Dock 的“手机连接”中生成配对码，用安卓首页“扫码连接电脑”连接；也可输入完整网关地址并用网关账号密码登录。公网地址必须为 HTTPS，HTTP 仅接受明确的局域网 / 回环 IP。App 不代办域名、证书和穿透配置。

主导航为聊天／电脑／我的。聊天详情隐藏底部导航；电脑详情提供账号与接入、诊断、改名和移除。切换标签保留当前聊天、草稿、工作台及下载。聊天菜单保留返回电脑列表、通知、我的和刷新；启动时恢复上次电脑和聊天定位。原生设置提供任务通知、按电脑控制通知来源、后台通知、主题、当前版本及 Dock 发布页。没有 DSH、其他 AI、上游部署 / APK 更新入口。版本页打开 Dock GitHub Releases，由用户选择安装 APK；本版不自动下载或安装更新。

普通 APK 不包含 Firebase SDK。前台同步和用户主动开启的后台通知沿用已鉴权的网关连接，后台使用带常驻通知的 Android 前台服务；不承诺进程被系统终止后仍可收到任务提醒。聊天、登录和工作台需要电脑网关在线。

## 源码与构建

Dock 自有源码位于 `src/CodexAndroid/`，包含 MainActivity、SettingsPage、MobileStrings、MobileWebScripts、清单和品牌资源。`assets/lovest-shell.js` 提供版本化导航桥、阅读位置和安卓专属页面样式；其余扫码、下载、Cookie、设备绑定和通知帮助类复用固定上游快照；上游源码与七项既有 Web 覆盖未因本轮修改。构建校验全部上游散列，不复制共享 Web 脚本，资源只在临时目录叠加。

构建需要 JDK 17、Android SDK platform 35、build-tools 35.0.0、Python 3.12+。从 `src/CodexAndroid/android.json` 的固定 URL 下载 ZXing Core 3.5.3 到 `artifacts/android-deps/zxing-core-3.5.3.jar`，构建检查 SHA-256。

首次生成长期签名（只运行一次，禁止覆盖旧密钥）：

```powershell
pwsh -NoProfile -File scripts/init-android-signing.ps1 -Jdk C:/path/to/jdk-17
```

默认保存在 `%LOCALAPPDATA%/CodexDock/android-signing`，仅当前用户和 SYSTEM 可访问。请将整个私有签名目录安全备份，后续升级必须沿用同一密钥；不要提交或分发密钥与密码文件。APK 和 manifest 只公开证书与公钥指纹。

审查、测试并提交后，从干净工作区构建：

```powershell
python -B scripts/build-android.py --jdk C:/path/to/jdk-17 --sdk C:/path/to/Android/Sdk
```

输出 APK、SHA-256 清单和包含源提交 / 输入散列 / 签名验证结果的 manifest。临时验证可加 `--allow-dirty`，其输出带 `-qa` 后缀，不作为发布包。Windows 安装器和自更新版本仍为 0.15.1，Android 单独使用 versionName 0.2.0 / versionCode 3；后续 Android 更新须递增 versionCode，并同步 `android.json` 和清单。

## 验证与边界

`tests/android/run-smoke.py` 在单独签名的测试 APK 中运行 Instrumentation，目标 APK 不加入测试钩子、不开放调试。使用明确的模拟器 serial、ADB 回环端口转发和临时 Codex 数据；拒绝直接在真机 serial 运行。

```powershell
python -B tests/android/run-smoke.py --jdk C:/path/to/jdk-17 --sdk C:/path/to/Android/Sdk --device emulator-5562 --apk artifacts/LovestCode-Android-0.2.0.apk
node --test src/CodexMobile/upstream/mobile/tests/*.test.cjs
```

隔离测试的网关运行生产 Python / Web 代码，并在仅回环的测试代理映射 Host、Origin 和 Cookie Secure 属性，让模拟器通过 HTTP 测试页面；不改变生产安全设置。此测试不代表公网 HTTPS 或证书验收。执行端使用不存在的原生程序，不发起模型任务。

UI 与交互调整见 [Android 0.2.0 说明](release-android-0.2.0.md)，改名见 [Android 0.1.1 说明](release-android-0.1.1.md)，原始功能验收记录见 [Android 0.1.0 审查](review-android-0.1.0.md)。Android 14 模拟器可以验证界面、WebView、文件与系统通知流程；真实相机扫码、厂商键盘 / 后台策略、公网接入和真实 Codex / SSH / WSL 任务仍需真机与对应环境验证。
