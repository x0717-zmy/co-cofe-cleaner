#requires -Version 5.1
<#
  Generate two app icons from the Co_cofe logo:
    viewer.ico  = base + magnifier badge (bottom-right)  -> Co_cofe 建模看图工具
    cleaner.ico = base + trash can badge (bottom-right)  -> Co_cofe 系统垃圾清理
  Also writes preview sheets for eyeballing.
#>
param(
    [string]$LogoPath = 'D:\desk\Co_cofe建模看图工具\源码\logo.png',
    [string]$OutDir   = 'C:\Users\lenovo\Documents\deepseek-harness\default-workspace\iconwork'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$Dark     = [System.Drawing.Color]::FromArgb(255, 38, 38, 43)
$White    = [System.Drawing.Color]::White
$BlueAcc  = [System.Drawing.Color]::FromArgb(255, 31, 111, 235)
$GreenAcc = [System.Drawing.Color]::FromArgb(255, 31, 160, 90)

# ---------------------------------------------------------------- base crop
function Get-BaseCrop {
    param([string]$Path)
    $img = [System.Drawing.Image]::FromFile($Path)
    $side = 520
    $cx = [int]($img.Width * 0.515)
    $cy = [int]($img.Height * 0.535)
    $left = [Math]::Max(0, $cx - [int]($side / 2))
    $top  = [Math]::Max(0, $cy - [int]($side / 2))
    if ($left + $side -gt $img.Width)  { $left = $img.Width - $side }
    if ($top  + $side -gt $img.Height) { $top  = $img.Height - $side }

    $bmp = New-Object System.Drawing.Bitmap($side, $side, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle(0, 0, $side, $side)), $left, $top, $side, $side, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $img.Dispose()
    return $bmp
}

function New-RoundedPath {
    param([single]$x, [single]$y, [single]$w, [single]$h, [single]$r)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single]($r * 2)
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Pen {
    param([System.Drawing.Color]$c, [single]$deviceWidth, [single]$s)
    $w = [single]([Math]::Max(1.0, $deviceWidth * $s) / $s)
    $pen = New-Object System.Drawing.Pen($c, $w)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

# ---------------------------------------------------------------- badge
function Draw-Badge {
    param([System.Drawing.Graphics]$g, [single]$s, [string]$Kind, [System.Drawing.Color]$Accent)

    $detail = ($s * 256) -ge 32
    $cx = 194; $cy = 194; $r = 48

    # soft shadow
    $sh = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(28, 0, 0, 0))
    $g.FillEllipse($sh, $cx - $r + 1.5, $cy - $r + 2.5, 2 * $r, 2 * $r)
    $sh.Dispose()

    # white disc
    $wb = New-Object System.Drawing.SolidBrush($White)
    $g.FillEllipse($wb, $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $wb.Dispose()

    # accent ring
    $ringPen = New-Pen -c $Accent -deviceWidth 5.0 -s $s
    $rr = $r - 2.5
    $g.DrawEllipse($ringPen, $cx - $rr, $cy - $rr, 2 * $rr, 2 * $rr)
    $ringPen.Dispose()

    # hairline outline so the white disc survives on white backgrounds
    $hair = New-Pen -c ([System.Drawing.Color]::FromArgb(40, 0, 0, 0)) -deviceWidth 1.2 -s $s
    $g.DrawEllipse($hair, $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $hair.Dispose()

    $ink = New-Object System.Drawing.SolidBrush($Dark)

    if ($Kind -eq 'viewer') {
        # magnifier: lens + handle
        $lx = 187.0; $ly = 185.0; $lr = 17.5
        $lensPen = New-Pen -c $Dark -deviceWidth 7.0 -s $s
        $g.DrawEllipse($lensPen, $lx - $lr, $ly - $lr, 2 * $lr, 2 * $lr)
        $lensPen.Dispose()

        $handlePen = New-Pen -c $Dark -deviceWidth 9.0 -s $s
        $g.DrawLine($handlePen, [single]($lx + $lr * 0.72), [single]($ly + $lr * 0.72), [single]211, [single]209)
        $handlePen.Dispose()
    }
    else {
        # trash can: knob + lid + body
        $knob = New-RoundedPath -x 185 -y 172 -w 24 -h 9 -r 3
        $g.FillPath($ink, $knob); $knob.Dispose()

        $lid = New-RoundedPath -x 169 -y 179 -w 46 -h 8 -r 3
        $g.FillPath($ink, $lid); $lid.Dispose()

        $body = New-Object System.Drawing.Drawing2D.GraphicsPath
        $body.AddPolygon([System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(172, 191)),
            (New-Object System.Drawing.PointF(212, 191)),
            (New-Object System.Drawing.PointF(205, 215)),
            (New-Object System.Drawing.PointF(179, 215))))
        $g.FillPath($ink, $body); $body.Dispose()

        if ($detail) {
            $slotPen = New-Pen -c $White -deviceWidth 3.2 -s $s
            $g.DrawLine($slotPen, [single]186, [single]197, [single]186, [single]208)
            $g.DrawLine($slotPen, [single]198, [single]197, [single]198, [single]208)
            $slotPen.Dispose()
        }
    }

    $ink.Dispose()
}

# ---------------------------------------------------------------- render one size
function Render-Icon {
    param([System.Drawing.Bitmap]$Base, [int]$Size, [string]$Kind, [System.Drawing.Color]$Accent)
    $s = [single]($Size / 256.0)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.ScaleTransform($s, $s)

    $g.DrawImage($Base, [single]0, [single]0, [single]256, [single]256)
    Draw-Badge -g $g -s $s -Kind $Kind -Accent $Accent

    $g.Dispose()
    return $bmp
}

# ---------------------------------------------------------------- ico writer
function Get-PngBytes {
    param([System.Drawing.Bitmap]$Bmp)
    $ms = New-Object System.IO.MemoryStream
    $Bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b = $ms.ToArray()
    $ms.Dispose()
    return $b
}

function Get-DibBytes {
    param([System.Drawing.Bitmap]$Bmp)
    $w = $Bmp.Width; $h = $Bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    # BITMAPINFOHEADER: height doubled (XOR + AND mask)
    $bw.Write([int]40)
    $bw.Write([int]$w)
    $bw.Write([int]($h * 2))
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int]0)
    $bw.Write([int]($w * $h * 4))
    $bw.Write([int]0)
    $bw.Write([int]0)
    $bw.Write([int]0)
    $bw.Write([int]0)

    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $Bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B)
            $bw.Write([byte]$c.G)
            $bw.Write([byte]$c.R)
            $bw.Write([byte]$c.A)
        }
    }

    $rowBytes = [int]([Math]::Floor(($w + 31) / 32) * 4)
    $zero = New-Object byte[] $rowBytes
    for ($y = 0; $y -lt $h; $y++) { $bw.Write($zero) }

    $bw.Flush()
    $b = $ms.ToArray()
    $bw.Dispose()
    $ms.Dispose()
    return $b
}

function Write-Ico {
    param([string]$Path, [hashtable]$Entries)  # size -> byte[] payload

    $sizes = $Entries.Keys | Sort-Object -Descending
    $out = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($out)

    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    $blobs = @()
    foreach ($sz in $sizes) {
        $data = $Entries[$sz]
        $wb = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
        $hb = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
        $bw.Write($wb)
        $bw.Write($hb)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$data.Length)
        $bw.Write([uint32]$offset)
        $offset += $data.Length
        $blobs += , $data
    }
    foreach ($b in $blobs) { $bw.Write([byte[]]$b) }
    $bw.Flush()

    [System.IO.File]::WriteAllBytes($Path, $out.ToArray())
    $bw.Dispose()
    $out.Dispose()
    Write-Host ("icon written: {0} ({1} bytes, sizes: {2})" -f $Path, (Get-Item -LiteralPath $Path).Length, ($sizes -join ','))
}

# ---------------------------------------------------------------- preview sheet
function Write-Preview {
    param([System.Drawing.Bitmap]$Base, [string]$Kind, [System.Drawing.Color]$Accent, [string]$Path)

    $sizesFor = @(256, 64, 48, 32, 24, 16)
    $w = 700; $h = 560
    $sheet = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::White)
    $darkBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 32, 32, 36))
    $g.FillRectangle($darkBrush, 0, 280, $w, 280)
    $darkBrush.Dispose()

    foreach ($row in 0, 1) {
        $y0 = if ($row -eq 0) { 12 } else { 292 }
        $x = 12
        foreach ($sz in $sizesFor) {
            $bmp = Render-Icon -Base $Base -Size $sz -Kind $Kind -Accent $Accent
            $y = $y0 + (([Math]::Max(16, 256)) - $sz) / 2
            if ($sz -eq 256) { $y = $y0 }
            $g.DrawImage($bmp, [int]$x, [int]$y, $sz, $sz)
            if ($sz -eq 16) {
                $g.DrawImage($bmp, [int]$x, 480 + $row * 0, 16, 16)
            }
            $bmp.Dispose()
            $x += $sz + 12
        }
    }

    $g.Dispose()
    $sheet.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
    Write-Host "preview written: $Path"
}

# ---------------------------------------------------------------- main
$base = Get-BaseCrop -Path $LogoPath

$sizes = @(256, 128, 64, 48, 32, 24, 16)
foreach ($spec in @(
        @{ Kind = 'viewer';  Accent = $BlueAcc;  Ico = 'viewer.ico';  Png = 'preview-viewer.png' },
        @{ Kind = 'cleaner'; Accent = $GreenAcc; Ico = 'cleaner.ico'; Png = 'preview-cleaner.png' })) {

    $entries = @{}
    foreach ($sz in $sizes) {
        $bmp = Render-Icon -Base $base -Size $sz -Kind $spec.Kind -Accent $spec.Accent
        # 与现有 app.ico 一致：所有尺寸都用 PNG 压缩条目（Win11 已验证可用）
        $entries[$sz] = Get-PngBytes -Bmp $bmp
        if ($sz -eq 256) { $bmp.Save((Join-Path $OutDir ($spec.Kind + '-256.png')), [System.Drawing.Imaging.ImageFormat]::Png) }
        $bmp.Dispose()
    }
    Write-Ico -Path (Join-Path $OutDir $spec.Ico) -Entries $entries
    Write-Preview -Base $base -Kind $spec.Kind -Accent $spec.Accent -Path (Join-Path $OutDir $spec.Png)
}

$base.Save((Join-Path $OutDir 'base-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$base.Dispose()
Write-Host "done -> $OutDir"
