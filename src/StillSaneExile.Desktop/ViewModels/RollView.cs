using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Reading;
using StillSaneExile.Domain.Rolls;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>A stat's tier and where its roll sits in the tier's range, small enough for every row of the
/// card: "T8  170 ━━━●━━ 179". The tier is the game's own number: Tier 1 is the best a stat rolls on that kind
/// of item (docs/GAME.md §1.5). The game prints no tier count, so the badge shows the number alone.</summary>
/// <param name="Tier">"T8", or null for a line without a tier (a unique's stat, an implicit).</param>
/// <param name="Scored">Which end is better is known: the bar fills from the worse end, and <paramref name="Low"/>
/// is the worse end. Otherwise it only marks the roll between the lowest and the highest number.</param>
/// <param name="Position">0 to 1: where the marker sits, from the left.</param>
/// <param name="Low">The number at the left end; a line with two numbers shows both, "20/34".</param>
/// <param name="High">The number at the right end.</param>
/// <param name="Spoken">What a screen reader says: "Tier 8, rolls 170 to 179".</param>
public sealed record RollView(string? Tier, bool HasBar, bool Scored, double Position, string Low, string High, string Spoken)
{
    public bool HasTier => Tier is not null;

    /// <summary>What a screen reader says (WPF reads a data object through ToString).</summary>
    public override string ToString() => Spoken;

    /// <summary>The tier and bar for a line, or null when there is neither: no reading, a line that could be
    /// two stats, or no range and no tier.</summary>
    public static RollView? From(LineReading? l)
    {
        if (l is null || l.Status == LineStatus.Ambiguous)
        {
            return null;
        }

        var tier = l.Tier is { } t ? $"T{t}" : null;
        var tierWords = l.Tier is { } n ? $"Tier {n}" : null;
        var scored = l.Rolls.Where(r => r.Status == RollStatus.Scored).ToList();
        var used = scored.Count > 0 ? scored : l.Rolls.Where(r => r.Status == RollStatus.PositionOnly).ToList();
        if (used.Count == 0)
        {
            return tier is null ? null : new RollView(tier, false, false, 0, "", "", tierWords!);
        }

        var isScored = scored.Count > 0;
        var position = (double)used.Average(r => isScored ? r.Fill!.Value : r.Position!.Value);
        string End(Func<RollAssessment, decimal?> pick) => string.Join("/", used.Select(r => RollQuality.Num(pick(r))));
        var low = isScored ? End(r => r.Direction == StatDirection.LowerIsBetter ? r.Max : r.Min) : End(r => r.Min);
        var high = isScored ? End(r => r.Direction == StatDirection.LowerIsBetter ? r.Min : r.Max) : End(r => r.Max);
        var ranges = string.Join(" and ", used.Select(r => $"{RollQuality.Num(r.Min)} to {RollQuality.Num(r.Max)}"));
        var spoken = string.Join(", ", new[] { tierWords, $"rolls {ranges}" }.Where(s => s is not null));
        return new RollView(tier, true, isScored, Math.Clamp(position, 0, 1), low, high, spoken);
    }
}
