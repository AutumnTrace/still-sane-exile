# Refreshes data/tree/ from Grinding Gear Games' official Path of Exile 2 passive tree export
# (https://github.com/grindinggear/poe2-skilltree-export), which GGG's developer docs list under "Data
# Exports" as the one piece of game data it provides outside its APIs. SSE draws a pasted build's tree
# from these files and never downloads them itself.
#
# data/tree/poe2-tree.json: per node its id, name, kind, position, ascendancy, stat lines (markup removed),
# icon and mastery pattern; per connection its two nodes and, for a curve, the centre and index of its
# orbit. Left out: recipes, flavour text, everything else.
# data/tree/art/: the export's sprite sheets SSE draws with (node icons, frames, connectors and orbits,
# the background, the centre circle, mastery patterns), turned from WebP into JPEG and PNG by
# tools/TreeArt because WPF cannot read WebP on a clean Windows, plus atlas.json. The class illustrations,
# jewel art and jewel radii are not used and not fetched.
#
# One request for the commit, one for data.json, two per sheet (its JSON and its WebP); no retries. Needs
# the .NET SDK (for tools/TreeArt). Run it by hand after a game patch, check the diff, commit it.
# Usage: ./scripts/update-passive-tree.ps1 [-Commit <sha>]
param([string]$Commit = '')
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Web.Extensions

$root = Split-Path -Parent $PSScriptRoot
$outFile = Join-Path $root 'data/tree/poe2-tree.json'
$artDir = Join-Path $root 'data/tree/art'
$userAgent = 'StillSaneExile-dev-script (+https://github.com/AutumnTrace/still-sane-exile)'
$repo = 'grindinggear/poe2-skilltree-export'
$inv = [Globalization.CultureInfo]::InvariantCulture
$sheets = @('skills', 'skills-disabled', 'frame', 'line', 'background', 'group-background', 'mastery-effect-active', 'mastery-effect-disabled')

function Get-Json([string]$url) {
    $response = Invoke-WebRequest -UseBasicParsing -Uri $url -UserAgent $userAgent -TimeoutSec 120
    $text = [System.Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray())
    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $serializer.MaxJsonLength = [int]::MaxValue
    $serializer.RecursionLimit = 256
    , $serializer.DeserializeObject($text)
}

function ConvertTo-JsonString([string]$s) {
    $e = $s.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
    $e = [regex]::Replace($e, '[\x00-\x1f]', { param($m) '\u{0:x4}' -f [int][char]$m.Value })
    '"' + $e + '"'
}

function Get-Number($value) { [Math]::Round([double]$value, 1).ToString('0.#', $inv) }

function Get-Flag($node, [string]$key) { $node.ContainsKey($key) -and [bool]$node[$key] }

# "[EnergyShield|Energy Shield]" reads "Energy Shield"; "[Spell]" reads "Spell"; "<underline>{Life Remnants}"
# reads "Life Remnants".
function Get-Plain([string]$text) {
    $t = [regex]::Replace($text, '<[A-Za-z]+>\{([^}]*)\}', '$1')
    $t = [regex]::Replace($t, '\[([^\]|]+)\|([^\]]+)\]', '$2')
    [regex]::Replace($t, '\[([^\]]+)\]', '$1')
}

function Get-Kind($node) {
    if ($node.ContainsKey('classStartIndex')) { return 'start' }
    if (Get-Flag $node 'isAscendancyStart') { return 'ascendancyStart' }
    if (Get-Flag $node 'isKeystone') { return 'keystone' }
    if (Get-Flag $node 'isMastery') { return 'mastery' }
    if (Get-Flag $node 'isJewelSocket') { return 'jewel' }
    if (Get-Flag $node 'isNotable') { return 'notable' }
    if (Get-Flag $node 'isGenericAttribute') { return 'attribute' }
    'normal'
}

if ($Commit.Length -eq 0) {
    $head = Get-Json "https://api.github.com/repos/$repo/commits/main"
    $Commit = [string]$head['sha']
    $message = [string]$head['commit']['message']
} else {
    $head = Get-Json "https://api.github.com/repos/$repo/commits/$Commit"
    $message = [string]$head['commit']['message']
}
$version = ($message -split "`n")[0].Trim()
$base = "https://raw.githubusercontent.com/$repo/$Commit"
$source = "$base/data.json"
$data = Get-Json $source
$fetched = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd', $inv)

$nodes = $data['nodes']
$ids = [System.Collections.Generic.List[int]]::new()
$parsed = 0
foreach ($key in $nodes.Keys) { if ([int]::TryParse([string]$key, [ref]$parsed)) { $ids.Add($parsed) } } # "root" is the tree's centre, not a node
$ids.Sort()

$nl = "`r`n"
$sb = [System.Text.StringBuilder]::new()
function Add-Line([string]$line) { [void]$sb.Append($line).Append($nl) }

Add-Line '{'
Add-Line ('  "about": ' + (ConvertTo-JsonString "Path of Exile 2's passive tree, trimmed from Grinding Gear Games' official export: node ids, names, kinds, positions, ascendancies, stat lines, icons and mastery patterns, and the connections between nodes. The pictures are in art/. SSE draws a pasted build's tree from it and never downloads it itself. Regenerate with scripts/update-passive-tree.ps1.") + ',')
Add-Line ('  "source": ' + (ConvertTo-JsonString $source) + ',')
Add-Line ('  "commit": ' + (ConvertTo-JsonString $Commit) + ',')
Add-Line ('  "version": ' + (ConvertTo-JsonString $version) + ',')
Add-Line ('  "fetched": ' + (ConvertTo-JsonString $fetched) + ',')
Add-Line '  "classes": ['
$classes = @($data['classes'])
for ($i = 0; $i -lt $classes.Count; $i++) {
    $c = $classes[$i]
    $asc = @($c['ascendancies'] | Where-Object { $_ -and $_['name'] } | ForEach-Object { '{ "id": ' + (ConvertTo-JsonString ([string]$_['id'])) + ', "name": ' + (ConvertTo-JsonString ([string]$_['name'])) + ' }' })
    Add-Line ('    { "name": ' + (ConvertTo-JsonString ([string]$c['name'])) + ', "ascendancies": [' + ($asc -join ', ') + '] }' + $(if ($i -lt $classes.Count - 1) { ',' } else { '' }))
}
Add-Line '  ],'

Add-Line '  "nodes": ['
$kept = [System.Collections.Generic.HashSet[int]]::new()
$lines = [System.Collections.Generic.List[string]]::new()
foreach ($id in $ids) {
    $n = $nodes[[string]$id]
    if (-not $n.ContainsKey('x') -or -not $n.ContainsKey('y')) { continue }
    [void]$kept.Add($id)
    $parts = @(('"id": ' + $id), ('"name": ' + (ConvertTo-JsonString ([string]$n['name']))), ('"kind": "' + (Get-Kind $n) + '"'), ('"x": ' + (Get-Number $n['x'])), ('"y": ' + (Get-Number $n['y'])))
    if ($n.ContainsKey('ascendancyId') -and $n['ascendancyId']) { $parts += '"ascendancy": ' + (ConvertTo-JsonString ([string]$n['ascendancyId'])) }
    if ($n.ContainsKey('icon') -and $n['icon']) { $parts += '"icon": ' + (ConvertTo-JsonString ([string]$n['icon'])) }
    if ((Get-Flag $n 'isMastery') -and $n['activeEffectImage']) { $parts += '"pattern": ' + (ConvertTo-JsonString ([string]$n['activeEffectImage'])) }
    if (Get-Flag $n 'isBlighted') { $parts += '"blighted": true' }
    $stats = @($n['stats'] | Where-Object { $_ } | ForEach-Object { ConvertTo-JsonString (Get-Plain ([string]$_)) })
    if ($stats.Count -gt 0) { $parts += '"stats": [' + ($stats -join ', ') + ']' }
    $lines.Add('    { ' + ($parts -join ', ') + ' }')
}
for ($i = 0; $i -lt $lines.Count; $i++) { Add-Line ($lines[$i] + $(if ($i -lt $lines.Count - 1) { ',' } else { '' })) }
Add-Line '  ],'

# Connections as the game's export lists them, minus the root links to class starts and the ones the
# export hides. A curve keeps the centre of its orbit and the orbit's index, which names its sprite
# (orbit 0 is Orbit1); everything else is a straight line.
Add-Line '  "edges": ['
$edgeLines = [System.Collections.Generic.List[string]]::new()
foreach ($e in @($data['edges'])) {
    $from = $e['from']; $to = $e['to']
    if ($from -is [string] -or $to -is [string]) { continue }
    if (-not $kept.Contains([int]$from) -or -not $kept.Contains([int]$to)) { continue }
    if ((Get-Flag $nodes[[string]$from] 'hideConnection') -or (Get-Flag $nodes[[string]$to] 'hideConnection')) { continue }
    $line = '    [' + $from + ', ' + $to
    if ($e.ContainsKey('orbitX') -and $e.ContainsKey('orbitY') -and $e.ContainsKey('orbit')) { $line += ', ' + (Get-Number $e['orbitX']) + ', ' + (Get-Number $e['orbitY']) + ', ' + [int]$e['orbit'] }
    $edgeLines.Add($line + ']')
}
for ($i = 0; $i -lt $edgeLines.Count; $i++) { Add-Line ($edgeLines[$i] + $(if ($i -lt $edgeLines.Count - 1) { ',' } else { '' })) }
Add-Line '  ]'
Add-Line '}'

New-Item -ItemType Directory -Force (Split-Path -Parent $outFile) | Out-Null
[System.IO.File]::WriteAllText($outFile, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $outFile from $repo@$($Commit.Substring(0, 8)) ($version): $($lines.Count) nodes, $($edgeLines.Count) edges."

# The art: GGG's sheets into a scratch folder, then tools/TreeArt writes what the app ships.
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('sse-tree-art-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $scratch | Out-Null
try {
    foreach ($sheet in $sheets) {
        foreach ($ext in @('json', 'webp')) {
            Invoke-WebRequest -UseBasicParsing -Uri "$base/assets/$sheet.$ext" -UserAgent $userAgent -TimeoutSec 120 -OutFile (Join-Path $scratch "$sheet.$ext")
        }
    }

    if (Test-Path $artDir) { Remove-Item -Recurse -Force $artDir }
    & dotnet run --project (Join-Path $root 'tools/TreeArt') -c Release -- $scratch $artDir "$base/assets ($version)"
    if ($LASTEXITCODE -ne 0) { throw "tools/TreeArt failed with exit code $LASTEXITCODE." }
} finally {
    Remove-Item -Recurse -Force $scratch
}
