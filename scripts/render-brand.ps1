$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskBrandFolder = Join-Path $taskRoot 'assets\brand'
$taskXaml = [IO.File]::ReadAllText((Join-Path $taskRoot 'src\CodexConfig.App\Assets\Brand.xaml'))
$taskResources = [Windows.Markup.XamlReader]::Parse($taskXaml)
$taskDrawing = $taskResources['BrandMark'].Drawing
$taskFrames = @()
foreach ($taskSize in @(16,20,24,32,40,48,64,128,256,512,1024)) {
    $taskVisual = [Windows.Media.DrawingVisual]::new()
    $taskContext = $taskVisual.RenderOpen()
    try {
        $taskContext.PushTransform([Windows.Media.ScaleTransform]::new($taskSize / 128.0, $taskSize / 128.0))
        $taskContext.DrawDrawing($taskDrawing)
        $taskContext.Pop()
    } finally { $taskContext.Close() }
    $taskBitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($taskSize,$taskSize,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $taskBitmap.Render($taskVisual)
    $taskEncoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $taskEncoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($taskBitmap))
    $taskPath = Join-Path $taskBrandFolder "logo-$taskSize.png"
    $taskOutput = [IO.File]::Create($taskPath)
    try { $taskEncoder.Save($taskOutput) } finally { $taskOutput.Dispose() }
    if ($taskSize -le 256) { $taskFrames += @{ Size=$taskSize; Bytes=[IO.File]::ReadAllBytes($taskPath) } }
}
$taskIconPath = Join-Path $taskRoot 'src\CodexConfig.App\Assets\app.ico'
$taskIcon = [IO.BinaryWriter]::new([IO.File]::Create($taskIconPath))
try {
    $taskIcon.Write([uint16]0); $taskIcon.Write([uint16]1); $taskIcon.Write([uint16]$taskFrames.Count)
    $taskOffset = 6 + 16 * $taskFrames.Count
    foreach ($taskFrame in $taskFrames) {
        $taskDim = if ($taskFrame.Size -eq 256) { 0 } else { $taskFrame.Size }
        $taskIcon.Write([byte]$taskDim); $taskIcon.Write([byte]$taskDim)
        $taskIcon.Write([byte]0); $taskIcon.Write([byte]0)
        $taskIcon.Write([uint16]1); $taskIcon.Write([uint16]32)
        $taskIcon.Write([uint32]$taskFrame.Bytes.Length); $taskIcon.Write([uint32]$taskOffset)
        $taskOffset += $taskFrame.Bytes.Length
    }
    foreach ($taskFrame in $taskFrames) { $taskIcon.Write([byte[]]$taskFrame.Bytes) }
} finally { $taskIcon.Dispose() }
Write-Output "Rendered original vector logo at 16–1024 px and Windows app.ico."
