# Builds a self-contained portable build of Still Sane, Exile for Windows x64.
# Output: publish/StillSaneExile-portable-win-x64.zip (settings live in .\data next to the exe).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'publish/portable'
$zip = Join-Path $root 'publish/StillSaneExile-portable-win-x64.zip'

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root 'src/StillSaneExile.Desktop') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Set-Content -Path (Join-Path $out 'portable.txt') -Value 'Settings and comparables are stored in the data folder next to StillSaneExile.exe.' -Encoding utf8
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Write-Host "Portable build: $zip"
