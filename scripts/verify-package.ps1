param([string]$Version = '0.14.1')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskArtifacts = Join-Path $taskRoot 'artifacts'
$taskRelease = Join-Path $taskArtifacts "CodexDock-$Version-win-x64"
$taskSetup = Join-Path $taskArtifacts "CodexDock-$Version-Setup-win-x64.exe"
$taskTestRoot = Join-Path $taskRoot ('.installer-check-' + [Guid]::NewGuid().ToString('N'))
$taskInstall = Join-Path $taskTestRoot 'app'
if (-not ([IO.Path]::GetFullPath($taskInstall).StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) { throw 'Verification path escapes workspace.' }
New-Item -ItemType Directory -Path $taskTestRoot -Force | Out-Null
New-Item -ItemType Directory -Path $taskInstall -Force | Out-Null
$taskLegacyNames = @('CodexConfig.exe','CodexConfig.dll','CodexConfig.deps.json','CodexConfig.runtimeconfig.json','CodexConfig.pdb')
foreach ($taskName in $taskLegacyNames) { [IO.File]::WriteAllText((Join-Path $taskInstall $taskName), 'Isolated legacy application fixture') }
$taskSentinel = Join-Path $taskInstall 'user-owned-note.txt'
[IO.File]::WriteAllText($taskSentinel, 'Installer and uninstaller must preserve files they did not install.')
$taskUserEnv = [Environment]::GetEnvironmentVariable('CODEX_AUTHAPI_BASE_URL', 'User')
$taskStartupPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$taskStartupBefore = (Get-ItemProperty -LiteralPath $taskStartupPath -Name CodexDock -ErrorAction SilentlyContinue).CodexDock
$taskRegistry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{D20748E0-BC17-4CB7-AD07-CE57F065D5F1}_is1'
$taskRegistryBefore = if (Test-Path -LiteralPath $taskRegistry) { Get-ItemProperty -LiteralPath $taskRegistry | ConvertTo-Json -Compress } else { 'absent' }
$taskRun = Start-Process -FilePath $taskSetup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/VERIFYONLY=1',"/DIR=$taskInstall","/LOG=$taskTestRoot\install.log") -WindowStyle Hidden -PassThru -Wait
if ($taskRun.ExitCode -ne 0) { throw "Installer failed: $($taskRun.ExitCode). See $taskTestRoot\install.log" }
$taskExe = Join-Path $taskInstall 'CodexDock.exe'
foreach ($taskName in $taskLegacyNames) { if (Test-Path -LiteralPath (Join-Path $taskInstall $taskName)) { throw "Legacy application file remains after upgrade: $taskName" } }
if (-not (Test-Path -LiteralPath $taskSentinel)) { throw 'Upgrade removed a user-owned file.' }
$taskExpectedHash = (Get-FileHash -LiteralPath (Join-Path $taskRelease 'CodexDock.exe') -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash -ne $taskExpectedHash) { throw 'Installed executable differs from tested release.' }
$taskCompared = 0
Get-ChildItem -LiteralPath $taskRelease -Recurse -File | ForEach-Object {
    $taskRelative = $_.FullName.Substring($taskRelease.Length + 1)
    if ((Get-FileHash -LiteralPath (Join-Path $taskInstall $taskRelative) -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash) { throw "Installed file mismatch: $taskRelative" }
    $taskCompared++
}
$taskUi = Join-Path $taskTestRoot 'ui'
$taskRun = Start-Process -FilePath $taskExe -ArgumentList @('--smoke-test',$taskUi) -WindowStyle Hidden -PassThru -Wait
if ($taskRun.ExitCode -ne 0) { throw "Installed EXE UI check failed. See $taskUi\failure.txt" }
$taskReport = Get-Content -LiteralPath (Join-Path $taskUi 'report.json') -Raw | ConvertFrom-Json
if ($taskReport.status -ne 'PASS' -or $taskReport.executableName -ne 'CodexDock.exe' -or $taskReport.executableSha256 -ne $taskExpectedHash.ToLowerInvariant() -or $taskReport.applicationAssemblySha256 -ne (Get-FileHash -LiteralPath (Join-Path $taskInstall 'CodexDock.dll') -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Installed application report mismatch.' }
$taskRun = Start-Process -FilePath (Join-Path $taskInstall 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/VERIFYONLY=1',"/LOG=$taskTestRoot\uninstall.log") -WindowStyle Hidden -PassThru -Wait
if ($taskRun.ExitCode -ne 0 -or (Test-Path -LiteralPath $taskExe) -or -not (Test-Path -LiteralPath $taskSentinel)) { throw 'Uninstall did not remove application cleanly or removed a user file.' }
$taskRegistryAfter = if (Test-Path -LiteralPath $taskRegistry) { Get-ItemProperty -LiteralPath $taskRegistry | ConvertTo-Json -Compress } else { 'absent' }
if ($taskRegistryBefore -ne $taskRegistryAfter -or $taskUserEnv -ne [Environment]::GetEnvironmentVariable('CODEX_AUTHAPI_BASE_URL', 'User')) { throw 'Verification modified the real registration or user auth environment.' }
if ($taskStartupBefore -ne (Get-ItemProperty -LiteralPath $taskStartupPath -Name CodexDock -ErrorAction SilentlyContinue).CodexDock) { throw 'Verification modified the real startup registration.' }
$taskVerification = [ordered]@{ status='PASS'; version=$Version; installerSha256=(Get-FileHash -LiteralPath $taskSetup -Algorithm SHA256).Hash.ToLowerInvariant(); installerExit=0; installedFilesCompared=$taskCompared; executableSha256=$taskExpectedHash.ToLowerInvariant(); installedUiChecks=$taskReport.tests.Count; legacyApplicationFilesRemoved=$taskLegacyNames; uninstallExit=0; userFilePreserved=$true; registryAndUserEnvironmentUnchanged=$true; evidenceDirectory=$taskTestRoot }
$taskVerification | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskArtifacts "installer-verification-$Version.json") -Encoding utf8
$taskVerification | ConvertTo-Json -Depth 4
