using StillSaneExile.Domain.Craft;
using StillSaneExile.Domain.Data;
using StillSaneExile.Domain.Items;

namespace StillSaneExile.Domain.Tests;

/// <summary>The craft simulator's rules on the synthetic pack's crossbow, with seeded randomness.</summary>
public class CraftEngineTests
{
    private static readonly CraftSetup At82 = CraftSetup.Of(82);

    private static CraftEngine Engine(int seed = 1)
    {
        var pack = Fixtures.Pack();
        return new CraftEngine(pack, pack.Bases![0], new Random(seed));
    }

    private static CraftItem Use(CraftEngine e, CraftItem item, string orb, CraftSetup? setup = null)
    {
        var outcome = e.Use(orb, item, setup ?? At82);
        Assert.True(outcome.Ok, outcome.Message);
        return outcome.Item!;
    }

    /// <summary>A Rare with the given stats, built by hand: family id â†’ tier.</summary>
    private static CraftItem Rare(CraftEngine e, params (string Family, int Tier)[] stats) =>
        new(CraftRarity.Rare, "Test Item", false, stats.Select((s, i) =>
        {
            var tier = e.Family(s.Family).Tiers.First(t => t.Tier == s.Tier);
            return new CraftMod(1000 + i, s.Family, s.Tier, tier.Ranges.Select(r => r[0]).ToList());
        }).ToList());

    [Fact]
    public void The_pack_gives_the_crossbow_its_own_families_bases_and_essences()
    {
        var e = Engine();

        Assert.Equal("Expert Bombard Crossbow", e.Base.Name);
        Assert.Contains(e.Families, f => f.Id == "syn.phys_pct");
        Assert.DoesNotContain(e.Families, f => f.Id == "syn.movement_speed"); // boots only
        Assert.Equal(["transmute", "aug", "regal", "alch", "essence-of-abrasion", "essence-of-haste", "exalted", "chaos", "annul", "divine", "fracturing-orb", "vaal"],
            e.Currencies.Select(c => c.Id));
        Assert.All(e.Families.SelectMany(f => f.Tiers), t => Assert.True(t.Weight > 0));
    }

    [Fact]
    public void A_craft_goes_from_Normal_to_Magic_to_Rare_and_each_orb_says_why_it_cannot_be_used()
    {
        var e = Engine();
        var item = CraftItem.Normal;
        Assert.Equal("Augmentation only works on a Magic (blue) item.", e.Check("aug", item, At82));
        Assert.Equal("Exalted only works on a Rare (yellow) item.", e.Check("exalted", item, At82));

        item = Use(e, item, "transmute");
        Assert.Equal(CraftRarity.Magic, item.Rarity);
        Assert.Single(item.Mods);
        Assert.Equal("Transmutation only works on a Normal (white) item.", e.Check("transmute", item, At82));

        item = Use(e, item, "aug");
        Assert.Equal(2, item.Mods.Count);
        Assert.Equal(1, e.Count(item, AffixSide.Prefix)); // a Magic item: one of each
        Assert.Equal("This Magic item is full: one prefix and one suffix. Try a Regal Orb.", e.Check("aug", item, At82));

        item = Use(e, item, "regal");
        Assert.Equal(CraftRarity.Rare, item.Rarity);
        Assert.Equal(3, item.Mods.Count);
        Assert.False(string.IsNullOrEmpty(item.Name));

        while (e.Check("exalted", item, At82) is null)
        {
            item = Use(e, item, "exalted");
        }

        Assert.Equal(6, item.Mods.Count);
        Assert.Equal("The item is full: 3 prefixes and 3 suffixes.", e.Check("exalted", item, At82));
    }

    [Fact]
    public void Alchemy_makes_a_Rare_with_four_stats_and_never_more_than_three_on_a_side()
    {
        for (var seed = 1; seed <= 40; seed++)
        {
            var e = Engine(seed);
            var item = Use(e, CraftItem.Normal, "alch");
            Assert.Equal(CraftRarity.Rare, item.Rarity);
            Assert.Equal(4, item.Mods.Count);
            Assert.InRange(e.Count(item, AffixSide.Prefix), 1, 3);
            Assert.Equal(item.Mods.Count, item.Mods.Select(m => m.Family).Distinct().Count());
        }
    }

    [Fact]
    public void Graded_orbs_only_roll_stats_from_their_level_up_and_need_the_item_level_for_it()
    {
        var e = Engine();
        Assert.Equal("Item level 30 is too low for greater orbs: they only roll stats of level 35 and up.",
            e.Check("transmute", CraftItem.Normal, CraftSetup.Of(30, OrbGrade.Greater)));

        for (var seed = 1; seed <= 60; seed++)
        {
            var engine = Engine(seed);
            var item = Use(engine, CraftItem.Normal, "transmute", CraftSetup.Of(82, OrbGrade.Perfect));
            Assert.True(engine.TierOf(item.Mods[0]).Level >= 50);
        }

        Assert.Equal("greater-exalted-orb", CraftEngine.PriceId("exalted", OrbGrade.Greater));
        Assert.Equal("perfect-orb-of-transmutation", CraftEngine.PriceId("transmute", OrbGrade.Perfect));
        Assert.Equal("annul", CraftEngine.PriceId("annul", OrbGrade.Perfect)); // not graded
    }

    [Fact]
    public void An_essence_makes_a_Magic_item_Rare_with_its_stat()
    {
        var e = Engine();
        var magic = Use(e, CraftItem.Normal, "transmute", CraftSetup.Of(82));
        while (magic.Mods.Any(m => e.Family(m.Family).Id == "syn.flat_phys"))
        {
            magic = Use(e, CraftItem.Normal, "transmute");
        }

        var rare = Use(e, magic, "essence-of-abrasion");

        Assert.Equal(CraftRarity.Rare, rare.Rarity);
        var essence = Assert.Single(rare.Mods, m => m.Essence);
        Assert.Equal("syn.flat_phys", essence.Family);
        Assert.Equal(3, essence.Tier);
        Assert.Equal("Essences work on a Magic (blue) item and make it Rare.", e.Check("essence-of-abrasion", rare, At82));
    }

    [Fact]
    public void Omens_steer_the_next_orb_and_are_used_up_by_it()
    {
        var e = Engine();
        var item = Rare(e, ("syn.phys_pct", 3), ("syn.attack_speed", 3));

        var prefix = e.Use("exalted", item, CraftSetup.Of(82, OrbGrade.Regular, Omen.SinistralExaltation));
        Assert.True(prefix.Ok);
        Assert.Equal(AffixSide.Prefix, e.SideOf(prefix.Item!.Mods.Single(m => prefix.Fresh.Contains(m.Id))));
        Assert.Equal([Omen.SinistralExaltation], prefix.OmensUsed);

        var two = e.Use("exalted", item, CraftSetup.Of(82, OrbGrade.Regular, Omen.GreaterExaltation));
        Assert.Equal(4, two.Item!.Mods.Count);

        Assert.Equal("Two opposite omens are on. Turn one off.",
            e.Check("exalted", item, CraftSetup.Of(82, OrbGrade.Regular, Omen.SinistralExaltation, Omen.DextralExaltation)));
    }

    [Fact]
    public void Whittling_makes_Chaos_remove_the_lowest_level_stat()
    {
        var e = Engine();
        var item = Rare(e, ("syn.phys_pct", 1), ("syn.attack_speed", 5), ("syn.crit_chance", 2)); // attack speed T5 is level 1

        for (var i = 0; i < 10; i++)
        {
            var outcome = e.Use("chaos", item, CraftSetup.Of(82, OrbGrade.Regular, Omen.Whittling));
            Assert.DoesNotContain(outcome.Item!.Mods, m => m.Id == item.Mods[1].Id); // the level-1 attack speed went
            Assert.Contains(outcome.Item!.Mods, m => m.Id == item.Mods[0].Id);
            Assert.Contains(outcome.Item!.Mods, m => m.Id == item.Mods[2].Id);
            Assert.Equal([Omen.Whittling], outcome.OmensUsed);
        }
    }

    [Fact]
    public void A_fractured_stat_is_locked_and_a_corrupted_item_changes_no_more()
    {
        var e = Engine();
        var item = Rare(e, ("syn.phys_pct", 1), ("syn.attack_speed", 2), ("syn.crit_chance", 2), ("syn.accuracy", 3));
        Assert.Equal("Fracturing needs at least 4 stats on the item.", e.Check("fracturing-orb", item with { Mods = item.Mods.Take(3).ToList() }, At82));

        item = Use(e, item, "fracturing-orb");
        var locked = Assert.Single(item.Mods, m => m.Fractured);
        Assert.Equal("This item already has a fractured stat.", e.Check("fracturing-orb", item, At82));
        for (var i = 0; i < 20; i++)
        {
            item = Use(e, item, "annul");
            Assert.Contains(item.Mods, m => m.Id == locked.Id);
            if (item.Mods.Count == 1)
            {
                break;
            }
        }

        Assert.Equal("Nothing can be removed there.", e.Check("annul", item, At82));
        var corrupted = Use(e, item, "vaal");
        Assert.True(corrupted.Corrupted);
        Assert.All(e.Currencies, c => Assert.Equal("This item is corrupted: nothing can change it now. Undo or start over.", e.Check(c.Id, corrupted, At82)));
    }

    [Fact]
    public void Divine_rerolls_the_numbers_within_each_tier()
    {
        var e = Engine();
        var item = Rare(e, ("syn.phys_pct", 1), ("syn.flat_phys", 2));

        var rerolled = Use(e, item, "divine");

        Assert.Equal(item.Mods.Select(m => (m.Family, m.Tier)), rerolled.Mods.Select(m => (m.Family, m.Tier)));
        Assert.All(rerolled.Mods, m =>
        {
            var tier = e.TierOf(m);
            Assert.All(m.Values.Select((v, i) => (v, i)), x => Assert.InRange(x.v, tier.Ranges[x.i][0], tier.Ranges[x.i][1]));
        });
    }

    [Fact]
    public void Odds_are_the_wanted_tiers_weight_over_everything_that_can_roll()
    {
        var e = Engine();
        var wants = new Dictionary<string, int> { ["syn.phys_pct"] = 2 }; // T2 or better

        var odds = Assert.Single(e.Odds(CraftItem.Normal, At82, wants));

        var pool = e.Pool(CraftItem.Normal, [AffixSide.Prefix, AffixSide.Suffix], 82, 0, CraftRarity.Magic);
        var expected = (250.0 + 500.0) / pool.Sum(p => p.Weight);
        Assert.Equal("Next Transmutation", odds.Label);
        Assert.Equal(expected, odds.Chance, 10);
        Assert.False(odds.Estimate);

        var have = Rare(e, ("syn.phys_pct", 1));
        Assert.Empty(e.Odds(have, At82, wants)); // already there at T1
        Assert.Equal("Next Exalted (adds 2)", e.Odds(Rare(e, ("syn.attack_speed", 3)), CraftSetup.Of(82, OrbGrade.Regular, Omen.GreaterExaltation), wants)[0].Label);
    }

    [Fact]
    public void Stats_read_as_the_game_prints_them_and_the_weapon_numbers_follow()
    {
        var e = Engine();
        var item = Rare(e, ("syn.phys_pct", 1), ("syn.flat_phys", 2), ("syn.attack_speed", 3), ("syn.accuracy", 3), ("syn.phys_pct_accuracy", 4));

        Assert.Equal("170% increased Physical Damage", e.Text(item.Mods[0]));
        Assert.Equal("Adds 20 to 34 Physical Damage", e.Text(item.Mods[1]));
        Assert.Equal("+161 to Accuracy Rating", e.Text(item.Mods[3]));
        Assert.Equal("45% increased Physical Damage\n+73 to Accuracy Rating", e.Text(item.Mods[4]));

        // (41 + 20) Ã— (1 + 2.15) and (76 + 34) Ã— 3.15, at 1.6 Ã— 1.16 attacks per second.
        Assert.Equal(new CraftWeapon(192, 347, 1.86m, 501.3m), e.Weapon(item));
        Assert.Equal("Expert Bombard Crossbow", e.MagicName(CraftItem.Normal));
    }

    [Fact]
    public void A_pack_without_weights_offers_nothing_to_roll()
    {
        var pack = Fixtures.Pack();
        var bare = pack with { Families = pack.Families.Select(f => f with { Tiers = f.Tiers.Select(t => t with { Weight = null }).ToList() }).ToList() };

        var e = new CraftEngine(bare, pack.Bases![0], new Random(1));

        Assert.Empty(e.Families);
        Assert.Equal("No stat can roll at these settings.", e.Check("transmute", CraftItem.Normal, At82));
    }

    [Fact]
    public void The_loader_turns_down_an_essence_that_points_nowhere()
    {
        var json = Fixtures.Text("packs/synthetic-demo.json").Replace("\"family\": \"syn.flat_phys\", \"tier\": 3", "\"family\": \"syn.flat_phys\", \"tier\": 9", StringComparison.Ordinal);

        var load = DataPackLoader.Load(json);

        Assert.Null(load.Pack);
        Assert.Equal("Essence essence-of-abrasion: family syn.flat_phys has no tier 9.", load.Error);
    }
}
