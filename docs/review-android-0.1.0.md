# Android 0.1.0 实施审查

## 结果与边界

Dock 独立应用身份 `io.github.lovest20018.codexdock`，versionName 0.1.0 / versionCode 1，minSdk 26 / targetSdk 35。复用固定上游 Android 传输、安全、扫码和文件帮助类，独立维护 MainActivity、SettingsPage、MobileStrings 和品牌资源。网页仍由 Windows Dock 0.15.1 提供，本次没有改动 Windows 程序、工作台发送协议或上游快照。

首页、电脑列表、聊天菜单、设置和更新来源均归 Dock 管理。去掉 DSH、其他 AI、上游部署与 APK 更新菜单；原生上游 APK 更新协调和安装会话类不编译。下载帮助类的 MobileRelease 数据 / URL 帮助依赖仍保留，原生菜单不再调用 APK 更新流程。文件下载只接受当前已鉴权网关的白名单附件路径。设置中的“版本与更新”打开 Dock GitHub 发布页，不自动下载或安装。

包名与长期签名独立于上游 App，需要重新配对或登录，不读取旧 App 私有数据。签名存储在仓库外并限制为当前用户 / SYSTEM，构建清单和 APK 只带公开证书。签名脚本拒绝覆盖既有密钥，发布构建拒绝脏工作区，校验上游 1264 个文件、显式覆盖、ZXing SHA-256、清单与 JSON 版本 / SDK 一致性、APK 签名及对齐。

## 保留的安全与生命周期约束

- 原生 prompt 继续核验 token、origin、当前 WebView、窗口焦点和有效值 / 长度；无效保留名称的桥接 prompt 显式取消，避免落到普通网页输入框。
- 外部导航继续要求真实点击并二次确认，子框架限制、可信设备绑定、Cookie 路径与回调代际检查保留。
- 设置、主题、窗口尺寸变化不重建 WebView，系统文件选择仍验证读取权限和回调代际。
- 下载独立于聊天页，保留 Range / ETag 续传、每段重读 Cookie、拒绝携带 Cookie 的跨源重定向及失败清理。
- 窗口重新获得焦点时恢复已保存的系统栏颜色并重新触发网页采样；修正从深色设置返回时系统栏延迟恢复。
- 普通 APK 不包含 Firebase；任务提醒使用已鉴权网关同步，后台须用户主动启用有常驻通知的前台服务。

## 验证

Android 14 / API 34 独立 AVD、1080×2400 / 420 dpi。生产 APK 无 debuggable 标志；签名 Instrumentation 放在另一个测试 APK，不向生产包添加测试入口。隔离真实生产 Python / Web 网关的检查覆盖：首页、密码登录、伪造桥接 token、无手势外链、Dock 设置、主题 / WebView / localStorage 保留、配置变化、聊天菜单与返回、系统文件选择取消、旧代文件回调、鉴权诊断下载、下载跨页保留、重新连接恢复 Cookie / 存储、后台前台服务和本地测试通知。截图已检查中文首页、英文首页、浅色 / 深色设置、工作台、菜单、系统文件选择器和下载卡片。

- 初次完整原生检查 17 项通过；最终 APK 加入英文首页检查，由 `tests/android/run-smoke.py` 输出实际报告。
- `node --test src/CodexMobile/upstream/mobile/tests/*.test.cjs`：62 项通过。
- URL 脚本使用 `--android-only`：37 项 URL / 路由断言、通知页复用边界通过；原生设备绑定 Cookie 选择通过。Windows 不运行 iOS Swift 部分。
- 下载帮助类：14 项下载检查与 47 项续传检查通过。原断言保留，仅在 `tests/android/helper-regression.py` 中把夹具服务和 URL 切到本地 HTTPS；本机 HTTP 过滤器会移除超大 Content-Length 并改为 chunked，已用同进程服务器 / 客户端响应头对照复现。HTTPS 只信任临时夹具证书，不改生产网络设置。首次续传运行遇到一次 Windows 目录清理断言，原断言不变重跑全部 47 项通过。
- 个人配置 / 凭据与 Git 历史隐私扫描通过；发布归档单独核对 APK、源码、校验清单与构建 manifest。

以上使用独立配置和不存在的原生执行端，不发送模型任务、不更改真实 Codex 配置、不安装到真机。未将模拟器原生通知等同于公网推送：真实相机扫码、第三方键盘 / 后台策略、Android 8 / 15 真机、公网 HTTPS、真实模型 / SSH / WSL 执行仍需对应环境验收。
