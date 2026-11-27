# Working on Still Sane, Exile? (SSE)

A Windows companion app for Path of Exile 2: a quick price card over the game, a price check, live
prices, wealth tracking, a campaign guide and (later) stash labels. This file is the brief for anyone,
human or agent, changing the code. Read it before your first change; it is short on purpose and links to
the docs that hold the details.

## The rules that are not negotiable

SSE must stay on the permitted side of Grinding Gear Games' third-party policy (quoted with sources in
[docs/SOURCES.md](docs/SOURCES.md#ggg-third-party-policy-checked-27-november-2026)). Every change is checked
against these, and a reviewer will reject anything that bends them:

1. **Never touch the game.** No reading or writing its memory or files, no reading its logs (`Client.txt`
   included: the guide takes the zone from the player), no DLL injection, no client changes.
2. **Never send input to the game.** No keys, clicks, chat or macros, synthetic or otherwise. The hooks in
   `Infrastructure/` only observe: they always call `CallNextHookEx` and ignore injected events.
3. **One physical press, at most one read.** Clipboard and screen reads go through `CaptureGate`
   (`src/StillSaneExile.Domain/Capture`): auto-repeat and injected input never read again, late results are
   dropped by generation id. `CaptureGateTests` must keep passing unchanged.
4. **The player knows what is read.** Settings and onboarding say what each key reads; keep them true when
   behaviour changes.
5. **A key press never makes a network request.** `HudController` holds no network client (a test checks
   it). Network requests happen only on a timer (poe.ninja prices), an explicit click (trade search, update
   check) or app start, always through the rate-limit client in `src/StillSaneExile.Domain/Market`, which
   honours `Cache-Control`, `ETag`, `Retry-After` and GGG's `X-Rate-Limit-*` headers. Never rotate proxies,
   use extra accounts, send cookies/POESESSID, or retry aggressively. See
   [docs/PROVIDER-ETIQUETTE.md](docs/PROVIDER-ETIQUETTE.md).
6. **Nothing about the player leaves the computer.** No telemetry. Requests carry only what the request
   needs (league, category, the search the player built) and the User-Agent
   `StillSaneExile/<version> (+https://github.com/AutumnTrace/still-sane-exile)`.
7. **No made-up data that looks real.** The demo profile's prices, listings and data pack are synthetic and
   cover only the practice items in `fixtures/`; a live profile never sees them (`PriceBookGuard`), and
   nothing carries across leagues. Game facts that have not been checked in the game (route packs, key
   defaults, data packs) are labelled unverified in the data and the UI. When unsure, leave it out.
8. **Overlays never steal focus.** The HUD card is click-through while the key is held and stays
   `WS_EX_NOACTIVATE` even when pinned (activating it makes a fullscreen game drop out). Only the F3 price
   check window takes focus, and it hands it back to the game when it closes.

If a feature seems to need breaking one of these, stop and ask the owner instead.

## Layout

| Path | What it is |
|---|---|
| `src/StillSaneExile.Domain` | Pure .NET 10 library, no packages, no Windows or UI code: item parsing (`Items`, `Reading`, `Rolls`), pricing and evidence (`Pricing`), the trade worksheet and trade-site links (`Trade`), the rate-limit client and poe.ninja client (`Market`), the capture gate (`Capture`), wealth (`Wealth`), the guide (`Guide`), release notes (`Releases`). Put logic here so it can be unit-tested. |
| `src/StillSaneExile.Desktop` | WPF app (`net10.0-windows`, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, Velopack). `Infrastructure/` wraps Windows (hooks, foreground, clipboard, SQLite `Store`, paths), `Services/` holds the app logic (`HudController`, `Session`, `Settings`, `LivePrices`, `UpdateService`, `SnapshotRenderer`), `ViewModels/` and `Views/` the UI. `App.xaml` holds the theme and every shared style. |
| `tests/StillSaneExile.Domain.Tests` | xunit tests for the domain. Fixtures come from `fixtures/` via `Fixtures.cs`. |
| `tests/StillSaneExile.Desktop.Tests` | xunit tests for the app: seams with fakes (`Fakes.cs`), STA helpers (`Sta.Run`, `UiThread.Run`), `TestApp`, and `AccessibilityTests` (tab order, names, visible focus). |
| `tools/CaptureSpike` (+ `.Tests`) | Developer tool from the #4 spike: one Windows.Graphics.Capture frame per press. Not packed with the app. |
| `fixtures/` | Synthetic test and demo data (items, demo price book, data pack, poe.ninja shapes, wealth CSVs). Copied to the app output; never real market data. |
| `data/` | Data the app ships: route packs (`routes/`), the trade site's filter ids (`trade/trade-data.json`, refreshed by `scripts/update-trade-data.ps1`), poe.ninja categories (`poe-ninja.json`). |
| `scripts/` | `pack-local.ps1` (publish + `vpk pack`, used by CI and releases), `smoke-packed.ps1` (starts the packed app once), `release.ps1`, `release-notes.ps1`, `verify-release.ps1`, `update-trade-data.ps1`, `update-currency-icons.ps1`, `make-icon.ps1`. |
| `docs/` | The source of truth for behaviour and decisions; see below. |

## Build, test, run

```powershell
dotnet build StillSaneExile.sln -c Release      # must report 0 warnings (TreatWarningsAsErrors)
dotnet test  StillSaneExile.sln -c Release      # all suites; nothing may touch the network
dotnet run --project src/StillSaneExile.Desktop -- --data <empty folder>   # a clean profile
dotnet run --project src/StillSaneExile.Desktop -c Release -- --render-snapshots <dir> --data <empty dir>
```

- `--render-snapshots` renders every screen to PNG and exits (code 1 on error, never a dialog). Use it to
  look at UI changes. Do not overwrite `docs/screenshots/` (the README's images) unless asked.
- `./scripts/pack-local.ps1` then `./scripts/smoke-packed.ps1 -PackDir artifacts/releases` reproduces the CI
  `pack` job locally (Windows PowerShell 5.1 works).
- CI runs on GitHub's `windows-latest`: a 1024×768 desktop and no GPU guarantee. UI tests must not depend on
  the screen size (see `AccessibilityTests.Stage`), and GPU or capture tests must skip cleanly there.

## Conventions

- C#: file-scoped namespaces, nullable on, `TreatWarningsAsErrors`, CRLF line endings, 4-space indents (2 in
  XAML, JSON, YAML). Follow `.editorconfig`.
- Doc comments explain *why*, not what. Small sealed records and classes; no abstractions without a second
  use or a test seam.
- UI text is plain, short and exact ("Only 3 for sale — rough price"). Say what is unknown instead of
  guessing. Use the existing styles and brushes in `App.xaml`: the accent colour is only for the search,
  rarity colours only for item names. Meet the contrast and keyboard rules in
  [docs/ACCESSIBILITY.md](docs/ACCESSIBILITY.md): names for symbol-only buttons, `Tag="active"` for the chosen
  item of a group, a `ToString` on list-row view models, and a `Check(...)` in `AccessibilityTests` for new
  views.
- SQLite schema changes use `CREATE TABLE IF NOT EXISTS`; existing databases must keep opening.
- Tests use temp folders (`TempDir`), fake clocks (`TimeProvider`) and fake HTTP handlers. Fixtures are
  synthetic, in the real shapes; never commit real listings, account names or screenshots with names in them.

## Docs to keep true

When behaviour changes, update the doc that describes it in the same change:

- `README.md`: what players see, the keys table, "Is it allowed?".
- `docs/CAPTURE-AND-PRIVACY.md`: every Windows mechanism, everything stored, every network request.
- `docs/SOURCES.md`: providers, their terms, and real-versus-fixture coverage.
- `docs/PROVIDER-ETIQUETTE.md`, `docs/GUIDE.md`, `docs/WEALTH-CSV.md`, `docs/ACCESSIBILITY.md`,
  `docs/CAPTURE-SPIKE.md`, `docs/RELEASING.md`: their own areas.
- `docs/ROADMAP.md`: tick an item only when its issue is done, and say what is still unverified.
- `CHANGELOG.md` is written for players (the app shows the section as "What's new" after an update). Add the
  section when cutting a release, not in feature changes.

## Workflow

- Issues live on GitHub (`gh issue list --repo AutumnTrace/still-sane-exile`), grouped by milestone and
  labelled `area: …`, `P1`/`P2`/`P3`, `needs: decision` and `needs: real game data`. Don't start an issue
  labelled `needs: …` without the owner.
- Work on a branch, open a pull request, and merge with a rebase. CI does not run on pull requests: before
  merging, build with 0 warnings and run every test suite locally (and the pack scripts if packaging
  changed). CI (`build-test` and `pack`) runs on every push to `main`; if it fails after a merge, fixing
  `main` comes first. It can also be started by hand on any branch from the Actions tab.
  `main` keeps a linear history with one commit per change. Commit subjects are plain sentences
  ("Pinned card takes clicks without taking the focus"); the body says why, and ends with `Closes #N` or
  `Refs #N` when there is an issue. Use `Refs` when the issue's acceptance still needs a person or the game.
- Releases: `./scripts/release.ps1 -Version X.Y.Z` on an up-to-date `main` (bumps `<Version>` in
  `Directory.Build.props`, commits, tags `vX.Y.Z`, pushes; the Release workflow builds, smoke-tests and
  publishes, and installed copies update themselves). Only the owner decides when to release. Details in
  [docs/RELEASING.md](docs/RELEASING.md).
- Anything that can only be verified in the real game goes in the pull request and the reply to the owner
  as a checklist; don't claim it works.
