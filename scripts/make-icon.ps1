# Builds the app icon from src/StillSaneExile.Desktop/Assets/sse.svg ("Clear quartz" from the
# "UI & overlay" design) and writes a multi-size .ico: 32-bit DIB entries for 16-128 px, PNG for 256 px.
# The SVG uses gradients and blur filters that WPF cannot draw, so each size is rasterized by a local
# headless Chrome or Edge (set SSE_BROWSER to a chrome.exe or msedge.exe path to choose one).
# Usage: ./scripts/make-icon.ps1   → src/StillSaneExile.Desktop/Assets/sse.ico and docs/screenshots/icon.png
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$root = Split-Path -Parent $PSScriptRoot
$svgFile = Join-Path $root 'src/StillSaneExile.Desktop/Assets/sse.svg'
$outFile = Join-Path $root 'src/StillSaneExile.Desktop/Assets/sse.ico'

$candidates = @(
    $env:SSE_BROWSER,
    (Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe'),
    (Join-Path $env:LOCALAPPDATA 'Google\Chrome\Application\chrome.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
) | Where-Object { $_ -and (Test-Path $_) }
$browser = $candidates | Select-Object -First 1
if (-not $browser) { throw 'No Chrome or Edge found. Set SSE_BROWSER to the path of chrome.exe or msedge.exe.' }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("sse-icon-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
Copy-Item $svgFile (Join-Path $work 'icon.svg')

# Renders the SVG at exactly $s x $s pixels with a transparent background.
function Render([int]$s) {
    $html = Join-Path $work "icon-$s.html"
    $png = Join-Path $work "icon-$s.png"
    Set-Content -Encoding UTF8 $html "<!doctype html><html><body style='margin:0;background:transparent'><img src='icon.svg' width='$s' height='$s' style='display:block'></body></html>"
    $url = 'file:///' + ($html -replace '\\', '/')
    $browserArgs = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
        '--default-background-color=00000000', "--user-data-dir=$work\profile", "--screenshot=$png", "--window-size=$s,$s", $url)
    Start-Process -FilePath $browser -ArgumentList $browserArgs -Wait -NoNewWindow -RedirectStandardError (Join-Path $work 'browser.log')
    if (-not (Test-Path $png)) { throw "The browser did not render $s px (see $work\browser.log)." }

    $decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::new([Uri]$png, 'PreservePixelFormat', 'OnLoad')
    $frame = $decoder.Frames[0]
    if ($frame.PixelWidth -ne $s -or $frame.PixelHeight -ne $s) {
        $frame = [System.Windows.Media.Imaging.CroppedBitmap]::new($frame, [System.Windows.Int32Rect]::new(0, 0, $s, $s))
    }
    return $frame
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

try {
    $sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
    $bitmaps = @{}
    $images = foreach ($s in $sizes) {
        $bitmaps[$s] = Render $s
        , ($(if ($s -eq 256) { PngBytes $bitmaps[$s] } else { DibBytes $bitmaps[$s] $s }))
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

    # Preview PNG for the docs.
    [System.IO.File]::WriteAllBytes((Join-Path $root 'docs/screenshots/icon.png'), (PngBytes $bitmaps[256]))
    Write-Host "Wrote $outFile ($($out.Length) bytes) using $browser"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
