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

    /// <summary>False when the trade site link cannot carry this row (no stat id for it in the trade
    /// data). Shown as a stat to set by hand.</summary>
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

    /// <summary>The item level the "item level or higher" choice uses: the item's own level for a
    /// crafting base (normal, magic), rounded down to a multiple of 5 otherwise.</summary>
    public int? SuggestedItemLevel { get; init; }

    /// <summary>Only listings that can be bought at once, without messaging the seller.</summary>
    public bool BuyNowOnly { get; set; } = true;

    public string? ListingDenomination { get; set; }
    public List<WorksheetRow> Rows { get; } = [];

    public IEnumerable<WorksheetRow> Selected => Rows.Where(r => r.Selected);

    /// <summary>Controls that change the market and therefore deserve a deliberate choice.</summary>
    public IEnumerable<string> MarketControls()
    {
        yield return Corrupted switch { true => "Corrupted only", false => "Not corrupted", null => "Corruption: any" };
        if (MinItemLevel is { } l)
        {
            yield return Rarity is ItemRarity.Normal or ItemRarity.Magic ? $"Item level ≥ {l} (crafting base)" : $"Item level ≥ {l}";
        }
        else if (Rarity is ItemRarity.Normal or ItemRarity.Magic)
        {
            yield return "Item level: any (matters for crafting bases)";
        }

        if (BuyNowOnly)
        {
            yield return "Buy-now listings only";
        }

        if (Rarity == ItemRarity.Unique)
        {
            yield return "Exact unique variant";
        }
    }

    /// <summary>Sets every row's starting bound from the item's own number: <paramref name="tolerance"/>
    /// 0 searches for the exact roll, 0.1 for 10% worse, and so on. Which rows are selected is kept.</summary>
    public void SetStartingBounds(decimal tolerance)
    {
        foreach (var r in Rows)
        {
            r.Match = r.Direction == StatDirection.LowerIsBetter ? MatchType.Maximum : MatchType.Minimum;
            if (r.Actual is { } a)
            {
                r.Min = r.Direction == StatDirection.LowerIsBetter ? null : Math.Floor(a * (1 - tolerance));
                r.Max = r.Direction == StatDirection.LowerIsBetter ? Math.Ceiling(a * (1 + tolerance)) : null;
            }
        }
    }

    /// <summary>Copies every search choice from another worksheet for the same item (rows matched by id).
    /// Used to reset a worksheet to the defaults without replacing the object people are editing.</summary>
    public void CopySearchFrom(ComparisonWorksheet other)
    {
        RequireExactBase = other.RequireExactBase;
        Corrupted = other.Corrupted;
        MinItemLevel = other.MinItemLevel;
        BuyNowOnly = other.BuyNowOnly;
        foreach (var r in Rows)
        {
            if (other.Row(r.Id) is { } o)
            {
                r.Selected = o.Selected;
                r.Min = o.Min;
                r.Max = o.Max;
                r.Match = o.Match;
                r.Group = o.Group;
            }
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
            yield return $"\"{r.Label}\" isn't in the trade site link: set it by hand on the trade site.";
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
                var manual = r.Supported ? "" : "  (set by hand: not in the link)";
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

    /// <param name="trade">The trade site's ids. With them, a row is <see cref="WorksheetRow.Supported"/>
    /// only when the trade site link can carry it; without, explicit and implicit lines are assumed to be.</param>
    public static ComparisonWorksheet Create(ItemReading reading, decimal tolerance = 0.1m, TradeData? trade = null)
    {
        var item = reading.Item;
        var w = new ComparisonWorksheet
        {
            ItemClass = item.ItemClass,
            BaseType = item.BaseType,
            Rarity = item.Rarity,
            Corrupted = item.States.HasFlag(ItemStates.Corrupted),
            MinItemLevel = item.Rarity is ItemRarity.Normal or ItemRarity.Magic ? item.ItemLevel : null,
            SuggestedItemLevel = item.ItemLevel is not { } il ? null
                : item.Rarity is ItemRarity.Normal or ItemRarity.Magic ? il
                : il / 5 * 5,
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
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
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
            if (l.Status == LineStatus.Ambiguous)
            {
                ambiguous.Add(id);
            }

            if (!seen.Add(id))
            {
                continue;
            }

            // Multi-number lines ("Adds # to #") compare on their average, as the trade site's stat filters do.
            var actual = l.Line.Components.Count == 1 ? l.Line.Components[0].Value : l.Line.Components.Average(c => c.Value);
            w.Rows.Add(new WorksheetRow
            {
                Id = id,
                Label = l.Line.Display,
                Kind = kind,
                Template = l.Line.Template,
                Actual = actual,
                Direction = l.Components.FirstOrDefault()?.Direction ?? StatDirection.Unknown,
                Supported = trade is null
                    ? kind is RowKind.Explicit or RowKind.Implicit
                    : TradeQuery.CanLink(kind, id, l.Line.Template, item.ItemClass, trade),
            });
        }

        w.SetStartingBounds(tolerance);

        // A line that could be two different stats stays out of the starting search; the player can still tick it.
        foreach (var key in PresetFor(item.ItemClass))
        {
            var row = w.Rows.FirstOrDefault(r => r.Id == key || r.Template == key);
            if (row is not null && !ambiguous.Contains(row.Id))
            {
                row.Selected = true;
            }
        }

        return w;
    }
}
