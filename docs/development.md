# 开发与构建

Codex Dock 使用 C#、.NET 8、WPF 和 Tomlyn。面向使用者的安装与连接方式见 [README](../README.md)。

## 本地构建

需要 .NET 8 SDK；生成安装器另需 Inno Setup 6。

```powershell
dotnet build src/CodexConfig.App/CodexConfig.App.csproj -c Release
dotnet run --project tests/CodexConfig.Tests/CodexConfig.Tests.csproj -c Release
dotnet run --project src/CodexConfig.App/CodexConfig.App.csproj -c Release -- --smoke-test .ui-test-local
```

核心和 UI 检查使用虚构凭据、隔离配置目录及测试环境变量。UI 检查覆盖布局、草稿、模板、备份恢复、托盘、开机启动和更新入口；缩放使用布局变换模拟。

## 目录与字段维护

- `src/`：桌面应用与配置核心。
- `config/config-schema.json`：本地字段表，包含分类、类型、枚举和取值约束。
- `config/config-help.json`：独立维护的中文用途说明。
- `tests/`：核心测试。
- `scripts/`：打包、安装验证和隐私扫描。
- `docs/`：使用边界、版本记录与维护说明。
- `assets/`、`third_party/`：品牌资源与第三方许可。

字段表和帮助数据在构建时嵌入程序。维护方式见 [字段维护说明](schema-maintenance.md)。活动 profile、项目配置和托管策略可能覆盖本地文件，字段表不等同于运行会话的最终生效值。

## 打包与发布

先完成测试、审查并提交修改，再从干净工作区运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-package.ps1
```

打包包含 .NET 运行时，记录源提交与文件散列，并对实际发布程序运行隔离 UI 检查。安装验证使用独立目录，核对发布文件、旧入口清理、卸载保留用户文件以及真实用户环境未改变。发布流程和更新附件约定见 [GitHub 发布与更新](github-releases.md)。

个人 `auth.json`、`config.toml`、加密连接、备注、备份、测试运行目录、日志和构建缓存不得进入源码或发布包。打包脚本调用隐私扫描，在本机比较真实凭据与配置指纹，禁止打印其内容。仅上传明确的版本附件，不上传整个 artifacts 目录。

## 实现与兼容性

配置保存保留未编辑字段、注释、UTF-8 BOM 和原换行，采用外部修改检查、写锁、临时文件替换、加密备份与失败回滚。未设置的偏好不会自动写为默认值。复杂表数组保留原文，嵌套结构只作有限类型检查。

进程与主程序集使用 CodexDock；内部命名空间、用户数据目录、provider ID 和原有加密参数保留兼容。连接协议与验证边界见 [官方账号与独立模型连接](official-login-krill.md)。

历史界面设计与审查记录保留在 docs 中，用于维护参考，不代表额外承诺的产品能力。
