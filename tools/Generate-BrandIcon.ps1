[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$SourcePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not $SourcePath) { $SourcePath = Join-Path $Root 'assets/clipbridge-icon.png' }
$foregroundPath = Join-Path $Root 'android/app/src/main/res/drawable-nodpi/clipbridge_brand.png'
$iconPath = Join-Path $Root 'ClipBridge.ico'

function New-IconBitmap([System.Drawing.Image]$Source, [int]$Size, [int]$Margin = 0) {
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $bounds = [System.Drawing.Rectangle]::new($Margin, $Margin, $Size - 2 * $Margin, $Size - 2 * $Margin)
        $graphics.DrawImage($Source, $bounds, 0, 0, $Source.Width, $Source.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
    }
    finally { $attributes.Dispose(); $graphics.Dispose() }
    return $bitmap
}

$source = [System.Drawing.Image]::FromFile($SourcePath)
try {
    if ($source.Width -ne $source.Height) { throw 'The approved icon source must be square.' }
    New-Item -ItemType Directory -Force -Path (Split-Path $foregroundPath) | Out-Null
    # Keep the complete artwork inside the Android adaptive-icon safe zone.
    $foreground = New-IconBitmap $source 512 87
    try { $foreground.Save($foregroundPath, [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $foreground.Dispose() }

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $frame = New-IconBitmap $source $size
        $stream = [System.IO.MemoryStream]::new()
        try {
            $frame.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
        }
        finally { $stream.Dispose(); $frame.Dispose() }
    }

    $output = [System.IO.File]::Create($iconPath)
    $writer = [System.IO.BinaryWriter]::new($output)
    try {
        $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $size = $sizes[$index]
            $dimension = [byte]$(if ($size -eq 256) { 0 } else { $size })
            $writer.Write($dimension); $writer.Write($dimension); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$frames[$index].Length); $writer.Write([UInt32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frameBytes in $frames) { $writer.Write($frameBytes) }
    }
    finally { $writer.Dispose(); $output.Dispose() }
}
finally { $source.Dispose() }

Write-Host "Generated shared Android mark: $foregroundPath"
Write-Host "Generated multi-size Windows icon: $iconPath"
