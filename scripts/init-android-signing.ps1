param(
    [string]$Jdk = $env:JAVA_HOME,
    [string]$SigningHome = (Join-Path $env:LOCALAPPDATA 'CodexDock/android-signing')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath (Join-Path $Jdk 'bin/keytool.exe'))) { throw 'Specify a JDK using -Jdk.' }
$SigningHome = [IO.Path]::GetFullPath($SigningHome)
$taskKey = Join-Path $SigningHome 'codex-dock.p12'
$taskPassword = Join-Path $SigningHome 'store-password.txt'
if ((Test-Path -LiteralPath $taskKey) -or (Test-Path -LiteralPath $taskPassword)) {
    throw 'Signing material already exists; preserve it for upgrades. This script never replaces keys.'
}
New-Item -ItemType Directory -Path $SigningHome -Force | Out-Null
$taskSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$taskAcl = [Security.AccessControl.DirectorySecurity]::new()
$taskAcl.SetAccessRuleProtection($true, $false)
$taskAcl.SetOwner($taskSid)
foreach ($taskIdentity in @($taskSid, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
    $taskRule = [Security.AccessControl.FileSystemAccessRule]::new($taskIdentity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $taskAcl.AddAccessRule($taskRule)
}
Set-Acl -LiteralPath $SigningHome -AclObject $taskAcl
$env:CODEX_DOCK_ANDROID_STORE_PASSWORD = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
try {
    [IO.File]::WriteAllText($taskPassword, $env:CODEX_DOCK_ANDROID_STORE_PASSWORD, [Text.UTF8Encoding]::new($false))
    & (Join-Path $Jdk 'bin/keytool.exe') -genkeypair -keystore $taskKey -storetype PKCS12 -storepass:env CODEX_DOCK_ANDROID_STORE_PASSWORD -keypass:env CODEX_DOCK_ANDROID_STORE_PASSWORD -alias codex-dock -keyalg RSA -keysize 3072 -validity 10000 -dname 'CN=Codex Dock Android'
    if ($LASTEXITCODE -ne 0) { throw 'Key generation failed. Inspect the signing directory before retrying.' }
    Write-Output "Signing material created in $SigningHome. Back up this private directory securely; it is required for APK updates."
} finally {
    Remove-Item Env:CODEX_DOCK_ANDROID_STORE_PASSWORD -ErrorAction SilentlyContinue
}
