$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Draw a mint monitor and lock on a dark tile at every Windows icon size.
# Supersampling keeps the small Explorer and shortcut icons sharp.
function New-RoundedRectangle([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = 2 * $radius
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return ,$path
}

$ink = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(7, 18, 23))
$mint = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(120, 241, 173))
$outline = [Drawing.Pen]::new($mint, 3)
$outline.StartCap = $outline.EndCap = [Drawing.Drawing2D.LineCap]::Round
$keyhole = [Drawing.Pen]::new($ink, 2)
$keyhole.StartCap = $keyhole.EndCap = [Drawing.Drawing2D.LineCap]::Round
$images = @()
try {
    foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
        $canvas = [Drawing.Bitmap]::new($size * 4, $size * 4)
        $graphics = [Drawing.Graphics]::FromImage($canvas)
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $scaled = [Drawing.Graphics]::FromImage($bitmap)
        $png = [IO.MemoryStream]::new()
        $tile = New-RoundedRectangle 1 1 62 62 14
        $monitor = New-RoundedRectangle 10 13 44 30 4
        $lock = New-RoundedRectangle 26 26 12 11 2
        try {
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.ScaleTransform($canvas.Width / 64.0, $canvas.Height / 64.0)
            $graphics.FillPath($ink, $tile)
            $graphics.DrawPath($outline, $monitor)
            $graphics.DrawLine($outline, 32, 43, 32, 51)
            $graphics.DrawLine($outline, 23, 51, 41, 51)
            $graphics.DrawArc($outline, 28, 19, 8, 8, 180, 180)
            $graphics.DrawLine($outline, 28, 23, 28, 27)
            $graphics.DrawLine($outline, 36, 23, 36, 27)
            $graphics.FillPath($mint, $lock)
            $graphics.DrawLine($keyhole, 32, 30, 32, 33)
            $scaled.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scaled.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scaled.DrawImage($canvas, 0, 0, $size, $size)
            $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
            $images += @{ Size = $size; Data = $png.ToArray() }
        }
        finally {
            $tile.Dispose(); $monitor.Dispose(); $lock.Dispose()
            $png.Dispose(); $scaled.Dispose(); $bitmap.Dispose()
            $graphics.Dispose(); $canvas.Dispose()
        }
    }
}
finally {
    $outline.Dispose(); $keyhole.Dispose(); $ink.Dispose(); $mint.Dispose()
}

$assets = Join-Path $PSScriptRoot '..\src\PrivacyScreen\Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$output = Join-Path $assets 'app.ico'
$stream = [IO.File]::Create($output)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0) # Reserved
    $writer.Write([uint16]1) # ICO
    $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([uint16]0) # Palette count and reserved byte
        $writer.Write([uint16]1) # Planes
        $writer.Write([uint16]32) # Bits per pixel
        $writer.Write([uint32]$image.Data.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Data.Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image.Data) }
}
finally { $writer.Dispose() }
Write-Host "Icon: $([IO.Path]::GetFullPath($output))"
