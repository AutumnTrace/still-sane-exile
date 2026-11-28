using StillSaneExile.Domain.Data;
using StillSaneExile.Domain.Items;

namespace StillSaneExile.Domain.Trade;

/// <summary>SYNTHETIC listings for the demo profile, so the price check can be practised before live
/// prices exist. Not market data: every listing is invented around the item's own numbers. The same
/// item always gets the same listings, on every run and every machine.</summary>
public static class DemoMarket
{
    public const int DefaultCount = 120;

    /// <param name="seedKey">Stable text that identifies the item (its comparison-set key).</param>
    /// <param name="anchorExalted">What a listing exactly as good as this item asks, in Exalted Orbs.</param>
    /// <param name="exaltedPerDivine">When given, listings worth one Divine Orb or more are priced in Divine.</param>
    /// <param name="ceilings">The most each stat (row id) can show on this base (<see cref="Ceilings"/>): no listing
    /// goes past it, so none shows a roll the game can't have. The item's own number is always allowed.</param>
    public static IReadOnlyList<Listing> For(
        ComparisonWorksheet w,
        string seedKey,
        decimal anchorExalted,
        int? itemLevel,
        decimal? exaltedPerDivine,
        DateTimeOffset now,
        int count = DefaultCount,
        IReadOnlyDictionary<string, decimal>? ceilings = null)
    {
        var rng = new Lcg(Fnv1a(seedKey));
        var plain = w.Rows.Where(r => r.Actual is > 0 && r.Kind != RowKind.Derived).ToList();
        var derived = w.Rows.Where(r => r.Actual is > 0 && r.Kind == RowKind.Derived).ToList();
        var unique = w.Rarity == ItemRarity.Unique; // a unique always has its own stats; only the rolls differ
        var listings = new List<Listing>(count);

        for (var i = 0; i < count; i++)
        {
            var stats = new Dictionary<string, decimal>(StringComparer.Ordinal);
            var ratios = new Dictionary<string, double>(StringComparer.Ordinal);
            double score = 0, weight = 0;

            foreach (var r in plain)
            {
                var present = rng.Next() < (unique ? 1.0 : r.Selected ? 0.85 : 0.55);
                var spread = rng.Next();
                var wgt = r.Selected ? 2.0 : 1.0;
                weight += wgt;
                if (!present)
                {
                    score += wgt * 0.4;
                    continue;
                }

                var lower = r.Direction == StatDirection.LowerIsBetter;
                var factor = lower ? 0.85 + spread * 0.5 : unique ? 0.8 + spread * 0.3 : 0.65 + spread * 0.55;
                var value = Round((double)r.Actual!.Value * factor, r.Actual.Value);
                if (!lower && ceilings is not null && ceilings.TryGetValue(r.Id, out var most))
                {
                    value = Math.Min(value, Math.Max(most, r.Actual.Value));
                }

                var ratio = value <= 0 ? 0 : lower ? (double)(r.Actual.Value / value) : (double)(value / r.Actual.Value);
                stats[r.Id] = value;
                ratios[r.Template] = ratio;
                score += wgt * Math.Min(ratio, 1.15);
            }

            // A total such as physical DPS follows the stats it is made of.
            foreach (var r in derived)
            {
                var parts = r.Includes.Select(t => ratios.TryGetValue(t, out var x) ? x : 0.8).DefaultIfEmpty(0.9).Average();
                var ratio = Math.Clamp(parts, 0.5, 1.2) * (0.92 + rng.Next() * 0.2);
                stats[r.Id] = Round((double)r.Actual!.Value * ratio, r.Actual.Value);
                score += 3 * Math.Min(ratio, 1.15);
                weight += 3;
            }

            // The anchor is what items like this one ask: a typical match of the starting search for a
            // rare, the typical roll for a unique.
            var quality = weight > 0 ? score / weight : 0.85;
            var typical = unique ? 0.95 : 0.85;
            var exalted = (double)anchorExalted * Math.Exp(4.0 * (quality - typical)) * (0.75 + rng.Next() * 0.7);
            exalted = Math.Max(1, exalted);
            var (amount, currency) = exaltedPerDivine is { } perDivine && exalted >= (double)perDivine
                ? (Math.Round((decimal)exalted / perDivine, 1), "divine")
                : (Math.Round((decimal)exalted), "exalted");

            var level = itemLevel is { } il ? Math.Clamp(il - 5 + (int)(rng.Next() * 9), 1, 100) : (int?)null;
            var buyNow = rng.Next() < 0.6;
            var age = TimeSpan.FromMinutes(2 + Math.Round(rng.Next() * 600));
            listings.Add(new Listing($"demo-{i + 1}", amount, currency, stats, level, buyNow, now - age));
        }

        return listings;
    }

    /// <summary>The most each row's stat can show on an item of <paramref name="itemClass"/>: the best tier's top
    /// number of every family that prints that line, added up, since a normal copy merges such lines (the
    /// pure and the hybrid physical damage %). A line of two numbers counts their average, as the search does.
    /// Rows the pack doesn't know get no ceiling.</summary>
    public static IReadOnlyDictionary<string, decimal> Ceilings(DataPack? pack, string itemClass, IEnumerable<WorksheetRow> rows)
    {
        var ceilings = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (pack is null)
        {
            return ceilings;
        }

        foreach (var row in rows.Where(r => r.Kind != RowKind.Derived))
        {
            decimal total = 0;
            var found = false;
            foreach (var family in pack.Families.Where(f => f.AppliesTo(itemClass) && f.Tiers.Count > 0))
            {
                var index = family.Lines.ToList().FindIndex(l => l.Template == row.Template);
                if (index < 0)
                {
                    continue;
                }

                var best = family.TierOneIsBest ? family.Tiers.MinBy(t => t.Tier)! : family.Tiers.MaxBy(t => t.Tier)!;
                var offset = family.ComponentOffset(index);
                var tops = Enumerable.Range(offset, family.Lines[index].Components).Where(i => i < best.Ranges.Count).Select(i => best.Range(i).Hi).ToList();
                if (tops.Count > 0)
                {
                    total += tops.Average();
                    found = true;
                }
            }

            if (found)
            {
                ceilings[row.Id] = total;
            }
        }

        return ceilings;
    }

    private static decimal Round(double value, decimal like) =>
        Math.Round((decimal)value, like % 1 == 0 ? 0 : 1, MidpointRounding.AwayFromZero);

    private static uint Fnv1a(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash;
    }

    /// <summary>Tiny linear congruential generator: deterministic everywhere, unlike System.Random's seeding.</summary>
    private sealed class Lcg(uint seed)
    {
        private uint _state = seed;

        public double Next()
        {
            _state = unchecked(_state * 1664525u + 1013904223u);
            return _state / 4294967296.0;
        }
    }
}
