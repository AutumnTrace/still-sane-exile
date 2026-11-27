# Publishes the self-contained app and packs the installer and portable zip with Velopack. The
# Release workflow, the CI pack check and local testing all use this script, so a packaging problem
# shows up on a pull request instead of on a release tag. It publishes nothing.
# Use a different -PackId to test install and update without touching a real installation, and
# point the installed copy at the output folder with SSE_UPDATE_SOURCE.
#   ./scripts/pack-local.ps1 -Version 0.0.1 -PackId StillSaneExileTest
param(
    [string]$Version,
    [string]$PackId = 'StillSaneExile',
    [string]$OutDir,
    [string]$PublishDir,
    [string]$ReleaseNotes
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
}
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts/releases' }
if (-not $PublishDir) { $PublishDir = Join-Path $root "artifacts/publish/$Version" }

dotnet publish (Join-Path $root 'src/StillSaneExile.Desktop') -c Release -r win-x64 --self-contained true -p:Version=$Version -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed' }

# --packTitle has no '?': it names the shortcuts, and Windows file names cannot contain '?'.
$packArgs = @(
    '--packId', $PackId, '--packVersion', $Version, '--packDir', $PublishDir, '--mainExe', 'StillSaneExile.exe',
    '--packTitle', 'Still Sane, Exile', '--packAuthors', 'Mina Rotari',
    '--icon', (Join-Path $root 'src/StillSaneExile.Desktop/Assets/sse.ico'), '--outputDir', $OutDir
)
if ($ReleaseNotes) { $packArgs += @('--releaseNotes', $ReleaseNotes) }
dotnet vpk pack @packArgs
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }
Write-Host "Packages in $OutDir"
