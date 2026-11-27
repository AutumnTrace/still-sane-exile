# Source matrix and coverage

Milestone 0 asks for a source matrix, real-versus-fixture coverage, and proof that one input causes at most one read. This file tracks the first two. The third is covered by `CaptureGateTests` and the live counters in the status bar.

The source ledger below comes from the project handoff spec (reviewed 27 November 2026). **It has not been re-verified in this repository.** Recheck each source before a release and whenever a pack or provider changes.

## Providers and references

| # | Source | What it allows / says (per spec) | How SSE uses it | Status |
|---|---|---|---|---|
| 1 | GGG developer policy, resources, rate limits | Independent executables allowed. Users must know what is read. Screen/clipboard reads limited to once per manual keypress. Extra restrictions on game-affecting macros. New app registrations currently closed. | Physical input only, one read per press, zero game inputs, explicit "what each key reads" in onboarding. Market requests keep headroom in every rate-limit window, stop on 429, 401/403 and repeated errors, and never rotate proxies or accounts: see [Provider etiquette](PROVIDER-ETIQUETTE.md) | Followed by design. The rate-limit client is built and tested but not used yet (#11) |
| 2 | GGG Terms of Use | Governs the account and client | No automation of game input; no client modification | Followed by design |
| 3 | GGG API reference | PoE2 has a historical currency-exchange digest (`https://web.poecdn.com/api/currency-exchange/poe2/<hour>`). Account, guild and public stash APIs are PoE1-only. No general rare-listing search API. | Exchange digest as an independent check/fallback; wealth from explicit snapshots; rares through a manual trade-site workflow | Digest math implemented (`ExchangeDigestMath`); fetching is Milestone 2 |
| 4 | poe.ninja economy API | Documented PoE2 league, exchange and item overviews; caching and backend-proxy expectations | First production pricing integration, through a shared backend cache | Milestone 2 |
| 5 | Exiled Exchange 2 docs | Normal copy gives readable text; advanced copy (highlight modifier) adds affix metadata | Parser supports both copies | Parser built on synthetic fixtures; **needs real copies** |
| 6 | PoE2DB | Local physical damage families, generation type, tier ranges and levels (e.g. Merciless 170–179, level 82) | Verification of a real data pack; link-outs | Not bundled. The synthetic pack reuses that one example. |
| 7 | Craft of Exile (PoE2) | Calculator and emulator; extrapolated weights; its game version can differ | Independent check of craft routes | Milestone 5 |
| 8 | GGG Return of the Ancients | Aldur Remnant panel with selectable rewards and a common bonus | Row geometry plus per-row reward identity | Milestone 2 |
| 9 | Aldur Appraiser | Separates selectable rewards from the shared bonus | Design reference | Milestone 2 |
| 10 | Community campaign guide (0.5.5) | Route structure and optional steps | Structure reference only; original route text | Route engine built. The shipped Act 1 draft was written from general campaign knowledge, copies no prose and is marked unverified until walked in game ([GUIDE.md](GUIDE.md)) |
| 11 | MisoxShiru's Varashta guide (0.5.5) | Stage-specific gem instructions | Manual acceptance journey; no redistribution of prose or art | Stage gating built. The journey (cut Entangle, then Contagion; move Zenith I to Essence Drain only once it exists) is a unit test with notes written for SSE; nothing from the guide is in the repo. The manual run against the guide is still to do ([GUIDE.md](GUIDE.md)) |
| 12 | Mobalytics Terms of Service | Restrict software access, downloads, mirroring and framing | Save link, open in browser, user-authored steps; import only through an authorized export | Followed by design: the link is saved and opened in the browser on a click; steps come from the player's own notes; no fetching, embedding or import ([GUIDE.md](GUIDE.md)) |
| 13 | Wealthy Exile | Per-tab breakdowns, snapshots, currency per hour | Interaction reference | Milestone 3 |
| 14 | PoE2Scout | Swagger API | Optional adapter after usage terms are checked; no production dependency | Not planned |
| 15 | GGG full-release announcement | Full release on 11 December 2026 | 0.5.5 guides become a historical test pack until re-reviewed | Noted |
| 16 | Regal Orb (PoE2) | Upgrades magic to rare and adds one modifier | Craft model | Milestone 5 |

## Real captures versus fixtures

| Capture | Needed for | Real samples collected | Fixture coverage |
|---|---|---|---|
| English normal copy (rare weapon) | Parser, roll bars, worksheet | **0** | `rare-crossbow-normal-hybrid.txt`, `rare-boots-normal.txt` |
| English advanced copy (rare weapon) | Tiers and ranges | **0** | `rare-crossbow-advanced.txt` |
| Unique, magic, currency copies | Pricing paths | **0** | `unique-ring.txt`, `magic-crossbow-normal.txt`, `currency-exalted.txt` |
| Dedicated currency stash tab | Stash labels | **0** | none yet |
| Ordinary / quad stash tab | Grid labels | **0** | none yet |
| Aldur panel with common bonus | Reward ranking | **0** | none yet |
| Screenshots at two UI scales | Calibration | **0** | none yet |

All fixture text was written for this repo to match the documented copy format. The copy format, the advanced-copy header shape (`{ Prefix Modifier "Name" (Tier: N) — Tags }`) and the property names have **not** been checked against the live game. Before any compatibility claim, collect consensual real copies and add them under `fixtures/real/` with their patch and locale.

## One input, at most one read

- `CaptureGate` (domain) allows a read only on the first physical key-down after a key-up, only while the game (or SSE itself, in demo practice) is in front. Auto-repeat and synthetic input are ignored. Late results are dropped by generation id.
- `CaptureGateTests.Ten_second_hold_with_autorepeat_reads_once` holds the key for 300 auto-repeat events and asserts exactly one read and zero game actions.
- The status bar shows the live counters. The HUD path has no HTTP client, so market requests are 0 by construction.
- Still to do on real hardware: a timed trace in the game at 1080p, 1440p, ultrawide and 4K, mixed-DPI monitors and 100/125/150% scaling.
