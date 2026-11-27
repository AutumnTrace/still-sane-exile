# Refreshes the pictures of the Divine, Exalted and Chaos Orbs the app draws next to prices
# (src/StillSaneExile.Desktop/Assets/Currency). They are the game's own pictures: poe.ninja's PoE2
# exchange overview names each one's path on GGG's image server, and the picture comes from there. The
# app ships them and never downloads a picture itself.
#
# Four requests in all (the overview, then one per picture), no retries. Run it by hand when a picture
# changes, look at the three files, commit them.
# Usage: ./scripts/update-currency-icons.ps1 [-League 'Standard']
param([string]$League = 'Standard')
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'src/StillSaneExile.Desktop/Assets/Currency'
$userAgent = 'StillSaneExile-dev-script (+https://github.com/AutumnTrace/still-sane-exile)'
$overview = 'https://poe.ninja/poe2/api/economy/exchange/current/overview?league=' + [Uri]::EscapeDataString($League) + '&type=Currency'
$imageHost = 'https://web.poecdn.com'

$response = Invoke-WebRequest -UseBasicParsing -Uri $overview -UserAgent $userAgent -TimeoutSec 60
$json = [System.Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray()) | ConvertFrom-Json
if (-not $json.core.items) { throw "poe.ninja answered without core.items; its overview may have changed." }

foreach ($id in @('divine', 'exalted', 'chaos')) {
    $item = $json.core.items | Where-Object { $_.id -eq $id } | Select-Object -First 1
    if (-not $item -or -not $item.image) { throw "poe.ninja's overview has no picture for '$id'." }
    $path = [string]$item.image
    if (-not $path.StartsWith('/gen/image/')) { throw "Unexpected picture path for '${id}': $path" }
    $file = Join-Path $outDir "$id.png"
    Invoke-WebRequest -UseBasicParsing -Uri ($imageHost + $path) -UserAgent $userAgent -TimeoutSec 60 -OutFile $file
    $bytes = [System.IO.File]::ReadAllBytes($file)
    if ($bytes.Length -lt 8 -or $bytes[0] -ne 0x89 -or $bytes[1] -ne 0x50) { throw "$file is not a PNG." }
    Write-Output "$id <- $imageHost$path ($($bytes.Length) bytes)"
}
