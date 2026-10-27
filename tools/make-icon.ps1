param(
    [string]$OutPath = (Join-Path $PSScriptRoot "..\src\LolStatsCheck.App\Assets\app.ico")
)

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [single]($size / 256.0)
    $bgRect = [System.Drawing.RectangleF]::new(8 * $s, 8 * $s, 240 * $s, 240 * $s)
    $bgPath = New-RoundedPath $bgRect.X $bgRect.Y $bgRect.Width $bgRect.Height (54 * $s)
    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $bgRect,
        [System.Drawing.Color]::FromArgb(255, 23, 34, 54),
        [System.Drawing.Color]::FromArgb(255, 10, 15, 26),
        [single]90.0)
    $g.FillPath($brush, $bgPath)

    $border = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(90, 88, 132, 190), [single](3 * $s))
    $g.DrawPath($border, $bgPath)

    $bars = @(
        @{ X = 52; W = 44; H = 58; C = [System.Drawing.Color]::FromArgb(255, 76, 201, 240) },
        @{ X = 106; W = 44; H = 98; C = [System.Drawing.Color]::FromArgb(255, 61, 220, 151) },
        @{ X = 160; W = 44; H = 142; C = [System.Drawing.Color]::FromArgb(255, 245, 196, 81) }
    )

    foreach ($bar in $bars) {
        $x = [single]($bar.X * $s)
        $w = [single]($bar.W * $s)
        $h = [single]($bar.H * $s)
        $y = [single]((198 * $s) - $h)
        $radius = [single][Math]::Min(12 * $s, $w / 2)
        $p = New-RoundedPath $x $y $w $h $radius
        $b = [System.Drawing.SolidBrush]::new($bar.C)
        $g.FillPath($b, $p)
        $glow = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(70, 255, 255, 255), [single](2 * $s))
        $g.DrawPath($glow, $p)
        $b.Dispose()
        $glow.Dispose()
        $p.Dispose()
    }

    $line = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(210, 232, 238, 249), [single](7 * $s))
    $line.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($line, 50 * $s, 120 * $s, 96 * $s, 84 * $s)
    $g.DrawLine($line, 96 * $s, 84 * $s, 137 * $s, 104 * $s)
    $g.DrawLine($line, 137 * $s, 104 * $s, 205 * $s, 50 * $s)

    $dot = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 245, 196, 81))
    $dotSize = 15 * $s
    $g.FillEllipse($dot, (205 * $s) - ($dotSize / 2), (50 * $s) - ($dotSize / 2), $dotSize, $dotSize)

    $bytes = New-IconDib $bmp

    $dot.Dispose()
    $line.Dispose()
    $g.Dispose()
    $bmp.Dispose()
    $brush.Dispose()
    $bgPath.Dispose()
    $border.Dispose()

    return ,$bytes
}

function New-IconDib([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $rect = [System.Drawing.Rectangle]::new(0, 0, $size, $size)
    $locked = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $locked.Stride
    $pixels = [byte[]]::new($size * $size * 4)
    for ($y = 0; $y -lt $size; $y++) {
        $src = [IntPtr]::Add($locked.Scan0, (($size - 1 - $y) * $stride))
        [System.Runtime.InteropServices.Marshal]::Copy($src, $pixels, $y * $size * 4, $size * 4)
    }
    $bmp.UnlockBits($locked)

    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $mask = [byte[]]::new($maskStride * $size)

    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)
    $bw.Write([UInt32]40)
    $bw.Write([Int32]$size)
    $bw.Write([Int32]($size * 2))
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]0)
    $bw.Write([UInt32]($size * $size * 4))
    $bw.Write([Int32]0)
    $bw.Write([Int32]0)
    $bw.Write([UInt32]0)
    $bw.Write([UInt32]0)
    $bw.Write($pixels)
    $bw.Write($mask)
    $bw.Flush()
    $result = $ms.ToArray()
    $bw.Dispose()
    $ms.Dispose()

    return ,$result
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.ArrayList]::new()
foreach ($size in $sizes) {
    [void]$images.Add([pscustomobject]@{ Size = $size; Data = (New-IconPng $size) })
}

$OutPath = [System.IO.Path]::GetFullPath($OutPath)
$dir = Split-Path -Parent $OutPath
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }

$fs = [System.IO.File]::Create($OutPath)
$bw = [System.IO.BinaryWriter]::new($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($item in $images) {
    [byte]$dim = if ($item.Size -ge 256) { 0 } else { $item.Size }
    $bw.Write($dim)
    $bw.Write($dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$item.Data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $item.Data.Length
}

foreach ($item in $images) {
    $bw.Write([byte[]]$item.Data)
}

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Host "Icon written to $OutPath ($((Get-Item $OutPath).Length) bytes)"
