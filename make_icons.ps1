# Draws the "Phone Overhaul" app icon for the ShopOS tablet into .\assets\tablet_icon.png (256x256, transparent,
# ShopOS draws the plate behind it); build.ps1 embeds it in PhoneBridge.dll.
# Drawn at 1024 px and scaled down, so the edges stay smooth.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Force $out | Out-Null

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $d = 2 * $r
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}
function C($r, $g, $b, $a = 255) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
function Grad($x, $y, $w, $h, $c1, $c2, $angle) {
    New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.RectangleF $x, $y, $w, $h), $c1, $c2, [float]$angle)
}

$S = 1024
$big = New-Object System.Drawing.Bitmap $S, $S
$g = [System.Drawing.Graphics]::FromImage($big)
$g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'

# tablet body (landscape)
$bx = 104; $by = 228; $bw = 816; $bh = 568; $br = 78
# soft shadow: a few widening, fading layers
foreach ($i in 6..1) {
    $g.FillPath((New-Object System.Drawing.SolidBrush (C 0 0 0 (14 + (6 - $i) * 4))), (RoundRect ($bx - $i * 5) ($by + 26 - $i * 3) ($bw + $i * 10) ($bh + $i * 8) ($br + $i * 5)))
}
$g.FillPath((Grad $bx $by $bw $bh (C 78 86 98) (C 20 23 29) 90), (RoundRect $bx $by $bw $bh $br))
# metal rim: light on top, dark at the bottom
$rim = New-Object System.Drawing.Pen ((Grad $bx $by $bw $bh (C 190 198 210 220) (C 40 44 52 200) 90)), 7
$g.DrawPath($rim, (RoundRect ($bx + 4) ($by + 4) ($bw - 8) ($bh - 8) ($br - 4)))
# inner black bezel
$g.FillPath((New-Object System.Drawing.SolidBrush (C 10 12 16)), (RoundRect ($bx + 22) ($by + 22) ($bw - 44) ($bh - 44) ($br - 20)))

# screen
$sx = $bx + 44; $sy = $by + 44; $sw = $bw - 88; $sh = $bh - 88
$screen = RoundRect $sx $sy $sw $sh 34
$g.FillPath((Grad $sx $sy $sw $sh (C 22 34 72) (C 24 104 108) 35), $screen)
# warm glow in the top right corner of the screen (PTCGO orange)
$g.SetClip($screen)
$glow = New-Object System.Drawing.Drawing2D.GraphicsPath
$glow.AddEllipse(($sx + $sw - 300), ($sy - 220), 520, 440)
$pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush($glow)
$pgb.CenterColor = C 245 150 50 110; $pgb.SurroundColors = @((C 245 150 50 0))
$g.FillPath($pgb, $glow)

# app tiles, 3 x 2
$cols = @(
    @((C 255 196 80), (C 240 140 30)),   # orange
    @((C 255 224 110), (C 236 176 40)),  # gold
    @((C 110 220 240), (C 40 160 205)),  # cyan
    @((C 110 220 240), (C 40 160 205)),
    @((C 255 196 80), (C 240 140 30)),
    @((C 255 224 110), (C 236 176 40))
)
$t = 148; $gap = 42
$tx0 = $sx + ($sw - (3 * $t + 2 * $gap)) / 2
$ty0 = $sy + ($sh - (2 * $t + $gap)) / 2
for ($i = 0; $i -lt 6; $i++) {
    $x = $tx0 + ($i % 3) * ($t + $gap); $y = $ty0 + [math]::Floor($i / 3) * ($t + $gap)
    $g.FillPath((New-Object System.Drawing.SolidBrush (C 0 0 0 70)), (RoundRect ($x + 4) ($y + 10) $t $t 36))
    $g.FillPath((Grad $x $y $t $t $cols[$i][0] $cols[$i][1] 90), (RoundRect $x $y $t $t 36))
    # small highlight on the upper half of each tile
    $g.FillPath((New-Object System.Drawing.SolidBrush (C 255 255 255 45)), (RoundRect ($x + 10) ($y + 8) ($t - 20) ($t * 0.42) 26))
}

# diagonal glass reflection
$gl = New-Object System.Drawing.Drawing2D.GraphicsPath
$gl.AddPolygon(@(
    (New-Object System.Drawing.PointF ($sx - 40), ($sy + $sh)),
    (New-Object System.Drawing.PointF ($sx + 260), ($sy - 40)),
    (New-Object System.Drawing.PointF ($sx + 420), ($sy - 40)),
    (New-Object System.Drawing.PointF ($sx + 120), ($sy + $sh))))
$g.FillPath((New-Object System.Drawing.SolidBrush (C 255 255 255 18)), $gl)
$g.ResetClip()

# front camera on the top bezel
$g.FillEllipse((New-Object System.Drawing.SolidBrush (C 40 46 56)), 502, ($by + 24), 20, 20)
$g.FillEllipse((New-Object System.Drawing.SolidBrush (C 70 120 170 160)), 508, ($by + 30), 8, 8)
$g.Dispose()

# scale down to 256
$small = New-Object System.Drawing.Bitmap 256, 256
$g2 = [System.Drawing.Graphics]::FromImage($small)
$g2.InterpolationMode = 'HighQualityBicubic'; $g2.PixelOffsetMode = 'HighQuality'; $g2.SmoothingMode = 'AntiAlias'
$g2.DrawImage($big, 0, 0, 256, 256)
$g2.Dispose(); $big.Dispose()
$small.Save((Join-Path $out 'tablet_icon.png'), [System.Drawing.Imaging.ImageFormat]::Png); $small.Dispose()
