# Cuts a release in one step: bumps <Version>, adds a CHANGELOG section if missing, commits,
# tags vX.Y.Z on main and pushes both. The Release workflow then builds the installer and publishes
# it; installed copies pick the update up on their next check.
#   ./scripts/release.ps1 -Version 0.2.0
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $tag = "v$Version"
    if ((git rev-parse --abbrev-ref HEAD) -ne 'main') { throw 'Switch to main first.' }
    if (git status --porcelain) { throw 'Commit or stash your changes first.' }
    git fetch --tags origin
    if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { throw 'main is not in sync with origin/main. Pull or push first.' }

    # Bump the version (the only place it is declared).
    $propsPath = Join-Path $root 'Directory.Build.props'
    $props = [System.IO.File]::ReadAllText($propsPath)
    $props = [regex]::Replace($props, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
    [System.IO.File]::WriteAllText($propsPath, $props, [System.Text.UTF8Encoding]::new($false))

    # Validate before anything is committed (tag rules, version match, higher than the last release).
    & (Join-Path $PSScriptRoot 'verify-release.ps1') -Tag $tag -SkipMainCheck

    # CHANGELOG: add a section from commit subjects if the author has not written one.
    $changelog = Join-Path $root 'CHANGELOG.md'
    $text = if (Test-Path $changelog) { [System.IO.File]::ReadAllText($changelog) } else { "# Changelog`n" }
    if ($text -notmatch "(?m)^## \[$([regex]::Escape($Version))\]") {
        $previous = git tag --list 'v*' --sort=-v:refname | Select-Object -First 1
        $range = if ($previous) { "$previous..HEAD" } else { 'HEAD' }
        $items = (git log $range --no-merges --pretty='- %s') -join "`n"
        $section = "## [$Version] - $(Get-Date -Format 'yyyy-MM-dd')`n`n$items`n`n"
        $first = [regex]::Match($text, '(?m)^## ')
        $text = if ($first.Success) { $text.Insert($first.Index, $section) } else { $text.TrimEnd() + "`n`n$section" }
        [System.IO.File]::WriteAllText($changelog, $text, [System.Text.UTF8Encoding]::new($false))
    }

    function Invoke-Git { & git.exe @args; if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed" } }

    Invoke-Git add Directory.Build.props CHANGELOG.md
    git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        Invoke-Git commit -m "chore: release $Version"
        Invoke-Git push origin main
    }
    else {
        Write-Host "Version and changelog already say $Version; tagging the current commit."
    }

    Invoke-Git tag -a $tag -m "Still Sane, Exile? $Version"
    Invoke-Git push origin $tag
    Write-Host "Pushed $tag. Watch the Release workflow: gh run watch"
}
finally {
    Pop-Location
}
