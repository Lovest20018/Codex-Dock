param(
    [Parameter(Mandatory=$true)][string]$InstallDir,
    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA 'CodexConfig\mobile')
)
$ErrorActionPreference = 'Stop'
# Runs before Inno Setup checks locked files. Only authenticated mobile workers
# whose executable is in this exact installation may receive a stop request.
# No process is killed and no saved settings or device sessions are rewritten.
try {
    $taskWorkerPath = [IO.Path]::GetFullPath((Join-Path $InstallDir 'mobile\CodexDock.Mobile.exe'))
    $taskTargets = @{}
    foreach ($taskProcess in @(Get-Process -Name 'CodexDock.Mobile' -ErrorAction SilentlyContinue)) {
        if ([string]::Equals($taskProcess.Path, $taskWorkerPath, [StringComparison]::OrdinalIgnoreCase)) {
            $null = $taskProcess.Handle
            if (-not $taskProcess.HasExited) { $taskTargets[$taskProcess.Id] = $taskProcess }
        } else { $taskProcess.Dispose() }
    }
    if ($taskTargets.Count -eq 0) { exit 0 }
    Add-Type -AssemblyName System.Security
    Add-Type -AssemblyName System.Net.Http
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class InstallerMobileListener {
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
    public static int Owner(int port) {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        for (int attempt = 0; attempt < 3 && size > 0 && size <= 4 * 1024 * 1024; attempt++) {
            IntPtr table = Marshal.AllocHGlobal(size);
            try {
                uint result = GetExtendedTcpTable(table, ref size, false, 2, 3, 0);
                if (result == 122) continue;
                if (result != 0) return 0;
                int count = Marshal.ReadInt32(table);
                if (count < 0 || count > (size - 4) / 24) return 0;
                for (int row = 0; row < count; row++) {
                    int offset = 4 + row * 24;
                    int rawPort = Marshal.ReadInt32(table, offset + 8);
                    if ((((rawPort & 255) << 8) | ((rawPort >> 8) & 255)) == port && Marshal.ReadInt32(table, offset + 4) == 0x0100007f)
                        return Marshal.ReadInt32(table, offset + 20);
                }
                return 0;
            } finally { Marshal.FreeHGlobal(table); }
        }
        return 0;
    }
}
'@
    $taskHandler = New-Object System.Net.Http.HttpClientHandler
    $taskHandler.UseProxy = $false; $taskHandler.AllowAutoRedirect = $false
    $taskClient = New-Object System.Net.Http.HttpClient($taskHandler)
    $taskClient.Timeout = [TimeSpan]::FromSeconds(3)
    $taskEntropy = [Text.Encoding]::UTF8.GetBytes('CodexDock.mobile.v1')
    foreach ($taskDirectory in @(Get-ChildItem -LiteralPath $StateRoot -Directory -ErrorAction SilentlyContinue)) {
        $taskSettingsPath = Join-Path $taskDirectory.FullName 'mobile-settings.dat'
        if (-not (Test-Path -LiteralPath $taskSettingsPath) -or (Get-Item -LiteralPath $taskSettingsPath).Length -gt 65536) { continue }
        $taskPlain = $null
        try {
            $taskPlain = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($taskSettingsPath), $taskEntropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
            $taskConfig = [Text.Encoding]::UTF8.GetString($taskPlain) | ConvertFrom-Json
            $taskPort = [int]$taskConfig.controlPort
            if ($taskPort -lt 1024 -or $taskPort -gt 65535 -or $taskConfig.controlToken -notmatch '^[a-fA-F0-9]{64}$' -or
                $taskConfig.instanceId -notmatch '^[a-fA-F0-9]{32}$' -or
                -not [string]::Equals([IO.Path]::GetFullPath($taskConfig.dataDir), $taskDirectory.FullName, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $taskOwner = [InstallerMobileListener]::Owner($taskPort)
            if (-not $taskTargets.ContainsKey($taskOwner)) { continue }
            $taskProcess = $taskTargets[$taskOwner]
            if ($taskProcess.HasExited) { continue }
            $taskRequest = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, "http://127.0.0.1:$taskPort/health")
            $taskRequest.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $taskConfig.controlToken)
            try {
                $taskResponse = $taskClient.SendAsync($taskRequest).GetAwaiter().GetResult()
                try {
                    $taskResponse.EnsureSuccessStatusCode() | Out-Null
                    $taskHealth = $taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                } finally { $taskResponse.Dispose() }
            } finally { $taskRequest.Dispose() }
            if ($taskHealth.service -ne 'codex-dock-mobile' -or $taskHealth.instanceId -cne $taskConfig.instanceId -or
                $taskHealth.publicUrl -cne $taskConfig.publicUrl -or $taskHealth.port -ne $taskConfig.port -or
                $taskProcess.HasExited -or [InstallerMobileListener]::Owner($taskPort) -ne $taskOwner) { continue }
            $taskRequest = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "http://127.0.0.1:$taskPort/stop")
            $taskRequest.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $taskConfig.controlToken)
            $taskRequest.Content = New-Object System.Net.Http.StringContent('{}', [Text.Encoding]::UTF8, 'application/json')
            try {
                $taskResponse = $taskClient.SendAsync($taskRequest).GetAwaiter().GetResult()
                try { $taskResponse.EnsureSuccessStatusCode() | Out-Null } finally { $taskResponse.Dispose() }
            } finally { $taskRequest.Dispose() }
            if (-not $taskProcess.WaitForExit(15000)) { throw 'Mobile worker has not fully exited.' }
        } catch {
            # A corrupt or obsolete state directory must not expose settings in
            # installer logs. The final process check below blocks unsafe copies.
        } finally {
            if ($null -ne $taskPlain) { [Array]::Clear($taskPlain, 0, $taskPlain.Length) }
        }
    }
    foreach ($taskProcess in $taskTargets.Values) {
        if (-not $taskProcess.HasExited -and -not $taskProcess.WaitForExit(1000)) { exit 1 }
    }
    exit 0
} catch { exit 1 } finally {
    if ($null -ne $taskClient) { $taskClient.Dispose() }
    if ($null -ne $taskTargets) { foreach ($taskProcess in $taskTargets.Values) { $taskProcess.Dispose() } }
}
