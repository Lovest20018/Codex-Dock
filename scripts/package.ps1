param(
    [string]$Version = '0.12.0',
    [switch]$SkipPublish,
    [string]$InnoCompiler = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskArtifacts = Join-Path $taskRoot 'artifacts'
$taskCommit = (git -C $taskRoot rev-parse HEAD).Trim()
if (git -C $taskRoot status --porcelain) { throw 'Commit reviewed changes before producing a release package.' }
$taskRelease = Join-Path $taskArtifacts "CodexDock-$Version-win-x64"
if (-not ([IO.Path]::GetFullPath($taskRelease).StartsWith($taskArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) { throw 'Release path escapes artifacts.' }
if (-not $SkipPublish) {
    if (Test-Path -LiteralPath $taskRelease) { Remove-Item -LiteralPath $taskRelease -Recurse -Force }
    New-Item -ItemType Directory -Path $taskRelease -Force | Out-Null
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot '.dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $taskRoot '.packages'
    $taskFallback = Join-Path $env:USERPROFILE '.nuget\packages'
    $taskRestoreArgs = @()
    if (Test-Path -LiteralPath $taskFallback) { $taskRestoreArgs += "-p:RestoreFallbackFolders=$taskFallback" }
    & dotnet publish (Join-Path $taskRoot 'src\CodexConfig.App\CodexConfig.App.csproj') -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:DebugType=None @taskRestoreArgs -o $taskRelease
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
$taskExe = Join-Path $taskRelease 'CodexDock.exe'
if (-not (Test-Path -LiteralPath $taskExe)) { throw 'Missing published executable.' }
if (-not (Test-Path -LiteralPath $InnoCompiler)) { throw 'Inno Setup 6 compiler not found.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md'),(Join-Path $taskRoot 'LICENSE'),(Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $taskRelease -Force
foreach ($taskFolder in @('docs','third_party','brand')) { New-Item -ItemType Directory -Path (Join-Path $taskRelease $taskFolder) -Force | Out-Null }
Copy-Item -LiteralPath (Join-Path $taskRoot "docs\release-$Version.md"),(Join-Path $taskRoot 'docs\schema-maintenance.md'),(Join-Path $taskRoot 'docs\codex-dock-delivery.md'),(Join-Path $taskRoot 'docs\ui-layout-review.md'),(Join-Path $taskRoot 'docs\process-rename-review.md'),(Join-Path $taskRoot 'docs\github-releases.md'),(Join-Path $taskRoot "docs\review-$Version.md") -Destination (Join-Path $taskRelease 'docs') -Force
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'third_party') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskRelease 'third_party') -Force }
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\magpie-codex-comparison.md') -Destination (Join-Path $taskRelease 'docs') -Force
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'assets\brand') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskRelease 'brand') -Force }
Copy-Item -LiteralPath (Join-Path $taskRoot 'src\CodexConfig.App\Assets\app.ico') -Destination (Join-Path $taskRelease 'brand') -Force
$taskUiOutput = Join-Path $taskRoot ('.ui-test-v' + ($Version -replace '\.0$',''))
$taskRun = Start-Process -FilePath $taskExe -ArgumentList @('--smoke-test',$taskUiOutput) -WindowStyle Hidden -PassThru -Wait
if ($taskRun.ExitCode -ne 0) { throw "Published EXE UI check failed. See $taskUiOutput\failure.txt" }
$taskUiReport = Join-Path $taskUiOutput 'report.json'
if (-not (Test-Path -LiteralPath $taskUiReport) -or (Get-Content -LiteralPath $taskUiReport -Raw | ConvertFrom-Json).status -ne 'PASS') { throw 'The published EXE must pass its isolated UI check before packaging.' }
$taskExeHash = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant()
$taskAssemblyHash = (Get-FileHash -LiteralPath (Join-Path $taskRelease 'CodexDock.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
$taskReport = Get-Content -LiteralPath $taskUiReport -Raw | ConvertFrom-Json
if ($taskReport.executableName -ne 'CodexDock.exe' -or $taskReport.executableSha256 -ne $taskExeHash -or $taskReport.applicationAssemblySha256 -ne $taskAssemblyHash) { throw 'The UI report must match this published executable and application assembly.' }
New-Item -ItemType Directory -Path (Join-Path $taskRelease 'verification') -Force | Out-Null
Copy-Item -LiteralPath $taskUiReport -Destination (Join-Path $taskRelease 'verification\ui-report.json') -Force

@(
    "Codex Dock $Version / Windows x64"
    ''
    '运行 Setup EXE 安装；也可解压便携包并打开 CodexDock.exe。无需安装 .NET。'
    '常用设置、服务连接、配置模板、备份恢复在同一窗口。保存配置后重新打开 Codex。'
    'Ctrl+S 保存；Ctrl+F 搜索；Esc 关闭面板。'
    '模型目录获取为可选操作，不会改变所选认证方式。'
    '模板支持新增、编辑、复制和删除；应用所选设置后，再保存配置。'
    '设置默认显示一句用途，点击“了解更多”查看说明。'
    '保存前自动加密备份，可在备份与恢复页恢复上次配置。'
    '完整说明见 README.md。'
) | Set-Content -LiteralPath (Join-Path $taskRelease '使用说明.txt') -Encoding utf8
$taskExeHash = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant()
@{
    name='CodexDock'; version=$Version; platform='win-x64'; selfContained=$true
    createdAtUtc=[DateTimeOffset]::UtcNow.ToString('o'); sourceCommit=$taskCommit; executableSha256=$taskExeHash; applicationAssemblySha256=$taskAssemblyHash
    installer='Inno Setup, per-user'; settings='Shared draft and one save action'
    configSchema=(Get-Content -LiteralPath (Join-Path $taskRoot 'config\config-schema.json') -Raw -Encoding UTF8 | ConvertFrom-Json).schema_version
    configSchemaSha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot 'config\config-schema.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    configHelpSha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot 'config\config-help.json') -Algorithm SHA256).Hash.ToLowerInvariant()
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $taskRelease 'manifest.json') -Encoding utf8
& $InnoCompiler "/DAppVersion=$Version" "/DPayloadDir=$taskRelease" (Join-Path $PSScriptRoot 'installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compile failed.' }
$taskSetup = Join-Path $taskArtifacts "CodexDock-$Version-Setup-win-x64.exe"
$taskZip = Join-Path $taskArtifacts "CodexDock-$Version-win-x64.zip"
Compress-Archive -LiteralPath $taskRelease -DestinationPath $taskZip -Force

Add-Type -AssemblyName System.IO.Compression
$taskSourceZip = Join-Path $taskArtifacts "CodexDock-$Version-source.zip"
$taskSourceFiles = @()
foreach ($taskFolder in @('src','config','tests','scripts','docs','third_party','assets')) {
    $taskSourceFiles += Get-ChildItem -LiteralPath (Join-Path $taskRoot $taskFolder) -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
}
foreach ($taskName in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','Directory.Build.props','.gitignore')) { $taskSourceFiles += Get-Item -LiteralPath (Join-Path $taskRoot $taskName) }
$taskStream = [IO.File]::Open($taskSourceZip, [IO.FileMode]::Create)
try {
    $taskArchive = [IO.Compression.ZipArchive]::new($taskStream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($taskFile in $taskSourceFiles) {
            $taskName = $taskFile.FullName.Substring($taskRoot.Length + 1).Replace('\','/')
            $taskEntry = $taskArchive.CreateEntry($taskName, [IO.Compression.CompressionLevel]::Optimal)
            $taskInput = [IO.File]::OpenRead($taskFile.FullName); $taskOutput = $taskEntry.Open()
            try { $taskInput.CopyTo($taskOutput) } finally { $taskOutput.Dispose(); $taskInput.Dispose() }
        }
    } finally { $taskArchive.Dispose() }
} finally { $taskStream.Dispose() }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'verify-release-privacy.ps1') -PayloadDir $taskRelease -SourceArchive $taskSourceZip -History
if ($LASTEXITCODE -ne 0) { throw 'Release privacy scan failed.' }
$taskChecksumLines = foreach ($taskFile in @($taskSetup,$taskZip,$taskSourceZip,$taskExe)) {
    $taskHash = (Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash.ToLowerInvariant()
    '{0}  {1}' -f $taskHash, ($taskFile.Substring($taskArtifacts.Length + 1).Replace('\','/'))
}
$taskChecksumLines | Set-Content -LiteralPath (Join-Path $taskArtifacts "SHA256SUMS-$Version.txt") -Encoding ascii
Get-Item -LiteralPath $taskSetup,$taskZip,$taskSourceZip,$taskExe | Select-Object FullName,Length
