# Changelog

Every release is a tag `vX.Y.Z` on `main` that matches `<Version>` in `Directory.Build.props`. See [docs/RELEASING.md](docs/RELEASING.md).

## [0.2.0] - 2026-11-27

- New look from the "UI & overlay" design: cool greys, one blue accent used only for the search, item names in their rarity colours, plain words ("Only 3 for sale — rough price", "Can't tell which stat this is").
- **Price check** replaces Inspect: what items like yours sell for and how many are for sale, then the search behind it. Tick stats, type or step numbers, start from your exact rolls or 10% / 20% lower, buy-now and item-level filters, cheapest matches; it updates as you go. "Open this search on the trade site" and "Copy search".
- `F3` over the game now opens the price check for your last item (`Esc` closes it and hands focus back to the game).
- Quick peek card shows the price, how many are for sale and exactly which stats were searched; currency shows what the stack is worth.
- "How good are your rolls?" with tier bars and open affix slots, and "Prices you write down" next to the price check.
- New first-start screen with a labelled sample card ("How to read a price check").
- Demo profile: synthetic listings made around each practice item, so the search can be practised before live prices exist.
- New app icon: a clear quartz crystal. `scripts/make-icon.ps1` now builds the `.ico` from `Assets/sse.svg`.

## [0.1.1] - 2026-11-27

- The app now goes by **Still Sane, Exile?**, with Zana's question mark, in the window title, header, HUD card, Settings and release names.
- Shortcuts, the Apps list entry and file names keep "Still Sane, Exile": Windows does not allow `?` in file names.
- First update delivered through the automatic updater: installed copies of 0.1.0 pick this up by themselves.

## [0.1.0] - 2026-11-27

First public build: the Milestone 1 foundation and a demo profile.

- Item peek: hold `Ctrl+C` over an item in game; SSE reads the game's copy once and shows a click-through card until release.
- Inspector with roll bars, tiers and sources; hybrid lines in normal copies are flagged as ambiguous instead of guessed.
- Comparison worksheet, official trade-site handoff, and private comparables summarized as median, thin market or one ask.
- Demo profile with synthetic items, ranges, prices and listings, labelled DEMO everywhere.
- Installer and portable build with automatic updates from GitHub releases; updates install when you close SSE.
- Not yet: live prices, a verified game data pack, stash and Aldur labels, Wealth, Guide, Craft.
