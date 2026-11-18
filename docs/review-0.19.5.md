# Windows 第一批验收范围

## 首次连接和首页

验收覆盖首次打开不写配置、服务模型同页选择、密钥遮蔽、返回时保留草稿、目标变化后重新核对、仅保存、备份后打开、读取原生界面写入的登录，以及聚焦运行中的窗口。登录和保存的界面回归使用隔离目录和模拟桌面目标。真实账号的登录与服务商连通性需要在使用者的 Codex 中核对。

首页按进程状态显示操作。多实例且没有与当前资料关联的启动记录时，用户明确选择窗口。已运行且资料无法确定时，可从原快捷方式登录，或在原生 Codex 中退出后重新打开。

## 上游合入

- W2 将原有代理排除项与 `localhost`、`127.0.0.1`、`::1` 合并至被启动进程的环境。无显式 `NO_PROXY` 时，沿用启用中的 Windows 系统代理排除项。[Codex++ 实现](https://raw.githubusercontent.com/BigPizzaV3/CodexPlusPlus/70fb845eef3a44bfb2e44d382eb4b75fa7ec0204/crates/codex-plus-core/src/proxy.rs)
- W3 优先使用当前注册的商店版内核。Windows 拒绝直接启动时校验已有副本，必要时复制内核及配套工具后按大小和 SHA-256 校验。测试覆盖助手文件损坏、内核变更与自动查找。[Superba 实现](https://github.com/krill-lab/superba/blob/3e6ffccf3f4406cdf8485fb5a8e8b51839494610/crates/superba-appctl/src/runnable.rs)
- W4 按 `0.171.0-alpha.5` 的官方 Schema 核对路径和约束，增加 32 条定义并更新发布阶段。旧字段保留，当前执行端观察值可在“配置来源”读取。[配置资料维护](schema-maintenance.md)

诊断改进与手机配对、聊天功能按后续批次处理。[分批计划](simple-workflows-and-upstream-plan-2026-11-18.md)
