using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Domain.Trade;

/// <summary>One item for sale: its asking price and the numbers a search can filter on, keyed by
/// worksheet row id. Asking prices are not sales.</summary>
/// <param name="BuyNow">Instant buyout: bought at once, without the seller being there.</param>
/// <param name="ListedAt">When the listing went up; <see cref="DateTimeOffset.MinValue"/> when the site did not say.</param>
public sealed record Listing(
    string Id,
    decimal Amount,
    string Currency,
    IReadOnlyDictionary<string, decimal> Stats,
    int? ItemLevel,
    bool BuyNow,
    DateTimeOffset ListedAt)
{
    public bool HasListedAt => ListedAt != DateTimeOffset.MinValue;
}

/// <summary>A listing with its price converted to the display currency.</summary>
public sealed record PricedListing(Listing Listing, decimal Value);

/// <summary>How far a search result can be trusted, in the words people see.</summary>
public enum SearchConfidence
{
    /// <summary>Nothing matches: the search is too strict.</summary>
    NoMatches,
    /// <summary>One or two instant buyout prices: show the cheapest, say it is too few to trust.</summary>
    TooFew,
    /// <summary>Three to seven: a rough price.</summary>
    Rough,
    /// <summary>Eight or more: a good price guess.</summary>
    Good,
    /// <summary>Listings match, but none sets a price: none of them can be bought at once (only with
    /// "Buy-now listings only" unticked), or none is in a currency with a rate.</summary>
    NoBuyout,
}

/// <summary>What a search found. <see cref="Matches"/> are all the listings shown, cheapest first; only
/// the instant buyout ones among them set <see cref="Low"/>–<see cref="High"/>, the band items like this
/// one sell for, because a seller asking without instant buyout is often unsure of the price.</summary>
/// <param name="Total">How many the trade site counted, when the matches are only the cheapest it sent.</param>
public sealed record SearchResult(
    SearchConfidence Confidence,
    IReadOnlyList<PricedListing> Matches,
    decimal? Low,
    decimal? High,
    string Denomination,
    int ExcludedNoRate,
    int? Total = null)
{
    /// <summary>How many are for sale: every match, or the trade site's own count.</summary>
    public int Count => Total ?? Matches.Count;

    /// <summary>The cheapest instant buyout match: an ask without instant buyout never sets the price.</summary>
    public PricedListing? Cheapest => Matches.FirstOrDefault(m => m.Listing.BuyNow);

    /// <summary>Instant buyout matches with a price, which <see cref="Low"/>–<see cref="High"/> come from.</summary>
    public int PricedCount => Matches.Count(m => m.Listing.BuyNow);
}

/// <summary>Runs a comparison worksheet against listings. Pure: the same worksheet and listings always
/// give the same result, and nothing here talks to a server.</summary>
public static class MarketSearch
{
    public const int TooFewMax = 2;
    public const int RoughMax = 7;

    public static bool Matches(ComparisonWorksheet w, Listing l)
    {
        if (w.BuyNowOnly && !l.BuyNow)
        {
            return false;
        }

        if (w.MinItemLevel is { } minLevel && (l.ItemLevel is not { } level || level < minLevel))
        {
            return false;
        }

        var optional = new List<WorksheetRow>();
        foreach (var r in w.Selected)
        {
            if (r.Group == FilterGroup.Optional)
            {
                optional.Add(r);
            }
            else if (!RowMatches(r, l))
            {
                return false;
            }
        }

        return optional.Count == 0 || optional.Any(r => RowMatches(r, l));
    }

    /// <summary>Every listing (the demo's synthetic market) searched with the worksheet. The band runs from
    /// just above the cheapest instant buyout match to the middle one (15th to 50th percentile; 20th to
    /// 60th for a rough price).</summary>
    public static SearchResult Run(ComparisonWorksheet w, IEnumerable<Listing> listings, CurrencyConverter converter, string denomination, DateTimeOffset now)
    {
        var (matches, noRate) = Price(listings.Where(l => Matches(w, l)), converter, denomination, now, tiesById: true);
        var values = matches.Where(m => m.Listing.BuyNow).Select(m => m.Value).ToList();
        var confidence = Confidence(matches.Count, values.Count);
        var (low, high) = confidence switch
        {
            SearchConfidence.Good => (EvidencePolicy.Percentile(values, 0.15m), EvidencePolicy.Percentile(values, 0.5m)),
            SearchConfidence.Rough => (EvidencePolicy.Percentile(values, 0.2m), EvidencePolicy.Percentile(values, 0.6m)),
            SearchConfidence.TooFew => (values[0], values[0]),
            _ => ((decimal?)null, (decimal?)null),
        };

        return new SearchResult(confidence, matches, low, high, denomination, noRate);
    }

    /// <summary>The cheapest listings the trade site sent for a search, out of <paramref name="total"/> that
    /// match. Only the instant buyout ones set the price: from the second-cheapest of them (so one odd ask
    /// at the bottom never does) to their middle one; one or two are too few to trust and show the cheapest.
    /// How sure that is follows how many instant buyout prices there are; the count shown is the site's.</summary>
    public static SearchResult Summarize(IEnumerable<Listing> cheapest, int total, CurrencyConverter converter, string denomination, DateTimeOffset now)
    {
        var (matches, noRate) = Price(cheapest, converter, denomination, now, tiesById: false);
        var values = matches.Where(m => m.Listing.BuyNow).Select(m => m.Value).ToList();
        var confidence = total <= 0 && matches.Count == 0 ? SearchConfidence.NoMatches : Confidence(Math.Max(matches.Count, 1), values.Count);
        var (low, high) = confidence switch
        {
            SearchConfidence.Good or SearchConfidence.Rough => (values[1], EvidencePolicy.Percentile(values, 0.5m)),
            SearchConfidence.TooFew => (values[0], values[0]),
            _ => ((decimal?)null, (decimal?)null),
        };

        return new SearchResult(confidence, matches, low, high, denomination, noRate, Math.Max(total, matches.Count));
    }

    /// <summary>A listing the trade site sent, with the numbers of the worksheet's stats read off its item:
    /// physical DPS from the site's own figure, each other stat from the line filed under its trade stat id.</summary>
    public static Listing FromTradeSite(TradeListing t, ComparisonWorksheet w, TradeData data)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(w);
        var stats = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var r in w.Rows)
        {
            var value = r.Id == ComparisonWorksheet.PhysicalDpsId ? t.PhysicalDps
                : r.Kind != RowKind.Derived && TradeQuery.StatIdFor(r.Kind, r.Template, w.ItemClass, data) is { } id ? t.ValueOf(id)
                : null;
            if (value is { } v)
            {
                stats[r.Id] = v;
            }
        }

        return new Listing(t.Id, t.Amount, t.Currency, stats, t.ItemLevel, t.InstantBuyout, t.IndexedAt ?? DateTimeOffset.MinValue);
    }

    private static SearchConfidence Confidence(int matches, int priced) =>
        matches == 0 ? SearchConfidence.NoMatches
        : priced == 0 ? SearchConfidence.NoBuyout
        : priced <= TooFewMax ? SearchConfidence.TooFew
        : priced <= RoughMax ? SearchConfidence.Rough
        : SearchConfidence.Good;

    /// <summary>Converted to <paramref name="denomination"/>, cheapest first; ties by listing id, or in the
    /// order given (the site's). A listing in a currency without a rate is left out and counted.</summary>
    private static (List<PricedListing> Matches, int NoRate) Price(IEnumerable<Listing> listings, CurrencyConverter converter, string denomination, DateTimeOffset now, bool tiesById)
    {
        var noRate = 0;
        var matches = new List<PricedListing>();
        foreach (var l in listings)
        {
            var c = converter.Convert(l.Amount, l.Currency, denomination, now);
            if (!c.Ok)
            {
                noRate++;
                continue;
            }

            matches.Add(new PricedListing(l, c.Value));
        }

        if (tiesById)
        {
            matches.Sort((a, b) => a.Value != b.Value ? a.Value.CompareTo(b.Value) : string.CompareOrdinal(a.Listing.Id, b.Listing.Id));
            return (matches, noRate);
        }

        return ([.. matches.Select((m, i) => (m, i)).OrderBy(x => x.m.Value).ThenBy(x => x.i).Select(x => x.m)], noRate);
    }

    private static bool RowMatches(WorksheetRow r, Listing l)
    {
        if (!l.Stats.TryGetValue(r.Id, out var v))
        {
            return false;
        }

        return r.Match switch
        {
            MatchType.Minimum => r.Min is not { } min || v >= min,
            MatchType.Maximum => r.Max is not { } max || v <= max,
            _ => (r.Min is not { } lo || v >= lo) && (r.Max is not { } hi || v <= hi),
        };
    }
}
