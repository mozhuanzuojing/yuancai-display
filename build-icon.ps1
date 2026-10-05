$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$taskAssets=Join-Path $PSScriptRoot 'Assets'
$originalIcon=[Drawing.Image]::FromFile((Join-Path $taskAssets 'app-icon.png'))
$frames=New-Object 'System.Collections.Generic.List[byte[]]'
$sizes=@(16,24,32,48,64,128,256)
try{
 foreach($size in $sizes){
  $bitmap=New-Object Drawing.Bitmap($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $graphics=[Drawing.Graphics]::FromImage($bitmap)
  $stream=New-Object IO.MemoryStream
  try{
   $graphics.CompositingMode=[Drawing.Drawing2D.CompositingMode]::SourceCopy
   $graphics.CompositingQuality=[Drawing.Drawing2D.CompositingQuality]::HighQuality
   $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
   $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
   $graphics.DrawImage($originalIcon,0,0,$size,$size)
   $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
   $frames.Add($stream.ToArray())
  }finally{$graphics.Dispose();$bitmap.Dispose();$stream.Dispose()}
 }
 $iconFile=[IO.File]::Create((Join-Path $taskAssets 'app-icon.ico'))
 $writer=New-Object IO.BinaryWriter($iconFile)
 try{
  $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
  $offset=6+16*$sizes.Count
  for($index=0;$index -lt $sizes.Count;$index++){
   $dimension=if($sizes[$index] -eq 256){0}else{$sizes[$index]}
   $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
   $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frames[$index].Length);$writer.Write([uint32]$offset)
   $offset+=$frames[$index].Length
  }
  foreach($frame in $frames){$writer.Write([byte[]]$frame)}
 }finally{$writer.Dispose();$iconFile.Dispose()}
}finally{$originalIcon.Dispose()}