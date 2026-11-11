param([string]$CodexCli)
$ErrorActionPreference = 'Stop'
$verificationRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$verificationOutput = Join-Path $verificationRoot ('artifacts\superba-2026-11-11\verification-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $verificationOutput -Force | Out-Null
$verificationOldCli = $env:CODEX_CONFIG_TEST_CLI
$verificationOldFilter = $env:CODEX_CONFIG_TEST_FILTER
$verificationOldUiFilter = $env:CODEX_CONFIG_UI_TEST_FILTER
Push-Location $verificationRoot
try {
    if ($CodexCli) {
        $CodexCli = [IO.Path]::GetFullPath($CodexCli)
        if (-not (Test-Path -LiteralPath $CodexCli -PathType Leaf) -or [IO.Path]::GetFileName($CodexCli) -ne 'codex.exe') { throw 'CodexCli must identify a native codex.exe.' }
        $env:CODEX_CONFIG_TEST_CLI = $CodexCli
    }
    $env:CODEX_CONFIG_TEST_FILTER = $null
    $env:CODEX_CONFIG_UI_TEST_FILTER = $null
    & dotnet build src/CodexConfig.App/CodexConfig.App.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
    & dotnet build tests/CodexConfig.Tests/CodexConfig.Tests.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & .\tests\CodexConfig.Tests\bin\Release\net8.0-windows\CodexConfig.Tests.exe (Join-Path $verificationOutput 'core') *> (Join-Path $verificationOutput 'core.log')
    $verificationCoreExit = $LASTEXITCODE
    Get-Content -LiteralPath (Join-Path $verificationOutput 'core.log') -Tail 15
    $verificationUiOutput = Join-Path $verificationOutput 'ui'
    $verificationUi = Start-Process -FilePath '.\src\CodexConfig.App\bin\Release\net8.0-windows\CodexDock.exe' -ArgumentList @('--smoke-test', ('"' + $verificationUiOutput + '"')) -WindowStyle Hidden -PassThru -Wait
    if (Test-Path -LiteralPath (Join-Path $verificationUiOutput 'failure.txt')) { Get-Content -LiteralPath (Join-Path $verificationUiOutput 'failure.txt') }
    $verificationSummary = [ordered]@{
        coreExit = $verificationCoreExit
        uiExit = $verificationUi.ExitCode
        nativeCliChecks = [bool]$env:CODEX_CONFIG_TEST_CLI
        coreLog = (Join-Path $verificationOutput 'core.log')
        uiReport = (Join-Path $verificationUiOutput 'report.json')
        uiFailure = (Join-Path $verificationUiOutput 'failure.txt')
        output = $verificationOutput
    }
    $verificationSummary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $verificationOutput 'result.json') -Encoding utf8
    Write-Output ('Verification evidence: ' + $verificationOutput)
    if ($verificationCoreExit -ne 0 -or $verificationUi.ExitCode -ne 0) { throw 'Verification did not pass; see result.json and the recorded logs.' }
} finally {
    $env:CODEX_CONFIG_TEST_CLI = $verificationOldCli
    $env:CODEX_CONFIG_TEST_FILTER = $verificationOldFilter
    $env:CODEX_CONFIG_UI_TEST_FILTER = $verificationOldUiFilter
    Pop-Location
}
