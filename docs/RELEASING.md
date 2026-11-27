# Releasing and updates

## The rule

A release exists only as a tag `vMAJOR.MINOR.PATCH` that

1. equals `<Version>` in `Directory.Build.props` (the single place the version is declared),
2. points at a commit on `main`,
3. is higher than every earlier release tag.

`scripts/verify-release.ps1` enforces this. The Release workflow runs it first and stops on any mismatch. CI also fails if `main` carries an invalid version or one lower than the last release.

## Cut a release

```powershell
./scripts/release.ps1 -Version 0.2.0
```

This bumps `<Version>`, adds a `CHANGELOG.md` section from the commit subjects if you have not written one, commits `chore: release 0.2.0`, tags `v0.2.0`, and pushes `main` and the tag. Edit the changelog section first if you want hand-written notes; the script keeps an existing section.

Doing it by hand works too: bump `<Version>`, commit to `main`, `git tag -a v0.2.0 -m "Still Sane, Exile? 0.2.0"`, push the commit and the tag.

## What the Release workflow does

On the tag push (`.github/workflows/release.yml`, Windows runner):

1. Checks the release rule.
2. Builds and runs all tests in Release.
3. Publishes a self-contained win-x64 build with that version.
4. Downloads the previous release so Velopack can make a small delta package.
5. Packs with Velopack (`vpk pack`) and publishes the GitHub release with:
   - `StillSaneExile-win-Setup.exe`: the installer (per-user, no admin rights, Start menu and desktop shortcuts),
   - `StillSaneExile-win-Portable.zip`: the portable build (also self-updating; settings in `data\` next to it),
   - `StillSaneExile-X.Y.Z-full.nupkg`, `-delta.nupkg` and `releases.win.json`: the update feed.

Release notes come from the version's `CHANGELOG.md` section, or the commit subjects since the previous tag.

## How installed copies update

- At startup (after 5 s) and every 6 hours, SSE checks the latest **non-prerelease** GitHub release (`UpdateService`, Velopack `GithubSource`).
- A newer version downloads in the background (a delta when possible). The header then shows "Update X ready · Restart".
- The update installs when SSE closes, without restarting it, so a game session is never interrupted. "Restart now" installs it immediately. If SSE is killed instead of closed, Velopack installs the pending update on the next start.
- Players can turn automatic checks off in Settings and use "Check now". Development builds (run from source) never update.
- The check is an unauthenticated GitHub API request (60 per hour per IP address), so a 6-hour interval is far inside the limit.

## Test an update locally without publishing

```powershell
./scripts/pack-local.ps1 -Version 0.0.1 -PackId StillSaneExileTest -OutDir artifacts/test
Copy-Item artifacts/test/StillSaneExileTest-win-Setup.exe artifacts/setup-0.0.1.exe
./scripts/pack-local.ps1 -Version 0.0.2 -PackId StillSaneExileTest -OutDir artifacts/test
$env:SSE_UPDATE_SOURCE = (Resolve-Path artifacts/test).Path
./artifacts/setup-0.0.1.exe --silent
& "$env:LOCALAPPDATA\StillSaneExileTest\current\StillSaneExile.exe" --data "$env:TEMP\sse-test"
# wait for "Update 0.0.2 ready", close the window, and 0.0.2 is installed
& "$env:LOCALAPPDATA\StillSaneExileTest\Update.exe" uninstall --silent
```

A different `-PackId` keeps the test install apart from a real one. `SSE_UPDATE_SOURCE` points the app at a local folder instead of GitHub.

## Code signing

Builds are not signed yet, so Windows SmartScreen shows "Windows protected your PC" for a new download (More info → Run anyway). `vpk pack` supports signing (`--signParams` or `--azureTrustedSignFile`) once a certificate or Azure Trusted Signing account exists.
