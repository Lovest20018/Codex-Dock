# 官方登录收敛（2026-11-12）

按用户要求移除 Dock 自己的设备码、浏览器 CLI 登录、回调失败自动回退、授权链接、设备码复制及 CLI 程序位置选择。API Key 直连和旧 PAT 迁移保持。

参考固定的 Superba `v0.1.1-alpha.16`：[`official_auth`](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.16/crates/superba-config/src/render.rs#L331) 优先保留当前官方登录，其次读取加密暂存；没有凭据时提示到原生桌面登录。[暂存实现](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.16/crates/superba-config/src/stash.rs)按配置目录隔离，[混合登录测试](https://github.com/krill-lab/superba/blob/v0.1.1-alpha.16/crates/superba-config/tests/mixed_login_tests.rs)验证保留当前令牌、恢复备份并清理第三方账号接口。

## 当前流程

1. ChatGPT 登录由原生 Codex 桌面完成。Dock 显示“重新读取官方登录”和“使用已备份的官方登录”。
2. 重新读取只接受所选目录中的官方文件凭据，保留当前模型、API Key、连接资料及普通设置草稿；未发生变化时保持干净状态，不要求重复保存。若除认证外的配置也变化，停止读取并保留草稿，提示用户核对后重新读取整份配置。
3. 恢复使用本项目既有的加密配置备份，按所选配置目录查找最近一次被替换的官方登录。只暂存凭据，不立即改配置，不读取系统钥匙串，也不跨目录找账号。
4. 当前文件中的官方凭据优先于旧备份，避免覆盖 Codex 轮换后的刷新令牌；替换内存中的暂存凭据时清零旧缓冲。
5. 在原有差异确认页确认后，才保存官方认证和模型网关配置。API Key 直连保存时，官方凭据会随原配置加密备份。
6. 没有凭据时提示去 Codex 桌面登录；退出登录后不会自动复活备份账号。已过期的备份可能需要原生 Codex 重新登录，有效期与刷新由 Codex 管理。

这里复用既有加密备份保存官方登录，没有另建账号库或复制 Superba Rust 源码。没有修改真实用户账号、安全设置或启动真实登录流程。

## 验证

官方登录界面专项 **11 项通过**，覆盖入口收敛、缺少登录时的可操作提示、文件读取、草稿保留、无变化时保持干净、显式备份恢复、当前令牌优先、内存清理、外部冲突、退出登录语义和浅深主题截图。证据位于 `artifacts/official-login-2026-11-12/ui-focused-final/`。

完整核心回归 **132/132 通过**，包含 6 项官方登录恢复检查和原生 Codex 的 API Key 兼容检查，记录于同目录 `core.log`；最终交付程序的全量界面与安装验证以该本地包的 `installer-verification.json` 为准。
