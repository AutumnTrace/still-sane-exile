# Changelog

Every release is a tag `vX.Y.Z` on `main` that matches `<Version>` in `Directory.Build.props`. See [docs/RELEASING.md](docs/RELEASING.md).

## [0.3.0] - 2026-11-27

- **My wealth** is open: keep count of what you hold, tab by tab. Type a count, or import a CSV file that you check row by row before anything is saved. Holdings are valued with the league's prices where there are any.
- **Campaign guide**, first draft: the start of Act 1, from the Riverbank to the Red Vale, with a zone picker, next and back, skip and undo that survive a restart, and a list of missed rewards. The route isn't checked in game yet and says so.
- **Build plans**: type or paste your own notes and SSE turns them into gem steps (cut, upgrade, socket, move a support) for the stage you're at; later steps stay hidden until you get there. A build guide link you save only ever opens in your browser.
- Two optional lines pinned over the game: where you are, and your next gem step. They never take focus and step aside while an item price is showing.
- Quick price on a **side mouse button** (Mouse 4 or 5). A mouse button doesn't make the game copy the item, so the card shows the last item you copied with `Ctrl+C`. Settings warns when a key may clash with another binding or a game default.
- **What's new**: after an update, the main window shows this list once.
- Every control works from the keyboard, with a clear focus ring and a name for screen readers. Faint text and tick boxes have more contrast, and the windows follow the Windows text size setting as far as they fit your screen.
- Item text copied from a game set to another language now says plainly that SSE reads English only for now.
- The practice peek no longer pops up when you press `Ctrl+C` in SSE's own text boxes.
- The installer and updater are now 64-bit, like the app.

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
