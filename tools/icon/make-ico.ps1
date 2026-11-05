# Makes src/Orbit.App/Assets/Orbit.ico from Orbit's own app icon (MIT, see Orbit-icon.LICENSE.txt).
# macOS icons keep a wide transparent margin, which at 16 px leaves a speck, so it is cropped away.
#   powershell -File tools/icon/make-ico.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
$source = "$repo\.refs\orbit\Orbit\Resources\Assets.xcassets\AppIcon.appiconset\icon_512x512@2x.png"
$target = "$repo\src\Orbit.App\Assets\Orbit.ico"
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

$original = [Drawing.Bitmap]::FromFile($source)
try {
    # The opaque part's bounds. Sampled along the middle lines: the shape is centered and convex.
    $middleX = [int]($original.Width / 2)
    $middleY = [int]($original.Height / 2)
    $left = 0
    while ($original.GetPixel($left, $middleY).A -lt 128) { $left++ }
    $top = 0
    while ($original.GetPixel($middleX, $top).A -lt 128) { $top++ }
    $side = [Math]::Min($original.Width - 2 * $left, $original.Height - 2 * $top)
    Write-Host "Cropped to $side px of $($original.Width), at $left,$top"

    $images = foreach ($size in $sizes) {
        $bitmap = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.DrawImage($original, (New-Object Drawing.Rectangle 0, 0, $size, $size), $left, $top, $side, $side, [Drawing.GraphicsUnit]::Pixel)
        $graphics.Dispose()
        $stream = New-Object IO.MemoryStream
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    }
} finally {
    $original.Dispose()
}

New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
$file = [IO.File]::Create($target)
$writer = New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        # 0 means 256 in an icon directory.
        $writer.Write([byte]($image.Size % 256))
        $writer.Write([byte]($image.Size % 256))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }
    foreach ($image in $images) { $writer.Write($image.Bytes) }
} finally {
    $writer.Dispose()
}
Copy-Item "$repo\.refs\orbit\LICENSE" "$repo\src\Orbit.App\Assets\Orbit-icon.LICENSE.txt"
Write-Host "Wrote $target ($((Get-Item $target).Length) bytes, $($images.Count) sizes)"
