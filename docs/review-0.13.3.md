# 0.13.3 共享代理登录授权修复

## 问题与实现

0.13.2 接受群晖/端口映射而不信任远端 X-Forwarded-For，因此同一 socket 来源可能代表多个手机。旧 Auth.login 达到五次失败后删除同 IP 的所有会话；Auth.get 又将自动封禁作为一般访问禁止。仅移除删除语句仍会使已有设备无法验证。

现在 Auth.permitted 只表示显式 IP 黑白名单规则。login_status 额外检查持久化 autoBlocks，login 和 new_session 使用此状态限制新认证，get 仅检查显式规则和会话有效性。自动失败不再修改 sessions；保存显式策略也不会意外清除被自动限制来源的设备。手动 revoke/block、过期、身份变更保持原语义，锁内持久化及失败计数不变。新登录和配对不会绕过自动限制。

持久化格式和 worker 协议保持兼容，升级需停止旧后台。旧版已删除的会话无法迁移恢复。没有恢复对任意转发头的信任，也未引入代理白名单设置。

## 回归证据

Python 41 项回归通过。新增 Auth 用例覆盖同来源安卓及浏览器 Cookie、五次失败、新登录/配对仍拒绝、重启、保存策略、解除限制、无效 Cookie、单设备撤销和显式 IP 封禁。新增 HTTP 用例模拟两个设备共用远端代理，攻击者轮换伪造 X-Forwarded-For 仍只消耗同一来源计数，已有设备在限制期间和后台重建后仍能读取；未登录历史和缺少 CSRF 的写操作仍拒绝，解除限制保留原 Cookie，撤销只影响目标设备。

首次沙箱 HTTP 测试因 WinError 10013 阻止本机回环连接；在允许回环的环境运行同一套隔离用例后 41 项全部通过。所有凭据和数据均为测试夹具，未接真实 Codex owner。

81 项核心回归通过，其中实际冻结版 CodexDock.Mobile.exe 经五次失败、正确密码仍拒绝、后台重启、原 Cookie 保持有效、解除限制和显式撤销全链路验证。10 项浏览器/QR 检查和 107 项开发版桌面 UI 检查通过，核心及桌面编译零警告、零错误。核心本地输出保存在 artifacts/research/core-0.13.3.log，其他报告在 artifacts/mobile-browser/report.json 和 .ui-test-v0.13.3-dev/report.json。

最终发布脚本还强制对实际 EXE 运行完整 UI 回归，比较发布/安装文件，验证隔离安装和卸载；以包内 verification/ui-report.json、manifest 和本地 installer-verification-0.13.3.json 的实际结果为准。

## 功能核对

按上游固定提交 0721aa7、2.1.7/build40 的 README、手机说明、安卓原生代码、Web 模块与公开路由，对照 Dock 路由白名单、service 和 app.js。结果记录在 [功能对照](mobile-upstream-comparison.md)。原 APK 的扫码、绑定和导航协议保持，但原生壳不会补齐缺失的服务 API；通知、文件、账号和协助更新应分别判断。

代码与本地协议检查不代表已完成公网链路、安卓真机、蜂窝网络或真实模型调用验收。
