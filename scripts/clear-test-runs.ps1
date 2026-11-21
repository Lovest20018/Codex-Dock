#requires -Version 7.2
[CmdletBinding(SupportsShouldProcess)]
param([switch]$IncludeLegacy)

$ErrorActionPreference = 'Stop'
$artifacts = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'
$testRuns = Join-Path $artifacts 'test-runs'
if (-not (Test-Path -LiteralPath $testRuns -PathType Container)) { return }

foreach ($path in @($artifacts, $testRuns)) {
    $directory = Get-Item -LiteralPath $path -Force
    if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Refusing to traverse a reparse point at $path"
    }
}

$testRuns = (Resolve-Path -LiteralPath $testRuns).ProviderPath
$prefix = $testRuns + [IO.Path]::DirectorySeparatorChar
foreach ($run in Get-ChildItem -LiteralPath $testRuns -Directory -Force) {
    if ($run.Name -eq 'legacy' -and -not $IncludeLegacy) { continue }
    $target = (Resolve-Path -LiteralPath $run.FullName).ProviderPath
    if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        ($run.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        Write-Warning "Skipped $target"
        continue
    }
    if ($PSCmdlet.ShouldProcess($target, 'Remove test run')) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
