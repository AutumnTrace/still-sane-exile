using System.Globalization;
using StillSaneExile.Domain.Items;

namespace StillSaneExile.Domain.Rolls;

public enum RollStatus
{
    /// <summary>Verified range and direction: a filled bar is defensible.</summary>
    Scored,
    /// <summary>The value cannot roll.</summary>
    Fixed,
    /// <summary>Verified range, unverified or build-dependent direction: uncolored position marker only.</summary>
    PositionOnly,
    /// <summary>The value sits outside the range we were given. Excluded from any scoring.</summary>
    RangeMismatch,
    /// <summary>No defensible range. No bar.</summary>
    NoRange,
}

/// <summary>"How close to perfect is this roll?" for one numeric component.</summary>
/// <param name="Fill">0..1, how much of the bar to fill (already reversed for lower-is-better).</param>
/// <param name="Position">0..1, where the value sits between min and max regardless of direction.</param>
/// <param name="DistanceFromBest">Native units between the value and the best end of the range.</param>
public sealed record RollAssessment(
    RollStatus Status,
    decimal Value,
    decimal? Min,
    decimal? Max,
    decimal? Fill,
    decimal? Position,
    decimal? DistanceFromBest,
    StatDirection Direction,
    string Unit)
{
    public bool HasBar => Status is RollStatus.Scored or RollStatus.PositionOnly;
}

public static class RollQuality
{
    public static RollAssessment Assess(StatComponent c) =>
        Assess(c.Value, c.Min, c.Max, c.Direction, c.Unit, c.RangeSource);

    public static RollAssessment Assess(decimal value, decimal? min, decimal? max, StatDirection direction, string unit, RangeProvenance provenance = RangeProvenance.AdvancedCopy)
    {
        if (provenance == RangeProvenance.FixedByGame)
        {
            return new RollAssessment(RollStatus.Fixed, value, value, value, null, null, null, direction, unit);
        }

        if (min is null || max is null || provenance == RangeProvenance.None)
        {
            return new RollAssessment(RollStatus.NoRange, value, min, max, null, null, null, direction, unit);
        }

        var lo = Math.Min(min.Value, max.Value);
        var hi = Math.Max(min.Value, max.Value);

        if (lo == hi)
        {
            return value == lo
                ? new RollAssessment(RollStatus.Fixed, value, lo, hi, null, null, null, direction, unit)
                : new RollAssessment(RollStatus.RangeMismatch, value, lo, hi, null, null, null, direction, unit);
        }

        if (value < lo || value > hi)
        {
            return new RollAssessment(RollStatus.RangeMismatch, value, lo, hi, null, null, null, direction, unit);
        }

        var position = (value - lo) / (hi - lo);
        return direction switch
        {
            StatDirection.HigherIsBetter => new RollAssessment(RollStatus.Scored, value, lo, hi, position, position, hi - value, direction, unit),
            StatDirection.LowerIsBetter => new RollAssessment(RollStatus.Scored, value, lo, hi, 1 - position, position, value - lo, direction, unit),
            _ => new RollAssessment(RollStatus.PositionOnly, value, lo, hi, null, position, null, direction, unit),
        };
    }

    /// <summary>One-line description, e.g. "+125% · 50% of tier · 15 percentage points below max".</summary>
    public static string Describe(RollAssessment a)
    {
        var value = FormatValue(a.Value, a.Unit);
        return a.Status switch
        {
            RollStatus.Fixed => $"{value} · Fixed",
            RollStatus.NoRange => $"{value} · no verified range",
            RollStatus.RangeMismatch => $"{value} · Range mismatch ({Num(a.Min)}–{Num(a.Max)})",
            RollStatus.PositionOnly => $"{value} · {Percent(a.Position)} through {Num(a.Min)}–{Num(a.Max)} · direction not verified",
            _ => $"{value} · {Percent(a.Fill)} of tier · {Distance(a)}",
        };
    }

    public static string Distance(RollAssessment a)
    {
        if (a.DistanceFromBest is not { } d)
        {
            return "";
        }

        if (d == 0)
        {
            return "perfect roll";
        }

        var unit = a.Unit == "%" ? (d == 1 ? "percentage point" : "percentage points") : (d == 1 ? "point" : "points");
        var side = a.Direction == StatDirection.LowerIsBetter ? "above min" : "below max";
        return $"{Num(d)} {unit} {side}";
    }

    public static string Percent(decimal? fraction) =>
        fraction is null ? "?" : (Math.Round(fraction.Value * 100m, 0, MidpointRounding.AwayFromZero)).ToString("0", CultureInfo.InvariantCulture) + "%";

    public static string Num(decimal? v) => v is null ? "?" : v.Value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatValue(decimal v, string unit)
    {
        var sign = v > 0 ? "+" : "";
        return unit == "%" ? $"{sign}{Num(v)}%" : $"{sign}{Num(v)}";
    }
}
