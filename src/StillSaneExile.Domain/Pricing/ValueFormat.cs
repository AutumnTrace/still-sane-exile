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
