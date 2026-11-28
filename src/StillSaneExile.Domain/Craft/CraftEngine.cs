using System.Globalization;
using StillSaneExile.Domain.Data;
using StillSaneExile.Domain.Items;

namespace StillSaneExile.Domain.Craft;

/// <summary>Simulates crafting one base with a data pack's families, tiers and weights. Nothing here touches
/// the game: it is practice on a made-up item. What each orb and omen does follows the game as SSE
/// understands it and has not been checked in the game; the pack in use says whether its stats are real.
/// Randomness comes from <see cref="Random"/>, so a seeded one gives the same craft every time (tests).</summary>
public sealed class CraftEngine
{
    /// <summary>The orbs on the bench, in the order they are laid out; the pack's essences follow Alchemy.</summary>
    public static readonly IReadOnlyList<CraftCurrency> Orbs =
    [
        new("transmute", "Orb of Transmutation", "Transmute", "Normal → Magic with 1 stat", CraftGroup.Start, Graded: true),
        new("aug", "Orb of Augmentation", "Augment", "Adds a second stat to a Magic item", CraftGroup.Start, Graded: true),
        new("regal", "Regal Orb", "Regal", "Magic → Rare, adds 1 stat", CraftGroup.Start, Graded: true),
        new("alch", "Orb of Alchemy", "Alchemy", "Normal → Rare with 4 stats", CraftGroup.Start, Graded: false),
        new("exalted", "Exalted Orb", "Exalted", "Adds 1 stat to a Rare with room", CraftGroup.Change, Graded: true),
        new("chaos", "Chaos Orb", "Chaos", "Removes 1 random stat, adds 1 new", CraftGroup.Change, Graded: true),
        new("annul", "Orb of Annulment", "Annul", "Removes 1 random stat", CraftGroup.Change, Graded: false),
        new("divine", "Divine Orb", "Divine", "Rerolls the numbers, keeps the tiers", CraftGroup.Finish, Graded: false),
        new("fracturing-orb", "Fracturing Orb", "Fracture", "Locks 1 random stat forever (needs 4+ stats)", CraftGroup.Finish, Graded: false),
        new("vaal", "Vaal Orb", "Vaal", "Corrupts: a random result, then no more changes", CraftGroup.Finish, Graded: false),
    ];

    public static readonly IReadOnlyList<OmenInfo> OmenList =
    [
        new(Omen.SinistralExaltation, "omen-of-sinistral-exaltation", "Omen of Sinistral Exaltation", "Exalted → prefix", "exalted"),
        new(Omen.DextralExaltation, "omen-of-dextral-exaltation", "Omen of Dextral Exaltation", "Exalted → suffix", "exalted"),
        new(Omen.GreaterExaltation, "omen-of-greater-exaltation", "Omen of Greater Exaltation", "Exalted adds 2", "exalted"),
        new(Omen.Whittling, "omen-of-whittling", "Omen of Whittling", "Chaos hits lowest", "chaos"),
        new(Omen.SinistralErasure, "omen-of-sinistral-erasure", "Omen of Sinistral Erasure", "Chaos → prefix", "chaos"),
        new(Omen.DextralErasure, "omen-of-dextral-erasure", "Omen of Dextral Erasure", "Chaos → suffix", "chaos"),
        new(Omen.SinistralAnnulment, "omen-of-sinistral-annulment", "Omen of Sinistral Annulment", "Annul → prefix", "annul"),
        new(Omen.DextralAnnulment, "omen-of-dextral-annulment", "Omen of Dextral Annulment", "Annul → suffix", "annul"),
    ];

    private static readonly AffixSide[] BothSides = [AffixSide.Prefix, AffixSide.Suffix];
    private static readonly string[] FirstNames = ["Doom", "Storm", "Grim", "Hate", "Rune", "Blood", "Dusk", "Gale", "Pain", "Corpse", "Wrath", "Ghoul"];
    private static readonly string[] LastNames = ["Thirst", "Mark", "Volley", "Bane", "Song", "Sting", "Bite", "Needle", "Fletch", "Whisper", "Strike", "Spire"];

    private readonly Random _random;
    private readonly Dictionary<string, ModFamily> _byId;
    private int _nextId;

    public CraftEngine(DataPack pack, CraftBaseData craftBase, Random? random = null)
    {
        Pack = pack;
        Base = craftBase;
        _random = random ?? new Random();
        Families = pack.Families.Where(f => f.AppliesTo(craftBase.ItemClass) && f.Tiers.Count > 0 && f.Tiers.All(t => t.Weight is not null)).ToList();
        _byId = Families.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var essences = (pack.Essences ?? []).Where(e => _byId.ContainsKey(e.Family))
            .Select(e => new CraftCurrency(e.Id, e.Name, e.Name.Replace("Essence of ", "", StringComparison.Ordinal),
                $"Magic → Rare, adding {Label(_byId[e.Family])} (T{e.Tier})", CraftGroup.Start, Graded: false, e));
        var orbs = Orbs.ToList();
        orbs.InsertRange(orbs.FindIndex(o => o.Id == "alch") + 1, essences);
        Currencies = orbs;
    }

    public DataPack Pack { get; }
    public CraftBaseData Base { get; }

    /// <summary>The families that can roll on this base: those of the pack that apply to it and say how often
    /// each tier rolls.</summary>
    public IReadOnlyList<ModFamily> Families { get; }

    /// <summary>The orbs, with this pack's essences among them.</summary>
    public IReadOnlyList<CraftCurrency> Currencies { get; }

    /// <summary>The lowest stat level a graded orb rolls, the game's "Minimum Modifier Level" (0.5.0): Greater and
    /// Perfect Transmutation and Augmentation 44 and 70, Regal, Exalted and Chaos 35 and 50 (docs/GAME.md §3.2).</summary>
    public static int MinLevel(string currency, OrbGrade grade) => (currency, grade) switch
    {
        ("transmute" or "aug", OrbGrade.Greater) => 44,
        ("transmute" or "aug", OrbGrade.Perfect) => 70,
        ("regal" or "exalted" or "chaos", OrbGrade.Greater) => 35,
        ("regal" or "exalted" or "chaos", OrbGrade.Perfect) => 50,
        _ => 0,
    };

    /// <summary>The price book line of an orb at a grade: "greater-exalted-orb" for a Greater Exalted Orb.</summary>
    public static string PriceId(string currency, OrbGrade grade) => (currency, grade) switch
    {
        (_, OrbGrade.Regular) => currency,
        ("transmute", var g) => $"{Grade(g)}-orb-of-transmutation",
        ("aug", var g) => $"{Grade(g)}-orb-of-augmentation",
        ("regal", var g) => $"{Grade(g)}-regal-orb",
        ("exalted", var g) => $"{Grade(g)}-exalted-orb",
        ("chaos", var g) => $"{Grade(g)}-chaos-orb",
        _ => currency,
    };

    public ModFamily Family(string id) => _byId[id];

    public FamilyTier TierOf(CraftMod m) => Family(m.Family).Tiers.First(t => t.Tier == m.Tier);

    public AffixSide SideOf(CraftMod m) => Family(m.Family).Side;

    /// <summary>"#% increased Physical Damage": the family's lines with # for each number, for lists.</summary>
    public static string Label(ModFamily f) => string.Join(" and ", f.Lines.Select(l => Signed(l.Template, l.Template)));

    /// <summary>The stat as the game prints it, one line per line of a hybrid: "Adds 24 to 39 Physical Damage".</summary>
    public string Text(CraftMod m)
    {
        var f = Family(m.Family);
        var at = 0;
        var lines = new List<string>();
        foreach (var line in f.Lines)
        {
            var text = line.Template;
            for (var c = 0; c < line.Components && at < m.Values.Count; c++)
            {
                var hash = text.IndexOf('#', StringComparison.Ordinal);
                if (hash < 0)
                {
                    break;
                }

                text = text[..hash] + Number(m.Values[at++]) + text[(hash + 1)..];
            }

            lines.Add(Signed(line.Template, text));
        }

        return string.Join("\n", lines);
    }

    /// <summary>What every tier that can roll next weighs: families not on the item, on a side with room
    /// (as an item of <paramref name="asRarity"/>), from <paramref name="minLevel"/> up to the item level. A family
    /// the minimum would shut out entirely keeps its tiers, as in the game ("except if a specific Modifier type
    /// would be excluded entirely").</summary>
    public IReadOnlyList<PoolEntry> Pool(CraftItem item, IReadOnlyCollection<AffixSide> sides, int itemLevel, int minLevel, CraftRarity? asRarity = null)
    {
        var max = MaxPerSide(asRarity ?? item.Rarity);
        var groups = item.Mods.Select(m => Family(m.Family).Group).ToHashSet(StringComparer.Ordinal);
        var pool = new List<PoolEntry>();
        foreach (var f in Families)
        {
            if (!sides.Contains(f.Side) || groups.Contains(f.Group) || Count(item, f.Side) >= max)
            {
                continue;
            }

            var tiers = f.Tiers.Where(t => t.Level <= itemLevel && t.Weight > 0).ToList();
            var above = tiers.Where(t => t.Level >= minLevel).ToList();
            pool.AddRange((above.Count > 0 ? above : tiers).Select(t => new PoolEntry(f, t, t.Weight!.Value)));
        }

        return pool;
    }

    /// <summary>Why <paramref name="currency"/> can't be used on the item now, or null when it can.</summary>
    public string? Check(string currency, CraftItem item, CraftSetup setup)
    {
        var c = Currencies.FirstOrDefault(x => x.Id == currency);
        if (c is null)
        {
            return "Unknown orb.";
        }

        if (item.Corrupted)
        {
            return "Corrupted: no orb on this bench can change it now. Undo or start over.";
        }

        var ml = MinLevel(currency, setup.Grade);
        if (c.Graded && ml > setup.ItemLevel)
        {
            return $"Item level {setup.ItemLevel} is too low for {setup.Grade.ToString().ToLowerInvariant()} orbs: they only roll stats of level {ml} and up.";
        }

        if (c.Essence is { } essence)
        {
            var f = Family(essence.Family);
            return item.Rarity != CraftRarity.Magic ? "Essences work on a Magic (blue) item and make it Rare."
                : item.Mods.Any(m => Family(m.Family).Group == f.Group) ? "The item already has that kind of stat."
                : Count(item, f.Side) >= MaxPerSide(CraftRarity.Rare) ? "No room on that side."
                : null;
        }

        string? NoRoll(IReadOnlyList<PoolEntry> pool) => pool.Count == 0 ? "No stat can roll at these settings." : null;

        return currency switch
        {
            "transmute" => item.Rarity != CraftRarity.Normal ? "Transmutation only works on a Normal (white) item."
                : NoRoll(Pool(item, BothSides, setup.ItemLevel, ml, CraftRarity.Magic)),
            "aug" => item.Rarity != CraftRarity.Magic ? "Augmentation only works on a Magic (blue) item."
                : item.Mods.Count >= 2 ? "This Magic item is full: one prefix and one suffix. Try a Regal Orb."
                : NoRoll(Pool(item, BothSides, setup.ItemLevel, ml)),
            "regal" => item.Rarity != CraftRarity.Magic ? "Regal only works on a Magic (blue) item. Use Transmutation first."
                : NoRoll(Pool(item, BothSides, setup.ItemLevel, ml, CraftRarity.Rare)),
            "alch" => item.Rarity is not (CraftRarity.Normal or CraftRarity.Magic) ? "Alchemy works on a Normal or Magic item."
                : NoRoll(Pool(item with { Mods = [] }, BothSides, setup.ItemLevel, 0, CraftRarity.Rare)),
            "exalted" => item.Rarity != CraftRarity.Rare ? "Exalted only works on a Rare (yellow) item."
                : Opposite(setup, Omen.SinistralExaltation, Omen.DextralExaltation)
                ?? (Pool(item, ExaltSides(setup), setup.ItemLevel, ml).Count == 0
                    ? ExaltSides(setup).Count == 1 ? "No room or no stat left on that side." : "The item is full: 3 prefixes and 3 suffixes."
                    : null),
            "chaos" => item.Rarity != CraftRarity.Rare ? "Chaos only works on a Rare (yellow) item."
                : Opposite(setup, Omen.SinistralErasure, Omen.DextralErasure)
                ?? (Removable(item, Side(setup, Omen.SinistralErasure, Omen.DextralErasure)).Count == 0 ? "Nothing can be removed there: fractured stats are locked." : null),
            "annul" => item.Rarity is not (CraftRarity.Magic or CraftRarity.Rare) || item.Mods.Count == 0 ? "Annulment needs a Magic or Rare item with stats."
                : Opposite(setup, Omen.SinistralAnnulment, Omen.DextralAnnulment)
                ?? (Removable(item, Side(setup, Omen.SinistralAnnulment, Omen.DextralAnnulment)).Count == 0 ? "Nothing can be removed there." : null),
            "divine" => item.Mods.Count == 0 ? "Divine rerolls numbers: the item has no stats yet." : null,
            "fracturing-orb" => item.Rarity != CraftRarity.Rare ? "Fracturing needs a Rare item."
                : item.Mods.Count < 4 ? "Fracturing needs at least 4 stats on the item."
                : item.Mods.Any(m => m.Fractured) ? "This item already has a fractured stat."
                : null,
            "vaal" => null,
            _ => "Unknown orb.",
        };
    }

    /// <summary>Uses <paramref name="currency"/> on the item: the new item and what changed, or why not.</summary>
    public CraftOutcome Use(string currency, CraftItem item, CraftSetup setup)
    {
        if (Check(currency, item, setup) is { } why)
        {
            return CraftOutcome.No(why);
        }

        var c = Currencies.First(x => x.Id == currency);
        var ml = MinLevel(currency, setup.Grade);
        var mods = item.Mods.ToList();
        var fresh = new HashSet<int>();
        var used = new List<Omen>();
        var ilvl = setup.ItemLevel;

        CraftMod? Add(IReadOnlyCollection<AffixSide> sides, int minLevel, CraftRarity rarity)
        {
            var current = item with { Rarity = rarity, Mods = mods };
            if (Pick(Pool(current, sides, ilvl, minLevel)) is not { } p)
            {
                return null;
            }

            var m = Make(p.Family, p.Tier);
            mods.Add(m);
            fresh.Add(m.Id);
            return m;
        }

        CraftOutcome Done(CraftRarity rarity, string? name, string message, bool corrupted = false) =>
            new(new CraftItem(rarity, name, corrupted, mods), message, fresh, used);

        if (c.Essence is { } essence)
        {
            var f = Family(essence.Family);
            var m = Make(f, f.Tiers.First(t => t.Tier == essence.Tier), essence: true);
            mods.Add(m);
            fresh.Add(m.Id);
            return Done(CraftRarity.Rare, RareName(), $"Now Rare with {OneLine(m)} (essence, T{m.Tier})");
        }

        switch (currency)
        {
            case "transmute":
            {
                var m = Add(BothSides, ml, CraftRarity.Magic)!;
                return Done(CraftRarity.Magic, null, $"Now Magic: {OneLine(m)} (T{m.Tier})");
            }

            case "aug":
            {
                var m = Add(BothSides, ml, CraftRarity.Magic)!;
                return Done(CraftRarity.Magic, null, $"Added {OneLine(m)} (T{m.Tier})");
            }

            case "regal":
            {
                var m = Add(BothSides, ml, CraftRarity.Rare)!;
                return Done(CraftRarity.Rare, RareName(), $"Now Rare: {OneLine(m)} (T{m.Tier})");
            }

            case "alch":
            {
                var wasMagic = item.Rarity == CraftRarity.Magic;
                mods.Clear(); // a Magic item's stats are not kept
                for (var i = 0; i < 4; i++)
                {
                    Add(BothSides, 0, CraftRarity.Rare);
                }

                return Done(CraftRarity.Rare, RareName(), $"Now Rare with {fresh.Count} stats" + (wasMagic ? "; the Magic stats are gone" : ""));
            }

            case "exalted":
            {
                var sides = ExaltSides(setup);
                used.AddRange(setup.Omens.Where(o => o is Omen.SinistralExaltation or Omen.DextralExaltation));
                var n = setup.Omens.Contains(Omen.GreaterExaltation) ? 2 : 1;
                if (n == 2)
                {
                    used.Add(Omen.GreaterExaltation);
                }

                var added = new List<string>();
                for (var i = 0; i < n; i++)
                {
                    if (Add(sides, ml, CraftRarity.Rare) is { } m)
                    {
                        added.Add($"{OneLine(m)} (T{m.Tier})");
                    }
                }

                return Done(CraftRarity.Rare, item.Name, $"Added {string.Join(" and ", added)}");
            }

            case "chaos":
            {
                var side = Side(setup, Omen.SinistralErasure, Omen.DextralErasure);
                used.AddRange(setup.Omens.Where(o => o is Omen.SinistralErasure or Omen.DextralErasure));
                var candidates = Removable(item, side);
                if (setup.Omens.Contains(Omen.Whittling))
                {
                    used.Add(Omen.Whittling);
                    candidates = Lowest(candidates);
                }

                var gone = candidates[_random.Next(candidates.Count)];
                mods.RemoveAll(x => x.Id == gone.Id);
                var m = Add(BothSides, ml, CraftRarity.Rare);
                return Done(CraftRarity.Rare, item.Name, $"Removed {OneLine(gone)}" + (m is null ? "" : $" → added {OneLine(m)} (T{m.Tier})"));
            }

            case "annul":
            {
                var side = Side(setup, Omen.SinistralAnnulment, Omen.DextralAnnulment);
                used.AddRange(setup.Omens.Where(o => o is Omen.SinistralAnnulment or Omen.DextralAnnulment));
                var candidates = Removable(item, side);
                var gone = candidates[_random.Next(candidates.Count)];
                mods.RemoveAll(x => x.Id == gone.Id);
                return Done(item.Rarity, item.Name, $"Removed {OneLine(gone)}");
            }

            case "divine":
                Reroll(mods, fresh);
                return Done(item.Rarity, item.Name, "Rerolled every number; the tiers stay the same");

            case "fracturing-orb":
            {
                var lockable = mods.Where(m => !m.Fractured).ToList(); // essence stats too: real copies show fractured crafted stats
                var m = lockable[_random.Next(lockable.Count)];
                mods[mods.IndexOf(m)] = m with { Fractured = true };
                fresh.Add(m.Id);
                return Done(item.Rarity, item.Name, $"Locked forever: {OneLine(m)}");
            }

            default: // "vaal"
                return Vaal(item, mods, fresh, ilvl);
        }
    }

    /// <summary>The wanted stats not on the item yet at the wanted tier or better.</summary>
    /// <param name="wants">Family id → the worst tier still wanted.</param>
    public IReadOnlyDictionary<string, int> Open(CraftItem item, IReadOnlyDictionary<string, int> wants) =>
        wants.Where(w => _byId.ContainsKey(w.Key) && !Has(item, w.Key, w.Value)).ToDictionary(w => w.Key, w => w.Value, StringComparer.Ordinal);

    /// <summary>Whether the item has the family at the tier or better.</summary>
    public bool Has(CraftItem item, string family, int tier) =>
        item.Mods.Any(m => m.Family == family && AtLeast(Family(family), m.Tier, tier));

    /// <summary>The chance that the next of each orb that can be used now adds one of the wanted stats.</summary>
    public IReadOnlyList<CraftOdds> Odds(CraftItem item, CraftSetup setup, IReadOnlyDictionary<string, int> wants)
    {
        var open = Open(item, wants);
        var odds = new List<CraftOdds>();
        if (open.Count == 0 || item.Corrupted)
        {
            return odds;
        }

        void Row(string orb, string label, double? p, bool estimate = false)
        {
            if (p is { } chance && Check(orb, item, setup) is null)
            {
                odds.Add(new CraftOdds(orb, label, chance, estimate));
            }
        }

        switch (item.Rarity)
        {
            case CraftRarity.Normal:
                Row("transmute", "Next Transmutation", Hit(item, BothSides, setup.ItemLevel, MinLevel("transmute", setup.Grade), open, CraftRarity.Magic));
                break;

            case CraftRarity.Magic:
                if (item.Mods.Count < 2)
                {
                    Row("aug", "Next Augmentation", Hit(item, BothSides, setup.ItemLevel, MinLevel("aug", setup.Grade), open));
                }

                Row("regal", "Next Regal", Hit(item, BothSides, setup.ItemLevel, MinLevel("regal", setup.Grade), open, CraftRarity.Rare));
                break;

            case CraftRarity.Rare:
                var p = Hit(item, ExaltSides(setup), setup.ItemLevel, MinLevel("exalted", setup.Grade), open);
                if (setup.Omens.Contains(Omen.GreaterExaltation))
                {
                    Row("exalted", "Next Exalted (adds 2)", p is { } q ? 1 - Math.Pow(1 - q, 2) : null, estimate: true);
                }
                else
                {
                    Row("exalted", "Next Exalted", p);
                }

                var candidates = Removable(item, Side(setup, Omen.SinistralErasure, Omen.DextralErasure));
                if (setup.Omens.Contains(Omen.Whittling))
                {
                    candidates = Lowest(candidates);
                }

                var chances = candidates.Select(c => Hit(item with { Mods = item.Mods.Where(m => m.Id != c.Id).ToList() }, BothSides, setup.ItemLevel, MinLevel("chaos", setup.Grade), open))
                    .OfType<double>().ToList();
                Row("chaos", "Next Chaos", chances.Count > 0 ? chances.Average() : null);
                break;
        }

        return odds;
    }

    /// <summary>Physical damage, attack speed and physical DPS of the base with the item's stats.</summary>
    public CraftWeapon Weapon(CraftItem item)
    {
        decimal increased = 0, flatLow = 0, flatHigh = 0, speed = 0;
        foreach (var m in item.Mods)
        {
            var at = 0;
            foreach (var line in Family(m.Family).Lines)
            {
                var v = m.Values.Skip(at).Take(line.Components).ToList();
                at += line.Components;
                switch (line.Template)
                {
                    case "#% increased Physical Damage" when v.Count == 1:
                        increased += v[0];
                        break;
                    case "Adds # to # Physical Damage" when v.Count == 2:
                        flatLow += v[0];
                        flatHigh += v[1];
                        break;
                    case "#% increased Attack Speed" when v.Count == 1:
                        speed += v[0];
                        break;
                }
            }
        }

        const MidpointRounding Up = MidpointRounding.AwayFromZero;
        var low = Math.Round((Base.Physical[0] + flatLow) * (1 + increased / 100), Up);
        var high = Math.Round((Base.Physical[1] + flatHigh) * (1 + increased / 100), Up);
        var aps = Math.Round(Base.AttacksPerSecond * (1 + speed / 100), 2, Up);
        return new CraftWeapon(low, high, aps, Math.Round((low + high) / 2 * aps, 1, Up));
    }

    /// <summary>The magic item's name as the game builds it: the prefix's name, the base, the suffix's name.</summary>
    public string MagicName(CraftItem item)
    {
        var prefix = item.Mods.FirstOrDefault(m => SideOf(m) == AffixSide.Prefix);
        var suffix = item.Mods.FirstOrDefault(m => SideOf(m) == AffixSide.Suffix);
        return string.Join(" ", new[] { prefix is null ? null : TierOf(prefix).Name, Base.Name, suffix is null ? null : TierOf(suffix).Name }.Where(s => s is not null));
    }

    public static int MaxPerSide(CraftRarity rarity) => rarity switch
    {
        CraftRarity.Rare => 3,
        CraftRarity.Magic => 1,
        _ => 0,
    };

    public int Count(CraftItem item, AffixSide side) => item.Mods.Count(m => SideOf(m) == side);

    /// <summary>Whether <paramref name="tier"/> is as good as <paramref name="wanted"/> or better, by the
    /// family's own order.</summary>
    public static bool AtLeast(ModFamily f, int tier, int wanted) => f.TierOneIsBest ? tier <= wanted : tier >= wanted;

    private static string Grade(OrbGrade g) => g.ToString().ToLowerInvariant();

    private static string? Opposite(CraftSetup setup, Omen left, Omen right) =>
        setup.Omens.Contains(left) && setup.Omens.Contains(right) ? "Two opposite omens are on. Turn one off." : null;

    private static AffixSide? Side(CraftSetup setup, Omen prefix, Omen suffix) =>
        setup.Omens.Contains(prefix) ? AffixSide.Prefix : setup.Omens.Contains(suffix) ? AffixSide.Suffix : null;

    private static IReadOnlyCollection<AffixSide> ExaltSides(CraftSetup setup) =>
        Side(setup, Omen.SinistralExaltation, Omen.DextralExaltation) is { } side ? [side] : BothSides;

    private List<CraftMod> Removable(CraftItem item, AffixSide? side) =>
        item.Mods.Where(m => !m.Fractured && (side is null || SideOf(m) == side)).ToList();

    private List<CraftMod> Lowest(List<CraftMod> mods)
    {
        if (mods.Count == 0)
        {
            return mods;
        }

        var low = mods.Min(m => TierOf(m).Level);
        return mods.Where(m => TierOf(m).Level == low).ToList();
    }

    private double? Hit(CraftItem item, IReadOnlyCollection<AffixSide> sides, int itemLevel, int minLevel, IReadOnlyDictionary<string, int> open, CraftRarity? asRarity = null)
    {
        var pool = Pool(item, sides, itemLevel, minLevel, asRarity);
        var total = pool.Sum(p => (double)p.Weight);
        if (total <= 0)
        {
            return null;
        }

        var hit = pool.Where(p => open.TryGetValue(p.Family.Id, out var tier) && AtLeast(p.Family, p.Tier.Tier, tier)).Sum(p => (double)p.Weight);
        return hit / total;
    }

    private PoolEntry? Pick(IReadOnlyList<PoolEntry> pool)
    {
        var total = pool.Sum(p => p.Weight);
        if (total <= 0)
        {
            return null;
        }

        var x = _random.Next(total);
        foreach (var p in pool)
        {
            x -= p.Weight;
            if (x < 0)
            {
                return p;
            }
        }

        return pool[^1];
    }

    private CraftMod Make(ModFamily f, FamilyTier t, bool essence = false) =>
        new(++_nextId, f.Id, t.Tier, Roll(t), essence);

    private List<decimal> Roll(FamilyTier t) => t.Ranges.Select(r => RollOne(r[0], r[1])).ToList();

    private decimal RollOne(decimal lo, decimal hi) =>
        lo == Math.Floor(lo) && hi == Math.Floor(hi)
            ? _random.Next((int)lo, (int)hi + 1)
            : Math.Round(lo + (hi - lo) * (decimal)_random.NextDouble(), 2);

    private void Reroll(List<CraftMod> mods, HashSet<int> fresh)
    {
        for (var i = 0; i < mods.Count; i++)
        {
            if (!mods[i].Fractured)
            {
                mods[i] = mods[i] with { Values = Roll(TierOf(mods[i])) };
                fresh.Add(mods[i].Id);
            }
        }
    }

    /// <summary>Corrupts the item, then one of the game's four results for rare equipment, about equally likely
    /// (the odds are not published): nothing else; up to three stats rerolled into new ones; a corruption
    /// enchantment (which one is not simulated); or an extra socket, as a weapon (docs/GAME.md §3.4).</summary>
    private CraftOutcome Vaal(CraftItem item, List<CraftMod> mods, HashSet<int> fresh, int itemLevel)
    {
        var enchanted = item.Enchanted;
        var socket = item.ExtraSocket;
        var message = "Corrupted: no other change";
        switch (_random.Next(4))
        {
            case 1:
            {
                var removable = mods.Where(m => !m.Fractured).ToList();
                if (removable.Count == 0)
                {
                    break;
                }

                var count = _random.Next(1, Math.Min(3, removable.Count) + 1);
                var gone = removable.OrderBy(_ => _random.Next()).Take(count).ToList();
                mods.RemoveAll(m => gone.Contains(m));
                var added = new List<string>();
                foreach (var _ in gone)
                {
                    if (Pick(Pool(item with { Mods = mods }, BothSides, itemLevel, 0)) is { } p)
                    {
                        var m = Make(p.Family, p.Tier);
                        mods.Add(m);
                        fresh.Add(m.Id);
                        added.Add($"{OneLine(m)} (T{m.Tier})");
                    }
                }

                message = $"Corrupted: {string.Join(", ", gone.Select(OneLine))} rerolled into {(added.Count > 0 ? string.Join(", ", added) : "nothing")}";
                break;
            }

            case 2:
                enchanted = true;
                message = "Corrupted: a corruption enchantment added (which one is not simulated)";
                break;

            case 3:
                socket = true;
                message = "Corrupted: an extra socket";
                break;
        }

        return new CraftOutcome(new CraftItem(item.Rarity, item.Name, true, mods, enchanted, socket), message, fresh, []);
    }

    private string RareName() => $"{FirstNames[_random.Next(FirstNames.Length)]} {LastNames[_random.Next(LastNames.Length)]}";

    private string OneLine(CraftMod m) => Text(m).Replace("\n", ", ", StringComparison.Ordinal);

    private static string Number(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The game prints additions with a plus: "+196 to Accuracy Rating".</summary>
    private static string Signed(string template, string text) =>
        template.StartsWith("# to ", StringComparison.Ordinal) || template.StartsWith("#% to ", StringComparison.Ordinal) ? "+" + text : text;
}
