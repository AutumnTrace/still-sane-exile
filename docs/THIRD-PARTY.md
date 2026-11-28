# Third-party components

| Component | Use | Licence |
|---|---|---|
| .NET 10 / WPF | Runtime and UI | MIT |
| CommunityToolkit.Mvvm | View-model source generators | MIT |
| Microsoft.Data.Sqlite | Local settings and comparables | MIT |
| Velopack (library and `vpk` CLI) | Installer, portable build, delta updates | MIT |
| SQLitePCLRaw (via Microsoft.Data.Sqlite) | SQLite native binding | Apache-2.0 |
| SQLite | Embedded database | Public domain |
| xUnit, Microsoft.NET.Test.Sdk, coverlet | Tests only | Apache-2.0 / MIT / MIT |
| SkiaSharp | `tools/TreeArt` only, a developer tool run by `scripts/update-passive-tree.ps1` to turn the passive tree export's WebP sheets into JPEG and PNG. Not shipped | MIT |
| Path of Exile 2 trade site metadata (`data/trade/trade-data.json`) | The trade site's own ids, so "Open this search on the trade site" opens with the search filled in: stat filter ids and texts (explicit, implicit, enchant and augment groups), item category and filter options, base type and unique names | Grinding Gear Games' public trade metadata from `https://www.pathofexile.com/api/trade2/data/stats`, `/filters` and `/items`, trimmed by `scripts/update-trade-data.ps1`; the source URLs and fetch date (27 Nov 2026) are in the file. Ids and labels only, no listings. No licence is stated |
| Path of Exile 2 passive tree (`data/tree/poe2-tree.json`) | The tree the campaign guide draws a pasted build on: node ids, names, kinds, positions, ascendancies, stat lines, icons and mastery patterns, and the connections between nodes | Grinding Gear Games' official export, [github.com/grindinggear/poe2-skilltree-export](https://github.com/grindinggear/poe2-skilltree-export), which GGG's developer docs list under "Data Exports" as the game data it provides outside its APIs. Trimmed by `scripts/update-passive-tree.ps1`; the commit, game version (0.5.5) and fetch date (28 Nov 2026) are in the file. No licence is stated |
| Path of Exile 2 passive tree art (`data/tree/art/`) | The pictures the guide draws the tree with, as the game does: node icons, frames, connectors and orbit arcs, the ground, the centre circle, mastery patterns | Grinding Gear Games' art, from the `assets/` of the same export, turned from WebP into JPEG and PNG (two sheets at half size) with an atlas by `scripts/update-passive-tree.ps1` and `tools/TreeArt`; 4.9 MB. The owner chose the game's art on 28 Nov 2026, as for the currency pictures ([SOURCES.md](SOURCES.md), row 19). No licence is stated |

Apart from that trade metadata and passive tree, no game data or guide text is bundled; the only game art is the passive tree's pictures above and the currency pictures in [SOURCES.md](SOURCES.md), row 19: the Divine, Exalted and Chaos Orbs drawn next to prices, and the orbs, essences and omens of the craft simulator. Everything under `fixtures/` was written for this repository and is synthetic: the practice build in `fixtures/builds/` uses real gem names and tree node ids, with made-up choices, items and notes.

Path of Exile and Path of Exile 2 are trademarks of Grinding Gear Games. This product isn't affiliated with or endorsed by Grinding Gear Games in any way.
