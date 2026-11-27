using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using StillSaneExile.Domain.Items;

namespace StillSaneExile.Domain.Trade;

/// <summary>A worksheet turned into the official trade site's search. <see cref="ByHand"/> names what
/// the link could not carry, for the player to set on the site.</summary>
/// <param name="League">The league the link searches.</param>
/// <param name="Json">The site's query, as its own "Trade" links pass it in <c>?q=</c>.</param>
/// <param name="ByHand">Short labels of what to set by hand ("+18% to Fire Resistance", "Item base (…)").</param>
/// <param name="Unmapped">The ticked stats among <see cref="ByHand"/>.</param>
public sealed record TradeSearch(string League, string Json, IReadOnlyList<string> ByHand, IReadOnlyList<WorksheetRow> Unmapped)
{
    public string Url => TradeLink.Search(League, Json);
}

public static class TradeLink
{
    /// <summary>The official Path of Exile 2 trade site.</summary>
    public const string Site = "https://www.pathofexile.com/trade2";

    /// <summary>The demo league is made up, so demo links search Standard instead.</summary>
    public const string DemoLeague = "Standard";

    /// <summary>A search the site fills in when it opens: <c>/trade2/search/{league}?q={query}</c>, the
    /// format poe.ninja's PoE2 "Trade" links use. The site then redirects to its own saved search.</summary>
    public static string Search(string league, string json) =>
        $"{Site}/search/{Uri.EscapeDataString(league)}?q={Uri.EscapeDataString(json)}";
}

/// <summary>Builds the trade site's search from a comparison worksheet, using only the ids in
/// <see cref="TradeData"/>. Pure: no requests, and the same worksheet always gives the same text.</summary>
public static class TradeQuery
{
    /// <summary>"Instant Buyout": the default, because asks without it are often a seller unsure of the price.</summary>
    public const string BuyNowStatus = "securable";

    /// <summary>"Instant Buyout and In Person": both kinds, as SSE's own search counts them with
    /// "Buy-now listings only" unticked. ("online" would be in-person listings only.)</summary>
    public const string AnyListingStatus = "available";

    /// <summary>Item class as the game copies it → the trade site's category option.</summary>
    private static readonly Dictionary<string, string> CategoryByClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Claws"] = "weapon.claw",
        ["Daggers"] = "weapon.dagger",
        ["Wands"] = "weapon.wand",
        ["One Hand Swords"] = "weapon.onesword",
        ["One Hand Axes"] = "weapon.oneaxe",
        ["One Hand Maces"] = "weapon.onemace",
        ["Sceptres"] = "weapon.sceptre",
        ["Spears"] = "weapon.spear",
        ["Flails"] = "weapon.flail",
        ["Bows"] = "weapon.bow",
        ["Staves"] = "weapon.staff",
        ["Two Hand Swords"] = "weapon.twosword",
        ["Two Hand Axes"] = "weapon.twoaxe",
        ["Two Hand Maces"] = "weapon.twomace",
        ["Quarterstaves"] = "weapon.warstaff",
        ["Crossbows"] = "weapon.crossbow",
        ["Talismans"] = "weapon.talisman",
        ["Fishing Rods"] = "weapon.rod",
        ["Helmets"] = "armour.helmet",
        ["Body Armours"] = "armour.chest",
        ["Gloves"] = "armour.gloves",
        ["Boots"] = "armour.boots",
        ["Shields"] = "armour.shield",
        ["Bucklers"] = "armour.buckler",
        ["Foci"] = "armour.focus",
        ["Quivers"] = "armour.quiver",
        ["Amulets"] = "accessory.amulet",
        ["Rings"] = "accessory.ring",
        ["Belts"] = "accessory.belt",
        ["Jewels"] = "jewel",
        ["Life Flasks"] = "flask.life",
        ["Mana Flasks"] = "flask.mana",
        ["Charms"] = "flask.charm",
        ["Waystones"] = "map.waystone",
        ["Relics"] = "sanctum.relic",
        ["Skill Gems"] = "gem.activegem",
        ["Support Gems"] = "gem.supportgem",
        ["Stackable Currency"] = "currency",
    };

    /// <summary>Lines the site keeps a "(Local)" version of that are local on a weapon but not on armour:
    /// gloves' attack speed and a helmet's accuracy count for the character.</summary>
    private static readonly HashSet<string> WeaponOnlyLocal = new(StringComparer.Ordinal)
    {
        ComparisonWorksheet.LocalAttackSpeed,
        "# to Accuracy Rating",
    };

    private static readonly JsonWriterOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>All the category options SSE writes, so a test can check each against the data file.</summary>
    public static IEnumerable<string> KnownCategories => CategoryByClass.Values.Distinct();

    public static string? CategoryFor(string itemClass) => CategoryByClass.GetValueOrDefault(itemClass.Trim());

    /// <summary>The trade site's stat group for a worksheet row kind. Fractured, desecrated, crafted and
    /// unique lines are explicit rows, and are searched as explicit stats.</summary>
    public static string? GroupFor(RowKind kind) => kind switch
    {
        RowKind.Explicit => "explicit",
        RowKind.Implicit => "implicit",
        RowKind.Enchant => "enchant",
        RowKind.Rune => "rune",
        _ => null,
    };

    /// <summary>The trade site's stat id for a line on an item of <paramref name="itemClass"/>, or null when
    /// the data has none (or more than one, so SSE cannot tell which).</summary>
    public static string? StatIdFor(RowKind kind, string template, string itemClass, TradeData data)
    {
        if (GroupFor(kind) is not { } group)
        {
            return null;
        }

        if (IsLocal(CategoryFor(itemClass), template) && data.TryStat(group, template, local: true, out var local))
        {
            return local;
        }

        return data.TryStat(group, template, local: false, out var id) ? id : null;
    }

    /// <summary>Whether the link can carry this row: physical DPS through the site's equipment filter,
    /// other rows through a stat id.</summary>
    public static bool CanLink(RowKind kind, string id, string template, string itemClass, TradeData data) =>
        kind == RowKind.Derived ? id == ComparisonWorksheet.PhysicalDpsId : StatIdFor(kind, template, itemClass, data) is not null;

    public static TradeSearch Build(ComparisonWorksheet w, ItemSnapshot item, string league, TradeData data)
    {
        var byHand = new List<string>();
        var unmapped = new List<WorksheetRow>();
        var category = CategoryFor(w.ItemClass) is { } c && (data.Categories.Count == 0 || data.Categories.Contains(c)) ? c : null;

        string? name = null, type = null;
        if (w.Rarity == ItemRarity.Unique)
        {
            if (item.Name is { } n && Knows(data.Uniques, n))
            {
                name = n;
            }
            else
            {
                byHand.Add(item.Name is null ? "Unique name" : $"Name ({item.Name})");
            }

            type = KnownBase(w.BaseType, data, byHand);
        }
        else if (w.Rarity == ItemRarity.Magic)
        {
            // A magic name wraps the base in its affixes ("Merciless Bombard Crossbow of Fame").
            type = w.RequireExactBase ? BaseInside(w.BaseType, data) : null;
            if (w.RequireExactBase && type is null)
            {
                byHand.Add("Item base");
            }
        }
        else if (w.RequireExactBase)
        {
            type = KnownBase(w.BaseType, data, byHand);
        }

        if (type is null && category is null)
        {
            byHand.Add($"Item category ({w.ItemClass})");
        }

        var rarity = w.Rarity switch
        {
            ItemRarity.Normal => "normal",
            ItemRarity.Magic => "magic",
            ItemRarity.Rare => "rare",
            ItemRarity.Unique when name is null => "unique", // a known name already means that unique
            _ => null,
        };

        var mandatory = new List<(string Id, WorksheetRow Row)>();
        var optional = new List<(string Id, WorksheetRow Row)>();
        WorksheetRow? pdps = null;
        foreach (var r in w.Selected)
        {
            if (r.Id == ComparisonWorksheet.PhysicalDpsId && r.Group == FilterGroup.Mandatory)
            {
                pdps = r;
            }
            else if (r.Kind != RowKind.Derived && StatIdFor(r.Kind, r.Template, w.ItemClass, data) is { } id)
            {
                (r.Group == FilterGroup.Optional ? optional : mandatory).Add((id, r));
            }
            else
            {
                // Includes physical DPS moved to the optional group: the site's DPS filter is always required.
                unmapped.Add(r);
                byHand.Add(r.Label);
            }
        }

        using var buffer = new MemoryStream();
        using (var j = new Utf8JsonWriter(buffer, Compact))
        {
            j.WriteStartObject();
            j.WriteStartObject("query");
            j.WriteStartObject("status");
            j.WriteString("option", w.BuyNowOnly ? BuyNowStatus : AnyListingStatus);
            j.WriteEndObject();
            if (name is not null)
            {
                j.WriteString("name", name);
            }

            if (type is not null)
            {
                j.WriteString("type", type);
            }

            if (mandatory.Count > 0 || optional.Count > 0)
            {
                j.WriteStartArray("stats");
                if (mandatory.Count > 0)
                {
                    WriteStatGroup(j, "and", mandatory, countMin: null);
                }

                if (optional.Count > 0)
                {
                    WriteStatGroup(j, "count", optional, countMin: 1);
                }

                j.WriteEndArray();
            }

            var filters = new List<(string Group, Action<Utf8JsonWriter> Write)>();
            if (category is not null || rarity is not null || w.MinItemLevel is not null)
            {
                filters.Add(("type_filters", f =>
                {
                    if (category is not null)
                    {
                        Option(f, "category", category);
                    }

                    if (rarity is not null)
                    {
                        Option(f, "rarity", rarity);
                    }

                    if (w.MinItemLevel is { } level)
                    {
                        f.WriteStartObject("ilvl");
                        f.WriteNumber("min", level);
                        f.WriteEndObject();
                    }
                }));
            }

            if (pdps is not null && Bounds(pdps) is { } dps)
            {
                filters.Add(("equipment_filters", f => WriteRange(f, "pdps", dps)));
            }

            // Currency has no corrupted state on the site; a stack is never "not corrupted".
            if (w.Corrupted is { } corrupted && w.Rarity != ItemRarity.Currency)
            {
                filters.Add(("misc_filters", f => Option(f, "corrupted", corrupted ? "true" : "false")));
            }

            if (w.ListingDenomination is { } d && data.Prices.Contains(d))
            {
                filters.Add(("trade_filters", f => Option(f, "price", d)));
            }

            if (filters.Count > 0)
            {
                j.WriteStartObject("filters");
                foreach (var (group, write) in filters)
                {
                    j.WriteStartObject(group);
                    j.WriteStartObject("filters");
                    write(j);
                    j.WriteEndObject();
                    j.WriteEndObject();
                }

                j.WriteEndObject();
            }

            j.WriteEndObject();
            j.WriteEndObject();
        }

        return new TradeSearch(league, Encoding.UTF8.GetString(buffer.ToArray()), byHand, unmapped);
    }

    private static bool IsLocal(string? category, string template) =>
        category is not null
        && (category.StartsWith("weapon.", StringComparison.Ordinal)
            || (category.StartsWith("armour.", StringComparison.Ordinal) && category != "armour.quiver" && !WeaponOnlyLocal.Contains(template)));

    /// <summary>Without a list to check against, the game's own base name is trusted.</summary>
    private static bool Knows(IReadOnlySet<string> list, string value) => list.Count == 0 || list.Contains(value);

    private static string? KnownBase(string baseType, TradeData data, List<string> byHand)
    {
        if (Knows(data.Bases, baseType))
        {
            return baseType;
        }

        byHand.Add($"Item base ({baseType})");
        return null;
    }

    /// <summary>The longest base the site knows that sits whole inside a magic item's name.</summary>
    private static string? BaseInside(string magicName, TradeData data)
    {
        string? best = null;
        foreach (var b in data.Bases)
        {
            if ((best is null || b.Length > best.Length) && ContainsWords(magicName, b))
            {
                best = b;
            }
        }

        return best;
    }

    private static bool ContainsWords(string text, string words)
    {
        for (var at = text.IndexOf(words, StringComparison.Ordinal); at >= 0; at = text.IndexOf(words, at + 1, StringComparison.Ordinal))
        {
            var end = at + words.Length;
            if ((at == 0 || text[at - 1] == ' ') && (end == text.Length || text[end] == ' '))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteStatGroup(Utf8JsonWriter j, string type, List<(string Id, WorksheetRow Row)> rows, int? countMin)
    {
        j.WriteStartObject();
        j.WriteString("type", type);
        if (countMin is { } min)
        {
            j.WriteStartObject("value");
            j.WriteNumber("min", min);
            j.WriteEndObject();
        }

        j.WriteStartArray("filters");
        foreach (var (id, row) in rows)
        {
            j.WriteStartObject();
            j.WriteString("id", id);
            if (Bounds(row) is { } b)
            {
                WriteRange(j, "value", b);
            }

            j.WriteEndObject();
        }

        j.WriteEndArray();
        j.WriteEndObject();
    }

    /// <summary>The bounds the row searches with: its minimum, its maximum for a lower-is-better stat, or
    /// both. A multi-number line ("Adds # to #") carries the average, which the site compares as well.</summary>
    private static (decimal? Min, decimal? Max)? Bounds(WorksheetRow r)
    {
        var (min, max) = r.Match switch
        {
            MatchType.Minimum => (r.Min, (decimal?)null),
            MatchType.Maximum => (null, r.Max),
            _ => (r.Min, r.Max),
        };
        return min is null && max is null ? null : (min, max);
    }

    private static void WriteRange(Utf8JsonWriter j, string name, (decimal? Min, decimal? Max) b)
    {
        j.WriteStartObject(name);
        if (b.Min is { } min)
        {
            j.WriteNumber("min", Plain(min));
        }

        if (b.Max is { } max)
        {
            j.WriteNumber("max", Plain(max));
        }

        j.WriteEndObject();
    }

    private static void Option(Utf8JsonWriter j, string name, string option)
    {
        j.WriteStartObject(name);
        j.WriteString("option", option);
        j.WriteEndObject();
    }

    /// <summary>319.0 → 319: trailing zeros dropped, so the query reads as a person would type it.</summary>
    private static decimal Plain(decimal value) => value / 1.0000000000000000000000000000m;
}
