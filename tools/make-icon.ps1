# make-icon.ps1 - 生成 src\app.ico（多尺寸，供 build.ps1 用 /win32icon 嵌入 exe）
# 图形：深蓝圆底 + 黄色播放三角
# 注意：<256 的尺寸写成标准 DIB(不压缩)，256 用 PNG —— 这样老 .NET / 资源管理器 / 托盘都认
[CmdletBinding()]
param(
    [string]$Out = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\src\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # 深蓝渐变圆底
    $m = [Math]::Max(1, [int]($s * 0.04))
    $rect = New-Object System.Drawing.Rectangle($m, $m, ($s - 2 * $m), ($s - 2 * $m))
    $c1 = [System.Drawing.Color]::FromArgb(255, 40, 56, 92)
    $c2 = [System.Drawing.Color]::FromArgb(255, 18, 26, 46)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 60.0)
    $g.FillEllipse($brush, $rect)
    $brush.Dispose()

    # 亮黄播放三角
    $pts = @(
        (New-Object System.Drawing.PointF(($s * 0.38), ($s * 0.24))),
        (New-Object System.Drawing.PointF(($s * 0.78), ($s * 0.50))),
        (New-Object System.Drawing.PointF(($s * 0.38), ($s * 0.76)))
    )
    $tri = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tri.AddPolygon($pts)
    $fg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 250, 206, 66))
    $g.FillPath($fg, $tri)
    $fg.Dispose()
    $tri.Dispose()
    $g.Dispose()
    return $bmp
}

# 32bpp BGRA -> ICO 内的 DIB（BITMAPINFOHEADER + 自下而上的像素 + AND 掩码）
function ConvertTo-IconDib([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    $bw.Write([UInt32]40)                     # biSize
    $bw.Write([Int32]$s)                      # biWidth
    $bw.Write([Int32]($s * 2))                # biHeight = 2x (XOR + AND)
    $bw.Write([UInt16]1)                      # biPlanes
    $bw.Write([UInt16]32)                     # biBitCount
    $bw.Write([UInt32]0)                      # BI_RGB
    $bw.Write([UInt32]($s * $s * 4))          # biSizeImage
    $bw.Write([Int32]0); $bw.Write([Int32]0)  # resolution
    $bw.Write([UInt32]0); $bw.Write([UInt32]0) # palette

    # 像素：自下而上，BGRA
    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $row = New-Object byte[] $stride
        for ($y = $s - 1; $y -ge 0; $y--) {
            [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($data.Scan0, $y * $stride), $row, 0, $stride)
            $bw.Write($row, 0, $s * 4)
        }
    }
    finally { $bmp.UnlockBits($data) }

    # AND 掩码占位（每行 4 字节对齐）
    $maskRow = [int][Math]::Floor(($s + 31) / 32) * 4
    $zero = New-Object byte[] ($maskRow * $s)
    $bw.Write($zero, 0, $zero.Length)

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return ,$bytes
}

function ConvertTo-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

$entries = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -ge 256) { $blob = ConvertTo-PngBytes $bmp; $kind = 'PNG' }
    else            { $blob = ConvertTo-IconDib $bmp;  $kind = 'DIB' }
    $bmp.Dispose()
    $entries += ,@{ Size = $s; Data = $blob; Kind = $kind }
    Write-Host ("  {0,3}x{0,-3} {1}  {2:N0} 字节" -f $s, $kind, $blob.Length)
}

$outDir = Split-Path -Parent $Out
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
        $bw.Write([byte]$dim)
        $bw.Write([byte]$dim)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$e.Data.Length)
        $bw.Write([UInt32]$offset)
        $offset += $e.Data.Length
    }
    foreach ($e in $entries) { $bw.Write($e.Data) }
}
finally { $bw.Dispose(); $fs.Dispose() }

$fi = Get-Item $Out
Write-Host ("已生成: {0} ({1:N0} 字节, {2} 个尺寸)" -f $fi.FullName, $fi.Length, $entries.Count)
