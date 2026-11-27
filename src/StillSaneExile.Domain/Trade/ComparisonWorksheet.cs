using System.Text;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Reading;
using StillSaneExile.Domain.Rolls;

namespace StillSaneExile.Domain.Trade;

public enum MatchType
{
    Minimum,
    Maximum,
    Range,
}

public enum FilterGroup
{
    /// <summary>All mandatory rows must match (AND).</summary>
    Mandatory,
    /// <summary>A separate group of alternatives: at least one should match.</summary>
    Optional,
}

public enum RowKind
{
    Derived,
    Explicit,
    Implicit,
    Rune,
    Enchant,
    Other,
}

/// <summary>One stat the player may compare on. Mutable on purpose: the worksheet is an editor.</summary>
public sealed class WorksheetRow
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required RowKind Kind { get; init; }
    public required string Template { get; init; }
    public decimal? Actual { get; init; }
    public StatDirection Direction { get; init; }
    public bool Selected { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public MatchType Match { get; set; }
    public FilterGroup Group { get; set; }

    /// <summary>False when the official site has no matching filter we have verified. Shown as a manual instruction.</summary>
    public bool Supported { get; init; } = true;

    /// <summary>Templates whose value this derived row already includes.</summary>
    public IReadOnlyList<string> Includes { get; init; } = [];
}

public sealed record RelaxationSuggestion(string RowId, string Description, Action<ComparisonWorksheet> Apply);

/// <summary>The query a player builds before checking listings on the official trade site.
/// It never broadens itself: relaxations are offered one change at a time for the player to apply.</summary>
public sealed class ComparisonWorksheet
{
    public const string PhysicalDpsId = "derived.pdps";
    public const string LocalPhysPercent = "#% increased Physical Damage";
    public const string LocalFlatPhys = "Adds # to # Physical Damage";
    public const string LocalAttackSpeed = "#% increased Attack Speed";

    public required string ItemClass { get; init; }
    public required string BaseType { get; init; }
    public required ItemRarity Rarity { get; init; }
    public bool RequireExactBase { get; set; } = true;
    public bool? Corrupted { get; set; }
    public int? MinItemLevel { get; set; }
    public string? ListingDenomination { get; set; }
    public List<WorksheetRow> Rows { get; } = [];

    public IEnumerable<WorksheetRow> Selected => Rows.Where(r => r.Selected);

    /// <summary>Controls that change the market and therefore deserve a deliberate choice.</summary>
    public IEnumerable<string> MarketControls()
    {
        yield return Corrupted switch { true => "Corrupted only", false => "Not corrupted", null => "Corruption: any" };
        if (Rarity is ItemRarity.Normal or ItemRarity.Magic)
        {
            yield return MinItemLevel is { } l ? $"Item level ≥ {l} (crafting base)" : "Item level: any (matters for crafting bases)";
        }

        if (Rarity == ItemRarity.Unique)
        {
            yield return "Exact unique variant";
        }
    }

    public IEnumerable<string> Warnings()
    {
        var selected = Selected.ToList();
        foreach (var derived in selected.Where(r => r.Kind == RowKind.Derived))
        {
            var overlap = selected.Where(r => r.Kind != RowKind.Derived && derived.Includes.Contains(r.Template)).ToList();
            if (overlap.Count > 0)
            {
                yield return $"{derived.Label} already includes {string.Join(", ", overlap.Select(o => o.Label))}. Treat them as one piece of evidence, not several.";
            }
        }

        foreach (var r in selected.Where(r => !r.Supported))
        {
            yield return $"\"{r.Label}\" has no verified trade filter: set it by hand on the trade site.";
        }

        if (selected.Count == 0)
        {
            yield return "No stats selected: the comparison would match any item of this base.";
        }
    }

    public IReadOnlyList<RelaxationSuggestion> SuggestRelaxations(decimal step = 0.1m)
    {
        var list = new List<RelaxationSuggestion>();
        foreach (var r in Selected.Where(r => r.Group == FilterGroup.Mandatory))
        {
            if (r.Match is MatchType.Minimum or MatchType.Range && r.Min is { } min && min > 0)
            {
                var lower = Math.Floor(min * (1 - step));
                list.Add(new RelaxationSuggestion(r.Id, $"Lower {r.Label} minimum from {RollQuality.Num(min)} to {RollQuality.Num(lower)}", w => w.Row(r.Id)!.Min = lower));
            }

            list.Add(new RelaxationSuggestion(r.Id, $"Move {r.Label} to the optional group", w => w.Row(r.Id)!.Group = FilterGroup.Optional));
        }

        if (RequireExactBase)
        {
            list.Add(new RelaxationSuggestion("base", $"Allow any {ItemClass} base instead of {BaseType} only", w => w.RequireExactBase = false));
        }

        return list;
    }

    public WorksheetRow? Row(string id) => Rows.FirstOrDefault(r => r.Id == id);

    /// <summary>Plain-text instructions to set the same search by hand on the official trade site.</summary>
    public string ToManualInstructions(string league)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"League: {league}");
        sb.AppendLine(RequireExactBase ? $"Item base: {BaseType}" : $"Item category: {ItemClass}");
        if (Rarity is not ItemRarity.Unknown and not ItemRarity.Other)
        {
            sb.AppendLine($"Rarity: {Rarity}");
        }

        foreach (var c in MarketControls())
        {
            sb.AppendLine(c);
        }

        if (ListingDenomination is { } d)
        {
            sb.AppendLine($"Buyout currency: {Currencies.Long(d)}");
        }

        void Group(FilterGroup g, string title)
        {
            var rows = Selected.Where(r => r.Group == g).ToList();
            if (rows.Count == 0)
            {
                return;
            }

            sb.AppendLine(title);
            foreach (var r in rows)
            {
                var bounds = r.Match switch
                {
                    MatchType.Minimum => $"min {RollQuality.Num(r.Min)}",
                    MatchType.Maximum => $"max {RollQuality.Num(r.Max)}",
                    _ => $"{RollQuality.Num(r.Min)} to {RollQuality.Num(r.Max)}",
                };
                var manual = r.Supported ? "" : "  (manual: no verified filter)";
                sb.AppendLine($"  - {r.Label}: {bounds}{manual}");
            }
        }

        Group(FilterGroup.Mandatory, "Stat filters (AND):");
        Group(FilterGroup.Optional, "Optional (COUNT ≥ 1):");
        return sb.ToString();
    }
}

public static class WorksheetFactory
{
    private static readonly HashSet<string> AttackWeaponClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bows", "Crossbows", "One Hand Maces", "Two Hand Maces", "Quarterstaves", "Spears", "Flails", "Claws",
        "Daggers", "One Hand Swords", "Two Hand Swords", "One Hand Axes", "Two Hand Axes",
    };

    /// <summary>Small visible preset by item class; everything else is listed but unselected.</summary>
    public static IReadOnlyList<string> PresetFor(string itemClass)
    {
        if (AttackWeaponClasses.Contains(itemClass))
        {
            return [ComparisonWorksheet.PhysicalDpsId, ComparisonWorksheet.LocalAttackSpeed];
        }

        if (itemClass.Equals("Boots", StringComparison.OrdinalIgnoreCase))
        {
            return ["#% increased Movement Speed", "# to maximum Life"];
        }

        return [];
    }

    public static ComparisonWorksheet Create(ItemReading reading, decimal tolerance = 0.1m)
    {
        var item = reading.Item;
        var w = new ComparisonWorksheet
        {
            ItemClass = item.ItemClass,
            BaseType = item.BaseType,
            Rarity = item.Rarity,
            Corrupted = item.States.HasFlag(ItemStates.Corrupted),
            MinItemLevel = item.Rarity is ItemRarity.Normal or ItemRarity.Magic ? item.ItemLevel : null,
        };

        if (reading.PhysicalDps is { } pdps)
        {
            w.Rows.Add(new WorksheetRow
            {
                Id = ComparisonWorksheet.PhysicalDpsId,
                Label = "Physical DPS",
                Kind = RowKind.Derived,
                Template = "Physical DPS",
                Actual = Math.Round(pdps, 1),
                Direction = StatDirection.HigherIsBetter,
                Includes = [ComparisonWorksheet.LocalPhysPercent, ComparisonWorksheet.LocalFlatPhys, ComparisonWorksheet.LocalAttackSpeed],
            });
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in reading.Lines)
        {
            if (l.Line.Components.Count == 0)
            {
                continue;
            }

            var kind = l.Source switch
            {
                ModSource.Implicit => RowKind.Implicit,
                ModSource.Rune => RowKind.Rune,
                ModSource.Enchant => RowKind.Enchant,
                ModSource.Explicit or ModSource.Fractured or ModSource.Desecrated or ModSource.Crafted or ModSource.Unique => RowKind.Explicit,
                _ => RowKind.Other,
            };
            var id = $"{kind}:{l.Line.Template}";
            if (!seen.Add(id))
            {
                continue;
            }

            // Multi-number lines ("Adds # to #") compare on their average, as the trade site does for pseudo values.
            var actual = l.Line.Components.Count == 1 ? l.Line.Components[0].Value : l.Line.Components.Average(c => c.Value);
            w.Rows.Add(new WorksheetRow
            {
                Id = id,
                Label = l.Line.Display,
                Kind = kind,
                Template = l.Line.Template,
                Actual = actual,
                Direction = l.Components.FirstOrDefault()?.Direction ?? StatDirection.Unknown,
                Supported = kind is RowKind.Explicit or RowKind.Implicit or RowKind.Derived,
            });
        }

        foreach (var r in w.Rows)
        {
            r.Match = r.Direction == StatDirection.LowerIsBetter ? MatchType.Maximum : MatchType.Minimum;
            if (r.Actual is { } a)
            {
                r.Min = r.Direction == StatDirection.LowerIsBetter ? null : Math.Floor(a * (1 - tolerance));
                r.Max = r.Direction == StatDirection.LowerIsBetter ? Math.Ceiling(a * (1 + tolerance)) : null;
            }
        }

        foreach (var key in PresetFor(item.ItemClass))
        {
            var row = w.Rows.FirstOrDefault(r => r.Id == key || r.Template == key);
            if (row is not null)
            {
                row.Selected = true;
            }
        }

        return w;
    }
}

public static class TradeHandoff
{
    /// <summary>The official PoE2 trade site. No result-link format is documented, so the site is
    /// opened as-is and the player sets filters from the worksheet.</summary>
    public const string OfficialTradeUrl = "https://www.pathofexile.com/trade2";
}
