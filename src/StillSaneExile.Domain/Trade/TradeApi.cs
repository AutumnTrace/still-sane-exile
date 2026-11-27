using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StillSaneExile.Domain.Items;

namespace StillSaneExile.Domain.Trade;

/// <summary>What the site's search answered: its id for the search, the listing ids it found (cheapest
/// first, as asked) and how many listings match in all.</summary>
public sealed record TradeSearchAnswer(string Id, IReadOnlyList<string> Ids, int Total);

/// <summary>One modifier line on a listed item: the trade stat id it is filed under ("explicit.stat_123"),
/// its text with the site's markup removed, and its numbers.</summary>
public sealed record TradeMod(string? StatId, string Text, IReadOnlyList<decimal> Values)
{
    /// <summary>The number the site's stat filters compare: the value, or the average of a "# to #" line.</summary>
    public decimal? Value => Values.Count switch
    {
        0 => null,
        1 => Values[0],
        _ => Values.Average(),
    };
}

/// <summary>One listing from the site's fetch: the asking price, whether it can be bought at once, when
/// it was listed and the item's numbers. Nothing about the seller: the account, character and stash
/// tab the site sends are never read.</summary>
/// <param name="PriceType">The site's price note kind, "~b/o" or "~price". Both are asking prices.</param>
/// <param name="InstantBuyout">Bought at once from the seller's merchant, without them being there.</param>
public sealed record TradeListing(
    string Id,
    decimal Amount,
    string Currency,
    string PriceType,
    bool InstantBuyout,
    DateTimeOffset? IndexedAt,
    int? ItemLevel,
    decimal? PhysicalDps,
    IReadOnlyList<TradeMod> Mods)
{
    /// <summary>The listed item's number for a trade stat id, or null when it has no such line. A fractured,
    /// desecrated or crafted line counts for its explicit stat, as the site's explicit filters count it.</summary>
    public decimal? ValueOf(string statId)
    {
        if (Mods.FirstOrDefault(m => m.StatId == statId) is { } exact)
        {
            return exact.Value;
        }

        var dot = statId.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0 || !statId.StartsWith("explicit.", StringComparison.Ordinal))
        {
            return null;
        }

        var stat = statId[dot..];
        return Mods.FirstOrDefault(m => m.StatId is { } id && id.EndsWith(stat, StringComparison.Ordinal)
            && (id.StartsWith("fractured.", StringComparison.Ordinal) || id.StartsWith("desecrated.", StringComparison.Ordinal) || id.StartsWith("crafted.", StringComparison.Ordinal)))?.Value;
    }
}

/// <summary>One search's results: the site's count and the listings fetched, cheapest first.</summary>
public sealed record TradePage(string QueryId, int Total, IReadOnlyList<TradeListing> Listings);

/// <summary>The official trade site's search API, used the way the site's own pages use it: a POST of the
/// query to <c>/api/trade2/search/poe2/{league}</c>, which answers with listing ids, then a GET of up to ten
/// of them from <c>/api/trade2/fetch/{ids}?query={id}&amp;realm=poe2</c>. Pure: URLs, bodies and parsing,
/// no requests (<see cref="TradeClient"/> sends them). Shapes checked against the live site on 27 Nov 2026.</summary>
public static partial class TradeApi
{
    /// <summary>The governor's name for the trade site; the routes are <see cref="SearchRoute"/> and <see cref="FetchRoute"/>.</summary>
    public const string Integration = "trade";

    public const string SearchRoute = "search";

    public const string FetchRoute = "fetch";

    /// <summary>The most listing ids one fetch takes.</summary>
    public const int FetchBatch = 10;

    public static Uri Site { get; } = new("https://www.pathofexile.com/");

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [GeneratedRegex(@"^[0-9A-Za-z]{1,128}$")]
    private static partial Regex ListingId();

    /// <summary>"[Physical] Damage" → "Physical Damage", "[Critical|Critical Hit] Chance" → "Critical Hit Chance".</summary>
    [GeneratedRegex(@"\[(?:[^\]|]*\|)?([^\]]*)\]")]
    private static partial Regex Markup();

    public static Uri SearchUri(string league)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(league);
        return new Uri(Site, $"api/trade2/search/poe2/{Uri.EscapeDataString(league)}");
    }

    /// <summary>The fetch for up to <see cref="FetchBatch"/> listing ids of one search.</summary>
    public static Uri FetchUri(IReadOnlyCollection<string> ids, string queryId)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        if (ids.Count is 0 or > FetchBatch || !ids.All(IsListingId))
        {
            throw new ArgumentException($"A fetch takes 1 to {FetchBatch} listing ids of letters and digits.", nameof(ids));
        }

        return new Uri(Site, $"api/trade2/fetch/{string.Join(',', ids)}?query={Uri.EscapeDataString(queryId)}&realm=poe2");
    }

    /// <summary>The query <see cref="TradeQuery"/> built, sorted cheapest first.</summary>
    public static string SearchBody(string queryJson)
    {
        var root = JsonNode.Parse(queryJson) as JsonObject ?? throw new JsonException("The trade query is not a JSON object.");
        root["sort"] = new JsonObject { ["price"] = "asc" };
        return root.ToJsonString(Compact);
    }

    public static bool IsListingId(string? id) => id is not null && ListingId().IsMatch(id);

    /// <summary>The search's answer. Ids that are not plain letters and digits are dropped, so nothing the
    /// site sends can change the fetch's path. Throws <see cref="JsonException"/> on another shape.</summary>
    public static TradeSearchAnswer ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(id.GetString()))
        {
            throw new JsonException("The trade site's search answer has no id.");
        }

        var ids = new List<string>();
        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in result.EnumerateArray())
            {
                if (r.ValueKind == JsonValueKind.String && r.GetString() is { } s && IsListingId(s))
                {
                    ids.Add(s);
                }
            }
        }

        var total = root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var n) ? Math.Max(n, ids.Count) : ids.Count;
        return new TradeSearchAnswer(id.GetString()!, ids, total);
    }

    /// <summary>The fetch's listings, in the order sent. A listing without a usable price is left out; so is
    /// a gone one (the site sends null for it).</summary>
    /// <param name="instantBuyoutSearch">The search asked for instant buyout only ("securable"), so every
    /// listing is one. Otherwise a listing counts as instant buyout when it carries the merchant's gold fee.</param>
    public static IReadOnlyList<TradeListing> ParseFetch(string json, bool instantBuyoutSearch)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The trade site's fetch answer has no result list.");
        }

        var listings = new List<TradeListing>();
        foreach (var r in result.EnumerateArray())
        {
            if (Listing(r, instantBuyoutSearch) is { } listing)
            {
                listings.Add(listing);
            }
        }

        return listings;
    }

    /// <summary>A mod line as the site writes it, without its markup.</summary>
    public static string PlainText(string text) => Markup().Replace(text, "$1");

    private static TradeListing? Listing(JsonElement r, bool instantBuyoutSearch)
    {
        if (r.ValueKind != JsonValueKind.Object
            || Text(r, "id") is not { } id
            || !r.TryGetProperty("listing", out var listing) || listing.ValueKind != JsonValueKind.Object
            || !listing.TryGetProperty("price", out var price) || price.ValueKind != JsonValueKind.Object
            || !price.TryGetProperty("amount", out var amountElement) || amountElement.ValueKind != JsonValueKind.Number || !amountElement.TryGetDecimal(out var amount) || amount <= 0
            || Text(price, "currency") is not { Length: > 0 } currency)
        {
            return null;
        }

        DateTimeOffset? indexed = Text(listing, "indexed") is { } at
            && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when)
            ? when
            : null;
        var fee = listing.TryGetProperty("fee", out var f) && f.ValueKind == JsonValueKind.Number;

        int? level = null;
        decimal? pdps = null;
        var mods = new List<TradeMod>();
        if (r.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
        {
            level = item.TryGetProperty("ilvl", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var lv) ? lv : null;
            var extended = item.TryGetProperty("extended", out var e) && e.ValueKind == JsonValueKind.Object ? e : default;
            pdps = extended.ValueKind == JsonValueKind.Object && extended.TryGetProperty("pdps", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var pd) ? pd : null;
            Mods(item, extended, mods);
        }

        return new TradeListing(id, amount, currency, Text(price, "type") ?? "", instantBuyoutSearch || fee, indexed, level, pdps, mods);
    }

    /// <summary>Every "...Mods" list on the item. A line is an object with its text and stat hash; an older
    /// shape sends plain text, with the hashes in <c>extended.hashes</c> in the same order.</summary>
    private static void Mods(JsonElement item, JsonElement extended, List<TradeMod> into)
    {
        var hashes = extended.ValueKind == JsonValueKind.Object && extended.TryGetProperty("hashes", out var h) && h.ValueKind == JsonValueKind.Object ? h : default;
        foreach (var group in item.EnumerateObject())
        {
            if (!group.Name.EndsWith("Mods", StringComparison.Ordinal) || group.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var name = group.Name[..^4];
            var order = hashes.ValueKind == JsonValueKind.Object && hashes.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().ToList()
                : [];
            var k = 0;
            foreach (var line in group.Value.EnumerateArray())
            {
                string? text = null, stat = null;
                if (line.ValueKind == JsonValueKind.Object)
                {
                    text = Text(line, "description");
                    stat = Text(line, "hash") is { } hash && hash.StartsWith("stat.", StringComparison.Ordinal) ? hash[5..] : null;
                }
                else if (line.ValueKind == JsonValueKind.String)
                {
                    text = line.GetString();
                    stat = k < order.Count && order[k].ValueKind == JsonValueKind.Array && order[k].GetArrayLength() > 0 && order[k][0].ValueKind == JsonValueKind.String
                        ? order[k][0].GetString()
                        : null;
                }

                k++;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var plain = PlainText(text);
                into.Add(new TradeMod(stat, plain, [.. StatText.Parse(plain, advanced: false).Components.Select(c => c.Value)]));
            }
        }
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
