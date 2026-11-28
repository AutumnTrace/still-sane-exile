using System.Globalization;

namespace StillSaneExile.Domain.Pricing;

public static class Currencies
{
    public static readonly IReadOnlyList<string> Display = ["exalted", "divine", "chaos"];

    public static string Short(string id) => id.ToLowerInvariant() switch
    {
        "exalted" => "ex",
        "divine" => "div",
        "chaos" => "c",
        _ => id,
    };

    public static string Long(string id) => id.ToLowerInvariant() switch
    {
        "exalted" => "Exalted Orb",
        "divine" => "Divine Orb",
        "chaos" => "Chaos Orb",
        _ => id,
    };

    /// <summary>The one-word name used in short labels, e.g. "Show prices in: Divine".</summary>
    public static string Word(string id) => id.ToLowerInvariant() switch
    {
        "exalted" => "Exalted",
        "divine" => "Divine",
        "chaos" => "Chaos",
        _ => id,
    };
}

/// <summary>Prices as people read them. Values are in the display currency; when that is Divine, anything
/// under one Divine Orb shows in whole Exalted Orbs instead ("cheap items still show in Exalted").
/// Divine amounts keep one decimal. Rounding happens here only, never in the maths before it. A price's
/// colour step follows <paramref name="ladder"/>, the player's, or the default one.</summary>
public sealed class PriceText(CurrencyConverter converter, string display, DateTimeOffset now, ValueLadder? ladder = null)
{
    public ValueLadder Ladder { get; } = ladder ?? ValueLadder.Default;

    public string Display => display;

    public (decimal Value, string Denomination) Unit(decimal value)
    {
        if (display.Equals("divine", StringComparison.OrdinalIgnoreCase) && value < 1m)
        {
            var ex = converter.Convert(value, display, "exalted", now);
            if (ex.Ok)
            {
                return (ex.Value, "exalted");
            }
        }

        return (value, display);
    }

    public string One(decimal value)
    {
        var (v, d) = Unit(value);
        return $"{Number(v, d)} {Currencies.Short(d)}";
    }

    /// <summary>"2.1–2.8 div", "60–95 ex", or "95 ex – 1.2 div" when the ends fall in different units.</summary>
    public string Range(decimal low, decimal high)
    {
        var (a, da) = Unit(low);
        var (b, db) = Unit(high);
        if (!string.Equals(da, db, StringComparison.OrdinalIgnoreCase))
        {
            return $"{One(low)} – {One(high)}";
        }

        var x = Number(a, da);
        var y = Number(b, db);
        return x == y ? $"{x} {Currencies.Short(da)}" : $"{x}–{y} {Currencies.Short(da)}";
    }

    /// <summary>The colour step of a price in the display currency, or null when the price book can't say
    /// what it is in Divine and Exalted Orbs (the label is then drawn plain rather than guessed).</summary>
    public ValueTier? Tier(decimal value) => Tier(value, display);

    /// <summary>The colour step of a price in <paramref name="denomination"/>.</summary>
    public ValueTier? Tier(decimal value, string denomination)
    {
        var div = converter.Convert(value, denomination, "divine", now);
        var ex = converter.Convert(value, denomination, "exalted", now);

        // A rate stored as its inverse turns 120 ex into 0.99999 div: on a step's edge that is the step.
        return div.Ok && ex.Ok ? Ladder.Of(Math.Round(div.Value, 4), Math.Round(ex.Value, 2)) : null;
    }

    /// <summary>"1 div = 120 ex", or null when the price book has no rate.</summary>
    public string? DivineRate() => ExaltedPerDivine() is { } rate ? $"1 div = {ValueFormat.Amount(Math.Round(rate))} ex" : null;

    /// <summary>Exalted Orbs to one Divine Orb, or null when the price book has no rate.</summary>
    public decimal? ExaltedPerDivine()
    {
        var rate = converter.Convert(1m, "divine", "exalted", now);
        return rate.Ok ? rate.Value : null;
    }

    private static string Number(decimal v, string denomination) =>
        denomination.Equals("exalted", StringComparison.OrdinalIgnoreCase) && v >= 1m ? ValueFormat.Amount(Math.Round(v))
        : denomination.Equals("divine", StringComparison.OrdinalIgnoreCase) && v >= 1m ? ValueFormat.Amount(Math.Round(v, v < 100 ? 1 : 0))
        : ValueFormat.Amount(v);
}

public static class ValueFormat
{
    /// <summary>Rounds only for display. Tiny positive values show as "&lt;0.01".</summary>
    public static string Amount(decimal v)
    {
        var abs = Math.Abs(v);
        if (v == 0)
        {
            return "0";
        }

        if (abs < 0.01m)
        {
            return v > 0 ? "<0.01" : ">-0.01";
        }

        var format = abs < 10 ? "0.##" : abs < 100 ? "0.#" : "#,0";
        return v.ToString(format, CultureInfo.InvariantCulture);
    }

    public static string Money(decimal v, string denomination) => $"{Amount(v)} {Currencies.Short(denomination)}";

    public static string Age(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours} h ago"
            : $"{(int)age.TotalDays} d ago";
    }
}
