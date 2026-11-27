namespace StillSaneExile.Domain.Pricing;

public enum Freshness
{
    Fresh,
    /// <summary>Two hourly updates missed. Still shown, with its age.</summary>
    Stale,
    /// <summary>Older than the ranking limit (24 h by default): shown with a warning, never used to rank.</summary>
    RankingSuppressed,
}

public sealed record StalenessPolicy(TimeSpan Cadence, int MissedUpdatesForStale, TimeSpan RankingLimit, TimeSpan Grace)
{
    public static StalenessPolicy Hourly { get; } = new(TimeSpan.FromHours(1), 2, TimeSpan.FromHours(24), TimeSpan.FromMinutes(5));

    public Freshness Classify(DateTimeOffset observedTo, DateTimeOffset now)
    {
        var age = now - observedTo;
        if (age > RankingLimit)
        {
            return Freshness.RankingSuppressed;
        }

        var missed = (int)Math.Floor((age - Grace).Ticks / (double)Cadence.Ticks);
        return missed >= MissedUpdatesForStale ? Freshness.Stale : Freshness.Fresh;
    }
}

public enum ConversionPath
{
    Identity,
    Direct,
    Inverted,
    Bridge,
    NoRate,
}

public sealed record Conversion(
    bool Ok,
    decimal Value,
    string Denomination,
    ConversionPath Path,
    Freshness Freshness,
    DateTimeOffset? OldestObservation,
    string Explanation)
{
    public static Conversion None(decimal amount, string from, string why) =>
        new(false, amount, from, ConversionPath.NoRate, Freshness.Fresh, null, why);
}

/// <summary>Converts between denominations using one price book. All arithmetic is decimal and happens
/// before any rounding. Prefers a direct quote, then its inverse, then one transparent bridge whose legs
/// are both fresh and liquid. No rate means the caller shows the original denomination.</summary>
public sealed class CurrencyConverter(PriceBook book, StalenessPolicy policy, decimal minBridgeVolume = 50m)
{
    public PriceBook Book => book;

    public Conversion Convert(decimal amount, string from, string to, DateTimeOffset now)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return new Conversion(true, amount, to, ConversionPath.Identity, Freshness.Fresh, null, "same denomination");
        }

        if (Leg(from, to) is { } direct)
        {
            return Build(amount * direct.Rate, to, direct.Inverted ? ConversionPath.Inverted : ConversionPath.Direct, now, direct.Quote);
        }

        foreach (var via in BridgeCandidates(from, to))
        {
            if (Leg(from, via) is { } a && Leg(via, to) is { } b
                && Usable(a.Quote, now) && Usable(b.Quote, now))
            {
                var c = Build(amount * a.Rate * b.Rate, to, ConversionPath.Bridge, now, a.Quote, b.Quote);
                return c with { Explanation = $"{from} → {via} → {to} (bridge)" };
            }
        }

        return Conversion.None(amount, from, $"no usable {from}→{to} rate");
    }

    /// <summary>How many <paramref name="to"/> one <paramref name="from"/> is worth, from one quote.</summary>
    private (decimal Rate, bool Inverted, Quote Quote)? Leg(string from, string to)
    {
        if (Pair(from, to) is { } q)
        {
            return (q.Amount, false, q);
        }

        if (Pair(to, from) is { } r)
        {
            return (1m / r.Amount, true, r);
        }

        return null;
    }

    /// <summary>A usable quote of <paramref name="item"/> priced in <paramref name="denomination"/>.
    /// Zero, negative and other-league quotes are rejected.</summary>
    private Quote? Pair(string item, string denomination) =>
        book.Quotes.FirstOrDefault(q => q.Amount > 0
            && q.Variant is null
            && q.League == book.League
            && q.ItemId.Equals(item, StringComparison.OrdinalIgnoreCase)
            && q.Denomination.Equals(denomination, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<string> BridgeCandidates(string from, string to)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from, to };
        if (seen.Add(book.PrimaryCurrency))
        {
            yield return book.PrimaryCurrency;
        }

        foreach (var d in book.Quotes.Select(q => q.Denomination).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(d))
            {
                yield return d;
            }
        }
    }

    private bool Usable(Quote q, DateTimeOffset now) =>
        policy.Classify(q.ObservedTo, now) == Freshness.Fresh && q.Volume is { } v && v >= minBridgeVolume;

    private Conversion Build(decimal value, string to, ConversionPath path, DateTimeOffset now, params Quote[] legs)
    {
        var oldest = legs.Min(l => l.ObservedTo);
        var freshness = legs.Select(l => policy.Classify(l.ObservedTo, now)).Max();
        return new Conversion(true, value, to, path, freshness, oldest, path.ToString().ToLowerInvariant());
    }
}

/// <summary>Rates derived from GGG's historical currency-exchange digest, where each hour reports the
/// traded volume of both sides of a pair.</summary>
public static class ExchangeDigestMath
{
    /// <summary>Units of B per one A over a window = volume B ÷ volume A. Both sides must be positive.</summary>
    public static decimal? UnitsOfBPerA(decimal volumeA, decimal volumeB) =>
        volumeA > 0 && volumeB > 0 ? volumeB / volumeA : null;

    /// <summary>Merges compatible hours by summing each side's volume before dividing.</summary>
    public static decimal? Merge(IEnumerable<(decimal VolumeA, decimal VolumeB)> hours)
    {
        decimal a = 0, b = 0;
        foreach (var (va, vb) in hours)
        {
            if (va <= 0 || vb <= 0)
            {
                continue;
            }

            a += va;
            b += vb;
        }

        return UnitsOfBPerA(a, b);
    }

    /// <summary>The digest hour to look up first: the last complete UTC hour.</summary>
    public static DateTimeOffset LastCompleteHour(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero).AddHours(-1);
    }
}
