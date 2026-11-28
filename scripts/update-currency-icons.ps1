# Refreshes the game pictures the app ships (src/StillSaneExile.Desktop/Assets/Currency): the Divine,
# Exalted and Chaos Orbs drawn next to prices, and the orbs, essences and omens of the craft simulator.
# They are the game's own pictures: poe.ninja's PoE2 exchange overviews name each one's path on GGG's image
# server, and the picture comes from there. The app ships them and never downloads a picture itself.
#
# One overview per type and league until every picture is found (at least 5 s apart, as poe.ninja asks),
# then one request per picture (1 s apart), no retries. A picture only shows in a league where it trades,
# so pass the current league too when Standard lacks one. Run it by hand when a picture changes, look at
# the files, commit them.
# Usage: ./scripts/update-currency-icons.ps1 [-Leagues 'Standard','Forbidden Rites']
param([string[]]$Leagues = @('Standard'))
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'src/StillSaneExile.Desktop/Assets/Currency'
$userAgent = 'StillSaneExile-dev-script (+https://github.com/AutumnTrace/still-sane-exile)'
$imageHost = 'https://web.poecdn.com'

# poe.ninja's id for each picture, by the overview it is listed in. The file is named after the id;
# CurrencyIcon.cs lists the same ids.
$wanted = [ordered]@{
    'Currency' = @('divine', 'exalted', 'chaos', 'transmute', 'aug', 'regal', 'alch', 'annul', 'fracturing-orb', 'vaal')
    'Essences' = @('essence-of-abrasion', 'essence-of-haste')
    'Ritual'   = @('omen-of-sinistral-exaltation', 'omen-of-dextral-exaltation', 'omen-of-greater-exaltation', 'omen-of-whittling',
                   'omen-of-sinistral-erasure', 'omen-of-dextral-erasure', 'omen-of-sinistral-annulment', 'omen-of-dextral-annulment')
}

$paths = @{}
$first = $true
foreach ($type in $wanted.Keys) {
    foreach ($league in $Leagues) {
        $missing = @($wanted[$type] | Where-Object { -not $paths.ContainsKey($_) })
        if ($missing.Count -eq 0) { break }
        if (-not $first) { Start-Sleep -Seconds 5 }
        $first = $false
        $overview = 'https://poe.ninja/poe2/api/economy/exchange/current/overview?league=' + [Uri]::EscapeDataString($league) + '&type=' + $type
        $response = Invoke-WebRequest -UseBasicParsing -Uri $overview -UserAgent $userAgent -TimeoutSec 60
        $json = [System.Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray()) | ConvertFrom-Json
        if (-not $json.items) { throw "poe.ninja answered $type in $league without items; its overview may have changed." }
        foreach ($item in @($json.core.items) + @($json.items)) {
            if ($item -and $missing -contains $item.id -and $item.image) { $paths[$item.id] = [string]$item.image }
        }
    }
}

$all = @($wanted.Values | ForEach-Object { $_ })
$notFound = @($all | Where-Object { -not $paths.ContainsKey($_) })
if ($notFound.Count -gt 0) { throw "No picture in $($Leagues -join ', ') for: $($notFound -join ', '). Add the current league with -Leagues." }

foreach ($id in $all) {
    $path = $paths[$id]
    if (-not $path.StartsWith('/gen/image/')) { throw "Unexpected picture path for '${id}': $path" }
    $file = Join-Path $outDir "$id.png"
    Invoke-WebRequest -UseBasicParsing -Uri ($imageHost + $path) -UserAgent $userAgent -TimeoutSec 60 -OutFile $file
    $bytes = [System.IO.File]::ReadAllBytes($file)
    if ($bytes.Length -lt 8 -or $bytes[0] -ne 0x89 -or $bytes[1] -ne 0x50) { throw "$file is not a PNG." }
    Write-Output "$id <- $imageHost$path ($($bytes.Length) bytes)"
    Start-Sleep -Seconds 1
}
