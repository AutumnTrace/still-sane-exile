# Roadmap

Each milestone ships only what passed its fixtures **and** its real-device acceptance check. Anything else is exposed with its actual availability.

Open work is tracked as [issues](https://github.com/AutumnTrace/still-sane-exile/issues) grouped by [milestone](https://github.com/AutumnTrace/still-sane-exile/milestones). Labels: `area: …` for the part of the app, `P1`/`P2`/`P3` for priority, `needs: real game data` and `needs: decision` for what blocks an issue.

## 0 · Capability and data spike

- [x] Source matrix and real-versus-fixture table ([SOURCES.md](SOURCES.md))
- [x] Instrumentation: one physical press → at most one read; zero game inputs; zero market requests (unit tests + live counters)
- [ ] Real English normal and advanced copies; parser validated against them (#1)
- [ ] Re-check policy, terms, APIs and guides after the 11 December 2026 full release (#2)
- [ ] Real stash, currency-tab and Aldur captures at two UI scales (#3)
- [ ] One frame per keypress from the game window (#4): spike done, Windows.Graphics.Capture chosen ([CAPTURE-SPIKE.md](CAPTURE-SPIKE.md)); to be checked in the real game
- [ ] HUD measured on real hardware: placement, DPI, resolutions, latency (#5)

## 1 · Item inspector (v0.1.x)

- [x] Native-copy path: hold `Ctrl+C`, read once after the game's copy, click-through HUD card, release hides
- [x] Item model with separate sources (explicit, implicit, rune, enchant, unique, desecrated, fractured)
- [x] Roll bars from advanced-copy ranges, or a data pack when exactly one explanation fits; hybrid ambiguity
- [x] Editable comparison worksheet, official trade-site handoff, manual comparables with the evidence policy
- [x] Deterministic demo profile with synthetic fixtures and visible DEMO state
- [ ] A verified (non-synthetic) data pack for one weapon family (#6)
- [ ] Real-game acceptance run (#7)
- [x] Peek on a spare mouse button, key-collision warnings (#8); game default keys still to be checked in game
- [x] Clear "unsupported language" message (#9); practice-peek fix (#10)

## 2 · Economy and held labels

- [ ] Economy cache service: poe.ninja and the GGG exchange digest (#11)
- [x] Rate limits and cache headers for providers (#12): library and fixture tests, used once the economy cache exists ([PROVIDER-ETIQUETTE.md](PROVIDER-ETIQUETTE.md))
- [x] Live prices in the app: league list, atomic league switch, stale states (#13): each install fetches poe.ninja hourly (the owner's choice over the shared cache, #11); the SSF reference league is not built
- [ ] Dedicated currency tab labels (#14) and Aldur reward panel values (#15)
- [ ] Validated generic-grid subset (#16)

## 3 · Wealth

My wealth is switched off for now while it is rethought (its page says so; holdings saved earlier stay in the data folder). The data layer below stays built and tested.

- [ ] Snapshot preview, tab identity and coverage (#17)
- [x] Manual quantities and CSV import (#18) ([WEALTH-CSV.md](WEALTH-CSV.md))
- [ ] History and session rate (#19)

## 4 · Guide

- [x] Versioned zone graph, original route pack and HUD lines (#20): the route covers the start of Act 1 as an unverified draft ([GUIDE.md](GUIDE.md))
- [ ] Build plans from a saved Mobalytics link and player-written steps; stage-aware gem actions (#21): built; the manual run against the 0.5.5 Varashta guide is still to do

## 5 · Craft

- [ ] Crafting planner for one weapon family with success and failure branches (#22)

## Release

- [x] Installer and portable build (Velopack), per tag `vX.Y.Z` on main that matches `<Version>` ([RELEASING.md](RELEASING.md))
- [x] Automatic updates from GitHub releases; install on close, never mid-game
- [ ] Code signing (#23)
- [x] Pack the installer in CI (#24; now on every push to `main`); Dependabot (#25)
- [x] "What's new" after an update (#26); desktop tests (#27); accessibility and UI polish (#28) ([ACCESSIBILITY.md](ACCESSIBILITY.md))
- [ ] Changelog listing tested patches, layouts, languages and sources
