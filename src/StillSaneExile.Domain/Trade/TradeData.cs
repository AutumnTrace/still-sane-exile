using System.Text.Json;
using StillSaneExile.Domain.Items;

namespace StillSaneExile.Domain.Trade;

/// <summary>The official trade site's own ids: stat filters by group, item categories, filter options,
/// and the base type and unique names it knows. Read from <c>data/trade/trade-data.json</c>, a trimmed
/// copy of GGG's public trade metadata that ships with the app (refreshed by
/// <c>scripts/update-trade-data.ps1</c>). Nothing here talks to the trade site.</summary>
public sealed class TradeData
{
    private const string LocalSuffix = " (Local)";

    /// <summary>Stat id by (group, template, local). Null when two ids share the same text, so SSE
    /// cannot tell which one the line is.</summary>
    private readonly Dictionary<(string Group, string Template, bool Local), string?> _stats;

    private TradeData(
        string? fetched,
        Dictionary<(string, string, bool), string?> stats,
        IReadOnlyDictionary<string, IReadOnlySet<string>> filters,
        IReadOnlySet<string> status,
        IReadOnlySet<string> categories,
        IReadOnlySet<string> rarities,
        IReadOnlySet<string> prices,
        IReadOnlySet<string> bases,
        IReadOnlySet<string> uniques)
    {
        Fetched = fetched;
        _stats = stats;
        Filters = filters;
        StatusOptions = status;
        Categories = categories;
        Rarities = rarities;
        Prices = prices;
        Bases = bases;
        Uniques = uniques;
    }

    /// <summary>No data: links still open with the item's base and rarity, and every stat is set by hand.</summary>
    public static TradeData Empty { get; } = new(null, new(), new Dictionary<string, IReadOnlySet<string>>(),
        new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>());

    /// <summary>The day the file was downloaded from the trade site (yyyy-MM-dd), or null without data.</summary>
    public string? Fetched { get; }

    /// <summary>The filter keys the file vouches for, by filter group ("type_filters" → "category", …).</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> Filters { get; }

    public IReadOnlySet<string> StatusOptions { get; }
    public IReadOnlySet<string> Categories { get; }
    public IReadOnlySet<string> Rarities { get; }
    public IReadOnlySet<string> Prices { get; }

    /// <summary>Base type names the trade site knows ("Gold Ring", "Bombard Crossbow", "Exalted Orb").</summary>
    public IReadOnlySet<string> Bases { get; }

    /// <summary>Unique item names the trade site knows ("Mageblood").</summary>
    public IReadOnlySet<string> Uniques { get; }

    public bool IsEmpty => _stats.Count == 0;

    /// <summary>The stat id for a line template in a stat group ("explicit", "implicit", "enchant",
    /// "rune"). <paramref name="local"/> asks for the "(Local)" version where the site has one, as a
    /// weapon's own attack speed or an armour's own armour does. False with a null id when the group has
    /// the text under more than one id.</summary>
    public bool TryStat(string group, string template, bool local, out string? id) => _stats.TryGetValue((group, template, local), out id);

    /// <summary>Reads the shipped file. Throws <see cref="FormatException"/> when it is not trade data.</summary>
    public static TradeData Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("stats", out var groups) || groups.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Trade data has no \"stats\" object.");
            }

            var stats = new Dictionary<(string, string, bool), string?>();
            foreach (var group in groups.EnumerateObject())
            {
                foreach (var entry in group.Value.EnumerateArray())
                {
                    var id = entry.GetProperty("id").GetString();
                    var text = entry.GetProperty("text").GetString();
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    var local = text.EndsWith(LocalSuffix, StringComparison.Ordinal);
                    var key = (group.Name, StatText.NormalizeTemplate(local ? text[..^LocalSuffix.Length] : text), local);
                    if (stats.TryGetValue(key, out var existing))
                    {
                        stats[key] = existing == id ? id : null;
                    }
                    else
                    {
                        stats[key] = id;
                    }
                }
            }

            var filters = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
            if (root.TryGetProperty("filters", out var f) && f.ValueKind == JsonValueKind.Object)
            {
                foreach (var g in f.EnumerateObject())
                {
                    filters[g.Name] = Strings(g.Value);
                }
            }

            return new TradeData(
                root.TryGetProperty("fetched", out var fetched) ? fetched.GetString() : null,
                stats,
                filters,
                Options(root, "status"),
                Options(root, "categories"),
                Options(root, "rarities"),
                Options(root, "prices"),
                root.TryGetProperty("bases", out var bases) ? Strings(bases) : new HashSet<string>(),
                root.TryGetProperty("uniques", out var uniques) ? Strings(uniques) : new HashSet<string>());
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new FormatException($"Trade data could not be read: {e.Message}", e);
        }
    }

    private static HashSet<string> Strings(JsonElement array) =>
        array.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> Options(JsonElement root, string name) =>
        root.TryGetProperty(name, out var list)
            ? list.EnumerateArray().Select(e => e.GetProperty("id").GetString()).OfType<string>().ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
}
