#requires -Version 7.2
param([string]$OutputDirectory, [string]$SpecFile, [switch]$Flat)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskSource = if ($SpecFile) { [IO.Path]::GetFullPath($SpecFile) } else { Join-Path $taskRoot 'assets\brand-refresh-20261117\concepts.json' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskRoot 'assets\brand-refresh-20261117\rendered' }
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $taskOutput.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Brand output must stay inside this workspace.' }
$taskSpec = Get-Content -LiteralPath $taskSource -Raw | ConvertFrom-Json
if ($Flat -and $taskSpec.concepts.Count -ne 1) { throw 'Flat export requires exactly one selected concept.' }
$taskSizes = @(16,20,24,32,40,48,64,128,256,512,1024)
function Write-TaskPng($Drawing, [int]$Size, [string]$Path) {
    $taskVisual = [Windows.Media.DrawingVisual]::new()
    $taskContext = $taskVisual.RenderOpen()
    try {
        $taskContext.PushTransform([Windows.Media.ScaleTransform]::new($Size / 128.0,$Size / 128.0))
        $taskContext.DrawDrawing($Drawing)
        $taskContext.Pop()
    } finally { $taskContext.Close() }
    $taskBitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Size,$Size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $taskBitmap.Render($taskVisual)
    $taskEncoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $taskEncoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($taskBitmap))
    $taskStream = [IO.File]::Create($Path)
    try { $taskEncoder.Save($taskStream) } finally { $taskStream.Dispose() }
}
function Write-TaskIco([string]$Folder, [string]$Prefix, [int[]]$Sizes, [string]$Path) {
    $taskWriter = [IO.BinaryWriter]::new([IO.File]::Create($Path))
    $taskFrames = foreach ($taskSize in $Sizes) { @{ Size = $taskSize; Bytes = [IO.File]::ReadAllBytes((Join-Path $Folder "$Prefix-$taskSize.png")) } }
    try {
        $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskFrames.Count)
        $taskOffset = 6 + 16 * $taskFrames.Count
        foreach ($taskFrame in $taskFrames) {
            $taskDimension = if ($taskFrame.Size -eq 256) { 0 } else { $taskFrame.Size }
            $taskWriter.Write([byte]$taskDimension); $taskWriter.Write([byte]$taskDimension); $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
            $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32); $taskWriter.Write([uint32]$taskFrame.Bytes.Length); $taskWriter.Write([uint32]$taskOffset)
            $taskOffset += $taskFrame.Bytes.Length
        }
        foreach ($taskFrame in $taskFrames) { $taskWriter.Write([byte[]]$taskFrame.Bytes) }
    } finally { $taskWriter.Dispose() }
}
foreach ($taskConcept in $taskSpec.concepts) {
    $taskFolder = if ($Flat) { $taskOutput } else { Join-Path $taskOutput $taskConcept.id }
    New-Item -ItemType Directory -Path $taskFolder -Force | Out-Null
    foreach ($taskVariant in @('app','symbol','mono-dark','mono-light')) {
        $taskDrawing = [Windows.Media.DrawingGroup]::new()
        $taskSvg = [Collections.Generic.List[string]]::new()
        $taskSvg.Add('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128" role="img" aria-label="Codex Dock ' + $taskConcept.english + '">')
        $taskSvg.Add('  <title>Codex Dock · ' + $taskConcept.name + '</title>')
        if ($taskVariant -eq 'app') {
            $taskBrush = [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($taskConcept.background))
            $taskDrawing.Children.Add([Windows.Media.GeometryDrawing]::new($taskBrush,$null,[Windows.Media.RectangleGeometry]::new([Windows.Rect]::new(0,0,128,128),30,30)))
            $taskSvg.Add('  <rect width="128" height="128" rx="30" fill="' + $taskConcept.background + '"/>')
        }
        foreach ($taskShape in $taskConcept.shapes) {
            $taskColor = if ($taskVariant -eq 'mono-dark') { '#18211D' } elseif ($taskVariant -eq 'mono-light') { '#FFFFFF' } else { $taskConcept.($taskShape.fill) }
            $taskBrush = [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($taskColor))
            $taskGeometry = [Windows.Media.Geometry]::Parse($taskShape.path)
            $taskDrawing.Children.Add([Windows.Media.GeometryDrawing]::new($taskBrush,$null,$taskGeometry))
            $taskSvg.Add('  <path d="' + $taskShape.path + '" fill="' + $taskColor + '"/>')
        }
        $taskSvg.Add('</svg>')
        [IO.File]::WriteAllText((Join-Path $taskFolder "$taskVariant.svg"),($taskSvg -join "`n") + "`n",[Text.UTF8Encoding]::new($false))
        # An explicit transparent bounds rect preserves the same 128-unit box in every monochrome export.
        $taskBounds = [Windows.Media.GeometryDrawing]::new([Windows.Media.Brushes]::Transparent,$null,[Windows.Media.RectangleGeometry]::new([Windows.Rect]::new(0,0,128,128)))
        $taskDrawing.Children.Insert(0,$taskBounds)
        foreach ($taskSize in $taskSizes) { Write-TaskPng $taskDrawing $taskSize (Join-Path $taskFolder "$taskVariant-$taskSize.png") }
    }
    Write-TaskIco $taskFolder 'app' @(16,20,24,32,40,48,64,128,256) (Join-Path $taskFolder 'app.ico')
    Write-TaskIco $taskFolder 'mono-dark' @(16,20,24,32,40,48) (Join-Path $taskFolder 'tray-on-light.ico')
    Write-TaskIco $taskFolder 'mono-light' @(16,20,24,32,40,48) (Join-Path $taskFolder 'tray-on-dark.ico')
    Write-Output "Rendered $($taskConcept.id): SVG, 16–1024 px PNG, multi-resolution app and tray ICO."
}
