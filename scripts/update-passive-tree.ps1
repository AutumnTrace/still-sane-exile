# Refreshes data/tree/poe2-tree.json from Grinding Gear Games' official Path of Exile 2 passive tree
# export (https://github.com/grindinggear/poe2-skilltree-export), which GGG's developer docs list under
# "Data Exports" as the one piece of game data it provides outside its APIs. SSE draws a pasted build's
# tree from the trimmed copy and never downloads it itself.
#
# Kept per node: id, name, kind, position, ascendancy and stat lines (markup removed). Kept per edge: its
# two nodes and, for a curve, the centre of its orbit. Left out: icons and art, recipes, everything else.
# Two requests (the commit, then its data.json), no retries. Run it by hand after a game patch, check the
# diff, commit it.
# Usage: ./scripts/update-passive-tree.ps1 [-Commit <sha>]
param([string]$Commit = '')
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Web.Extensions

$root = Split-Path -Parent $PSScriptRoot
$outFile = Join-Path $root 'data/tree/poe2-tree.json'
$userAgent = 'StillSaneExile-dev-script (+https://github.com/AutumnTrace/still-sane-exile)'
$repo = 'grindinggear/poe2-skilltree-export'
$inv = [Globalization.CultureInfo]::InvariantCulture

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
$source = "https://raw.githubusercontent.com/$repo/$Commit/data.json"
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
Add-Line ('  "about": ' + (ConvertTo-JsonString "Path of Exile 2's passive tree, trimmed from Grinding Gear Games' official export: node ids, names, kinds, positions, ascendancies and stat lines, and the connections between nodes. No art. SSE draws a pasted build's tree from it and never downloads it itself. Regenerate with scripts/update-passive-tree.ps1.") + ',')
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
    $stats = @($n['stats'] | Where-Object { $_ } | ForEach-Object { ConvertTo-JsonString (Get-Plain ([string]$_)) })
    if ($stats.Count -gt 0) { $parts += '"stats": [' + ($stats -join ', ') + ']' }
    $lines.Add('    { ' + ($parts -join ', ') + ' }')
}
for ($i = 0; $i -lt $lines.Count; $i++) { Add-Line ($lines[$i] + $(if ($i -lt $lines.Count - 1) { ',' } else { '' })) }
Add-Line '  ],'

# Connections as the game's export lists them, minus the root links to class starts and the ones the
# export hides. A curve keeps the centre of its orbit; everything else is a straight line.
Add-Line '  "edges": ['
$edgeLines = [System.Collections.Generic.List[string]]::new()
foreach ($e in @($data['edges'])) {
    $from = $e['from']; $to = $e['to']
    if ($from -is [string] -or $to -is [string]) { continue }
    if (-not $kept.Contains([int]$from) -or -not $kept.Contains([int]$to)) { continue }
    if ((Get-Flag $nodes[[string]$from] 'hideConnection') -or (Get-Flag $nodes[[string]$to] 'hideConnection')) { continue }
    $line = '    [' + $from + ', ' + $to
    if ($e.ContainsKey('orbitX') -and $e.ContainsKey('orbitY') -and [int]$e['orbit'] -gt 0) { $line += ', ' + (Get-Number $e['orbitX']) + ', ' + (Get-Number $e['orbitY']) }
    $edgeLines.Add($line + ']')
}
for ($i = 0; $i -lt $edgeLines.Count; $i++) { Add-Line ($edgeLines[$i] + $(if ($i -lt $edgeLines.Count - 1) { ',' } else { '' })) }
Add-Line '  ]'
Add-Line '}'

New-Item -ItemType Directory -Force (Split-Path -Parent $outFile) | Out-Null
[System.IO.File]::WriteAllText($outFile, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $outFile from $repo@$($Commit.Substring(0, 8)) ($version): $($lines.Count) nodes, $($edgeLines.Count) edges."
