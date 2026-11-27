# Capture and privacy

## What runs while SSE is open

| Mechanism | Windows API | Why | Never |
|---|---|---|---|
| Keyboard observer | `SetWindowsHookEx(WH_KEYBOARD_LL)` in SSE's own process | Notice physical presses and releases of your bindings | Swallow, delay, change or send a key. Events flagged as injected are ignored. |
| Foreground watcher | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND / MINIMIZESTART)`, out of context | Only react while the game is in front; hide on focus change or minimize | Inspect another process beyond its window title and executable name |
| Clipboard change counter | `GetClipboardSequenceNumber` | Wait until the game's own copy has landed | Read content (it is a counter) |
| Clipboard read | `Clipboard.GetText`, once per qualifying press | Get the item text the game just copied | Read again on auto-repeat or release; read when the game copied nothing |
| HUD card | Topmost, layered, click-through, no-activate window | Show the result over the game | Take focus or intercept clicks |

## What is stored

- **Settings** (`sse.db`, SQLite) in `%APPDATA%\StillSaneExile`, or in `data\` beside the portable build. Program files live separately in `%LOCALAPPDATA%\StillSaneExile` and are replaced on each update; uninstalling removes them but keeps your settings.
- **Comparison sets** you create by adding a comparable: the asks you typed and the item text they compare against.
- **Not stored:** clipboard text that is not an item (discarded immediately), recent items (memory only, cleared on exit), key presses, screen pixels.

## Network

The only request SSE makes itself is the update check: an anonymous call to GitHub's releases API for this repository at start and every 6 hours, plus the package download when there is a new version. It sends nothing about you or your game, and Settings → "Check for updates automatically" turns it off. "Open official trade site" asks Windows to open your default browser. The Milestone 2 economy cache will fetch public market data only; screen pixels, clipboard text, stash contents and build notes stay local by default.

## Demo isolation

The demo profile reads `fixtures/` only, uses a fixed demo clock (so ages are the same on every run), and is labelled DEMO in the header and on the HUD card. `PriceBookGuard` refuses a synthetic price book in a live profile and any price book from another league.
