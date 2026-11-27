# Refreshes data/trade/trade-data.json from Grinding Gear Games' public trade metadata: the stat filter
# ids and their texts, the item category and filter options, and the base type and unique names the
# trade site knows. Ids and labels only, no listings. SSE builds its "Open this search on the trade
# site" link from this file and never calls these endpoints itself.
#
# One request per endpoint, no retries. Run it by hand after a game patch, check the diff, commit it.
# Usage: ./scripts/update-trade-data.ps1
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $PSScriptRoot
$outFile = Join-Path $root 'data/trade/trade-data.json'
$userAgent = 'StillSaneExile-dev-script (+https://github.com/AutumnTrace/still-sane-exile)'
$base = 'https://www.pathofexile.com/api/trade2/data'
$sources = [ordered]@{ stats = "$base/stats"; filters = "$base/filters"; items = "$base/items" }

# Stat groups SSE maps worksheet rows to: explicit (also fractured, desecrated, crafted and unique
# lines), implicit, enchant and rune ("Augment" on the site).
$statGroups = @('explicit', 'implicit', 'enchant', 'rune')

# The filter keys the query builder writes (src/StillSaneExile.Domain/Trade/TradeQuery.cs). The script
# stops if the site no longer has one, so a renamed filter is noticed here rather than in a broken link.
$usedFilters = [ordered]@{
    equipment_filters = @('pdps')
    misc_filters = @('corrupted')
    trade_filters = @('price')
    type_filters = @('category', 'ilvl', 'rarity')
}
$usedStatus = @('available', 'securable')

function Get-TradeJson([string]$url) {
    $response = Invoke-WebRequest -UseBasicParsing -Uri $url -UserAgent $userAgent -TimeoutSec 60
    $text = [System.Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray())
    $json = $text | ConvertFrom-Json
    if (-not $json.result) { throw "$url answered without a result list." }
    $json.result
}

function ConvertTo-JsonString([string]$s) {
    $e = $s.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
    $e = [regex]::Replace($e, '[\x00-\x1f]', { param($m) '\u{0:x4}' -f [int][char]$m.Value })
    '"' + $e + '"'
}

function Get-Sorted([string[]]$values) {
    $list = [System.Collections.Generic.List[string]]::new([System.Collections.Generic.HashSet[string]]::new([string[]]$values, [StringComparer]::Ordinal))
    $list.Sort([StringComparer]::Ordinal)
    , $list.ToArray()
}

# Options as { id, text } sorted by id; the empty "Any" option is left out.
function Get-Options($filter) {
    $byId = [System.Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($o in $filter.option.options) {
        $id = [string]$o.id
        if ($id.Length -gt 0 -and -not $byId.ContainsKey($id)) { $byId[$id] = [string]$o.text }
    }
    , $byId
}

function Find-Filter($groups, [string]$group, [string]$id) {
    $g = $groups | Where-Object { $_.id -eq $group }
    $f = $g.filters | Where-Object { $_.id -eq $id }
    if (-not $f) { throw "The trade site's filters have no $group.$id any more. Update TradeQuery.cs and this script together." }
    $f
}

$stats = Get-TradeJson $sources.stats
$filters = Get-TradeJson $sources.filters
$items = Get-TradeJson $sources.items
$fetched = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)

foreach ($group in $usedFilters.Keys) {
    foreach ($id in $usedFilters[$group]) { [void](Find-Filter $filters $group $id) }
}

$status = Get-Options (Find-Filter $filters 'status_filters' 'status')
foreach ($s in $usedStatus) {
    if (-not $status.ContainsKey($s)) { throw "The trade site's status filter has no '$s' option any more." }
}

$categories = Get-Options (Find-Filter $filters 'type_filters' 'category')
$rarities = Get-Options (Find-Filter $filters 'type_filters' 'rarity')
$prices = Get-Options (Find-Filter $filters 'trade_filters' 'price')

# "[DNT] ..." entries are placeholders the game never shows to players.
$bases = Get-Sorted @($items | ForEach-Object { $_.entries } | Where-Object { $_.type -and -not ([string]$_.type).StartsWith('[') } | ForEach-Object { [string]$_.type })
$uniques = Get-Sorted @($items | ForEach-Object { $_.entries } | Where-Object { $_.name -and $_.flags.unique } | ForEach-Object { [string]$_.name })

$nl = "`r`n"
$sb = [System.Text.StringBuilder]::new()
function Add-Line([string]$line) { [void]$sb.Append($line).Append($nl) }

function Add-Options([string]$name, $options, [bool]$last = $false) {
    Add-Line "  $(ConvertTo-JsonString $name): ["
    $i = 0
    foreach ($kv in $options.GetEnumerator()) {
        $comma = if (++$i -lt $options.Count) { ',' } else { '' }
        Add-Line "    { `"id`": $(ConvertTo-JsonString $kv.Key), `"text`": $(ConvertTo-JsonString $kv.Value) }$comma"
    }
    Add-Line "  ]$(if ($last) { '' } else { ',' })"
}

function Add-Strings([string]$name, [string[]]$values) {
    Add-Line "  $(ConvertTo-JsonString $name): ["
    for ($i = 0; $i -lt $values.Count; $i++) {
        Add-Line "    $(ConvertTo-JsonString $values[$i])$(if ($i -lt $values.Count - 1) { ',' } else { '' })"
    }
    Add-Line '  ],'
}

Add-Line '{'
Add-Line ('  "about": ' + (ConvertTo-JsonString "Path of Exile 2 trade site metadata from Grinding Gear Games' public trade data: stat filter ids and texts, filter options, base type and unique names. Ids and labels only, no listings. SSE builds trade site links from it and never calls these endpoints itself. Regenerate with scripts/update-trade-data.ps1.") + ',')
Add-Line '  "sources": {'
$i = 0
foreach ($kv in $sources.GetEnumerator()) {
    Add-Line "    $(ConvertTo-JsonString $kv.Key): $(ConvertTo-JsonString $kv.Value)$(if (++$i -lt $sources.Count) { ',' } else { '' })"
}
Add-Line '  },'
Add-Line "  `"fetched`": $(ConvertTo-JsonString $fetched),"
Add-Line '  "filters": {'
$i = 0
foreach ($group in $usedFilters.Keys) {
    $sorted = Get-Sorted $usedFilters[$group]
    $keys = ($sorted | ForEach-Object { ConvertTo-JsonString $_ }) -join ', '
    Add-Line "    $(ConvertTo-JsonString $group): [ $keys ]$(if (++$i -lt $usedFilters.Count) { ',' } else { '' })"
}
Add-Line '  },'
Add-Options 'status' $status
Add-Options 'categories' $categories
Add-Options 'rarities' $rarities
Add-Options 'prices' $prices
Add-Strings 'bases' $bases
Add-Strings 'uniques' $uniques
Add-Line '  "stats": {'
$total = 0
for ($g = 0; $g -lt $statGroups.Count; $g++) {
    $group = $statGroups[$g]
    $entries = ($stats | Where-Object { $_.id -eq $group }).entries
    if (-not $entries) { throw "The trade site's stats have no '$group' group any more." }

    # Sorted by id, then text: some ids appear more than once with different texts.
    $pairs = [System.Collections.Generic.List[string[]]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($e in $entries) {
        if ($seen.Add("$($e.id)`n$($e.text)")) { $pairs.Add([string[]]@([string]$e.id, [string]$e.text)) }
    }
    $pairs.Sort([Comparison[string[]]] {
        param($a, $b)
        $c = [string]::CompareOrdinal($a[0], $b[0])
        if ($c -ne 0) { $c } else { [string]::CompareOrdinal($a[1], $b[1]) }
    })

    Add-Line "    $(ConvertTo-JsonString $group): ["
    for ($k = 0; $k -lt $pairs.Count; $k++) {
        $comma = if ($k -lt $pairs.Count - 1) { ',' } else { '' }
        Add-Line "      { `"id`": $(ConvertTo-JsonString $pairs[$k][0]), `"text`": $(ConvertTo-JsonString $pairs[$k][1]) }$comma"
    }
    Add-Line "    ]$(if ($g -lt $statGroups.Count - 1) { ',' } else { '' })"
    $total += $pairs.Count
}
Add-Line '  }'
Add-Line '}'

New-Item -ItemType Directory -Force (Split-Path -Parent $outFile) | Out-Null
[System.IO.File]::WriteAllText($outFile, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
$size = [math]::Round((Get-Item $outFile).Length / 1KB)
Write-Host "Wrote $outFile ($size KB): $total stats in $($statGroups -join ', '), $($categories.Count) categories, $($bases.Count) bases, $($uniques.Count) uniques, fetched $fetched."
