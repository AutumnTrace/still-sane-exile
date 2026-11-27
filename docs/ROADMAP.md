# Roadmap

Each milestone ships only what passed its fixtures **and** its real-device acceptance check. Anything else is exposed with its actual availability.

Open work is tracked as [issues](https://github.com/AutumnTrace/still-sane-exile/issues) grouped by [milestone](https://github.com/AutumnTrace/still-sane-exile/milestones). Labels: `area: …` for the part of the app, `P1`/`P2`/`P3` for priority, `needs: real game data` and `needs: decision` for what blocks an issue.

## 0 · Capability and data spike

- [x] Source matrix and real-versus-fixture table ([SOURCES.md](SOURCES.md))
- [x] Instrumentation: one physical press → at most one read; zero game inputs; zero market requests (unit tests + live counters)
- [ ] Real English normal and advanced copies; parser validated against them (#1)
- [ ] Re-check policy, terms, APIs and guides after the 11 December 2026 full release (#2)
- [ ] Real stash, currency-tab and Aldur captures at two UI scales (#3)
- [ ] One frame per keypress from the game window (#4)
- [ ] HUD measured on real hardware: placement, DPI, resolutions, latency (#5)

## 1 · Item inspector (v0.1.x)

- [x] Native-copy path: hold `Ctrl+C`, read once after the game's copy, click-through HUD card, release hides
- [x] Item model with separate sources (explicit, implicit, rune, enchant, unique, desecrated, fractured)
- [x] Roll bars from advanced-copy ranges, or a data pack when exactly one explanation fits; hybrid ambiguity
- [x] Editable comparison worksheet, official trade-site handoff, manual comparables with the evidence policy
- [x] Deterministic demo profile with synthetic fixtures and visible DEMO state
- [ ] A verified (non-synthetic) data pack for one weapon family (#6)
- [ ] Real-game acceptance run (#7)
- [ ] Peek on a spare mouse button, key-collision warnings (#8)
- [ ] Clear "unsupported language" message (#9); practice-peek fix (#10)

## 2 · Economy and held labels

- [ ] Economy cache service: poe.ninja and the GGG exchange digest (#11)
- [ ] Rate limits and cache headers for providers (#12)
- [ ] Live prices in the app: league list, atomic league switch, stale states (#13)
- [ ] Dedicated currency tab labels (#14) and Aldur reward panel values (#15)
- [ ] Validated generic-grid subset (#16)

## 3 · Wealth

- [ ] Snapshot preview, tab identity and coverage (#17)
- [ ] Manual quantities and CSV import (#18)
- [ ] History and session rate (#19)

## 4 · Guide

- [ ] Versioned zone graph, original route pack and HUD lines (#20)
- [ ] Build plans from a saved Mobalytics link and player-written steps; stage-aware gem actions (#21)

## 5 · Craft

- [ ] Crafting planner for one weapon family with success and failure branches (#22)

## Release

- [x] Installer and portable build (Velopack), per tag `vX.Y.Z` on main that matches `<Version>` ([RELEASING.md](RELEASING.md))
- [x] Automatic updates from GitHub releases; install on close, never mid-game
- [ ] Code signing (#23)
- [ ] Pack the installer on every pull request (#24); Dependabot (#25)
- [ ] "What's new" after an update (#26); desktop tests (#27); accessibility and UI polish (#28)
- [ ] Changelog listing tested patches, layouts, languages and sources
