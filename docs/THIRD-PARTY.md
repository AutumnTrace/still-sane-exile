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
| Path of Exile 2 modifier and base data (`fixtures/packs/practice-0.5.5.json`) | The practice pack the demo reads its practice items with and the craft simulator crafts with: patch 0.5.5's crossbow modifier families (names, sides, tiers, levels, ranges, wording), the life, movement speed and fire resistance families of the practice boots and helmet, the Siege Crossbow base and the tier Essence of Abrasion and Essence of Haste add on a crossbow. Its weights are made up | Grinding Gear Games' item data, as [Path of Building Community for PoE2](https://github.com/PathOfBuildingCommunity/PathOfBuilding-PoE2) exports it (commit `49e9392`, "Export 0.5.5 data", 10 Sep 2026; [SOURCES.md](SOURCES.md), row 25). No licence is stated for the game data; GGG's developer docs say it provides no game data outside its APIs. Path of Building's code is MIT, Copyright (c) 2016 David Gowor; its notice is below and at the top of the pack |

Apart from that trade metadata, passive tree and practice pack, no game data or guide text is bundled; the only game art is the passive tree's pictures above and the currency pictures in [SOURCES.md](SOURCES.md), row 19: the Divine, Exalted and Chaos Orbs drawn next to prices, and the orbs, essences and omens of the craft simulator. Everything else under `fixtures/` was written for this repository and is synthetic. The practice items in `fixtures/items/` are made up (names and rolls) on real 0.5.5 bases, with the game's tiers and ranges from the same export; the practice build in `fixtures/builds/` uses real gem names and tree node ids, with made-up choices, items and notes.

## Path of Building Community (MIT)

The practice pack's numbers were read from Path of Building Community's export, which is distributed under this licence:

```
Copyright (c) 2016 David Gowor

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Path of Exile and Path of Exile 2 are trademarks of Grinding Gear Games. This product isn't affiliated with or endorsed by Grinding Gear Games in any way.
