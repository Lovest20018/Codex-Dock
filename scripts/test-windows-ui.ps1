[CmdletBinding()]
param([string]$OutputDirectory, [switch]$WinUi, [switch]$Wpf, [switch]$NoRestore)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $taskRoot ('artifacts\test-runs\windows-ui-' + [Guid]::NewGuid().ToString('N'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.dotnet-home'
[string[]]$taskRestoreOptions = @()
if ($NoRestore) { $taskRestoreOptions += '--no-restore' }
$taskUserPackages = Join-Path $env:USERPROFILE '.nuget\packages'
if (-not $NoRestore -and (Test-Path -LiteralPath $taskUserPackages)) {
    $taskRestoreOptions += "-p:RestoreFallbackFolders=$taskUserPackages"
}

function Invoke-Suite([string]$Name, [string]$Project, [string]$Application, [string]$Filter, [switch]$SelfContained) {
    $build = Join-Path $OutputDirectory "$Name-build"
    $output = Join-Path $OutputDirectory $Name
    $projectPath = Join-Path $taskRoot $Project
    if ($SelfContained) {
        & dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $build @taskRestoreOptions `
            -p:WinUiSmokeBuild=true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None
    } else {
        & dotnet build $projectPath -c Release -o $build @taskRestoreOptions
    }
    if ($LASTEXITCODE -ne 0) { throw "$Name UI test build failed." }
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $build $Application))
    $start.UseShellExecute = $false
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.Environment.Remove($Filter) | Out-Null
    $start.ArgumentList.Add('--smoke-test')
    $start.ArgumentList.Add($output)
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(300000)) { $process.Kill(); throw "$Name UI test timed out. See $output" }
        if ($process.ExitCode -ne 0) {
            $status = '0x{0:X8}' -f $process.ExitCode
            $failure = Join-Path $output 'failure.txt'
            if (Test-Path -LiteralPath $failure) { throw "$Name UI test exited with $status. See $failure" }
            throw "$Name UI test exited with $status before writing a failure report."
        }
        $report = Get-Content -LiteralPath (Join-Path $output 'report.json') -Raw | ConvertFrom-Json
        if ($report.status -ne 'PASS' -or -not $report.tests.Count) { throw "$Name UI test did not produce a passing report." }
        Write-Output "$Name UI checks passed ($($report.tests.Count)). See $output\report.json"
    } finally { $process.Dispose() }
}

$taskFailures = @()
if ($WinUi -or -not $Wpf) {
    try { Invoke-Suite 'winui' 'src\CodexConfig.WinUI\CodexConfig.WinUI.csproj' 'CodexDock.WinUI.exe' 'CODEX_DOCK_WINUI_TEST_FILTER' -SelfContained }
    catch { $taskFailures += $_.Exception.Message }
}
if ($Wpf -or -not $WinUi) {
    try { Invoke-Suite 'wpf' 'src\CodexConfig.App\CodexConfig.App.csproj' 'CodexDock.exe' 'CODEX_CONFIG_UI_TEST_FILTER' }
    catch { $taskFailures += $_.Exception.Message }
}
if ($taskFailures.Count -gt 0) { throw ($taskFailures -join [Environment]::NewLine) }
