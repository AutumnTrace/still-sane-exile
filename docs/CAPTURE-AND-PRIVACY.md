# Capture and privacy

## What runs while SSE is open

| Mechanism | Windows API | Why | Never |
|---|---|---|---|
| Keyboard observer | `SetWindowsHookEx(WH_KEYBOARD_LL)` in SSE's own process | Notice physical presses and releases of your bindings | Swallow, delay, change or send a key. Events flagged as injected are ignored. |
| Foreground watcher | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND / MINIMIZESTART)`, out of context | Only react while the game is in front; hide on focus change or minimize | Inspect another process beyond its window title and executable name |
| Clipboard change counter | `GetClipboardSequenceNumber` | Wait until the game's own copy has landed | Read content (it is a counter) |
| Clipboard read | `Clipboard.GetText`, once per qualifying press | Get the item text the game just copied | Read again on auto-repeat or release; read when the game copied nothing |
| HUD card | Topmost, layered, click-through, no-activate window | Show the result over the game | Take focus or intercept clicks |
| Price check window (`F3`) | Topmost window that takes focus while open; `SetForegroundWindow` hands focus back to the game when it closes | Tick stats and type numbers over the game | Send input to the game; stay open once you press `Esc`, press `F3` again or click back into the game |

## What is stored

- **Settings** (`sse.db`, SQLite) in `%APPDATA%\StillSaneExile`, or in `data\` beside the portable build. Program files live separately in `%LOCALAPPDATA%\StillSaneExile` and are replaced on each update; uninstalling removes them but keeps your settings.
- **Comparison sets** you create by adding a comparable: the asks you typed and the item text they compare against.
- **Holdings** you type in or import on My wealth: league, tab, item id, variant, quantity and snapshot time, plus the file's name (not its folder) for an import. The CSV file itself is only read when you pick it, never copied or uploaded. Format: [Wealth CSV import](WEALTH-CSV.md).
- **Not stored:** clipboard text that is not an item (discarded immediately), recent items (memory only, cleared on exit), key presses, screen pixels.

## Network

The only request SSE makes itself is the update check: an anonymous call to GitHub's releases API for this repository at start and every 6 hours, plus the package download when there is a new version. It sends nothing about you or your game, and Settings → "Check for updates automatically" turns it off. "Open this search on the trade site" asks Windows to open your default browser on the official trade site; "Copy search" puts the filters on the clipboard as text. The Milestone 2 economy cache will fetch public market data only; screen pixels, clipboard text, stash contents and build notes stay local by default.

## Demo isolation

The demo profile reads `fixtures/` only, uses a fixed demo clock (so ages are the same on every run), and is labelled DEMO in the header, the price check and the HUD card. Its listings are synthetic: made up around each item from a fixed seed, so the same item gets the same listings on every run. A live profile has no listings until live prices arrive. `PriceBookGuard` refuses a synthetic price book in a live profile and any price book from another league.
