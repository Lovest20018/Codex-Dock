# Windows 与安卓图标统一

本轮按用户选择，将候选 01 的开口 D、接入箭头与候选 03 的浅底配色结合。当前选定方案为“接入 · 浅绿”，使用 `#EEF2ED` 浅绿底、`#23453C` 深墨绿主体与 `#498E70` 柔和绿箭头。图形保持两块基本形状。

## 源文件与应用范围

`assets/brand-refresh-20261117/selected.json` 定义选定形状与颜色。运行 `pwsh -NoProfile -File scripts/apply-brand.ps1`，从同一份定义生成：

- `assets/brand/` 中的 SVG、16–1024 像素 PNG、多分辨率应用 ICO 及深浅单色托盘 ICO；README 使用该目录中的 Logo。
- WPF 的 `Brand.xaml`、EXE/窗口图标和深浅托盘图。
- WinUI 的 EXE、侧栏、窗口及托盘图。托盘在启动时依据系统任务栏主题选择深浅图，与原有 WPF 规则一致。
- 安卓自有资源层中的应用内图标、独立启动器前景、浅底背景、Android 13+ 单色主题层和通知图标。
- Web 自有覆盖层中的页头图标、favicon、Apple touch、192/512 主屏幕图标；保留固定上游原文件，通过现有公开路径提供自有资源。页面与 Web manifest 使用新内容摘要，避免缓存旧上游图标。

候选目录保留原三组设计供追溯，应用现在使用 `assets/brand` 中的选定版，不再引用 Windows 旧提案目录。安卓上游资源保持固定快照，构建时使用自有覆盖资源；聊天中的 Codex 提供商图标保留其原本含义。

## 安卓系统适配

自适应图标使用无背景阴影的独立矢量前景和 108dp 图层，主体置于中央安全区，背景由系统按设备形状裁切。API 33 资源增加单色层；通知图标为透明背景上的白色形状。这些约束依据 [Android 自适应图标文档](https://developer.android.com/develop/ui/compose/system/icon_design_adaptive)。

使用 Android 14 隔离模拟器，将同一份自有图标资源编译进独立的资源探针应用，实际加载 `AdaptiveIconDrawable`。13 项检查通过，包括资源可解析、安全圆内无越界、前景最小尺寸、最终配色、应用内图标、单色主题与透明通知图。前景离中心最大半径约 30.6dp，小于 33dp；这验证资源与系统渲染，不等于正式 APK 或各品牌真机验收。

探针使用独立包名与临时测试签名，运行后卸载；不会代替正式应用证书。测试入口为 `tests/android/run-brand-icons.py`，不向正式 APK 注入测试代码。

## 构建与签名状态

Windows 新版 WinUI 完整窗口 423 项、WPF 兼容窗口 309 项均通过。Core 与共享应用层没有因图标修改而改变，DLL 摘要对应前一阶段的 362 项结果；预览 ZIP 的逐文件校验及解压后检查记录在 `artifacts/brand-applied-20261117/`。

安卓源码图标已替换，但当前本机默认发布签名目录 `%LOCALAPPDATA%/CodexDock/android-signing` 不存在。按用户要求搜索整个项目的证书文件与 257 个 ZIP 的成员名称后，只找到 `artifacts/android-helper-regression/fixture.p12`，它是 TLS 回归测试证书，不能签署已有应用的升级包。报告只记录路径，没有显示或提取私钥、口令。

需要原发布签名目录中的 `codex-dock.p12` 与 `store-password.txt` 后，才能生成沿用现有证书的覆盖安装 APK。本轮没有创建替代发布密钥，也没有声称已经完成该 APK。Windows 仍为 0.18.0 预览基线，安卓源码保持 0.2.4 / versionCode 7；本轮未公开发布新版本。
