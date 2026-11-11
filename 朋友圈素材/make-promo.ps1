#requires -Version 5.1
# 生成朋友圈一张图（1080x1440）
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$dir = $PSScriptRoot
$shot = [System.Drawing.Image]::FromFile((Join-Path $dir '窗口.png'))

$W = 1080
$H = 1440
$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

$c1 = [System.Drawing.Color]::FromArgb(255,253,243,249)
$c2 = [System.Drawing.Color]::FromArgb(255,246,228,240)
$p1 = New-Object System.Drawing.Point(0,0)
$p2 = New-Object System.Drawing.Point(0,$H)
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($p1,$p2,$c1,$c2)
$g.FillRectangle($bg, 0, 0, $W, $H)
$bg.Dispose()

$halo = New-Object System.Drawing.Drawing2D.GraphicsPath
$halo.AddEllipse(-200, -520, 1480, 900)
$pbr = New-Object System.Drawing.Drawing2D.PathGradientBrush($halo)
$pbr.CenterColor = [System.Drawing.Color]::FromArgb(70, 250, 150, 190)
$pbr.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 250, 150, 190))
$g.FillPath($pbr, $halo)
$pbr.Dispose()
$halo.Dispose()

$ink    = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,56,34,66))
$ink2   = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,112,82,124))
$white  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,226,62,124))

$fTitle = New-Object System.Drawing.Font('Microsoft YaHei UI', 44, [System.Drawing.FontStyle]::Bold)
$fSub   = New-Object System.Drawing.Font('Microsoft YaHei UI', 21)
$fSmall = New-Object System.Drawing.Font('Microsoft YaHei UI', 16)

$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center

$r1 = New-Object System.Drawing.RectangleF(0,92,$W,80)
$g.DrawString('Co_cofe 系统垃圾清理', $fTitle, $ink, $r1, $sf)
$r2 = New-Object System.Drawing.RectangleF(0,186,$W,50)
$g.DrawString('只清临时文件和回收站 · 不动你的文件', $fSub, $ink2, $r2, $sf)

$sw = 760
$ratio = $sw / $shot.Width
$sh = [int]($shot.Height * $ratio)
$sx = [int](($W - $sw) / 2)
$sy = 250
$cardX = $sx - 6
$cardY = $sy - 6
$cardW = $sw + 12
$cardH = $sh + 12
$g.FillRectangle($white, $cardX, $cardY, $cardW, $cardH)
$g.DrawImage($shot, $sx, $sy, $sw, $sh)

$y = $sy + $sh + 58
$lineW = $W - 240
$points = @(
  '· 不全盘扫 C 盘，只进 临时目录 / Windows\Temp / 回收站',
  '· 不删 .log 日志、.old 备份、prefetch 缓存',
  '· 占用中的文件自动跳过；默认只清 1 天以前的'
)
foreach ($line in $points) {
  $r = New-Object System.Drawing.RectangleF(120,$y,$lineW,34)
  $g.DrawString($line, $fSmall, $ink2, $r)
  $y = $y + 40
}

$hy1 = $H - 108
$hy2 = $H - 62
$rb1 = New-Object System.Drawing.RectangleF(0,$hy1,$W,40)
$rb2 = New-Object System.Drawing.RectangleF(0,$hy2,$W,32)
$g.DrawString('冷咖啡制作 · Co_cofe', $fSub, $accent, $rb1, $sf)
$g.DrawString('与「Co_cofe 建模看图工具」同一套图标与风格', $fSmall, $ink2, $rb2, $sf)

$g.Dispose()
$out = Join-Path $dir '一张图_朋友圈.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$shot.Dispose()
Write-Host "written: $out"