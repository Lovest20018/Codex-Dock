# GitHub 发布与更新

仓库：https://github.com/Lovest20018/Codex-Dock。

软件点击“检查更新”后读取 GitHub REST 的 `/releases/latest`。只接受 `v主版本.次版本.修订号` 的正式版本，不读取草稿或预发布；同版与旧版不提示升级。

每个正式 Release 必须上传：

- `CodexDock-X.Y.Z-Setup-win-x64.exe`
- `CodexDock-X.Y.Z-win-x64.zip`
- `CodexDock-X.Y.Z-source.zip`
- `SHA256SUMS-X.Y.Z.txt`

安装器与校验清单缺失时软件不会下载。下载仅允许 GitHub 及其 Release 资源域名；下载完成比对 SHA-256，并在 GitHub 提供 digest 时同时核对 digest。点击安装时重新计算散列，拒绝被替换的文件。

维护步骤：统一修改 csproj、侧栏、README、打包/验证脚本的版本号，填写 `docs/release-X.Y.Z.md`，运行核心测试及隐私扫描，提交代码，再运行 `scripts/package.ps1` 和 `scripts/verify-package.ps1`。打包 manifest 记录源提交和工作区状态，禁止从脏工作区制作正式发布包。核对安装、网关、ZIP 与源码包后创建 `vX.Y.Z` 标签；先创建草稿 Release，上传四个文件，逐一比对远端大小/散列，最后发布为最新正式版本。

隐私扫描会在本地比较当前真实凭据和配置指纹，值不会写入报告。个人配置、认证文件、加密连接、备份、构建缓存和测试运行目录均排除。上传只选择上述版本文件，不能上传 artifacts 整个目录。

更新检查依据 [GitHub 官方 Release API](https://docs.github.com/en/rest/releases/releases)。校验清单用于下载完整性检查，不能替代 GitHub 账户和仓库的维护权限管理。
