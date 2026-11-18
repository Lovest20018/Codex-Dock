param(
    [string]$Version = '0.19.7',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$PreviousVersion
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskArtifacts = Join-Path $taskRoot 'artifacts'
$taskRelease = Join-Path $taskArtifacts "CodexDock-$Version-win-x64"
$taskSetup = Join-Path $taskArtifacts "CodexDock-$Version-Setup-win-x64.exe"
$taskFixtureVersion = if ($PreviousVersion) { $PreviousVersion } else { $Version }
$taskFixtureRelease = Join-Path $taskArtifacts "CodexDock-$taskFixtureVersion-win-x64"
if (-not (Test-Path -LiteralPath (Join-Path $taskFixtureRelease 'CodexDock.exe'))) { throw 'The running upgrade fixture payload is missing.' }
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
. (Join-Path $taskRoot 'tests\installer\mobile-upgrade-fixture.ps1')
. (Join-Path $taskRoot 'tests\installer\dock-upgrade-fixture.ps1')
$taskMobileState = Join-Path $taskTestRoot 'mobile-state'
$taskOtherInstall = Join-Path $taskTestRoot 'other-installation'
$taskUpgradeWorker = $null; $taskOtherWorker = $null
$taskUpgradeDock = $null; $taskOtherDock = $null
$taskUpgradeModel = $null; $taskOtherModel = $null
try {
    New-Item -ItemType Directory -Path $taskOtherInstall | Out-Null
    Get-ChildItem -LiteralPath $taskFixtureRelease -Force | Copy-Item -Destination $taskInstall -Recurse
    Get-ChildItem -LiteralPath $taskFixtureRelease -Force | Copy-Item -Destination $taskOtherInstall -Recurse
    $taskUpgradeDock = Start-InstallerDockFixture $taskInstall (Join-Path $taskTestRoot 'target-dock')
    $taskOtherDock = Start-InstallerDockFixture $taskOtherInstall (Join-Path $taskTestRoot 'other-dock')
    $taskUpgradeModel = Start-InstallerModelFixture $taskInstall (Join-Path $taskTestRoot 'target model profile')
    $taskOtherModel = Start-InstallerModelFixture $taskOtherInstall (Join-Path $taskTestRoot 'other model profile')
    $taskUpgradeWorker = New-InstallerMobileFixture $taskInstall $taskMobileState
    $taskOtherWorker = New-InstallerMobileFixture $taskOtherInstall $taskMobileState
    $taskRun = Start-Process -FilePath $taskSetup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/VERIFYONLY=1',"/MOBILESTATEROOT=$taskMobileState","/DIR=$taskInstall","/LOG=$taskTestRoot\install.log") -WindowStyle Hidden -PassThru -Wait
    if ($taskRun.ExitCode -ne 0) { throw "Installer failed: $($taskRun.ExitCode). See $taskTestRoot\install.log" }
    if (-not $taskUpgradeDock.Process.HasExited -or $taskUpgradeDock.Process.ExitCode -ne 0) { throw 'Installer did not close the targeted tray Dock normally.' }
    if ($taskOtherDock.Process.HasExited) { throw 'Installer closed another installation of Dock.' }
    if (-not (Test-InstallerDockFixture $taskUpgradeDock) -or -not (Test-InstallerDockFixture $taskOtherDock)) { throw 'Installer changed a desktop fixture configuration or login.' }
    if (-not $taskUpgradeModel.Process.HasExited -or $taskUpgradeModel.Process.ExitCode -ne 0) { throw 'Installer did not stop the targeted model gateway.' }
    if ($taskOtherModel.Process.HasExited -or -not (Test-InstallerModelFixture $taskOtherModel)) { throw 'Installer affected another installation model gateway.' }
    foreach ($fixture in @($taskUpgradeModel, $taskOtherModel)) {
        if ((Get-FileHash -LiteralPath $fixture.Settings).Hash -ne $fixture.SettingsHash) { throw 'Installer rewrote saved model settings.' }
    }
    if (-not $taskUpgradeWorker.Process.HasExited) { throw 'Installer returned before the old mobile process exited.' }
    if ($taskOtherWorker.Process.HasExited -or (Invoke-InstallerMobileFixture $taskOtherWorker 'health').instanceId -ne $taskOtherWorker.Config.instanceId) { throw 'Installer affected another installation mobile worker.' }
    foreach ($taskFixture in @($taskUpgradeWorker, $taskOtherWorker)) {
        if ((Get-FileHash -LiteralPath $taskFixture.SettingsPath).Hash -ne $taskFixture.SettingsHash) { throw 'Installer rewrote saved mobile settings.' }
    }
} finally {
    Stop-InstallerMobileFixture $taskUpgradeWorker
    Stop-InstallerMobileFixture $taskOtherWorker
    foreach ($fixture in @($taskUpgradeDock, $taskOtherDock, $taskUpgradeModel, $taskOtherModel)) {
        if ($null -ne $fixture) { Stop-InstallerProcessFixture $fixture.Process }
    }
}
$taskExe = Join-Path $taskInstall 'CodexDock.exe'
if (-not (Test-Path -LiteralPath (Join-Path $taskInstall 'mobile\runtime\client-helpers\codex-quit-helper.exe'))) { throw 'The installed native Codex Quit helper is missing.' }
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
if ($taskReport.applicationVersion -ne $Version -or $taskReport.uiFramework -ne 'WinUI 3') { throw 'The installed WinUI application version must match the requested package version.' }
if ($taskReport.status -ne 'PASS' -or $taskReport.executableName -ne 'CodexDock.exe' -or $taskReport.executableSha256 -ne $taskExpectedHash.ToLowerInvariant() -or $taskReport.applicationAssemblySha256 -ne (Get-FileHash -LiteralPath (Join-Path $taskInstall 'CodexDock.dll') -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Installed application report mismatch.' }
$taskRun = Start-Process -FilePath (Join-Path $taskInstall 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/VERIFYONLY=1',"/LOG=$taskTestRoot\uninstall.log") -WindowStyle Hidden -PassThru -Wait
if ($taskRun.ExitCode -ne 0 -or (Test-Path -LiteralPath $taskExe) -or -not (Test-Path -LiteralPath $taskSentinel)) { throw 'Uninstall did not remove application cleanly or removed a user file.' }
$taskRegistryAfter = if (Test-Path -LiteralPath $taskRegistry) { Get-ItemProperty -LiteralPath $taskRegistry | ConvertTo-Json -Compress } else { 'absent' }
if ($taskRegistryBefore -ne $taskRegistryAfter -or $taskUserEnv -ne [Environment]::GetEnvironmentVariable('CODEX_AUTHAPI_BASE_URL', 'User')) { throw 'Verification modified the real registration or user auth environment.' }
if ($taskStartupBefore -ne (Get-ItemProperty -LiteralPath $taskStartupPath -Name CodexDock -ErrorAction SilentlyContinue).CodexDock) { throw 'Verification modified the real startup registration.' }
$taskVerification = [ordered]@{ status='PASS'; version=$Version; installerSha256=(Get-FileHash -LiteralPath $taskSetup -Algorithm SHA256).Hash.ToLowerInvariant();
    installerExit=0; upgradeFixtureVersion=$taskFixtureVersion; runningDockUpgrade=$true; otherInstallationDockPreserved=$true; dockSettingsUnchanged=$true;
    runningModelUpgrade=$true; otherInstallationModelPreserved=$true; modelSettingsUnchanged=$true;
    runningMobileUpgrade=$true; otherInstallationWorkerPreserved=$true; mobileSettingsUnchanged=$true;
    installedFilesCompared=$taskCompared; executableSha256=$taskExpectedHash.ToLowerInvariant(); installedUiChecks=$taskReport.tests.Count;
    legacyApplicationFilesRemoved=$taskLegacyNames; uninstallExit=0; userFilePreserved=$true;
    registryAndUserEnvironmentUnchanged=$true; evidenceDirectory=$taskTestRoot }
$taskVerification | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskArtifacts "installer-verification-$Version.json") -Encoding utf8
$taskVerification | ConvertTo-Json -Depth 4
