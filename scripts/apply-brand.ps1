#requires -Version 7.2
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskSource = Join-Path $taskRoot 'assets\brand-refresh-20261117\selected.json'
$taskBrand = Join-Path $taskRoot 'assets\brand'
$taskSpec = Get-Content -LiteralPath $taskSource -Raw | ConvertFrom-Json
$taskSelected = $taskSpec.concepts[0]
& (Join-Path $PSScriptRoot 'render-brand-refresh.ps1') -SpecFile $taskSource -OutputDirectory $taskBrand -Flat
if (-not $?) { throw 'Selected brand rendering failed.' }
foreach ($taskSize in @(16,20,24,32,40,48,64,128,256,512,1024)) {
    Copy-Item -LiteralPath (Join-Path $taskBrand "app-$taskSize.png") -Destination (Join-Path $taskBrand "logo-$taskSize.png") -Force
}
Copy-Item -LiteralPath (Join-Path $taskBrand 'app.svg') -Destination (Join-Path $taskBrand 'codex-dock.svg') -Force
Copy-Item -LiteralPath (Join-Path $taskBrand 'app.svg') -Destination (Join-Path $taskBrand 'codex-config.svg') -Force
Copy-Item -LiteralPath (Join-Path $taskBrand 'app.ico') -Destination (Join-Path $taskBrand 'codex-dock.ico') -Force
Copy-Item -LiteralPath (Join-Path $taskBrand 'app.ico') -Destination (Join-Path $taskRoot 'src\CodexConfig.App\Assets\app.ico') -Force
Copy-Item -LiteralPath (Join-Path $taskBrand 'mono-dark-32.png') -Destination (Join-Path $taskRoot 'src\CodexConfig.App\Assets\tray-dark.png') -Force
Copy-Item -LiteralPath (Join-Path $taskBrand 'mono-light-32.png') -Destination (Join-Path $taskRoot 'src\CodexConfig.App\Assets\tray-light.png') -Force
$taskXaml = [Collections.Generic.List[string]]::new()
$taskXaml.Add('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">')
$taskXaml.Add('  <DrawingImage x:Key="BrandMark"><DrawingImage.Drawing><DrawingGroup>')
$taskXaml.Add('    <GeometryDrawing Brush="' + $taskSelected.background + '"><GeometryDrawing.Geometry><RectangleGeometry Rect="0,0,128,128" RadiusX="30" RadiusY="30"/></GeometryDrawing.Geometry></GeometryDrawing>')
foreach ($taskShape in $taskSelected.shapes) {
    $taskXaml.Add('    <GeometryDrawing Brush="' + $taskSelected.($taskShape.fill) + '" Geometry="' + $taskShape.path + '"/>')
}
$taskXaml.Add('  </DrawingGroup></DrawingImage.Drawing></DrawingImage>')
$taskXaml.Add('</ResourceDictionary>')
[IO.File]::WriteAllText((Join-Path $taskRoot 'src\CodexConfig.App\Assets\Brand.xaml'),($taskXaml -join "`n") + "`n",[Text.UTF8Encoding]::new($false))

# Generate only Dock-owned overlays, leaving vendor resources and provider icons intact.
$taskAndroid = Join-Path $taskRoot 'src\CodexAndroid\res'
function Write-TaskVector([string]$Name, [int]$Dp, [bool]$Background, [bool]$Adaptive, [bool]$Monochrome) {
    $taskLines = [Collections.Generic.List[string]]::new()
    $taskLines.Add('<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="' + $Dp + 'dp" android:height="' + $Dp + 'dp" android:viewportWidth="128" android:viewportHeight="128">')
    if ($Background) {
        $taskLines.Add(' <path android:fillColor="' + $taskSelected.background + '" android:pathData="M30,0 L98,0 Q128,0 128,30 L128,98 Q128,128 98,128 L30,128 Q0,128 0,98 L0,30 Q0,0 30,0 Z"/>')
    }
    if ($Adaptive) { $taskLines.Add(' <group android:pivotX="64" android:pivotY="64" android:scaleX="0.8" android:scaleY="0.8">') }
    foreach ($taskShape in $taskSelected.shapes) {
        $taskColor = if ($Monochrome) { '#FFFFFFFF' } else { $taskSelected.($taskShape.fill) }
        $taskLines.Add(' <path android:fillColor="' + $taskColor + '" android:pathData="' + $taskShape.path + '"/>')
    }
    if ($Adaptive) { $taskLines.Add(' </group>') }
    $taskLines.Add('</vector>')
    $taskPath = Join-Path $taskAndroid "drawable\$Name.xml"
    New-Item -ItemType Directory -Path (Split-Path $taskPath -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($taskPath,($taskLines -join "`n") + "`n",[Text.UTF8Encoding]::new($false))
}
Write-TaskVector 'ic_bridge' 128 $true $false $false
Write-TaskVector 'ic_launcher_foreground' 108 $false $true $false
Write-TaskVector 'ic_launcher_monochrome' 108 $false $true $true
Write-TaskVector 'ic_notification' 24 $false $false $true
[IO.File]::WriteAllText((Join-Path $taskAndroid 'values\launcher.xml'),'<resources><color name="launcher_background">' + $taskSelected.background + '</color></resources>' + "`n",[Text.UTF8Encoding]::new($false))
foreach ($taskApi in @(26,33)) {
    $taskFolder = Join-Path $taskAndroid "mipmap-anydpi-v$taskApi"
    New-Item -ItemType Directory -Path $taskFolder -Force | Out-Null
    $taskLines = @('<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">', ' <background android:drawable="@color/launcher_background"/>', ' <foreground android:drawable="@drawable/ic_launcher_foreground"/>')
    if ($taskApi -ge 33) { $taskLines += ' <monochrome android:drawable="@drawable/ic_launcher_monochrome"/>' }
    $taskLines += '</adaptive-icon>'
    [IO.File]::WriteAllText((Join-Path $taskFolder 'ic_launcher.xml'),($taskLines -join "`n") + "`n",[Text.UTF8Encoding]::new($false))
}
Write-Output 'Applied PORT geometry with light sage palette to owned Windows and Android assets.'
