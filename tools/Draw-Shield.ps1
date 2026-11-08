# Redraws the package images in src/BitwardenCommandPalette.Extension/Assets:
#   tools/Draw-Shield.ps1 -OutDir src/BitwardenCommandPalette.Extension/Assets

param([Parameter(Mandatory)] [string] $OutDir)

# Draws the extension's shield (an outline shield, like the Segoe Fluent glyph it matches)
# into every package image. Our own geometry: no font glyph or logo is copied.
Add-Type -AssemblyName System.Drawing

$colour = [System.Drawing.Color]::FromArgb(255, 0x3B, 0x82, 0xF6)

function Draw-Shield([System.Drawing.Graphics] $g, [float] $cx, [float] $cy, [float] $size) {
    # Shield in a unit box, centred on (cx, cy).
    $p = { param($x, $y) [System.Drawing.PointF]::new($cx + ($x - 0.5) * $size, $cy + ($y - 0.5) * $size) }
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddBezier((& $p 0.5 0.06), (& $p 0.63 0.15), (& $p 0.76 0.19), (& $p 0.88 0.2))
    $path.AddLine((& $p 0.88 0.2), (& $p 0.88 0.47))
    $path.AddBezier((& $p 0.88 0.47), (& $p 0.88 0.72), (& $p 0.72 0.86), (& $p 0.5 0.95))
    $path.AddBezier((& $p 0.5 0.95), (& $p 0.28 0.86), (& $p 0.12 0.72), (& $p 0.12 0.47))
    $path.AddLine((& $p 0.12 0.47), (& $p 0.12 0.2))
    $path.AddBezier((& $p 0.12 0.2), (& $p 0.24 0.19), (& $p 0.37 0.15), (& $p 0.5 0.06))
    $path.CloseFigure()

    $width = [Math]::Max(1.25, $size * 0.085)
    $pen = [System.Drawing.Pen]::new($colour, $width)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($pen, $path)
    $pen.Dispose()
    $path.Dispose()
}

function Save-Image([string] $name, [int] $w, [int] $h, [float] $fraction) {
    $bitmap = [System.Drawing.Bitmap]::new($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    Draw-Shield $g ($w / 2) ($h / 2) ([Math]::Min($w, $h) * $fraction)
    $g.Dispose()
    $bitmap.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

Save-Image 'StoreLogo.png' 50 50 0.9
Save-Image 'LockScreenLogo.scale-200.png' 48 48 0.9
Save-Image 'Square44x44Logo.scale-200.png' 88 88 0.85
Save-Image 'Square150x150Logo.scale-200.png' 300 300 0.6
Save-Image 'Wide310x150Logo.scale-200.png' 620 300 0.6
Save-Image 'SplashScreen.scale-200.png' 1240 600 0.5
foreach ($size in 16, 24, 32, 48, 256) {
    Save-Image "Square44x44Logo.targetsize-$($size)_altform-unplated.png" $size $size 0.92
}
