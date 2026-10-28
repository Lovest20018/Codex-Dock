# 0.6.1 Krill 推荐与列表性能修复

- 连接页补回“应用 Krill 推荐配置”，加入草稿后显示全部 7 个推荐项，保存后生效，撤销可放弃。
- 推荐设置：`web_search="live"`、`suppress_unstable_features_warning=true`、`features.fast_mode=true`、`features.context_management=true`、`features.token_budget.enabled=true`、`features.token_budget.use_history_notes_extension=true`、`analytics.enabled=false`。
- 按先前固定来源核实：[Krill PAT 推荐表](https://github.com/krill-lab/superba/blob/f78d77a2849aceb8dffc1f27a83869ffceab7e90/crates/superba-core/src/codex.rs)。使用本地常量，不依赖上游仓库或后端；推荐含开发中配置，可在推荐筛选中逐项调整。
- 高级选项之前会同步创建近千行控件；现在每页最多创建 30 行，分类与搜索只刷新一次。
- 搜索覆盖全部结果；翻页、筛选和高级选项切换保留草稿。
- 核心回归增加推荐项完整性、只入草稿、持久化及原值恢复检查；UI 回归增加 7 项展示、保存、认证不变、翻页草稿保留以及多次高级切换耗时检查。
