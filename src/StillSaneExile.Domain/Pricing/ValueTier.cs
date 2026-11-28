using System.Globalization;

namespace StillSaneExile.Domain.Pricing;

/// <summary>How loud a price is drawn, cheapest first. The price label borrows the ladder players already
/// read in their loot filter (white and red for a jackpot down to a dark tag for small change), so the
/// colour says roughly what an item is worth before the number is read. Where each step starts is the
/// player's <see cref="ValueLadder"/>; the summaries give the default.</summary>
public enum ValueTier
{
    /// <summary>Under 1 Exalted Orb.</summary>
    E,

    /// <summary>1 to 6 Exalted Orbs.</summary>
    D,

    /// <summary>6 to 30 Exalted Orbs.</summary>
    C,

    /// <summary>30 Exalted Orbs to 1 Divine Orb.</summary>
    B,

    /// <summary>1 to 10 Divine Orbs.</summary>
    A,

    /// <summary>10 Divine Orbs or more.</summary>
    S,
}

/// <summary>Where one colour step starts: an amount in Divine or Exalted Orbs.</summary>
/// <param name="Currency">"divine" or "exalted".</param>
public sealed record ValueStep(decimal Amount, string Currency)
{
    public bool InDivine => Currency.Equals("divine", StringComparison.OrdinalIgnoreCase);

    /// <summary>"10 div", "1.5 div", "30 ex".</summary>
    public override string ToString() => $"{Amount.ToString("0.##", CultureInfo.InvariantCulture)} {Currencies.Short(Currency)}";
}

/// <summary>Where each colour of the price label starts, from the loudest down; anything under
/// <see cref="D"/> is the dark tag. Each step is compared in its own currency (a price is known in both),
/// so a step keeps meaning what the player wrote whatever the day's rate is. The player can move the steps
/// (Settings, Price colours): at a league's start 1 Divine Orb may already deserve the loudest label.</summary>
public sealed record ValueLadder(ValueStep S, ValueStep A, ValueStep B, ValueStep C, ValueStep D)
{
    public const decimal MaxAmount = 1_000_000m;

    /// <summary>The steps players know from loot filters: white and red from 10 div, red from 1 div, then
    /// 30, 6 and 1 ex.</summary>
    public static ValueLadder Default { get; } = new(
        new(10m, "divine"), new(1m, "divine"), new(30m, "exalted"), new(6m, "exalted"), new(1m, "exalted"));

    /// <summary>The steps with a start of their own, loudest first.</summary>
    public static IReadOnlyList<ValueTier> Starting { get; } = [ValueTier.S, ValueTier.A, ValueTier.B, ValueTier.C, ValueTier.D];

    public ValueStep Start(ValueTier tier) => tier switch
    {
        ValueTier.S => S,
        ValueTier.A => A,
        ValueTier.B => B,
        ValueTier.C => C,
        ValueTier.D => D,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "The dark step starts at nothing."),
    };

    public ValueLadder With(ValueTier tier, ValueStep step) => tier switch
    {
        ValueTier.S => this with { S = step },
        ValueTier.A => this with { A = step },
        ValueTier.B => this with { B = step },
        ValueTier.C => this with { C = step },
        ValueTier.D => this with { D = step },
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "The dark step starts at nothing."),
    };

    /// <summary>The step of a value known in both Divine and Exalted Orbs: the loudest whose start it reaches.</summary>
    public ValueTier Of(decimal divines, decimal exalteds)
    {
        foreach (var tier in Starting)
        {
            var step = Start(tier);
            if ((step.InDivine ? divines : exalteds) >= step.Amount)
            {
                return tier;
            }
        }

        return ValueTier.E;
    }

    /// <summary>The first step that does not start below the one above it, or null when each does. Steps in
    /// different currencies are compared at <paramref name="exaltedPerDivine"/>; without a rate only steps in
    /// the same currency can be compared.</summary>
    public ValueTier? OutOfOrder(decimal? exaltedPerDivine)
    {
        for (var i = 1; i < Starting.Count; i++)
        {
            var above = Start(Starting[i - 1]);
            var step = Start(Starting[i]);
            if (above.InDivine == step.InDivine)
            {
                if (step.Amount >= above.Amount)
                {
                    return Starting[i];
                }
            }
            else if (exaltedPerDivine is { } rate && InExalted(step, rate) >= InExalted(above, rate))
            {
                return Starting[i];
            }
        }

        return null;
    }

    /// <summary>"10 div;1 div;30 ex;6 ex;1 ex", as Settings stores it.</summary>
    public string Format() => string.Join(";", Starting.Select(t => Start(t).ToString()));

    /// <summary>The ladder <see cref="Format"/> wrote, or null for anything else (Settings then keeps the default).</summary>
    public static ValueLadder? Parse(string? text)
    {
        var parts = (text ?? "").Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length != Starting.Count)
        {
            return null;
        }

        var steps = new List<ValueStep>();
        foreach (var part in parts)
        {
            var words = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var currency = words.Length == 2 ? words[1] switch { "div" => "divine", "ex" => "exalted", _ => null } : null;
            if (currency is null || !decimal.TryParse(words[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
                || amount <= 0 || amount > MaxAmount)
            {
                return null;
            }

            steps.Add(new ValueStep(amount, currency));
        }

        return new ValueLadder(steps[0], steps[1], steps[2], steps[3], steps[4]);
    }

    private static decimal InExalted(ValueStep step, decimal rate) => step.InDivine ? step.Amount * rate : step.Amount;
}
