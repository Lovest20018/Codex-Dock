#requires -Version 7.2
param([string]$OutputDirectory, [switch]$Verify, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskArtifacts = Join-Path $taskRoot 'artifacts'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskArtifacts ('winui-preview-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $taskOutput.StartsWith($taskArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Preview output must be a subdirectory of this repository artifacts directory.'
}
$taskDelivery = Join-Path $taskOutput 'app'
$taskProject = Join-Path $taskRoot 'src\CodexConfig.WinUI\CodexConfig.WinUI.csproj'
$taskFormalVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'src\CodexConfig.App\CodexConfig.App.csproj') -Raw)).Project.PropertyGroup.Version
$taskPreviewVersion = ([xml](Get-Content -LiteralPath $taskProject -Raw)).Project.PropertyGroup.Version
if ($taskPreviewVersion -ne $taskFormalVersion) { throw 'WinUI update comparison version must match the formal Windows source version.' }
if (-not (Test-Path -LiteralPath (Join-Path $taskRoot 'artifacts\mobile-worker\CodexDock.Mobile\CodexDock.Mobile.exe'))) {
    throw 'The preview requires the bundled mobile worker. Run scripts/build-mobile.ps1 before building the complete preview.'
}
$taskOldCliHome = $env:DOTNET_CLI_HOME
$taskOldPackages = $env:NUGET_PACKAGES
try {
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot '.dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $taskRoot '.packages'
    if (-not $NoRestore) {
        $taskRestore = @()
        $taskFallback = Join-Path $env:USERPROFILE '.nuget\packages'
        if (Test-Path -LiteralPath $taskFallback) { $taskRestore += "-p:RestoreFallbackFolders=$taskFallback" }
        & dotnet restore $taskProject @taskRestore
        if ($LASTEXITCODE -ne 0) { throw 'WinUI preview restore failed.' }
    }
    & dotnet build $taskProject -c Release --no-restore -p:UiPreviewBuild=true -o $taskDelivery
    if ($LASTEXITCODE -ne 0) { throw 'WinUI preview build failed.' }
    if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $taskDelivery 'CodexDock.WinUI.dll')).Version.ToString(3) -ne $taskFormalVersion) {
        throw 'Delivered WinUI version does not match the Windows update baseline.'
    }
    $taskNotices = Join-Path $taskDelivery 'third_party'
    New-Item -ItemType Directory -Path $taskNotices -Force | Out-Null
    foreach ($taskName in @('WindowsAppSDK-LICENSE.txt','WindowsAppSDK-NOTICE.txt','WebView2-SDK-LICENSE.txt','Tomlyn-LICENSE.txt','Superba-LICENSE.txt','dotnet-LICENSE.txt','dotnet-THIRD-PARTY-NOTICES.txt')) {
        Copy-Item -LiteralPath (Join-Path $taskRoot "third_party\$taskName") -Destination $taskNotices -Force
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE'),(Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $taskDelivery -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\winui-preview.md') -Destination (Join-Path $taskDelivery 'README.md') -Force
    $taskExe = Join-Path $taskDelivery 'CodexDock.WinUI.exe'
    if ($Verify) {
        $taskPreviewProcess = Start-Process -FilePath $taskExe -WindowStyle Hidden -PassThru
        try {
            $taskWindowDeadline = [DateTime]::UtcNow.AddSeconds(20)
            do {
                if ($taskPreviewProcess.WaitForExit(100)) { throw 'The argument-free preview process exited before opening its own window.' }
                $taskPreviewProcess.Refresh()
                if ($taskPreviewProcess.MainWindowTitle -eq 'Codex Dock · 界面预览') { break }
            } while ([DateTime]::UtcNow -lt $taskWindowDeadline)
            if ($taskPreviewProcess.MainWindowTitle -ne 'Codex Dock · 界面预览') { throw 'The argument-free preview did not open its isolated window.' }
            Write-Output 'PASS WinUI preview opens its own titled window without arguments.'
        } finally {
            if (-not $taskPreviewProcess.HasExited -and (-not $taskPreviewProcess.CloseMainWindow() -or -not $taskPreviewProcess.WaitForExit(5000))) {
                $taskPreviewProcess.Kill()
                $taskPreviewProcess.WaitForExit()
            }
            $taskPreviewProcess.Dispose()
        }
        $taskUi = Join-Path $taskOutput ('ui-' + [Guid]::NewGuid().ToString('N'))
        $taskOldTestFilter = $env:CODEX_DOCK_WINUI_TEST_FILTER
        try {
            Remove-Item Env:CODEX_DOCK_WINUI_TEST_FILTER -ErrorAction SilentlyContinue
            $taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--smoke-test', ('"' + $taskUi + '"')) -WindowStyle Hidden -PassThru
        } finally { $env:CODEX_DOCK_WINUI_TEST_FILTER = $taskOldTestFilter }
        $taskDeadline = [DateTime]::UtcNow.AddMinutes(8)
        while (-not $taskProcess.WaitForExit(10000)) {
            if ([DateTime]::UtcNow -gt $taskDeadline) {
                $taskProcess.Kill($true); $taskProcess.WaitForExit(); throw 'WinUI preview smoke test timed out.'
            }
        }
        $taskReportPath = Join-Path $taskUi 'report.json'
        if ($taskProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $taskReportPath)) { throw "WinUI preview smoke failed; see $taskUi" }
        $taskReport = Get-Content -LiteralPath $taskReportPath -Raw | ConvertFrom-Json
        $taskHash = (Get-FileHash -LiteralPath (Join-Path $taskDelivery 'CodexDock.WinUI.dll') -Algorithm SHA256).Hash
        if ($taskReport.status -ne 'PASS' -or $taskReport.assemblySha256 -ne $taskHash) { throw 'WinUI report does not match the delivered application.' }
        Write-Output "PASS WinUI preview: $($taskReport.tests.Count) checks. Report: $taskReportPath"
    }
    Write-Output "Preview executable: $taskExe"
} finally {
    $env:DOTNET_CLI_HOME = $taskOldCliHome
    $env:NUGET_PACKAGES = $taskOldPackages
}
