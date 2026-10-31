# 0.13.2 手机接入简化

## 改动与上游核对

0.13.1 将允许的代理 socket 来源与转发头信任绑定到一个 IP 字段，使已有公网端口映射仍需额外配置白名单。此次移除此字段与接入白名单。上游固定提交 `0721aa7dafa6177751ba7e568e0ad0efee05655b` 的 [监听选择](https://github.com/try2love/codex-mobile-bridge/blob/0721aa7dafa6177751ba7e568e0ad0efee05655b/bridge/features/network/addresses.py) 选择本机/LAN 接口，[Auth.client](https://github.com/try2love/codex-mobile-bridge/blob/0721aa7dafa6177751ba7e568e0ad0efee05655b/bridge/features/auth/auth.py) 将可信转发头单独处理。上游不要求用一个代理来源 IP 才能接受 LAN 连接。

Dock 的裁剪实现统一将公开手机端口绑定 `0.0.0.0`，保留单一 HTTPS 域名的 Host、Origin、CSRF、Secure Cookie、密码/扫码与设备撤销。控制端口仍为 `127.0.0.1` 且要求独立 bearer。只允许现有专用手机路由，未加入终端、任意 RPC、文件系统或其他 AI 客户端。

后台协议更新为 3，管理器可读取原 protocol 1/2 DPAPI 设置，忽略旧 `proxyAddress`，保留密码、实例、控制端口和设备授权。显式启动时发现旧 worker 协议会先停止它再启动新版。程序不自动升级当前真实服务。

不配置远端可信代理。来源不为本机可信地址时，上游 Auth 使用 socket 来源计数并显示设备，不采信任意 X-Forwarded-For。旧持久化 trustedProxies 在加载时清空。多个手机经过同一代理可能共用失败次数；通过 Dock 可解除封禁。本机反向代理原有的转发头逻辑保留。

## 验证

回归覆盖无需来源白名单的远端访问、未登录历史仍拒绝、代理变化后的安卓 Cookie 恢复、伪造转发头不能重置失败计数、原 Host/Origin/CSRF 保护和旧协议设置迁移。真实冻结 EXE 验证公开端口的不同 IPv4 目标及来源、管理端口只监听回环、授权跨重启及撤销；UI 检查覆盖可改端口和移除代理字段后的布局。

本地结果：39 项手机 Python 测试、80 项核心回归、10 项浏览器/QR 检查通过，桌面程序和核心构建均为零警告、零错误。沙箱中的回环连接最初被拒绝，随后在允许回环测试的环境运行同一套用例并全部通过。发布脚本另强制检查实际发布 EXE 的完整 UI，并记录源码提交、文件散列和 UI 报告；安装验证对隔离安装目录逐文件比较并运行 UI 与卸载检查。最终结果保存在发布包的 manifest、verification/ui-report.json 和本机 installer-verification-0.13.2.json。

测试均使用隔离目录与虚构凭据，不向真实 Codex 聊天发送、停止或审批，不改变路由器、FRP 或真实设备授权。

## 部署边界

本地测试不等于实际群晖、公网链路或安卓真机验收。已有路由端口映射仍须能到达 Windows 电脑，反代保留完整 Host（含外部端口）。入口 TLS 只覆盖手机到反代的链路；反代到 worker 的 HTTP 应由可信或加密通道承载。实际设备上检测入口、扫码和撤销仍需部署后确认。
