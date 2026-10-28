#requires -Version 7.2
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\artifacts\feature-official-login-krill\CodexConfig.exe'),
    [string]$UiOutput = (Join-Path $PSScriptRoot '..\.ui-test-mixed-final')
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskOutput = [IO.Path]::GetFullPath($UiOutput)
if (-not $taskOutput.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use an isolated UI fixture inside this workspace.' }
$taskHome = Get-ChildItem -LiteralPath $taskOutput -Directory -Filter 'isolated-home-*' | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'codex-config-connection.dat') } | Select-Object -First 1
if (-not $taskHome) { throw 'Run the mixed UI smoke test first.' }
$taskAuth = Get-Content -LiteralPath (Join-Path $taskHome.FullName 'auth.json') -Raw
if ($taskAuth -notlike '*fake-mixed-official-access*') { throw 'Only the artificial smoke-test account is allowed.' }
Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
$taskPlain = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $taskHome.FullName 'codex-config-connection.dat')), [Text.Encoding]::UTF8.GetBytes('CodexConfig.mixed.v1'), [Security.Cryptography.DataProtectionScope]::CurrentUser)
try { $taskConnection = [Text.Encoding]::UTF8.GetString($taskPlain) | ConvertFrom-Json } finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($taskPlain) }
$taskHttp = [Net.Http.HttpClient]::new([Net.Http.HttpClientHandler]@{ UseProxy = $false; AllowAutoRedirect = $false })
$taskHttp.Timeout = [TimeSpan]::FromSeconds(2)
$taskBase = 'http://127.0.0.1:' + $taskConnection.Port
$taskProcess = Start-Process -FilePath ([IO.Path]::GetFullPath($Exe)) -ArgumentList @('--model-bridge', ('"' + $taskHome.FullName + '"')) -WindowStyle Hidden -PassThru
try {
    $taskHttp.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $taskConnection.LocalToken)
    $taskReady = $false
    for ($taskAttempt = 0; $taskAttempt -lt 30; $taskAttempt++) {
        try {
            $taskResponse = $taskHttp.GetAsync($taskBase + '/codex-config/health').GetAwaiter().GetResult()
            try { if ($taskResponse.IsSuccessStatusCode) { $taskReady = $true; break } } finally { $taskResponse.Dispose() }
        } catch { if ($taskProcess.HasExited) { break } }
        Start-Sleep -Milliseconds 100
    }
    if (-not $taskReady) { throw 'The published bridge did not become ready.' }
    $taskResponse = $taskHttp.GetAsync($taskBase + '/backend-api/accounts/check').GetAwaiter().GetResult()
    try { if ([int]$taskResponse.StatusCode -ne 404) { throw 'Account endpoints must not enter the inference bridge.' } } finally { $taskResponse.Dispose() }
    $taskHttp.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', 'wrong-local-token')
    $taskResponse = $taskHttp.GetAsync($taskBase + '/codex-config/health').GetAwaiter().GetResult()
    try { if ([int]$taskResponse.StatusCode -ne 401) { throw 'The bridge accepted an unknown credential.' } } finally { $taskResponse.Dispose() }
    $taskHttp.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $taskConnection.LocalToken)
    $taskResponse = $taskHttp.PostAsync($taskBase + '/codex-config/stop', [Net.Http.StringContent]::new('')).GetAwaiter().GetResult()
    try { if ([int]$taskResponse.StatusCode -ne 202) { throw 'The bridge rejected its authenticated stop command.' } } finally { $taskResponse.Dispose() }
    if (-not $taskProcess.WaitForExit(10000) -or $taskProcess.ExitCode -ne 0) { throw 'The bridge did not exit cleanly.' }
    Write-Output 'PASS published bridge: loopback startup, authenticated health, account isolation, rejected unknown token, graceful stop.'
} finally {
    if (-not $taskProcess.HasExited) { $taskProcess.Kill($true); $taskProcess.WaitForExit() }
    $taskProcess.Dispose(); $taskHttp.Dispose()
}
