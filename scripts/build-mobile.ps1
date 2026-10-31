param(
    [string]$Python = 'python',
    [switch]$InstallBuildTools
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskArtifacts = Join-Path $taskRoot 'artifacts'
$taskVenv = Join-Path $taskArtifacts 'mobile-build-venv'
$taskBuildPython = Join-Path $taskVenv 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $taskBuildPython)) {
    if (-not $InstallBuildTools) { throw 'Run build-mobile.ps1 -Python <Python 3.12 exe> -InstallBuildTools once.' }
    & $Python -m venv $taskVenv
    if ($LASTEXITCODE -ne 0) { throw 'Mobile build environment creation failed.' }
}
if ($InstallBuildTools) {
    & $taskBuildPython -m pip install --disable-pip-version-check --no-cache-dir -r (Join-Path $PSScriptRoot 'requirements-mobile-build.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Mobile build tool installation failed.' }
}
& $taskBuildPython -c "import struct,sys; assert sys.platform == 'win32' and sys.version_info[:2] == (3,12) and struct.calcsize('P') == 8, 'Use Windows x64 Python 3.12 to build the mobile worker.'"
if ($LASTEXITCODE -ne 0) { throw 'Mobile build requires Windows x64 Python 3.12.' }
& $taskBuildPython -m pip check
if ($LASTEXITCODE -ne 0) { throw 'Mobile build dependencies are inconsistent.' }
$taskSource = Join-Path $taskRoot 'src\CodexMobile'
$taskDist = Join-Path $taskArtifacts 'mobile-worker'
& $taskBuildPython (Join-Path $PSScriptRoot 'freeze-mobile.py')
if ($LASTEXITCODE -ne 0) { throw 'Mobile worker freeze failed.' }
$taskPayload = Join-Path $taskDist 'CodexDock.Mobile'
$taskLicenseDir = Join-Path $taskPayload 'licenses'
New-Item -ItemType Directory -Path $taskLicenseDir -Force | Out-Null
foreach ($taskName in @('codex-mobile-bridge-LICENSE.txt', 'nayuki-qrcodegen-LICENSE.txt', 'Python-LICENSE.txt', 'PyInstaller-COPYING.txt')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot ('third_party\' + $taskName)) -Destination $taskLicenseDir -Force
}
$taskFiles = Get-ChildItem -LiteralPath $taskPayload -File -Recurse
$taskHashes = foreach ($taskFile in $taskFiles) {
    [ordered]@{path=$taskFile.FullName.Substring($taskPayload.Length + 1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
[ordered]@{
    name='CodexDock.Mobile'; protocol=4; upstreamCommit='0721aa7dafa6177751ba7e568e0ad0efee05655b'
    python=(& $taskBuildPython -c 'import platform; print(platform.python_version())')
    pyinstaller=(& $taskBuildPython -m PyInstaller --version)
    createdAtUtc=[DateTimeOffset]::UtcNow.ToString('o'); files=$taskHashes
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskPayload 'worker-manifest.json') -Encoding utf8
Write-Output ('Mobile worker: ' + $taskPayload)
Write-Output ('Payload bytes: ' + ($taskFiles | Measure-Object -Property Length -Sum).Sum)
