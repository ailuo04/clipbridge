[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$foregroundPath = Join-Path $Root 'android/app/src/main/res/drawable-nodpi/clipbridge_brand.png'
$iconPath = Join-Path $Root 'ClipBridge.ico'
New-Item -ItemType Directory -Force -Path (Split-Path $foregroundPath) | Out-Null

function New-FolderPath([single]$X, [single]$Y, [single]$Width, [single]$Height) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.StartFigure()
    $path.AddLine($X + 18, $Y + 42, $X + 82, $Y + 42)
    $path.AddBezier($X + 82, $Y + 42, $X + 98, $Y + 42, $X + 106, $Y + 61, $X + 122, $Y + 61)
    $path.AddLine($X + 122, $Y + 61, $X + $Width - 20, $Y + 61)
    $path.AddBezier($X + $Width - 20, $Y + 61, $X + $Width - 9, $Y + 61, $X + $Width, $Y + 70, $X + $Width, $Y + 82)
    $path.AddLine($X + $Width, $Y + 82, $X + $Width, $Y + $Height - 20)
    $path.AddBezier($X + $Width, $Y + $Height - 20, $X + $Width, $Y + $Height - 9, $X + $Width - 9, $Y + $Height, $X + $Width - 20, $Y + $Height)
    $path.AddLine($X + $Width - 20, $Y + $Height, $X + 20, $Y + $Height)
    $path.AddBezier($X + 20, $Y + $Height, $X + 9, $Y + $Height, $X, $Y + $Height - 9, $X, $Y + $Height - 20)
    $path.AddLine($X, $Y + $Height - 20, $X, $Y + 62)
    $path.AddBezier($X, $Y + 62, $X, $Y + 51, $X + 7, $Y + 42, $X + 18, $Y + 42)
    $path.CloseFigure()
    return $path
}

function New-RightArrowPath {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.StartFigure()
    $path.AddLine(121, 207, 265, 207)
    $path.AddLine(265, 207, 265, 185)
    $path.AddBezier(265, 185, 265, 178, 273, 175, 278, 180)
    $path.AddLine(278, 180, 335, 232)
    $path.AddBezier(335, 232, 341, 237, 341, 243, 335, 248)
    $path.AddLine(335, 248, 278, 300)
    $path.AddBezier(278, 300, 273, 305, 265, 302, 265, 295)
    $path.AddLine(265, 295, 265, 273)
    $path.AddLine(265, 273, 121, 273)
    $path.AddBezier(121, 273, 99, 273, 89, 259, 89, 240)
    $path.AddBezier(89, 240, 89, 221, 99, 207, 121, 207)
    $path.CloseFigure()
    return $path
}

function New-LeftArrowPath {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.StartFigure()
    $path.AddLine(391, 297, 247, 297)
    $path.AddLine(247, 297, 247, 275)
    $path.AddBezier(247, 275, 247, 268, 239, 265, 234, 270)
    $path.AddLine(234, 270, 177, 322)
    $path.AddBezier(177, 322, 171, 327, 171, 333, 177, 338)
    $path.AddLine(177, 338, 234, 390)
    $path.AddBezier(234, 390, 239, 395, 247, 392, 247, 385)
    $path.AddLine(247, 385, 247, 363)
    $path.AddLine(247, 363, 391, 363)
    $path.AddBezier(391, 363, 413, 363, 423, 349, 423, 330)
    $path.AddBezier(423, 330, 423, 311, 413, 297, 391, 297)
    $path.CloseFigure()
    return $path
}

function Draw-GlassFill([System.Drawing.Graphics]$Graphics, [System.Drawing.Drawing2D.GraphicsPath]$Path, [System.Drawing.Color]$Top, [System.Drawing.Color]$Bottom) {
    $bounds = $Path.GetBounds()
    $fill = [System.Drawing.Drawing2D.LinearGradientBrush]::new($bounds, $Top, $Bottom, 90)
    $Graphics.FillPath($fill, $Path)
    $fill.Dispose()
    $state = $Graphics.Save()
    $Graphics.SetClip($Path)
    $shine = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new($bounds.X, $bounds.Y, $bounds.Width, [single]($bounds.Height * .44)),
        [System.Drawing.Color]::FromArgb(94, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
        90
    )
    $Graphics.FillRectangle($shine, $bounds.X, $bounds.Y, $bounds.Width, [single]($bounds.Height * .44))
    $shine.Dispose()
    $Graphics.Restore($state)
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(232, 46, 65, 88), 9)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $Graphics.DrawPath($pen, $Path)
    $pen.Dispose()
}

function New-BrandMark {
    $bitmap = [System.Drawing.Bitmap]::new(512, 512, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $folders = @(
        @{ Path = (New-FolderPath 91 84 260 281); Top = [System.Drawing.Color]::FromArgb(255, 252, 253, 255); Bottom = [System.Drawing.Color]::FromArgb(255, 174, 207, 244) },
        @{ Path = (New-FolderPath 164 141 260 281); Top = [System.Drawing.Color]::FromArgb(255, 242, 253, 255); Bottom = [System.Drawing.Color]::FromArgb(255, 146, 210, 218) }
    )
    foreach ($folder in $folders) {
        try { Draw-GlassFill $graphics $folder.Path $folder.Top $folder.Bottom }
        finally { $folder.Path.Dispose() }
    }

    $arrows = @(
        @{ Path = (New-RightArrowPath); Top = [System.Drawing.Color]::FromArgb(255, 157, 161, 255); Bottom = [System.Drawing.Color]::FromArgb(255, 82, 106, 224); Shine = [single[]]@(122, 219, 246, 219) },
        @{ Path = (New-LeftArrowPath); Top = [System.Drawing.Color]::FromArgb(255, 132, 226, 232); Bottom = [System.Drawing.Color]::FromArgb(255, 43, 151, 188); Shine = [single[]]@(264, 309, 388, 309) }
    )
    foreach ($arrow in $arrows) {
        try {
            $bounds = $arrow.Path.GetBounds()
            $fill = [System.Drawing.Drawing2D.LinearGradientBrush]::new($bounds, $arrow.Top, $arrow.Bottom, 0)
            $graphics.FillPath($fill, $arrow.Path)
            $fill.Dispose()
            $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(245, 44, 62, 84), 10)
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $graphics.DrawPath($pen, $arrow.Path)
            $pen.Dispose()
            $state = $graphics.Save()
            $graphics.SetClip($arrow.Path)
            $gloss = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(150, 255, 255, 255), 5)
            $gloss.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $gloss.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $graphics.DrawLine($gloss, $arrow.Shine[0], $arrow.Shine[1], $arrow.Shine[2], $arrow.Shine[3])
            $gloss.Dispose()
            $graphics.Restore($state)
        }
        finally { $arrow.Path.Dispose() }
    }
    $graphics.Dispose()
    $safeMark = [System.Drawing.Bitmap]::new(512, 512, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $safeGraphics = [System.Drawing.Graphics]::FromImage($safeMark)
    $safeGraphics.Clear([System.Drawing.Color]::Transparent)
    $safeGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $safeGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $safeGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $safeGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $safeGraphics.DrawImage($bitmap, [System.Drawing.RectangleF]::new(25.6, 25.6, 460.8, 460.8))
    $safeGraphics.Dispose()
    $bitmap.Dispose()
    return $safeMark
}

function New-WindowIconFrame([System.Drawing.Bitmap]$Mark, [int]$Size) {
    $frame = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($frame)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $scale = $Size / 512.0
    $bounds = [System.Drawing.RectangleF]::new([single](14 * $scale), [single](14 * $scale), [single](484 * $scale), [single](484 * $scale))
    $tile = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = 224 * $scale
    $tile.AddArc($bounds.X, $bounds.Y, $diameter, $diameter, 180, 90)
    $tile.AddArc($bounds.Right - $diameter, $bounds.Y, $diameter, $diameter, 270, 90)
    $tile.AddArc($bounds.Right - $diameter, $bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $tile.AddArc($bounds.X, $bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $tile.CloseFigure()
    try {
        $baseBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new($bounds, [System.Drawing.Color]::FromArgb(255, 250, 252, 255), [System.Drawing.Color]::FromArgb(255, 218, 231, 252), 115)
        $graphics.FillPath($baseBrush, $tile)
        $baseBrush.Dispose()
        $state = $graphics.Save()
        $graphics.SetClip($tile)
        $glowPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $glowPath.AddEllipse([single](48 * $scale), [single](18 * $scale), [single](404 * $scale), [single](244 * $scale))
        $glow = [System.Drawing.Drawing2D.PathGradientBrush]::new($glowPath)
        $glow.CenterColor = [System.Drawing.Color]::FromArgb(42, 154, 163, 255)
        $glow.SurroundColors = [System.Drawing.Color[]]@([System.Drawing.Color]::Transparent)
        $graphics.FillPath($glow, $glowPath)
        $glow.Dispose(); $glowPath.Dispose()
        $graphics.Restore($state)
        $markMargin = -0.1 * $Size
        $markSide = 1.2 * $Size
        $graphics.DrawImage($Mark, [System.Drawing.Rectangle]::new([int]$markMargin, [int]$markMargin, [int]$markSide, [int]$markSide))
        $edge = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(244, 255, 255, 255), [single]([Math]::Max(1, 2 * $scale)))
        $graphics.DrawPath($edge, $tile)
        $edge.Dispose()
    }
    finally { $tile.Dispose(); $graphics.Dispose() }
    return $frame
}

$mark = New-BrandMark
try {
    $mark.Save($foregroundPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $frame = New-WindowIconFrame $mark $size
        try {
            $stream = [System.IO.MemoryStream]::new()
            $frame.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
            $stream.Dispose()
        }
        finally { $frame.Dispose() }
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
finally { $mark.Dispose() }

Write-Host "Generated shared Android mark: $foregroundPath"
Write-Host "Generated multi-size Windows icon: $iconPath"

