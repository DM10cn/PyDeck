param([string]$Source)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $projectRoot 'src/PimGui.App/Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
$original = Join-Path $assets 'AppIcon.Source.png'
if ($Source) { Copy-Item -LiteralPath $Source -Destination $original }
if (!(Test-Path -LiteralPath $original)) { throw 'The selected source icon is missing.' }
Add-Type -AssemblyName System.Drawing
$inputImage = [System.Drawing.Image]::FromFile($original)
$sizes = @(16,24,32,48,64,128,256)
$frames = [System.Collections.Generic.List[byte[]]]::new()
try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($inputImage, 0, 0, $size, $size)
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
            if ($size -eq 256) { [System.IO.File]::WriteAllBytes((Join-Path $assets 'AppIcon.png'), $stream.ToArray()) }
        } finally { $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
    }
} finally { $inputImage.Dispose() }
$file = [System.IO.File]::Create((Join-Path $assets 'AppIcon.ico'))
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Output 'Generated AppIcon.ico (16–256 px) and AppIcon.png; original artwork preserved.'
