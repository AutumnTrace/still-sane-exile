namespace StillSaneExile.Domain.Pricing;

public enum ComparableOrigin
{
    /// <summary>The player looked at a listing on the official trade site and typed it in.</summary>
    PlayerEntered,
    /// <summary>Reserved for a future authorized listing adapter implementing the same contract.</summary>
    Adapter,
}

/// <summary>One asking price for an item that matches the worksheet. Asking prices are not sales.</summary>
public sealed record Comparable(
    string Id,
    string League,
    decimal Amount,
    string Currency,
    DateTimeOffset InspectedAt,
    string? SellerAlias,
    string? ListingId,
    string? ListingLink,
    string? Notes,
    ComparableOrigin Origin,
    DateTimeOffset? ExpiresAt);

/// <summary>A private set of comparables belongs to one item the player is pricing.</summary>
public static class ComparisonSet
{
    public static string KeyFor(Items.ItemSnapshot item) => $"{item.Rarity}|{item.BaseType}|{item.Name}";
}

public enum EvidenceLevel
{
    None,
    OneAsk,
    ThinMarket,
    Range,
}

public sealed record EvidenceOptions(
    TimeSpan FreshnessWindow,
    int RangeMinSellers = 5,
    decimal WideDispersionRatio = 2m,
    decimal LowClusterFactor = 0.5m)
{
    public static EvidenceOptions Default { get; } = new(TimeSpan.FromDays(7));
}

/// <summary>One independent seller's normalized ask (unknown sellers are pooled into one point).</summary>
public sealed record EvidencePoint(string Seller, decimal Value, DateTimeOffset InspectedAt, int ListingsPooled, bool UnknownSeller, bool MissingProvenance);

public sealed record EvidenceSummary(
    EvidenceLevel Level,
    string Denomination,
    decimal? Median,
    decimal? Low,
    decimal? High,
    int IndependentSellers,
    int ListingsUsed,
    int DuplicatesRemoved,
    int ExcludedOtherLeague,
    int ExcludedExpired,
    int ExcludedOutsideWindow,
    int ExcludedNoRate,
    int MissingProvenance,
    bool WideDispersion,
    bool ClusteredLowAsks,
    DateTimeOffset? Newest,
    DateTimeOffset? Oldest,
    IReadOnlyList<EvidencePoint> Points)
{
    public string Headline => Level switch
    {
        EvidenceLevel.Range => $"Median {ValueFormat.Money(Median!.Value, Denomination)} · middle 50% {ValueFormat.Amount(Low!.Value)}–{ValueFormat.Money(High!.Value, Denomination)} · {IndependentSellers} sellers",
        EvidenceLevel.ThinMarket => $"Thin market · {ValueFormat.Amount(Low!.Value)}–{ValueFormat.Money(High!.Value, Denomination)} · {IndependentSellers} sellers",
        EvidenceLevel.OneAsk => $"One ask · {ValueFormat.Money(Low!.Value, Denomination)}",
        _ => "No evidence · Compare on trade",
    };

    public IEnumerable<string> Warnings()
    {
        if (WideDispersion)
        {
            yield return "Asks are unusually spread out: check the individual listings.";
        }

        if (ClusteredLowAsks)
        {
            yield return "Several asks sit far below the rest: they may be bait or mismatched listings.";
        }

        if (MissingProvenance > 0)
        {
            yield return $"{MissingProvenance} ask(s) have no listing link or id.";
        }

        if (ExcludedNoRate > 0)
        {
            yield return $"{ExcludedNoRate} ask(s) could not be converted and are excluded.";
        }
    }
}

/// <summary>Turns comparable asks into a defensible summary (initial product defaults):
/// same league only; expired and out-of-window entries excluded; duplicate listing ids removed;
/// each known seller counted once (their latest ask); all unknown-seller asks pooled into a single
/// point so they cannot raise confidence. Five or more independent sellers → median and middle 50%;
/// two to four → observed span ("Thin market"); one → "One ask".</summary>
public static class EvidencePolicy
{
    public static EvidenceSummary Summarize(
        IEnumerable<Comparable> comparables,
        string league,
        string denomination,
        CurrencyConverter converter,
        DateTimeOffset now,
        EvidenceOptions? options = null)
    {
        options ??= EvidenceOptions.Default;
        int otherLeague = 0, expired = 0, outside = 0, noRate = 0, dupes = 0;

        var inLeague = new List<Comparable>();
        foreach (var c in comparables)
        {
            if (c.League != league)
            {
                otherLeague++;
            }
            else if (c.ExpiresAt is { } e && e <= now)
            {
                expired++;
            }
            else if (now - c.InspectedAt > options.FreshnessWindow)
            {
                outside++;
            }
            else
            {
                inLeague.Add(c);
            }
        }

        // Same listing id entered twice: keep the most recent inspection.
        var byListing = new List<Comparable>();
        foreach (var g in inLeague.GroupBy(c => string.IsNullOrWhiteSpace(c.ListingId) ? $"\0{c.Id}" : c.ListingId!.Trim()))
        {
            byListing.Add(g.OrderByDescending(c => c.InspectedAt).First());
            dupes += g.Count() - 1;
        }

        var converted = new List<(Comparable C, decimal Value)>();
        foreach (var c in byListing)
        {
            var conv = converter.Convert(c.Amount, c.Currency, denomination, now);
            if (!conv.Ok)
            {
                noRate++;
                continue;
            }

            converted.Add((c, conv.Value));
        }

        var points = new List<EvidencePoint>();
        foreach (var g in converted.Where(x => !string.IsNullOrWhiteSpace(x.C.SellerAlias))
                     .GroupBy(x => x.C.SellerAlias!.Trim().ToLowerInvariant()))
        {
            // A known seller relisting: one vote, their latest ask.
            var latest = g.OrderByDescending(x => x.C.InspectedAt).First();
            dupes += g.Count() - 1;
            points.Add(new EvidencePoint(latest.C.SellerAlias!.Trim(), latest.Value, latest.C.InspectedAt, g.Count(), false, MissingProv(latest.C)));
        }

        var unknown = converted.Where(x => string.IsNullOrWhiteSpace(x.C.SellerAlias)).ToList();
        if (unknown.Count > 0)
        {
            var values = unknown.Select(x => x.Value).OrderBy(v => v).ToList();
            points.Add(new EvidencePoint("unknown seller(s)", Percentile(values, 0.5m), unknown.Max(x => x.C.InspectedAt), unknown.Count, true, unknown.Any(x => MissingProv(x.C))));
        }

        var sorted = points.Select(p => p.Value).OrderBy(v => v).ToList();
        var n = sorted.Count;
        var level = n >= options.RangeMinSellers ? EvidenceLevel.Range
            : n >= 2 ? EvidenceLevel.ThinMarket
            : n == 1 ? EvidenceLevel.OneAsk
            : EvidenceLevel.None;

        decimal? median = n > 0 ? Percentile(sorted, 0.5m) : null;
        decimal? low = level switch
        {
            EvidenceLevel.Range => Percentile(sorted, 0.25m),
            EvidenceLevel.None => null,
            _ => sorted[0],
        };
        decimal? high = level switch
        {
            EvidenceLevel.Range => Percentile(sorted, 0.75m),
            EvidenceLevel.None => null,
            _ => sorted[^1],
        };

        var wide = level switch
        {
            EvidenceLevel.Range => low > 0 && high / low > options.WideDispersionRatio,
            EvidenceLevel.ThinMarket => sorted[0] > 0 && sorted[^1] / sorted[0] > options.WideDispersionRatio * 1.5m,
            _ => false,
        };
        var clustered = n >= 3 && median is { } m && sorted.Count(v => v < m * options.LowClusterFactor) >= 2;

        return new EvidenceSummary(
            level, denomination, median, low, high, n, converted.Count, dupes, otherLeague, expired, outside, noRate,
            converted.Count(x => MissingProv(x.C)), wide, clustered,
            converted.Count > 0 ? converted.Max(x => x.C.InspectedAt) : null,
            converted.Count > 0 ? converted.Min(x => x.C.InspectedAt) : null,
            points.OrderBy(p => p.Value).ToList());
    }

    /// <summary>Linear interpolation between closest ranks on a sorted list.</summary>
    public static decimal Percentile(IReadOnlyList<decimal> sorted, decimal q)
    {
        if (sorted.Count == 0)
        {
            throw new ArgumentException("No values.", nameof(sorted));
        }

        var pos = (sorted.Count - 1) * q;
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        return lo == hi ? sorted[lo] : sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    private static bool MissingProv(Comparable c) =>
        string.IsNullOrWhiteSpace(c.ListingLink) && string.IsNullOrWhiteSpace(c.ListingId);
}
