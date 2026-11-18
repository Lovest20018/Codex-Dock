param(
    [Parameter(Mandatory=$true)][string]$InstallDir,
    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA 'CodexConfig\mobile')
)
$ErrorActionPreference = 'Stop'

function Get-InstallationProcesses([string[]]$Names) {
    foreach ($name in $Names) {
        $executable = [IO.Path]::GetFullPath((Join-Path $InstallDir $name))
        foreach ($process in @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($name)) -ErrorAction SilentlyContinue)) {
            $keep = $false
            try {
                if ($process.SessionId -eq $taskSession -and [string]::Equals($process.Path, $executable, [StringComparison]::OrdinalIgnoreCase)) {
                    $keep = -not $process.HasExited -and [InstallerProcesses]::IsCurrentUser($process.Handle)
                    if ($keep) { $process }
                }
            } finally { if (-not $keep) { $process.Dispose() } }
        }
    }
}

function Read-WorkerSettings([string]$Path, [string]$Entropy) {
    $file = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $file -or $file.PSIsContainer -or $file.Length -gt 65536) { return }
    $plain = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($file.FullName),
        [Text.Encoding]::UTF8.GetBytes($Entropy), [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { [Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json }
    finally { [Array]::Clear($plain, 0, $plain.Length) }
}

function Test-WorkerOwner([Diagnostics.Process]$Process, [int]$Port, [string]$Token) {
    return $Port -ge 1024 -and $Port -le 65535 -and $Token -match '^[a-fA-F0-9]{64}$' -and
        -not $Process.HasExited -and [InstallerProcesses]::ListenerOwner($Port) -eq $Process.Id
}

function Invoke-WorkerControl([int]$Port, [string]$Token, [string]$Path, [switch]$Stop) {
    $method = if ($Stop) { [Net.Http.HttpMethod]::Post } else { [Net.Http.HttpMethod]::Get }
    $request = New-Object Net.Http.HttpRequestMessage($method, "http://127.0.0.1:${Port}/$Path")
    $request.Headers.Authorization = New-Object Net.Http.Headers.AuthenticationHeaderValue('Bearer', $Token)
    if ($Stop) { $request.Content = New-Object Net.Http.StringContent('{}', [Text.Encoding]::UTF8, 'application/json') }
    try {
        $response = $taskClient.SendAsync($request).GetAwaiter().GetResult()
        try {
            $null = $response.EnsureSuccessStatusCode()
            if (-not $Stop) { $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}

function Stop-ModelWorker([Diagnostics.Process]$Process, [string]$ModelHome) {
    if ($Process.HasExited) { return $true }
    # Use the exact launch directory, never the current user's default Codex home.
    if ($ModelHome -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\/])') { return $false }
    $config = Read-WorkerSettings (Join-Path $ModelHome 'codex-config-connection.dat') 'CodexConfig.mixed.v1'
    if ($null -eq $config -or -not (Test-WorkerOwner $Process $config.Port $config.LocalToken)) { return $false }
    $health = Invoke-WorkerControl $config.Port $config.LocalToken 'codex-config/health'
    if ($health.ProtocolVersion -lt 1 -or -not (Test-WorkerOwner $Process $config.Port $config.LocalToken)) { return $false }
    Invoke-WorkerControl $config.Port $config.LocalToken 'codex-config/stop' -Stop
    return $Process.WaitForExit(15000)
}

function Stop-MobileWorkers($Targets) {
    foreach ($directory in @(Get-ChildItem -LiteralPath $StateRoot -Directory -ErrorAction SilentlyContinue)) {
        try {
            $config = Read-WorkerSettings (Join-Path $directory.FullName 'mobile-settings.dat') 'CodexDock.mobile.v1'
            if ($null -eq $config -or $config.controlPort -lt 1024 -or $config.controlPort -gt 65535 -or
                $config.instanceId -notmatch '^[a-fA-F0-9]{32}$' -or
                -not [string]::Equals([IO.Path]::GetFullPath($config.dataDir), $directory.FullName, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $owner = [InstallerProcesses]::ListenerOwner($config.controlPort)
            $process = $Targets[$owner]
            if ($null -eq $process -or -not (Test-WorkerOwner $process $config.controlPort $config.controlToken)) { continue }
            $health = Invoke-WorkerControl $config.controlPort $config.controlToken 'health'
            if ($health.service -ne 'codex-dock-mobile' -or $health.instanceId -cne $config.instanceId -or
                $health.publicUrl -cne $config.publicUrl -or $health.port -ne $config.port -or
                -not (Test-WorkerOwner $process $config.controlPort $config.controlToken)) { continue }
            Invoke-WorkerControl $config.controlPort $config.controlToken 'stop' -Stop
            if (-not $process.WaitForExit(15000)) { return $false }
        } catch {
            # A corrupt or stale setting is not authority to stop a worker.
        }
    }
    foreach ($process in $Targets.Values) {
        if (-not $process.HasExited -and -not $process.WaitForExit(1000)) { return $false }
    }
    return $true
}

$taskDesktopNames = @('CodexDock.exe', 'CodexConfig.exe')
$taskMobileName = 'mobile\CodexDock.Mobile.exe'
$taskSession = [Diagnostics.Process]::GetCurrentProcess().SessionId
$taskApps = @()
$taskMobile = @()
$taskRemaining = @()
$taskClient = $null
$taskFailure = 13
try {
    # Most clean installs do not need the native helper at all.
    $taskPotential = @(Get-Process -Name 'CodexDock','CodexConfig','CodexDock.Mobile' -ErrorAction SilentlyContinue)
    try {
        $taskPaths = @($taskDesktopNames + $taskMobileName | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $InstallDir $_)) })
        if (-not ($taskPotential | Where-Object { $_.SessionId -eq $taskSession -and $taskPaths -contains $_.Path })) { exit 0 }
    } finally { foreach ($process in $taskPotential) { $process.Dispose() } }
    Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase
    $taskReferences = @('UIAutomationClient','UIAutomationTypes','WindowsBase') | ForEach-Object { ([Reflection.Assembly]::LoadWithPartialName($_)).Location }
    Add-Type -Path (Join-Path $PSScriptRoot 'installer-processes.cs') -ReferencedAssemblies (@('System.dll') + $taskReferences)
    $taskApps = @(Get-InstallationProcesses $taskDesktopNames)
    $taskModels = @{}
    $taskDesktops = @()
    foreach ($process in $taskApps) {
        $modelHome = [InstallerProcesses]::ModelHome($process.Handle)
        if ($modelHome) { $taskModels[$process.Id] = $modelHome }
        else { $taskDesktops += $process }
    }
    $taskFailure = 10
    foreach ($process in $taskDesktops) {
        if (-not [InstallerProcesses]::RequestDesktopExit($process)) { exit $taskFailure }
    }
    $taskExitTimer = [Diagnostics.Stopwatch]::StartNew()
    foreach ($process in $taskDesktops) {
        $remaining = [Math]::Max(0, 45000 - [int]$taskExitTimer.ElapsedMilliseconds)
        if (-not $process.WaitForExit($remaining)) { exit $taskFailure }
    }

    $taskMobile = @(Get-InstallationProcesses $taskMobileName)
    if ($taskModels.Count -gt 0 -or $taskMobile.Count -gt 0) {
        Add-Type -AssemblyName System.Security,System.Net.Http
        $handler = New-Object Net.Http.HttpClientHandler
        $handler.UseProxy = $false
        $handler.AllowAutoRedirect = $false
        $taskClient = New-Object Net.Http.HttpClient($handler)
        $taskClient.Timeout = [TimeSpan]::FromSeconds(3)
        $taskFailure = 11
        foreach ($process in $taskApps) {
            if ($taskModels.ContainsKey($process.Id) -and -not (Stop-ModelWorker $process $taskModels[$process.Id])) { exit $taskFailure }
        }
        $taskFailure = 12
        $targets = @{}
        foreach ($process in $taskMobile) { $targets[$process.Id] = $process }
        if ($targets.Count -gt 0 -and -not (Stop-MobileWorkers $targets)) { exit $taskFailure }
    }
    $taskFailure = 13
    $taskRemaining = @(Get-InstallationProcesses ($taskDesktopNames + $taskMobileName))
    if ($taskRemaining.Count -gt 0) { exit $taskFailure }
    exit 0
} catch {
    # Installation logs can retain stage codes without exposing connection settings.
    exit $taskFailure
} finally {
    if ($null -ne $taskClient) { $taskClient.Dispose() }
    foreach ($process in @($taskApps + $taskMobile + $taskRemaining)) { $process.Dispose() }
}
