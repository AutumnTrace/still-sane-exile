using StillSaneExile.Domain.Data;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Rolls;

namespace StillSaneExile.Domain.Reading;

public enum LineStatus
{
    /// <summary>The game printed the range (advanced copy).</summary>
    FromAdvancedCopy,
    /// <summary>Normal copy, exactly one pack explanation fits.</summary>
    Resolved,
    /// <summary>Normal copy, more than one explanation fits: no bar, ask for an advanced copy.</summary>
    Ambiguous,
    /// <summary>The pack knows the wording, but no tier range contains the value.</summary>
    RangeMismatch,
    /// <summary>The pack does not cover this wording, base class or source.</summary>
    NotInPack,
    /// <summary>No data pack loaded.</summary>
    NoPack,
}

/// <summary>One way to explain a displayed line: a single affix tier, or a pure affix and a hybrid
/// affix whose numbers the game merged into one line.</summary>
public sealed record Explanation(string Description, IReadOnlyList<(string FamilyId, int Tier)> Parts, bool IsMerge);

/// <param name="RangeCaveat">When the range came from a pack that isn't verified, what the line must say about it
/// (<see cref="DataPack.RangeCaveat"/>); null for the game's own range or a verified pack.</param>
public sealed record LineReading(
    ModGroup Group,
    ModLine Line,
    LineStatus Status,
    IReadOnlyList<StatComponent> Components,
    IReadOnlyList<RollAssessment> Rolls,
    int? Tier,
    int? TierCount,
    string? AffixName,
    string? RangeCaveat,
    IReadOnlyList<Explanation> Candidates,
    string? Note)
{
    public ModSource Source => Group.Source;

    public string TierLabel => Tier is null ? "" : TierCount is null ? $"T{Tier}" : $"T{Tier} of {TierCount}";
}

public sealed record ItemReading(ItemSnapshot Item, IReadOnlyList<LineReading> Lines, DataPack? Pack, decimal? PhysicalDps)
{
    public bool HasAmbiguity => Lines.Any(l => l.Status == LineStatus.Ambiguous);
}

/// <summary>Combines a parsed item with a data pack: decides, per line, whether a roll bar is defensible.</summary>
public static class ItemReader
{
    public static ItemReading Read(ItemSnapshot item, DataPack? pack)
    {
        var lines = new List<LineReading>();
        var explicitTemplates = item.Mods
            .Where(g => g.Source is ModSource.Explicit or ModSource.Fractured or ModSource.Desecrated)
            .SelectMany(g => g.Lines.Select(l => l.Template))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var group in item.Mods)
        {
            foreach (var line in group.Lines)
            {
                lines.Add(group.FromAdvancedCopy
                    ? ReadAdvanced(item, group, line, pack)
                    : ReadNormal(item, group, line, pack, explicitTemplates));
            }
        }

        return new ItemReading(item, lines, pack, WeaponMath.PhysicalDps(item.Weapon));
    }

    private static LineReading ReadAdvanced(ItemSnapshot item, ModGroup group, ModLine line, DataPack? pack)
    {
        var family = pack is null ? null : FindFamily(pack, item, group, line);
        var familyLine = family?.Lines.FirstOrDefault(l => l.Template == line.Template);
        var direction = familyLine?.Direction ?? StatDirection.Unknown;
        var components = line.Components.Select(c => c with { Direction = direction }).ToList();
        string? note = null;

        if (family is not null && group.Tier is { } tier && family.Tiers.FirstOrDefault(t => t.Tier == tier) is { } packTier)
        {
            var offset = family.ComponentOffset(family.Lines.ToList().FindIndex(l => l.Template == line.Template));
            for (var i = 0; i < components.Count && offset + i < packTier.Ranges.Count; i++)
            {
                var (lo, hi) = packTier.Range(offset + i);
                if (components[i].RangeSource == RangeProvenance.AdvancedCopy && (components[i].Min != lo || components[i].Max != hi))
                {
                    note = "The game's range differs from the loaded pack; the game's range is used. The pack may be out of date.";
                }
            }
        }

        if (familyLine is null && components.Any(c => c.RangeSource == RangeProvenance.AdvancedCopy))
        {
            note ??= "Direction not verified for this wording: position marker only.";
        }

        return new LineReading(group, line, LineStatus.FromAdvancedCopy, components, components.Select(RollQuality.Assess).ToList(),
            group.Tier, family?.Tiers.Count, group.AffixName, RangeCaveat: null, [], note);
    }

    private static ModFamily? FindFamily(DataPack pack, ItemSnapshot item, ModGroup group, ModLine line)
    {
        var candidates = pack.Families.Where(f => f.AppliesTo(item.ItemClass) && f.Lines.Any(l => l.Template == line.Template));
        if (group.Side != AffixSide.None)
        {
            candidates = candidates.Where(f => f.Side == group.Side);
        }

        // An advanced header lists every line of the affix: prefer the family with exactly that shape.
        var shape = group.Lines.Select(l => l.Template).ToList();
        return candidates.FirstOrDefault(f => f.Lines.Select(l => l.Template).SequenceEqual(shape))
            ?? candidates.FirstOrDefault();
    }

    private static LineReading ReadNormal(ItemSnapshot item, ModGroup group, ModLine line, DataPack? pack, HashSet<string> explicitTemplates)
    {
        LineReading Plain(LineStatus status, string? note) =>
            new(group, line, status, line.Components, line.Components.Select(RollQuality.Assess).ToList(), null, null, null, null, [], note);

        if (pack is null)
        {
            return Plain(LineStatus.NoPack, "No data pack loaded: ranges unknown. Advanced copy (Ctrl+Alt+C) shows the game's ranges.");
        }

        if (group.Source is not (ModSource.Explicit or ModSource.Fractured or ModSource.Desecrated) || line.Components.Count == 0)
        {
            return Plain(LineStatus.NotInPack, null);
        }

        var providers = pack.Families
            .Where(f => f.AppliesTo(item.ItemClass) && f.Lines.Any(l => l.Template == line.Template))
            .ToList();
        if (providers.Count == 0)
        {
            return Plain(LineStatus.NotInPack, "Not covered by the loaded pack.");
        }

        var singles = new List<(ModFamily Family, FamilyTier Tier)>();
        var merges = new List<Explanation>();

        foreach (var f in providers)
        {
            if (!OtherLinesPresent(f, line.Template, explicitTemplates))
            {
                continue;
            }

            var li = f.Lines.ToList().FindIndex(l => l.Template == line.Template);
            var offset = f.ComponentOffset(li);
            foreach (var t in EligibleTiers(f, item))
            {
                var fits = true;
                for (var c = 0; c < line.Components.Count; c++)
                {
                    var (lo, hi) = t.Range(offset + c);
                    var v = line.Components[c].Value;
                    fits &= v >= lo && v <= hi;
                }

                if (fits)
                {
                    singles.Add((f, t));
                }
            }
        }

        // Pure + hybrid sharing a single-number stat: the game shows one merged line that cannot be
        // decomposed from the total alone.
        if (line.Components.Count == 1)
        {
            var v = line.Components[0].Value;
            foreach (var pure in providers.Where(f => !f.IsHybrid))
            {
                foreach (var hybrid in providers.Where(f => f.IsHybrid && f.Group != pure.Group && OtherLinesPresent(f, line.Template, explicitTemplates)))
                {
                    var hOffset = hybrid.ComponentOffset(hybrid.Lines.ToList().FindIndex(l => l.Template == line.Template));
                    var pairs = new List<(int, int)>();
                    foreach (var tp in EligibleTiers(pure, item))
                    {
                        foreach (var th in EligibleTiers(hybrid, item))
                        {
                            var (plo, phi) = tp.Range(0);
                            var (hlo, hhi) = th.Range(hOffset);
                            if (v >= plo + hlo && v <= phi + hhi)
                            {
                                pairs.Add((tp.Tier, th.Tier));
                            }
                        }
                    }

                    if (pairs.Count > 0)
                    {
                        var parts = pairs.SelectMany(p => new[] { (pure.Id, p.Item1), (hybrid.Id, p.Item2) }).Distinct().ToList();
                        merges.Add(new Explanation(
                            $"{Describe(pure)} + {Describe(hybrid)} merged into one line ({pairs.Count} tier pair{(pairs.Count == 1 ? "" : "s")} fit)",
                            parts,
                            IsMerge: true));
                    }
                }
            }
        }

        var candidates = singles
            .Select(s => new Explanation($"{Describe(s.Family)} T{s.Tier.Tier} \"{s.Tier.Name}\" ({RangeText(s.Family, s.Tier, line)})", [(s.Family.Id, s.Tier.Tier)], IsMerge: false))
            .Concat(merges)
            .ToList();

        if (candidates.Count == 0)
        {
            return Plain(LineStatus.RangeMismatch, "The value fits no tier this item could roll. The pack may be out of date, or the line merges sources the pack does not model.");
        }

        if (candidates.Count > 1 || merges.Count > 0)
        {
            return new LineReading(group, line, LineStatus.Ambiguous, line.Components, line.Components.Select(RollQuality.Assess).ToList(),
                null, null, null, null, candidates, "More than one affix explains this line. Use an advanced copy (Ctrl+Alt+C) to split it.");
        }

        var (family, tier) = singles[0];
        var fl = family.Lines.First(l => l.Template == line.Template);
        var off = family.ComponentOffset(family.Lines.ToList().FindIndex(l => l.Template == line.Template));
        var resolved = line.Components
            .Select((c, i) =>
            {
                var (lo, hi) = tier.Range(off + i);
                return c with { Min = lo, Max = hi, Direction = fl.Direction, RangeSource = RangeProvenance.DataPack };
            })
            .ToList();

        return new LineReading(group, line, LineStatus.Resolved, resolved, resolved.Select(RollQuality.Assess).ToList(),
            tier.Tier, family.Tiers.Count, tier.Name, pack.RangeCaveat, candidates,
            family.IsHybrid ? "Resolved as a hybrid affix: its other line shares this tier." : null);
    }

    private static bool OtherLinesPresent(ModFamily f, string template, HashSet<string> present) =>
        f.Lines.Where(l => l.Template != template).All(l => present.Contains(l.Template));

    private static IEnumerable<FamilyTier> EligibleTiers(ModFamily f, ItemSnapshot item) =>
        item.ItemLevel is { } ilvl ? f.Tiers.Where(t => t.Level <= ilvl) : f.Tiers;

    private static string Describe(ModFamily f) => f.IsHybrid ? $"hybrid {f.Group}" : f.Group;

    private static string RangeText(ModFamily f, FamilyTier t, ModLine line)
    {
        var off = f.ComponentOffset(f.Lines.ToList().FindIndex(l => l.Template == line.Template));
        return string.Join(", ", Enumerable.Range(0, line.Components.Count).Select(i =>
        {
            var (lo, hi) = t.Range(off + i);
            return $"{RollQuality.Num(lo)}–{RollQuality.Num(hi)}";
        }));
    }
}

public static class WeaponMath
{
    /// <summary>Displayed average local physical hit × displayed attacks per second. The displayed
    /// numbers already include local modifiers and quality, so nothing is reapplied.</summary>
    public static decimal? PhysicalDps(WeaponProperties? w) =>
        w is { PhysicalMin: { } lo, PhysicalMax: { } hi, AttacksPerSecond: { } aps } ? (lo + hi) / 2m * aps : null;
}
