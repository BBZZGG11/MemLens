Add-Type -AssemblyName System.Drawing

$outDir = Split-Path -Parent $PSScriptRoot
$icoPath = Join-Path $outDir 'MemLens.ico'
$previewPath = Join-Path $PSScriptRoot 'icon-preview.png'

function New-RoundedPath([System.Drawing.RectangleF]$rect, [float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.Left, $rect.Top, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Top, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.Left, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [float]($size * 0.045)
    $bgRect = New-Object System.Drawing.RectangleF($pad, $pad, ($size - 2*$pad), ($size - 2*$pad))
    $bgPath = New-RoundedPath $bgRect ([float]($size * 0.20))
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $bgRect,
        [System.Drawing.Color]::FromArgb(255, 15, 23, 42),
        [System.Drawing.Color]::FromArgb(255, 30, 41, 59),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($brush, $bgPath)

    $barW = [float]($size * 0.145)
    $gap = [float]($size * 0.075)
    $baseY = [float]($size * 0.79)
    $startX = [float](($size - (3*$barW + 2*$gap)) / 2.0)
    $heights = @([float]($size * 0.30), [float]($size * 0.43), [float]($size * 0.56))
    $colors = @(
        [System.Drawing.Color]::FromArgb(255, 45, 212, 191),
        [System.Drawing.Color]::FromArgb(255, 245, 158, 11),
        [System.Drawing.Color]::FromArgb(255, 226, 232, 240)
    )

    for ($i = 0; $i -lt 3; $i++) {
        $x = $startX + $i * ($barW + $gap)
        $h = $heights[$i]
        $y = $baseY - $h
        $rect = New-Object System.Drawing.RectangleF($x, $y, $barW, $h)
        $path = New-RoundedPath $rect ([float]($barW * 0.38))
        $b = New-Object System.Drawing.SolidBrush($colors[$i])
        $g.FillPath($b, $path)
        $b.Dispose()
        $path.Dispose()
    }

    # A small lens ring in the upper-right, like a magnifier over memory.
    $ringSize = [float]($size * 0.22)
    $ringX = [float]($size * 0.68)
    $ringY = [float]($size * 0.16)
    $penWidth = [float][Math]::Max(1.0, $size * 0.035)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 94, 234, 212), $penWidth)
    $g.DrawEllipse($pen, $ringX, $ringY, $ringSize, $ringSize)
    $pen.Dispose()

    $g.Dispose()
    $brush.Dispose()
    $bgPath.Dispose()
    return $bmp
}

$sizes = @(16, 32, 48, 256)
$images = New-Object System.Collections.ArrayList
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    [void]$images.Add(@($size, $bytes))
    if ($size -eq 256) { $bmp.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png) }
    $ms.Dispose()
    $bmp.Dispose()
}

$fs = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($item in $images) {
    $size = [int]$item[0]
    $bytes = [byte[]]$item[1]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($item in $images) {
    $bw.Write([byte[]]$item[1])
}
$bw.Close()
$fs.Close()

Get-Item $icoPath, $previewPath | Select-Object FullName, Length
