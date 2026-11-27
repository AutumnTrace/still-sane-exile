# Starts a packed build once, unattended, to prove it runs: checks the packages exist, unzips the
# portable package and renders the demo screens with --render-snapshots into a temporary folder.
# Run after scripts/pack-local.ps1 (the CI pack check does). It installs nothing and sends nothing:
# snapshot mode installs no hooks, reads no clipboard and does not check for updates.
#   ./scripts/smoke-packed.ps1 -PackDir artifacts/releases
param(
    [Parameter(Mandatory)][string]$PackDir,
    [string]$PackId = 'StillSaneExile',
    [string]$WorkDir,
    [int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
if (-not $WorkDir) { $WorkDir = Join-Path ([IO.Path]::GetTempPath()) "sse-smoke-$([guid]::NewGuid().ToString('N'))" }

foreach ($name in "$PackId-win-Setup.exe", "$PackId-win-Portable.zip", 'releases.win.json') {
    if (-not (Test-Path (Join-Path $PackDir $name))) { throw "Missing $name in $PackDir" }
}
if (-not (Get-ChildItem $PackDir -Filter "$PackId-*-full.nupkg")) { throw "No full .nupkg in $PackDir" }

$app = Join-Path $WorkDir 'portable'
$shots = Join-Path $WorkDir 'snapshots'
$data = Join-Path $WorkDir 'data'
Expand-Archive (Join-Path $PackDir "$PackId-win-Portable.zip") -DestinationPath $app -Force
$exe = Join-Path $app 'current\StillSaneExile.exe'
if (-not (Test-Path $exe)) { throw 'The portable package has no current\StillSaneExile.exe' }

# Start-Process does not quote array arguments; the paths may contain spaces.
$p = Start-Process $exe -ArgumentList '--render-snapshots', "`"$shots`"", '--data', "`"$data`"" -PassThru
$null = $p.Handle # keeps the exit code readable after the process ends
if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
    $p.Kill()
    throw "The packed app did not finish within $TimeoutSeconds s"
}

$log = Join-Path $data 'errors.log'
if (Test-Path $log) { Get-Content $log | ForEach-Object { Write-Host $_ } }
if ($p.ExitCode -ne 0) { throw "The packed app exited with code $($p.ExitCode)" }

$expected = 'onboarding.png', 'inspect-advanced.png', 'settings.png', 'peek-advanced.png', 'price-check-window.png'
$missing = @($expected | Where-Object { -not (Test-Path (Join-Path $shots $_)) })
if ($missing.Count -gt 0) { throw "The packed app did not render: $($missing -join ', ')" }
Write-Host "Packed app started and rendered $(@(Get-ChildItem $shots -Filter *.png).Count) screens into $shots"
