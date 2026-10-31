#requires -Version 7.2
param([string]$PayloadDir, [string]$SourceArchive, [switch]$History)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskSecrets = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$taskPrivateHashes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$taskHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
function Read-PrivateValues($Object) {
    if ($null -eq $Object) { return }
    foreach ($taskProperty in $Object.PSObject.Properties) {
        if ($taskProperty.Name -match '^(personal_access_token|OPENAI_API_KEY|access_token|refresh_token|id_token|ApiKey|LocalToken|ControlToken)$' -and $taskProperty.Value -is [string] -and $taskProperty.Value.Length -ge 12) { $null = $taskSecrets.Add($taskProperty.Value) }
        elseif ($taskProperty.Value -is [pscustomobject]) { Read-PrivateValues $taskProperty.Value }
    }
}
foreach ($taskName in @('config.toml','auth.json','codex-config-connection.dat','codex-dock-details.dat')) {
    $taskPath = Join-Path $taskHome $taskName
    if (-not (Test-Path -LiteralPath $taskPath)) { continue }
    $taskBytes = [IO.File]::ReadAllBytes($taskPath)
    if ($taskBytes.Length) { $null = $taskPrivateHashes.Add([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskBytes))) }
    if ($taskName -eq 'auth.json') { Read-PrivateValues ([Text.Encoding]::UTF8.GetString($taskBytes) | ConvertFrom-Json) }
    if ($taskName -eq 'config.toml') {
        foreach ($taskMatch in [regex]::Matches([Text.Encoding]::UTF8.GetString($taskBytes), '(?m)(?:key|token|authorization)[^=\r\n]*=\s*["'']([^"''\r\n]{12,})["'']')) { $null = $taskSecrets.Add($taskMatch.Groups[1].Value) }
    }
    if ($taskName -eq 'codex-config-connection.dat') {
        Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
        $taskPlain = [Security.Cryptography.ProtectedData]::Unprotect($taskBytes, [Text.Encoding]::UTF8.GetBytes('CodexConfig.mixed.v1'), [Security.Cryptography.DataProtectionScope]::CurrentUser)
        try { Read-PrivateValues ([Text.Encoding]::UTF8.GetString($taskPlain) | ConvertFrom-Json) }
        finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($taskPlain) }
    }
}
function Check-Bytes([string]$Name, [byte[]]$Bytes) {
    if ($Name.Replace('\','/') -match '(^|/)(auth\.json|config\.toml|codex-config-connection\.dat|codex-dock-details\.dat|mobile-settings\.dat|auth-sessions\.json|operations\.json|worker\.lock|manager\.lock|codex-config-backups|\.env(?:\..*)?|[^/]+\.backup)(/|$)') { throw "Private configuration path in release input: $Name" }
    if ($Bytes.Length -gt 0 -and $taskPrivateHashes.Contains([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)))) { throw "Personal configuration bytes found: $Name" }
    $taskUtf8 = [Text.Encoding]::UTF8.GetString($Bytes); $taskUnicode = [Text.Encoding]::Unicode.GetString($Bytes)
    foreach ($taskSecret in $taskSecrets) {
        if ($taskUtf8.Contains($taskSecret) -or $taskUnicode.Contains($taskSecret)) { throw "Personal credential found in release input: $Name (value hidden)" }
    }
}
$taskFiles = git -C $taskRoot ls-files --cached --others --exclude-standard
foreach ($taskName in $taskFiles) { Check-Bytes $taskName ([IO.File]::ReadAllBytes((Join-Path $taskRoot $taskName))) }
if ($History) {
    foreach ($taskObject in (git -C $taskRoot rev-list --objects --all)) {
        if ($taskObject -notmatch '^([0-9a-f]+) (.+)$') { continue }
        $taskId = $Matches[1]; $taskName = $Matches[2]
        if ($taskName -match '\.(cs|json|md|ps1|iss|xaml|toml|txt|props|csproj)$' -or $taskName -match '(auth\.json|config\.toml|\.dat|\.backup)$') {
            $taskContents = (git -C $taskRoot cat-file blob $taskId) -join "`n"
            Check-Bytes $taskName ([Text.Encoding]::UTF8.GetBytes($taskContents))
        }
    }
}
if ($PayloadDir) { Get-ChildItem -LiteralPath $PayloadDir -Recurse -File | ForEach-Object { Check-Bytes $_.FullName ([IO.File]::ReadAllBytes($_.FullName)) } }
if ($SourceArchive) {
    $taskZip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($SourceArchive))
    try {
        foreach ($taskEntry in $taskZip.Entries) {
            $taskStream = $taskEntry.Open(); $taskMemory = [IO.MemoryStream]::new()
            try { $taskStream.CopyTo($taskMemory); Check-Bytes $taskEntry.FullName $taskMemory.ToArray() }
            finally { $taskMemory.Dispose(); $taskStream.Dispose() }
        }
    } finally { $taskZip.Dispose() }
}
Write-Output 'PASS privacy scan: personal credentials and configuration files excluded (values never printed).'
