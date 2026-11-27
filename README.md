# Still Sane, Exile?

Hold `Ctrl+C` over a Path of Exile 2 item to see what items like it sell for, and exactly which of its stats that price searched with. Release to get back to the game. `F3` opens the full price check over the game, where you choose the stats and numbers to search for and see how good each roll is.

[Download for Windows](https://github.com/AutumnTrace/still-sane-exile/releases/latest) · [Changelog](CHANGELOG.md)

Windows 10/11 · x64 · Windowed or borderless · Game set to English (other languages get a plain "not supported yet" message)

<img src="docs/screenshots/peek-advanced.png" width="384" alt="Demo card for Doom Thirst: items like it sell for about 1.8 to 2.6 divine, 29 for sale, searched with physical DPS of at least 319 and attack speed of at least 15">

Doom Thirst in the demo profile: items like it ask about 1.8–2.6 div, with 29 for sale. The search wanted physical DPS of at least 319 and attack speed of at least 15, 10% under its own rolls.

## Try it on a crossbow

You don't need the game running. Run `StillSaneExile-win-Setup.exe` from the [latest release](https://github.com/AutumnTrace/still-sane-exile/releases/latest) (it installs for your user, no admin rights) or unzip `StillSaneExile-win-Portable.zip` anywhere. The build isn't code-signed yet, so SmartScreen may warn you: choose **More info → Run anyway**. Installed copies apply downloaded updates when you close the app.

On first start, pick **Practice with demo data**, then **Done — try a price check**. SSE opens Doom Thirst, a made-up crossbow.

Doom Thirst has a T1 physical damage prefix. Its 175% roll is 56% of the way through that tier's 170–179%, four short of the best. The attack-speed roll reads 17%, a third of the way through its 16–19% range. Both bars sit under **How good are your rolls?**: each shows how close a roll is to the best number in its tier; tier 1 is the best a stat can be.

All of this is demo data: made-up tiers, ranges and listings. On a live item, an advanced copy (`Ctrl+Alt+C`) gives the game's own tiers and ranges, drawn as a position marker until a verified data pack confirms which end is better. Live market prices and a real-game test run are still to come; until then, a live league prices items only from asks you write down.

## Choose what you're comparing

[![The price check page for Doom Thirst: the price and its search in the middle, roll tiers and written-down prices on the right](docs/screenshots/inspect-advanced.png)](docs/screenshots/inspect-advanced.png)

Tick what a weapon like yours must have, set the numbers, and the price follows.

Press `F3` after a peek and this price check opens over the game. Tick **Physical DPS** and step its minimum: the price, the count and the cheapest matches update as you go. **Open this search on the trade site** opens the official site in your browser; it has no documented link format, so **Copy search** gives you the filters to set there. Asks worth keeping go under **Write down a price**.

The demo's 1.8–2.6 div runs from just above the cheapest of the 29 matching listings to the middle one: made-up asking prices, not sales or a league quote. Eight or more matches read *Good price guess*, three to seven a *Rough price*, one or two *too few to trust*.

<details>
<summary>Why does my 192% physical roll say "Can't tell which stat this is"?</summary>

Storm Mark, the second practice crossbow, is a normal copy. It shows `192% increased Physical Damage` next to `+212 to Accuracy Rating`. A weapon can roll a pure physical damage prefix and a hybrid prefix that adds both physical damage and accuracy, and a normal copy adds them together into one line. From the total alone, more than one pair of tiers fits, so SSE doesn't guess: both lines get no bar, stay out of the starting search, and list the explanations that fit when you hover them.

An advanced copy (`Ctrl+Alt+C`) prints each affix on its own with its tier and range, which settles it. In the demo, those tiers come from the made-up data pack.

[![Storm Mark on the price check page: both merged lines read "Can't tell which stat this is — Ctrl+Alt+C shows it"](docs/screenshots/inspect-hybrid.png)](docs/screenshots/inspect-hybrid.png)

</details>

## What the app reads when you press a key

| Key | What it does |
|---|---|
| Hold `Ctrl+C` | Shows the card for the item under your cursor; let go to hide it |
| Hold `Ctrl+Alt+C` | The same card from the game's advanced copy, with tiers and ranges |
| `F3` | Opens the price check for your last item over the game; `Esc` closes it |

Settings can change every key, and a toggle mode can replace holding.

The game copies the item. SSE reads the changed clipboard once for that press and draws a click-through card that never takes focus; releasing the key hides it. `F3` reopens the saved item without another read, and takes focus so you can type until you close it. SSE never sends input to the game. Its only network request is the update check against this repository's GitHub releases, which Settings can switch off. Details: [Capture and privacy](docs/CAPTURE-AND-PRIVACY.md).

## What's next

Shared market data comes next, bringing prices to live leagues and held-key price labels to stash and Aldur reward panels. Wealth history, the campaign guide and crafting plans follow; see the [roadmap](docs/ROADMAP.md). Players can help with two open issues: [English item copies to check the parser against](https://github.com/AutumnTrace/still-sane-exile/issues/1) and [how the card behaves on your screen](https://github.com/AutumnTrace/still-sane-exile/issues/5). Item text and measurements are enough; leave out account names and stash screenshots.

## Build from source

You need Windows 10 or 11 (x64) and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/AutumnTrace/still-sane-exile.git
cd still-sane-exile
dotnet run --project src/StillSaneExile.Desktop
```

Run the tests:

```powershell
dotnet test
```

Add `-- --data <empty folder>` for a clean profile. `src/StillSaneExile.Domain` owns parsing and the comparison rules, with no Windows or network code; `src/StillSaneExile.Desktop` owns WPF, foreground detection and clipboard access. See also [data sources and coverage](docs/SOURCES.md), [cutting a release](docs/RELEASING.md) and [third-party licences](docs/THIRD-PARTY.md).

Path of Exile and Path of Exile 2 are trademarks of Grinding Gear Games. This product isn't affiliated with or endorsed by Grinding Gear Games in any way.
