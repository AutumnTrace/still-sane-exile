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

This bumps `<Version>`, adds a `CHANGELOG.md` section from the commit subjects if you have not written one, commits `chore: release 0.2.0`, tags `v0.2.0`, and pushes `main` and the tag. Edit the changelog section first if you want hand-written notes; the script keeps an existing section. Players read that section in the app after the update ([What's new](#whats-new-after-an-update)), so write it for them.

Doing it by hand works too: bump `<Version>`, commit to `main`, `git tag -a v0.2.0 -m "Still Sane, Exile? 0.2.0"`, push the commit and the tag.

## What the Release workflow does

On the tag push (`.github/workflows/release.yml`, Windows runner):

1. Checks the release rule.
2. Builds and runs all tests in Release.
3. Downloads the previous release so Velopack can make a small delta package.
4. Publishes a self-contained win-x64 build with that version and packs it with Velopack (`vpk pack`), both through `scripts/pack-local.ps1`.
5. Starts the packed portable build once, unattended (`scripts/smoke-packed.ps1`, which renders the demo screens with `--render-snapshots`), so a build that cannot start is never published.
6. Publishes the GitHub release with:
   - `StillSaneExile-win-Setup.exe`: the installer (per-user, no admin rights, Start menu and desktop shortcuts),
   - `StillSaneExile-win-Portable.zip`: the portable build (also self-updating; settings in `data\` next to it),
   - `StillSaneExile-X.Y.Z-full.nupkg`, `-delta.nupkg` and `releases.win.json`: the update feed.

Release notes come from the version's `CHANGELOG.md` section, or the commit subjects since the previous tag.

## Packaging is checked on every pull request

The CI workflow's `pack` job runs steps 4 and 5 on every pull request and every push to `main`, into the runner's temp folder, and uploads nothing. Packaging problems therefore show up before a tag is pushed. Because the Release workflow packs with the same script, a change to the packaging only has to be made in `scripts/pack-local.ps1`.

## How installed copies update

- At startup (after 5 s) and every 6 hours, SSE checks the latest **non-prerelease** GitHub release (`UpdateService`, Velopack `GithubSource`).
- A newer version downloads in the background (a delta when possible). The header then shows "Update X ready · Restart".
- The update installs when SSE closes, without restarting it, so a game session is never interrupted. "Restart now" installs it immediately. If SSE is killed instead of closed, Velopack installs the pending update on the next start.
- Players can turn automatic checks off in Settings and use "Check now". Development builds (run from source) never update.
- The check is an unauthenticated GitHub API request (60 per hour per IP address), so a 6-hour interval is far inside the limit.

## What's new after an update

- On the first start after an update, the main window shows a "What's new" card above the current page: the `CHANGELOG.md` section of every version since the one that last ran (so skipped updates are covered), newest first, and a link to the version's GitHub release. It never appears on the HUD or as a dialog; if the game is in front, it waits in the main window. While onboarding is open it waits until onboarding is done. "Got it" hides it for good; Settings → Version and updates → "What's new in X" shows it again.
- The notes are the `CHANGELOG.md` the build was made from, embedded in the app, so they need no network. The Release workflow builds the tagged commit, which has the version's section (`release.ps1` adds one if missing), so players see the same text as the GitHub release. The card draws bullets, `###` headings, `**bold**`, `` `code` `` and `https://` links; anything else shows as plain text, and relative links (to `docs/`, say) as their text only.
- With no section for the version (a tag cut by hand without one), the card says "Updated to X" and links to the GitHub release, whose notes then come from the commit subjects.
- How a start counts as an update: settings keep the newest version that has run (`app.lastRunVersion`). A lower stored version means an update. Nothing stored but onboarding finished means an update from 0.2.0 or earlier, which did not store it; only the new version's section is shown. Nothing stored and not onboarded is a first install: no card. The same version, a downgrade and development builds show nothing. The version is stored once the card has been on screen, so it shows once.
- The local update test below shows it too: 0.0.2's first start says "Updated to 0.0.2" (the changelog has no 0.0.2 section) once onboarding is done.

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
