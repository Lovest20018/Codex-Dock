#requires -Version 7.2
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\artifacts\CodexDock-0.10.0-win-x64\CodexDock.exe'),
    [string]$UiOutput = (Join-Path $PSScriptRoot '..\.ui-test-v0.10')
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
    if ($taskProcess.ProcessName -ne 'CodexDock') { throw 'Gateway process must use the CodexDock name.' }
    $taskHttp.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $taskConnection.LocalToken)
    $taskReady = $false
    for ($taskAttempt = 0; $taskAttempt -lt 30; $taskAttempt++) {
        try {
            $taskResponse = $taskHttp.GetAsync($taskBase + '/codex-config/health').GetAwaiter().GetResult()
            try {
                if ($taskResponse.IsSuccessStatusCode) {
                    $taskHealth = $taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                    if ($taskHealth.ProtocolVersion -ne 2) { throw 'Expected a gateway that supports explicit proxy routing.' }
                    $taskReady = $true; break
                }
            } finally { $taskResponse.Dispose() }
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

# A private short-lived helper stands in for the editor; no real process is stopped.
$taskOwner = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 40') -WindowStyle Hidden -PassThru
$taskOwnedBridge = $null
$taskOwnerHttp = [Net.Http.HttpClient]::new([Net.Http.HttpClientHandler]@{UseProxy=$false;AllowAutoRedirect=$false})
$taskOwnerHttp.Timeout = [TimeSpan]::FromSeconds(1)
$taskOwnerHttp.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $taskConnection.LocalToken)
try {
    $taskStarted = $taskOwner.StartTime.ToUniversalTime().Ticks.ToString([Globalization.CultureInfo]::InvariantCulture)
    $taskOwnedBridge = Start-Process -FilePath ([IO.Path]::GetFullPath($Exe)) -ArgumentList @('--model-bridge', ('"' + $taskHome.FullName + '"'), '--owner-process', $taskOwner.Id.ToString(), $taskStarted) -WindowStyle Hidden -PassThru
    $taskReady = $false
    for ($taskAttempt = 0; $taskAttempt -lt 40; $taskAttempt++) {
        try { $taskHealth = $taskOwnerHttp.GetStringAsync($taskBase + '/codex-config/health').GetAwaiter().GetResult(); $taskReady = $true; break }
        catch { if ($taskOwnedBridge.HasExited) { break } }
        Start-Sleep -Milliseconds 100
    }
    if (-not $taskReady) { throw 'Owned gateway did not start.' }
    $taskOwner.Kill(); $taskOwner.WaitForExit()
    if (-not $taskOwnedBridge.WaitForExit(10000) -or $taskOwnedBridge.ExitCode -ne 0) { throw 'Gateway survived its editor process.' }
    $taskStillListening = $false
    try { $null = $taskOwnerHttp.GetStringAsync($taskBase + '/codex-config/health').GetAwaiter().GetResult(); $taskStillListening = $true } catch { }
    if ($taskStillListening) { throw 'Gateway did not release its port.' }
    Write-Output 'PASS gateway lifetime: editor process exit stops owned gateway and releases the loopback port.'
} finally {
    if ($taskOwnedBridge) { if (-not $taskOwnedBridge.HasExited) { $taskOwnedBridge.Kill($true); $taskOwnedBridge.WaitForExit() }; $taskOwnedBridge.Dispose() }
    if (-not $taskOwner.HasExited) { $taskOwner.Kill(); $taskOwner.WaitForExit() }
    $taskOwner.Dispose(); $taskOwnerHttp.Dispose()
}
