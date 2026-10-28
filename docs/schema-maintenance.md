# 配置字段维护

配置字段在 `config/config-schema.json` 维护；经过人工核对的作用说明在 `config/config-help.json` 独立维护。不克隆或构建其他配置工具。两份文件在构建时嵌入程序，更新后需重新构建发行版。

## 更新步骤

1. 查看 Codex 官方配置文档或其源码中的配置 schema，确认本次变更确实对应 `config.toml`。
2. 修改 `entries` 中对应条目；新增字段可以先只填写 `path`、`name`、`kind`、`category` 和 `platforms`。
3. 补充中文说明、枚举、范围或生命周期；`default` 未知时省略，不推断 Codex 的实际运行值。
4. 更新 `schema_version` 日期，执行核心测试和界面检查。

官方配置参考：<https://developers.openai.com/codex/config-reference/>。

## 数据结构

顶层为 `format`、`schema_version`、`categories` 和 `entries`，没有 Codex 版本矩阵或上游提交号。

```json
{
  "path": "model_reasoning_effort",
  "name": "推理强度",
  "kind": "enum",
  "category": "model",
  "platforms": [],
  "enum_values": [{ "value": "low" }, { "value": "medium" }, { "value": "high" }],
  "chinese": "默认的推理投入；所选模型必须支持对应档位。"
}
```

示例仅说明格式，枚举以实际字段定义为准。支持的类型包括 `boolean`、`string`、`enum`、`integer`、`number`、`array`、`union`、`bool_or_table`。父表、map 容器和递归结构作为说明项，编辑其子项。动态名称使用 `<name>`；实际名称包含点、空格或文件路径时使用 TOML 引号。

未收录的文件字段会出现在“未收录配置”，以 TOML 值编辑；也可在“显示选项 → 添加配置项”中加入新字段。这里只验证 TOML 语法，不保证 Codex 支持。复杂表数组保留原文，需在外部编辑器修改。

连接页管理 `model`、`model_provider`、`openai_base_url`、`chatgpt_base_url`。偏好项 `web_search`、`features.fast_mode`、`features.context_management` 仍通过配置编辑页显式修改。不要在保存普通配置时自动重写连接参数。

初始字段数据沿用原有授权快照，未重新宣称其为最新 Codex 文档；删除外部仓库不影响这些条目的许可归属。保留 `third_party/Superba-LICENSE.txt` 和第三方来源说明。

## 配置作用说明

`config-help.json` 按字段路径关联名称、简述、实际作用、取值解释、场景、限制、默认行为、关联字段、来源、核对日期和核对状态。`verified` 表示核对官方文档，不代表运行时验证；`historical` 表示保留的旧说明，当前可用性待验证；`local` 用于本工具的连接写入规则。

优先在官方文档确认语义。对不再收录的实验字段保留历史含义并说明差异，不根据名称猜测，也不静默更换用户的旧写法。未知默认值不推断为 true 或 false。字段表原有中文或英文与新核对结果不一致时，界面优先使用帮助表，避免同时给出相互冲突的解释。

配置模板维护于 `BuiltInTemplates`，与服务商线路独立。模板详情复用帮助表。新增模板必须设置真实来源，明确哪些项目默认不选；应用前展示文件值与已有草稿，校验所有选择后再一次合并。
