# Roadmap

Each milestone ships only what passed its fixtures **and** its real-device acceptance check. Anything else is exposed with its actual availability.

## 0 · Capability and data spike

- [x] Source matrix and real-versus-fixture table ([SOURCES.md](SOURCES.md))
- [x] Instrumentation: one physical press → at most one read; zero game inputs; zero market requests (unit tests + live counters)
- [ ] Consensual real English normal and advanced copies (weapon, armour, unique, currency)
- [ ] Real dedicated and ordinary stash captures, an Aldur panel with a bonus, screenshots at two UI scales
- [ ] One-shot frame capture spike in borderless mode on real hardware
- [ ] Re-check policy, terms, patch and provider coverage after the 11 December 2026 full release

## 1 · Item inspector (in progress: v0.1.0)

- [x] Native-copy path: hold `Ctrl+C`, read once after the game's copy, click-through HUD card, release hides
- [x] Item model with separate sources (explicit, implicit, rune, enchant, unique, desecrated, fractured)
- [x] Roll bars from advanced-copy ranges, or a data pack when exactly one explanation fits; hybrid ambiguity
- [x] Editable comparison worksheet, official trade-site handoff, manual comparables with the evidence policy
- [x] Deterministic demo profile with synthetic fixtures and visible DEMO state
- [ ] A verified (non-synthetic) data pack for one weapon family, checked against real advanced copies
- [ ] Real-game acceptance: hold to inspect a rolled weapon, identify a hybrid ambiguity, compare chosen stats

## 2 · Economy and held labels

- [ ] Small ASP.NET Core cache service: poe.ninja league list, exchange and item overviews; GGG exchange digest
- [ ] Rate-limit handling: policy/rule/state headers, shared buckets, 429 pause, circuit breaker, persisted cooldowns
- [ ] League and currency controls fed by the league list; atomic league switch with fetch in flight
- [ ] Dedicated currency tab and Aldur rows from one frame per press; unknown rows abstain
- [ ] Validated generic-grid subset

## 3 · Wealth

- [ ] Snapshot preview, tab identity and reconciliation, partial-coverage rules
- [ ] Manual quantities and CSV import
- [ ] Value at the time versus revalue today; session rate with transfers netting to zero

## 4 · Guide

- [ ] Versioned zone graph and original route pack; manual zone picker
- [ ] Stage-aware gem actions; Mobalytics link saved plus user-authored steps; authorized import only
- [ ] Early-campaign acceptance journey from the spec

## 5 · Craft

- [ ] One weapon family; verified Augmentation, Regal and Exalted operations
- [ ] Bounded route search with success and failure branches, budget stop, re-import after each real step

## Release

- [ ] Windows smoke test, accessibility pass, provider-failure exercises
- [ ] Installer plus portable build, version tag, changelog listing tested patches, layouts, languages and sources
