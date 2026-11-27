# Writes the release notes for a version: its CHANGELOG.md section if there is one, otherwise the
# commit subjects since the previous release tag.
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutFile
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$changelog = Join-Path $root 'CHANGELOG.md'
$notes = $null

if (Test-Path $changelog) {
    $lines = Get-Content $changelog
    $start = ($lines | Select-String -Pattern "^## \[$([regex]::Escape($Version))\]" | Select-Object -First 1).LineNumber
    if ($start) {
        $body = $lines[$start..($lines.Count - 1)]
        $end = ($body | Select-String -Pattern '^## ' | Select-Object -First 1).LineNumber
        if ($end) { $body = $body[0..($end - 2)] }
        $notes = ($body -join "`n").Trim()
    }
}

if (-not $notes) {
    $previous = git tag --list 'v*' --sort=-v:refname | Where-Object { $_ -ne "v$Version" } | Select-Object -First 1
    $range = if ($previous) { "$previous..HEAD" } else { 'HEAD' }
    $notes = (git log $range --no-merges --pretty='- %s') -join "`n"
}

$notes += "`n`n---`nInstall: download **StillSaneExile-win-Setup.exe** below. Installed copies update themselves.`n"
[System.IO.File]::WriteAllText($OutFile, $notes, [System.Text.UTF8Encoding]::new($false))
Write-Host "Release notes for $Version written to $OutFile"
