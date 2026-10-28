$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$taskResources=[Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'dock-mark.xaml')))
$taskDrawing=$taskResources['BrandMark'].Drawing
function Save-Drawing($visual,$width,$height,$name){
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream=[IO.File]::Create((Join-Path $PSScriptRoot $name));try{$encoder.Save($stream)}finally{$stream.Dispose()}
}
$taskFrames=@()
foreach($taskSize in @(16,20,24,32,48,64,128,256,512)){
    $v=[Windows.Media.DrawingVisual]::new();$c=$v.RenderOpen()
    $c.PushTransform([Windows.Media.ScaleTransform]::new($taskSize/128.0,$taskSize/128.0));$c.DrawDrawing($taskDrawing);$c.Pop();$c.Close()
    Save-Drawing $v $taskSize $taskSize "logo-$taskSize.png"
    if($taskSize -le 256){$taskFrames+=@{Size=$taskSize;Bytes=[IO.File]::ReadAllBytes((Join-Path $PSScriptRoot "logo-$taskSize.png"))}}
}
$taskIcon=[IO.BinaryWriter]::new([IO.File]::Create((Join-Path $PSScriptRoot 'codex-dock.ico')))
try{
    $taskIcon.Write([uint16]0);$taskIcon.Write([uint16]1);$taskIcon.Write([uint16]$taskFrames.Count);$taskOffset=6+16*$taskFrames.Count
    foreach($f in $taskFrames){$d=if($f.Size -eq 256){0}else{$f.Size};$taskIcon.Write([byte]$d);$taskIcon.Write([byte]$d);$taskIcon.Write([byte]0);$taskIcon.Write([byte]0);$taskIcon.Write([uint16]1);$taskIcon.Write([uint16]32);$taskIcon.Write([uint32]$f.Bytes.Length);$taskIcon.Write([uint32]$taskOffset);$taskOffset+=$f.Bytes.Length}
    foreach($f in $taskFrames){$taskIcon.Write([byte[]]$f.Bytes)}
}finally{$taskIcon.Dispose()}
foreach($tone in @('dark','light')){
    $v=[Windows.Media.DrawingVisual]::new();$c=$v.RenderOpen();$brush=[Windows.Media.BrushConverter]::new().ConvertFromString($(if($tone -eq 'dark'){'#23364D'}else{'#FFFFFF'}))
    $c.DrawGeometry($null,[Windows.Media.Pen]::new($brush,2),[Windows.Media.Geometry]::Parse('M3,3 L3,11 Q3,13 5,13 L11,13 Q13,13 13,11 L13,9'))
    $c.DrawRectangle($brush,$null,[Windows.Rect]::new(7,5,7,2));$c.Close();Save-Drawing $v 16 16 "tray-$tone-16.png"
}
$taskBoard=[Windows.Media.DrawingVisual]::new();$taskCanvas=$taskBoard.RenderOpen()
function Text($value,$x,$y,$size,$color){
    $brush=[Windows.Media.BrushConverter]::new().ConvertFromString($color)
    $formatted=[Windows.Media.FormattedText]::new($value,[Globalization.CultureInfo]::GetCultureInfo('zh-CN'),[Windows.FlowDirection]::LeftToRight,[Windows.Media.Typeface]::new('Segoe UI, Microsoft YaHei UI'),$size,$brush,1.0)
    $taskCanvas.DrawText($formatted,[Windows.Point]::new($x,$y))
}
function Mark($x,$y,$size){$taskCanvas.PushTransform([Windows.Media.TranslateTransform]::new($x,$y));$taskCanvas.PushTransform([Windows.Media.ScaleTransform]::new($size/128.0,$size/128.0));$taskCanvas.DrawDrawing($taskDrawing);$taskCanvas.Pop();$taskCanvas.Pop()}
$taskCanvas.DrawRectangle([Windows.Media.Brushes]::White,$null,[Windows.Rect]::new(0,0,1120,610))
Text 'BRAND PROPOSAL / 2026' 52 32 13 '#596779'
Mark 52 100 160
Text 'Codex Dock' 248 106 52 '#202A36'
Text '本地连接与配置中心' 252 182 22 '#596779'
Text '停靠接口 · 连接、承接、集中管理' 252 229 15 '#596779'
$taskCanvas.DrawLine([Windows.Media.Pen]::new([Windows.Media.BrushConverter]::new().ConvertFromString('#DDE3EB'),1),[Windows.Point]::new(52,315),[Windows.Point]::new(1068,315))
Text '应用图标 / 不同尺寸' 52 345 17 '#202A36'
$x=52
foreach($size in @(64,48,32,24,16)){Mark $x (405+64-$size) $size;Text "$size px" $x 486 12 '#596779';$x+=108}
Text '系统托盘 / 单色' 700 345 17 '#202A36'
$taskCanvas.DrawRoundedRectangle([Windows.Media.BrushConverter]::new().ConvertFromString('#EEF2F6'),$null,[Windows.Rect]::new(700,401,140,72),8,8)
$taskCanvas.DrawRoundedRectangle([Windows.Media.BrushConverter]::new().ConvertFromString('#202A36'),$null,[Windows.Rect]::new(860,401,140,72),8,8)
foreach($pair in @(@('dark',762),@('light',922))){$img=[Windows.Media.Imaging.BitmapImage]::new([Uri](Join-Path $PSScriptRoot ('tray-'+$pair[0]+'-16.png')));$taskCanvas.DrawImage($img,[Windows.Rect]::new([double]$pair[1],429,16,16))}
Text '设计候选 · 尚未替换产品名称与图标' 52 559 14 '#596779'
$taskCanvas.Close();Save-Drawing $taskBoard 1120 610 'brand-board.png'
'Rendered brand board, app icon sizes, monochrome tray previews and ICO proposal.'
