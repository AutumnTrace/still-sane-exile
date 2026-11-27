using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Domain.Trade;

/// <summary>One item for sale: its asking price and the numbers a search can filter on, keyed by
/// worksheet row id. Asking prices are not sales.</summary>
public sealed record Listing(
    string Id,
    decimal Amount,
    string Currency,
    IReadOnlyDictionary<string, decimal> Stats,
    int? ItemLevel,
    bool BuyNow,
    DateTimeOffset ListedAt);

/// <summary>A listing with its price converted to the display currency.</summary>
public sealed record PricedListing(Listing Listing, decimal Value);

/// <summary>How far a search result can be trusted, in the words people see.</summary>
public enum SearchConfidence
{
    /// <summary>Nothing matches: the search is too strict.</summary>
    NoMatches,
    /// <summary>One or two for sale: show the cheapest, say it is too few to trust.</summary>
    TooFew,
    /// <summary>Three to seven for sale: a rough price.</summary>
    Rough,
    /// <summary>Eight or more for sale: a good price guess.</summary>
    Good,
}

/// <summary>What a search found. <see cref="Low"/>–<see cref="High"/> is the band items like this one
/// sell for: the lower part of the asks, because asks above it tend to sit unsold.</summary>
public sealed record SearchResult(
    SearchConfidence Confidence,
    IReadOnlyList<PricedListing> Matches,
    decimal? Low,
    decimal? High,
    string Denomination,
    int ExcludedNoRate)
{
    public int Count => Matches.Count;

    public PricedListing? Cheapest => Matches.Count > 0 ? Matches[0] : null;
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

    public static SearchResult Run(ComparisonWorksheet w, IEnumerable<Listing> listings, CurrencyConverter converter, string denomination, DateTimeOffset now)
    {
        var noRate = 0;
        var matches = new List<PricedListing>();
        foreach (var l in listings.Where(l => Matches(w, l)))
        {
            var c = converter.Convert(l.Amount, l.Currency, denomination, now);
            if (!c.Ok)
            {
                noRate++;
                continue;
            }

            matches.Add(new PricedListing(l, c.Value));
        }

        matches.Sort((a, b) => a.Value != b.Value ? a.Value.CompareTo(b.Value) : string.CompareOrdinal(a.Listing.Id, b.Listing.Id));
        var n = matches.Count;
        var confidence = n == 0 ? SearchConfidence.NoMatches
            : n <= TooFewMax ? SearchConfidence.TooFew
            : n <= RoughMax ? SearchConfidence.Rough
            : SearchConfidence.Good;

        var values = matches.Select(m => m.Value).ToList();
        var (low, high) = confidence switch
        {
            SearchConfidence.Good => (EvidencePolicy.Percentile(values, 0.15m), EvidencePolicy.Percentile(values, 0.5m)),
            SearchConfidence.Rough => (EvidencePolicy.Percentile(values, 0.2m), EvidencePolicy.Percentile(values, 0.6m)),
            SearchConfidence.TooFew => (values[0], values[0]),
            _ => ((decimal?)null, (decimal?)null),
        };

        return new SearchResult(confidence, matches, low, high, denomination, noRate);
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
