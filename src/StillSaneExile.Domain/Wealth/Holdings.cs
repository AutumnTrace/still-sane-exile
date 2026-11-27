using System.Globalization;
using System.Text.RegularExpressions;

namespace StillSaneExile.Domain.Wealth;

/// <summary>Where an observation came from. Stash snapshots (#17) will add their own origin.</summary>
public enum HoldingOrigin
{
    /// <summary>Typed into the manual form; observed at the moment it was saved.</summary>
    Manual,

    /// <summary>A row of a CSV file the player imported; <see cref="HoldingEntry.Source"/> names the file.</summary>
    CsvImport,
}

/// <summary>What a quantity is of: one item (and variant) in one stash tab. Profile and league are not
/// part of it because a ledger is only ever read for one profile and one league at a time.</summary>
public readonly record struct HoldingKey(string Tab, string ItemId, string? Variant);

/// <summary>One observation: "at <see cref="SnapshotAt"/>, this tab held <see cref="Quantity"/> of this
/// item". Observations are never edited. A new count is a new observation, so the ledger keeps the
/// history (#19) and the current holding is simply the latest observation per <see cref="HoldingKey"/>.
/// A quantity of 0 is a real observation: "none left here".</summary>
/// <param name="ItemId">Trimmed and lower-case, the same ids the price book uses.</param>
/// <param name="Variant">Lower-case, or null for none.</param>
/// <param name="SnapshotAt">In UTC. Two spellings of the same instant are the same time.</param>
/// <param name="Source">The CSV file name (never its folder), or null for a manual entry.</param>
public sealed record HoldingEntry(
    string League,
    string Tab,
    string ItemId,
    string? Variant,
    long Quantity,
    DateTimeOffset SnapshotAt,
    HoldingOrigin Origin,
    string? Source = null)
{
    public HoldingKey Key => new(Tab, ItemId, Variant);
}

/// <summary>Field rules shared by the manual form and the CSV import, so both say the same thing about
/// the same mistake. Each method returns a plain-English error, or null when the value is fine.</summary>
public static partial class HoldingFields
{
    public const int MaxTabLength = 64;
    public const int MaxIdLength = 100;

    /// <summary>Far above any real stack or tab total; a bigger number is a typo, not a holding.</summary>
    public const long MaxQuantity = 1_000_000_000;

    /// <summary>How far ahead of the clock a snapshot time may be, for two clocks that disagree a little.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public static string? League(string? raw, string activeLeague, out string league)
    {
        league = (raw ?? "").Trim();
        if (league.Length == 0)
        {
            return "League is empty.";
        }

        // Ignoring case can't cross leagues (no two leagues differ only in case); anything else can, so
        // it is refused rather than carried over.
        if (!string.Equals(league, activeLeague.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return $"This row is for league \"{Clip(league)}\"; you are on \"{activeLeague}\".";
        }

        league = activeLeague;
        return null;
    }

    public static string? Tab(string? raw, out string tab)
    {
        tab = (raw ?? "").Trim();
        if (tab.Length == 0)
        {
            return "Tab is empty. Use the stash tab's name, e.g. Currency.";
        }

        if (tab.Any(char.IsControl))
        {
            return "Tab has a line break or another control character in it.";
        }

        return tab.Length > MaxTabLength ? $"Tab is longer than {MaxTabLength} characters." : null;
    }

    public static string? ItemId(string? raw, out string id)
    {
        var trimmed = (raw ?? "").Trim();
        id = trimmed.ToLowerInvariant();
        if (id.Length == 0)
        {
            return "Item id is empty.";
        }

        if (id.Any(char.IsControl))
        {
            return "Item id has a line break or another control character in it.";
        }

        if (id.Any(char.IsWhiteSpace))
        {
            // The usual cause is the display name ("Divine Orb") typed where the id goes.
            return $"Item id \"{Clip(trimmed)}\" has a space in it. Ids look like divine or greater-iron-rune.";
        }

        return id.Length > MaxIdLength ? $"Item id is longer than {MaxIdLength} characters." : null;
    }

    public static string? Variant(string? raw, out string? variant)
    {
        var v = (raw ?? "").Trim();
        variant = v.Length == 0 ? null : v.ToLowerInvariant();
        if (v.Any(char.IsControl))
        {
            return "Variant has a line break or another control character in it.";
        }

        return v.Length > MaxIdLength ? $"Variant is longer than {MaxIdLength} characters." : null;
    }

    /// <summary>A stack count: digits only, 0 to <see cref="MaxQuantity"/>. "1,200" is refused rather than
    /// read as 1200, because the same text means 1.2 in half the world's spreadsheets.</summary>
    public static string? Quantity(string? raw, out long quantity)
    {
        quantity = 0;
        var s = (raw ?? "").Trim();
        if (s.Length == 0)
        {
            return "Quantity is empty.";
        }

        if (s.All(char.IsAsciiDigit))
        {
            var digits = s.TrimStart('0');
            if (digits.Length > 10 || (digits.Length > 0 && long.Parse(digits, CultureInfo.InvariantCulture) > MaxQuantity))
            {
                return $"Quantity {Clip(s)} is more than {MaxQuantity.ToString("#,0", CultureInfo.InvariantCulture)}. Check the number.";
            }

            quantity = digits.Length == 0 ? 0 : long.Parse(digits, CultureInfo.InvariantCulture);
            return null;
        }

        if (Grouped().IsMatch(s))
        {
            return $"Quantity \"{s}\" has a thousands separator. Write digits only: {new string(s.Where(char.IsAsciiDigit).ToArray())}.";
        }

        if (s.StartsWith('-') && s.Length > 1 && s[1..].All(c => char.IsAsciiDigit(c) || c is '.' or ','))
        {
            return $"Quantity {s} is negative. Use 0 when there are none left.";
        }

        if (s.All(c => char.IsAsciiDigit(c) || c is '.' or ','))
        {
            return $"Quantity {s} isn't a whole number. Stacks count whole items.";
        }

        return $"Quantity \"{Clip(s)}\" isn't a number. Write digits only, e.g. 1200.";
    }

    /// <summary>ISO-8601 with a "Z" or an offset: 2026-11-27T10:00:00Z, 2026-11-27T11:00+01:00. A time
    /// without one is refused: it could be any of 24 hours.</summary>
    public static string? SnapshotTime(string? raw, DateTimeOffset now, out DateTimeOffset at)
    {
        at = default;
        var s = (raw ?? "").Trim();
        if (s.Length == 0)
        {
            return "Snapshot time is empty.";
        }

        var m = IsoTime().Match(s);
        if (!m.Success)
        {
            return $"Snapshot time \"{Clip(s)}\" isn't an ISO-8601 time. Write it like 2026-11-27T10:00:00Z.";
        }

        if (!m.Groups["zone"].Success)
        {
            return $"Snapshot time {s} has no time zone. Add Z for UTC, or an offset like +01:00.";
        }

        try
        {
            int Part(string name) => int.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
            var seconds = m.Groups["s"].Success ? Part("s") : 0;
            var ticks = m.Groups["f"].Success ? long.Parse(m.Groups["f"].Value.PadRight(7, '0'), CultureInfo.InvariantCulture) : 0;
            var local = new DateTime(Part("y"), Part("mo"), Part("d"), Part("h"), Part("mi"), seconds, DateTimeKind.Unspecified).AddTicks(ticks);
            at = new DateTimeOffset(local, Offset(m.Groups["zone"].Value)).ToUniversalTime();
        }
        catch (ArgumentException)
        {
            return $"Snapshot time {s} isn't a real date and time.";
        }

        if (at > now + FutureTolerance)
        {
            var clock = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return $"Snapshot time {s} is in the future: it is {clock} UTC now.";
        }

        return null;
    }

    /// <summary>"Z", "+01:00", "+0100" or "+01". Minutes must be under 60; the offset range is checked
    /// by <see cref="DateTimeOffset"/> itself.</summary>
    private static TimeSpan Offset(string zone)
    {
        if (zone is "Z" or "z")
        {
            return TimeSpan.Zero;
        }

        var digits = zone[1..].Replace(":", "", StringComparison.Ordinal);
        var hours = int.Parse(digits[..2], CultureInfo.InvariantCulture);
        var minutes = digits.Length > 2 ? int.Parse(digits[2..], CultureInfo.InvariantCulture) : 0;
        if (minutes >= 60)
        {
            throw new ArgumentOutOfRangeException(nameof(zone));
        }

        var offset = new TimeSpan(hours, minutes, 0);
        return zone[0] == '-' ? -offset : offset;
    }

    internal static string Clip(string s) => s.Length <= 40 ? s : s[..39] + "…";

    [GeneratedRegex(@"^\d{1,3}([,.' _ ]\d{3})+$")]
    private static partial Regex Grouped();

    [GeneratedRegex(@"^(?<y>\d{4})-(?<mo>\d{2})-(?<d>\d{2})[Tt ](?<h>\d{2}):(?<mi>\d{2})(:(?<s>\d{2})([.,](?<f>\d{1,7}))?)?(?<zone>[Zz]|[+-]\d{2}(:?\d{2})?)?$")]
    private static partial Regex IsoTime();
}
