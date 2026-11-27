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
| Path of Exile 2 trade site metadata (`data/trade/trade-data.json`) | The trade site's own ids, so "Open this search on the trade site" opens with the search filled in: stat filter ids and texts (explicit, implicit, enchant and augment groups), item category and filter options, base type and unique names | Grinding Gear Games' public trade metadata from `https://www.pathofexile.com/api/trade2/data/stats`, `/filters` and `/items`, trimmed by `scripts/update-trade-data.ps1`; the source URLs and fetch date (27 Nov 2026) are in the file. Ids and labels only, no listings. No licence is stated |

Apart from that trade metadata, no game data, artwork or guide text is bundled. Everything under `fixtures/` was written for this repository and is synthetic.

Path of Exile and Path of Exile 2 are trademarks of Grinding Gear Games. This product isn't affiliated with or endorsed by Grinding Gear Games in any way.
