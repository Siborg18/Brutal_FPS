# Paints the jungle shack's whiteboard (an old, dirty kanban board in worn red tape, with sticky notes, all handwritten
# in Spanish) into Assets/Textures: whiteboard_albedo/_normal/_orm.png, drawn on a 1.6 x 1.2 m board by
# Level.BuildBodegaWhiteboard.
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools\make_whiteboard.ps1
# (Accented letters are [char] codes: Windows PowerShell 5.1 misreads them in a file with no byte order mark.)
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\Assets\Textures'
$w = 2048
$h = 1536
$rnd = New-Object System.Random(1977)

function Rand([double]$a, [double]$b) { $a + ($b - $a) * $rnd.NextDouble() }
function Argb($a, $r, $gr, $b) { [System.Drawing.Color]::FromArgb([int]$a, [int]$r, [int]$gr, [int]$b) }

function Fill-Ellipse($g, $color, $x, $y, $ew, $eh) {
    $brush = New-Object System.Drawing.SolidBrush($color)
    $g.FillEllipse($brush, [float]$x, [float]$y, [float]$ew, [float]$eh)
    $brush.Dispose()
}

# A stain with no hard edge: strongest in the middle, fading to nothing.
function Soft-Blob($g, $color, $x, $y, $ew, $eh) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse([float]$x, [float]$y, [float]$ew, [float]$eh)
    $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush($path)
    $brush.CenterColor = $color
    $brush.SurroundColors = [System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(0, $color.R, $color.G, $color.B))
    $g.FillPath($brush, $path)
    $brush.Dispose()
    $path.Dispose()
}

$typographic = [System.Drawing.StringFormat]::GenericTypographic.Clone()
$typographic.FormatFlags = $typographic.FormatFlags -bor [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces

function Line-Width($g, [string]$line, $font) {
    $total = 0.0
    foreach ($c in $line.ToCharArray()) { $total += $g.MeasureString([string]$c, $font, (New-Object System.Drawing.PointF(0, 0)), $typographic).Width }
    $total
}

# Handwriting: a letter at a time, each a little off the line, turned a little, some pressed harder than others; the
# lines run uphill or down and don't quite centre.
function Draw-Hand($g, [string]$text, $font, $r, $gr, $b, [double]$alpha, [double]$cx, [double]$cy, [double]$wobble) {
    $lines = $text -split "`n"
    $lineHeight = $font.GetHeight($g) * 1.05
    $y = $cy - $lines.Count * $lineHeight / 2
    foreach ($line in $lines) {
        $width = Line-Width $g $line $font
        $x = $cx - $width / 2 + (Rand -9 9)
        $slope = Rand -0.03 0.03
        $startX = $x
        foreach ($c in $line.ToCharArray()) {
            $s = [string]$c
            $state = $g.Save()
            $g.TranslateTransform([float]$x, [float]($y + $slope * ($x - $startX) + (Rand (-$wobble) $wobble)))
            $g.RotateTransform([float](Rand (-$wobble * 1.3) ($wobble * 1.3)))
            $brush = New-Object System.Drawing.SolidBrush((Argb ($alpha * (Rand 0.72 1.0)) $r $gr $b))
            $g.DrawString($s, $font, $brush, 0, 0, $typographic)
            $brush.Dispose()
            $g.Restore($state)
            $x += $g.MeasureString($s, $font, (New-Object System.Drawing.PointF(0, 0)), $typographic).Width * (Rand 0.97 1.05)
        }
        $y += $lineHeight
    }
}

$bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAlias'
# Yellowed with age.
$g.Clear((Argb 255 226 222 204))

# Years of marker that never quite wiped off: smears and the rubbed-out swipes of an eraser.
for ($i = 0; $i -lt 150; $i++) {
    Soft-Blob $g (Argb (Rand 8 20) 70 72 78) (Rand -150 $w) (Rand -80 $h) (Rand 140 720) (Rand 40 190)
}
for ($i = 0; $i -lt 45; $i++) {
    $state = $g.Save()
    $g.TranslateTransform([float](Rand 0 $w), [float](Rand 0 $h))
    $g.RotateTransform([float](Rand -12 12))
    Soft-Blob $g (Argb (Rand 12 26) 248 244 228) -300 -30 (Rand 300 700) (Rand 30 80)
    $g.Restore($state)
}

# Ghosts of old writing, rubbed out but still there.
$ghostFont = New-Object System.Drawing.Font('Ink Free', 74, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$q = [char]0xBF
$a = [char]0xE1
foreach ($ghost in @(
    @("turno de noche", 340, 760), @("${q}qui$([char]0xE9)n fue?", 1030, 720), @("no tocar", 1720, 500), @("lunes 6:00", 1050, 1420),
    @("revisar anillo", 330, 1430), @("x4 bidones", 1730, 1340), @("llamar a Ram$([char]0xF3)n", 1040, 600), @("ma$([char]0xF1)ana", 300, 280)
)) {
    $state = $g.Save()
    $g.TranslateTransform([float]$ghost[1], [float]$ghost[2])
    $g.RotateTransform([float](Rand -5 5))
    Draw-Hand $g $ghost[0] $ghostFont 45 50 90 (Rand 16 30) 0 0 1.5
    $g.Restore($state)
}

# The damp: brown creeping in from the edges, drips from the top rail, mildew in the corners.
$edge = New-Object System.Drawing.Drawing2D.GraphicsPath
$edge.AddRectangle((New-Object System.Drawing.Rectangle(0, 0, $w, $h)))
$vignette = New-Object System.Drawing.Drawing2D.PathGradientBrush($edge)
$vignette.CenterColor = Argb 0 92 70 40
$vignette.SurroundColors = [System.Drawing.Color[]]@((Argb 70 92 70 40))
$vignette.FocusScales = New-Object System.Drawing.PointF(0.72, 0.7)
$g.FillRectangle($vignette, 0, 0, $w, $h)
for ($i = 0; $i -lt 34; $i++) {
    $x = Rand 0 $w
    $len = Rand 60 520
    $streak = New-Object System.Drawing.Pen((Argb (Rand 8 24) 84 58 28), [float](Rand 2 9))
    $g.DrawLine($streak, [float]$x, 0, [float]($x + (Rand -7 7)), [float]$len)
    $streak.Dispose()
}
foreach ($corner in @(@(40, 1500, 300), @(2000, 1490, 340), @(30, 50, 170), @(1320, 1525, 200))) {
    for ($i = 0; $i -lt 380; $i++) {
        $spread = $corner[2] * [math]::Pow($rnd.NextDouble(), 1.8)
        $angle = Rand 0 ([math]::PI * 2)
        $size = Rand 1.5 8
        Fill-Ellipse $g (Argb (Rand 25 100) 38 46 32) ($corner[0] + [math]::Cos($angle) * $spread) ($corner[1] + [math]::Sin($angle) * $spread) $size $size
    }
}

# The tape: faded, its edges uneven, laid by hand a little off square and scuffed bare in places.
function Draw-Tape($g, [double]$x0, [double]$y0, [double]$x1, [double]$y1, [double]$width) {
    $dx = $x1 - $x0
    $dy = $y1 - $y0
    $len = [math]::Sqrt($dx * $dx + $dy * $dy)
    $nx = -$dy / $len * $width / 2
    $ny = $dx / $len * $width / 2
    $one = New-Object System.Collections.ArrayList
    $two = New-Object System.Collections.ArrayList
    for ($t = 0.0; $t -le 1.0001; $t += 40.0 / $len) {
        $x = $x0 + $dx * $t
        $y = $y0 + $dy * $t
        $j1 = Rand -2.5 2.5
        $j2 = Rand -2.5 2.5
        [void]$one.Add((New-Object System.Drawing.PointF([float]($x + $nx * (1 + $j1 / 20)), [float]($y + $ny * (1 + $j1 / 20)))))
        [void]$two.Insert(0, (New-Object System.Drawing.PointF([float]($x - $nx * (1 + $j2 / 20)), [float]($y - $ny * (1 + $j2 / 20)))))
    }
    $points = [System.Drawing.PointF[]]($one + $two)
    $brush = New-Object System.Drawing.SolidBrush((Argb 255 108 44 42))
    $g.FillPolygon($brush, $points)
    $brush.Dispose()
    for ($i = 0; $i -lt ($len / 22); $i++) {
        $t = $rnd.NextDouble()
        $x = $x0 + $dx * $t
        $y = $y0 + $dy * $t
        $off = Rand -0.9 0.9
        $worn = New-Object System.Drawing.Pen((Argb (Rand 25 95) 200 176 150), [float](Rand 1 4))
        $g.DrawLine($worn, [float]($x + $nx * $off), [float]($y + $ny * $off), [float]($x + $dx / $len * (Rand 6 44) + $nx * $off), [float]($y + $dy / $len * (Rand 6 44) + $ny * $off))
        $worn.Dispose()
    }
}
Draw-Tape $g -5 202 2053 214 38
Draw-Tape $g 684 -5 672 1541 38
Draw-Tape $g 1378 -5 1390 1541 38
Draw-Tape $g 700 866 1372 856 32

# The headings, in marker going dry.
$marker = New-Object System.Drawing.Font('Segoe Print', 86, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$markerSmall = New-Object System.Drawing.Font('Segoe Print', 58, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
Draw-Hand $g 'en curso' $marker 22 26 52 225 340 110 2.2
Draw-Hand $g 'pendiente' $marker 22 26 52 215 1030 108 2.2
Draw-Hand $g 'hecho' $marker 22 26 52 205 1715 112 2.2
Draw-Hand $g 'urgente' $markerSmall 120 20 20 225 1030 282 2
Draw-Hand $g 'a largo plazo' $markerSmall 22 26 52 200 1030 938 2

# The sticky notes, sun-faded, stained and curling, in three people's hands.
$e = [char]0xE9
$open = [char]0xA1
$yellow = @(232, 200, 72)
$orange = @(226, 150, 78)
$green = @(150, 190, 82)
$pink = @(224, 132, 152)
$purple = @(176, 142, 196)
$notes = @(
    @(70, 300, -3, $yellow, "Recalibrar anillo`noliva, se sigue`ndesviando", 'Ink Free'),
    @(345, 590, 2, $green, "Pagar a los`nguardias`nVIERNES", 'Bradley Hand ITC'),
    @(55, 880, 3, $pink, "Arreglar cable del`ngenerador antes`nde que llueva", 'Ink Free'),
    @(350, 1170, -2, $purple, "Copia de`nportal --list", 'Segoe Print'),
    @(760, 345, 2, $yellow, "Leer registros`ndel rel$e Tor`n(o lo que sea)", 'Segoe Print'),
    @(1070, 480, -3, $orange, "${open}${open}CANDADO`nNUEVO para`nla puerta!!", 'Bradley Hand ITC'),
    @(765, 1010, -2, $pink, "Averiguar qui${e}n`nmueve el enlace`nde la central", 'Ink Free'),
    @(1060, 1200, 3, $green, "Conseguir un`nescritorio`nde verdad", 'Bradley Hand ITC'),
    @(1560, 300, 3, $orange, "OLV-2132`nsello del anillo", 'Segoe Print'),
    @(1600, 660, -2, $orange, "OLV-1267`nenlace a lima", 'Segoe Print'),
    @(1540, 1020, 2, $yellow, "203.0.113.57`nya funciona", 'Ink Free')
)
$size = 290
foreach ($n in $notes) {
    $g.ResetTransform()
    $g.TranslateTransform([float]($n[0] + $size / 2), [float]($n[1] + $size / 2))
    $g.RotateTransform([float]$n[2])
    $g.TranslateTransform(-$size / 2, -$size / 2)
    # Faded towards the board's colour, each by its own amount.
    $fade = Rand 0.14 0.34
    $c = $n[3] | ForEach-Object { $_ }
    $r0 = $c[0] * (1 - $fade) + 214 * $fade
    $g0 = $c[1] * (1 - $fade) + 208 * $fade
    $b0 = $c[2] * (1 - $fade) + 186 * $fade
    $shadow = New-Object System.Drawing.SolidBrush((Argb 80 30 26 18))
    $g.FillRectangle($shadow, 5, 9, $size, $size)
    $paper = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
        (Argb 255 ($r0 * 0.82) ($g0 * 0.82) ($b0 * 0.82)), (Argb 255 $r0 $g0 $b0), 90)
    $g.FillRectangle($paper, 0, 0, $size, $size)
    # Dirt on the paper, kept to the paper.
    $g.SetClip((New-Object System.Drawing.Rectangle(0, 0, $size, $size)))
    for ($i = 0; $i -lt 14; $i++) {
        Soft-Blob $g (Argb (Rand 10 34) 96 70 34) (Rand -60 $size) (Rand -60 $size) (Rand 60 220) (Rand 40 170)
    }
    for ($i = 0; $i -lt 60; $i++) {
        $d = Rand 1 4
        Fill-Ellipse $g (Argb (Rand 40 120) 60 44 24) (Rand 0 $size) (Rand 0 $size) $d $d
    }
    $crease = New-Object System.Drawing.Pen((Argb 30 60 44 24), 2)
    $g.DrawLine($crease, 0, [float](Rand 30 80), $size, [float](Rand 30 80))
    $crease.Dispose()
    $g.ResetClip()
    # The bottom corner curling off the board, its pale back showing.
    $curl = Rand 26 62
    $hole = New-Object System.Drawing.SolidBrush((Argb 255 196 190 168))
    $g.FillPolygon($hole, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF($size, ($size - $curl))), (New-Object System.Drawing.PointF($size, $size)), (New-Object System.Drawing.PointF(($size - $curl), $size))))
    $back = New-Object System.Drawing.SolidBrush((Argb 255 ($r0 * 1.05) ($g0 * 1.05) ($b0 * 1.05)))
    $g.FillPolygon($back, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF($size, ($size - $curl))), (New-Object System.Drawing.PointF(($size - $curl * 0.92), ($size - $curl * 0.92))), (New-Object System.Drawing.PointF(($size - $curl), $size))))
    $font = New-Object System.Drawing.Font($n[5], 46, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $lines = $n[4] -split "`n"
    while ((($lines | ForEach-Object { Line-Width $g $_ $font } | Measure-Object -Maximum).Maximum -gt ($size - 40)) -or ($lines.Count * $font.GetHeight($g) * 1.05 -gt ($size - 44))) {
        $font = New-Object System.Drawing.Font($n[5], ($font.Size - 2), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    }
    Draw-Hand $g $n[4] $font 26 26 48 240 ($size / 2) ($size / 2) 1.8
    $paper.Dispose()
    $shadow.Dispose()
    $hole.Dispose()
    $back.Dispose()
}
$g.ResetTransform()

# Over everything: a coffee ring, scratches, grime and fly specks.
$ring = New-Object System.Drawing.Pen((Argb 62 92 56 22), 9)
$g.DrawEllipse($ring, 1150, 930, 190, 182)
$ring.Dispose()
for ($i = 0; $i -lt 45; $i++) {
    $x = Rand 0 $w
    $y = Rand 0 $h
    $scratch = New-Object System.Drawing.Pen((Argb (Rand 18 60) 240 234 214), [float](Rand 1 2.5))
    $g.DrawLine($scratch, [float]$x, [float]$y, [float]($x + (Rand -240 240)), [float]($y + (Rand -60 60)))
    $scratch.Dispose()
}
for ($i = 0; $i -lt 70; $i++) {
    Soft-Blob $g (Argb (Rand 6 16) 80 62 36) (Rand -100 $w) (Rand -100 $h) (Rand 90 420) (Rand 60 260)
}
for ($i = 0; $i -lt 800; $i++) {
    $d = Rand 1 3.6
    Fill-Ellipse $g (Argb (Rand 40 150) 34 28 20) (Rand 0 $w) (Rand 0 $h) $d $d
}

$g.Dispose()
$bmp.Save((Join-Path $out 'whiteboard_albedo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# A flat surface: straight-up normals; full occlusion, gone dull with age, no metal.
function Save-Flat($name, $r, $green2, $b) {
    $flat = New-Object System.Drawing.Bitmap(4, 4, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $f = [System.Drawing.Graphics]::FromImage($flat)
    $f.Clear([System.Drawing.Color]::FromArgb(255, $r, $green2, $b))
    $f.Dispose()
    $flat.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $flat.Dispose()
}
Save-Flat 'whiteboard_normal.png' 128 128 255
Save-Flat 'whiteboard_orm.png' 255 165 0
