# Draws the app icon (a roll-quality bar on charcoal, "SSE" at larger sizes) and writes a
# multi-size .ico: 32-bit DIB entries for 16-128 px, PNG for 256 px.
# Usage: ./scripts/make-icon.ps1   → src/StillSaneExile.Desktop/Assets/sse.ico
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase, PresentationFramework

$root = Split-Path -Parent $PSScriptRoot
$outFile = Join-Path $root 'src/StillSaneExile.Desktop/Assets/sse.ico'
New-Item -ItemType Directory -Force (Split-Path $outFile) | Out-Null

function Brush([string]$hex) {
    $c = [System.Windows.Media.ColorConverter]::ConvertFromString($hex)
    $b = New-Object System.Windows.Media.SolidColorBrush $c
    $b.Freeze()
    return $b
}

$bg = Brush '#1A1B1E'; $amber = Brush '#D9A84E'; $track = Brush '#3A3C41'; $white = Brush '#F2ECE2'

function Render([int]$s) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $pen = [System.Windows.Media.Pen]::new($amber, [Math]::Max(1.0, [Math]::Round($s * 0.05)))
    $inset = $pen.Thickness / 2 + [Math]::Max(0, [Math]::Round($s * 0.02))
    $radius = $s * 0.2
    $dc.DrawRoundedRectangle($bg, $pen, ([System.Windows.Rect]::new($inset, $inset, $s - 2 * $inset, $s - 2 * $inset)), $radius, $radius)

    if ($s -ge 48) {
        $tf = [System.Windows.Media.Typeface]::new([System.Windows.Media.FontFamily]::new('Segoe UI'), [System.Windows.FontStyles]::Normal, [System.Windows.FontWeights]::Bold, [System.Windows.FontStretches]::Normal)
        $ft = [System.Windows.Media.FormattedText]::new('SSE', [Globalization.CultureInfo]::InvariantCulture, [System.Windows.FlowDirection]::LeftToRight, $tf, ($s * 0.30), $white, 1.0)
        $dc.DrawText($ft, ([System.Windows.Point]::new((($s - $ft.Width) / 2), ($s * 0.17))))
        $barTop = [Math]::Round($s * 0.62); $barH = [Math]::Max(2, [Math]::Round($s * 0.11))
    } else {
        $barTop = [Math]::Round($s * 0.40); $barH = [Math]::Max(2, [Math]::Round($s * 0.20))
    }

    $x0 = [Math]::Round($s * 0.2); $x1 = [Math]::Round($s * 0.76); $fill = [Math]::Round($s * 0.56)
    $tickW = [Math]::Max(1, [Math]::Round($s * 0.06)); $tickExtra = [Math]::Max(1, [Math]::Round($s * 0.05))
    $dc.DrawRectangle($track, $null, ([System.Windows.Rect]::new($x0, $barTop, ($x1 - $x0), $barH)))
    $dc.DrawRectangle($amber, $null, ([System.Windows.Rect]::new($x0, $barTop, ($fill - $x0), $barH)))
    $dc.DrawRectangle($white, $null, ([System.Windows.Rect]::new($x1, ($barTop - $tickExtra), $tickW, ($barH + 2 * $tickExtra))))
    $dc.Close()

    $rtb = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($s, $s, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    return $rtb
}

function DibBytes($bmp, [int]$s) {
    $bgra = [System.Windows.Media.Imaging.FormatConvertedBitmap]::new($bmp, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
    $stride = $s * 4
    $pixels = New-Object byte[] ($stride * $s)
    $bgra.CopyPixels($pixels, $stride, 0)
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    # BITMAPINFOHEADER: height is doubled (XOR image + AND mask)
    $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($stride * $s)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $stride, $stride) }   # bottom-up rows
    $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskStride * $s)))                                   # AND mask: alpha does the work
    $w.Flush()
    return , $ms.ToArray()
}

function PngBytes($bmp) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = Render $s
    , ($(if ($s -eq 256) { PngBytes $bmp } else { DibBytes $bmp $s }))
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $dim = [byte]$(if ($s -ge 256) { 0 } else { $s })
    $w.Write($dim); $w.Write($dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$len); $w.Write([int]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes($outFile, $out.ToArray())

# Preview PNG for the README.
[System.IO.File]::WriteAllBytes((Join-Path $root 'docs/screenshots/icon.png'), (PngBytes (Render 256)))
Write-Host "Wrote $outFile ($($out.Length) bytes)"
