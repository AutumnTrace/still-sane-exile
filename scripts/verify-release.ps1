# Release rules. A release happens only for a tag vX.Y.Z that:
#   1. is strict semantic versioning (no leading zeros, no suffix),
#   2. equals <Version> in Directory.Build.props,
#   3. points at a commit on main,
#   4. is higher than every earlier release tag.
# Used by the Release workflow (fails the run) and by scripts/release.ps1 (before tagging).
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$MainRef = 'origin/main',
    [switch]$SkipMainCheck
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Fail([string]$message) {
    if ($env:GITHUB_ACTIONS) { Write-Host "::error::$message" }
    throw $message
}

if ($Tag -notmatch '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    Fail "Tag '$Tag' is not a release tag. Use vMAJOR.MINOR.PATCH, e.g. v0.2.0."
}
$version = $Tag.Substring(1)

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$declared = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if ($declared -ne $version) {
    Fail "Tag $Tag does not match <Version>$declared</Version> in Directory.Build.props. Bump the version in the same commit you tag."
}

if (-not $SkipMainCheck) {
    git merge-base --is-ancestor HEAD $MainRef 2>$null
    if ($LASTEXITCODE -ne 0) { Fail "Tag $Tag points at a commit that is not on main. Release from main only." }
}

$current = [version]$version
$earlier = git tag --list 'v*' |
    Where-Object { $_ -ne $Tag -and $_ -match '^v\d+\.\d+\.\d+$' } |
    ForEach-Object { [version]$_.Substring(1) } |
    Sort-Object -Descending |
    Select-Object -First 1
if ($earlier -and $current -le $earlier) {
    Fail "Version $version must be higher than the latest release v$earlier."
}

Write-Host "Release $Tag OK (Directory.Build.props $declared, previous $(if ($earlier) { "v$earlier" } else { 'none' }))."
if ($env:GITHUB_OUTPUT) { "version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
