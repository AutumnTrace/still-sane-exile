# Builds the installer and portable zip locally, exactly as the Release workflow does, without
# publishing anything. Use a different -PackId to test install and update without touching a real
# installation, and point the installed copy at the output folder with SSE_UPDATE_SOURCE.
#   ./scripts/pack-local.ps1 -Version 0.0.1 -PackId StillSaneExileTest
param(
    [string]$Version,
    [string]$PackId = 'StillSaneExile',
    [string]$OutDir
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
}
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts/releases' }
$pub = Join-Path $root "artifacts/publish/$Version"

dotnet publish (Join-Path $root 'src/StillSaneExile.Desktop') -c Release -r win-x64 --self-contained true -p:Version=$Version -o $pub
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
dotnet tool restore
dotnet vpk pack --packId $PackId --packVersion $Version --packDir $pub --mainExe StillSaneExile.exe `
    --packTitle 'Still Sane, Exile' --packAuthors 'Mina Rotari' --icon (Join-Path $root 'src/StillSaneExile.Desktop/Assets/sse.ico') `
    --outputDir $OutDir
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }
Write-Host "Packages in $OutDir"
