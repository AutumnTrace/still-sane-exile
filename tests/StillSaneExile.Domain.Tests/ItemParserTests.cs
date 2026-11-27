using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Reading;

namespace StillSaneExile.Domain.Tests;

public class ItemParserTests
{
    [Fact]
    public void Advanced_copy_keeps_affix_groups_tiers_and_game_ranges()
    {
        var item = Fixtures.Item("rare-crossbow-advanced");

        Assert.Equal(CopyKind.Advanced, item.Copy);
        Assert.Equal("Crossbows", item.ItemClass);
        Assert.Equal(ItemRarity.Rare, item.Rarity);
        Assert.Equal("Doom Thirst", item.Name);
        Assert.Equal("Expert Bombard Crossbow", item.BaseType);
        Assert.Equal(82, item.ItemLevel);
        Assert.Equal(79, item.Requirements.Level);
        Assert.Equal(89, item.Requirements.Strength);
        Assert.Equal(20, item.Quality);
        Assert.Equal("S S", item.Sockets);

        var merciless = item.Mods.Single(m => m.AffixName == "Merciless");
        Assert.Equal(AffixSide.Prefix, merciless.Side);
        Assert.Equal(1, merciless.Tier);
        Assert.Contains("Physical", merciless.Tags);
        var c = Assert.Single(merciless.Lines.Single().Components);
        Assert.Equal(175m, c.Value);
        Assert.Equal(170m, c.Min);
        Assert.Equal(179m, c.Max);
        Assert.Equal(RangeProvenance.AdvancedCopy, c.RangeSource);
    }

    [Fact]
    public void Adds_x_to_y_keeps_both_components_with_their_own_ranges()
    {
        var item = Fixtures.Item("rare-crossbow-advanced");
        var line = item.Mods.Single(m => m.AffixName == "Tempered").Lines.Single();

        Assert.Equal("Adds # to # Physical Damage", line.Template);
        Assert.Equal([24m, 39m], line.Components.Select(x => x.Value));
        Assert.Equal([20m, 34m], line.Components.Select(x => x.Min!.Value));
        Assert.Equal([27m, 45m], line.Components.Select(x => x.Max!.Value));
    }

    [Fact]
    public void Implicit_rune_and_explicit_stay_separate_sources()
    {
        var item = Fixtures.Item("rare-crossbow-advanced");

        Assert.Single(item.ModsFrom(ModSource.Implicit));
        var rune = Assert.Single(item.ModsFrom(ModSource.Rune));
        Assert.Equal("#% to Fire Resistance", rune.Lines.Single().Template);
        Assert.Equal(18m, rune.Lines.Single().Components.Single().Value);
        Assert.Equal(5, item.ModsFrom(ModSource.Explicit).Count());
    }

    [Fact]
    public void Normal_copy_reads_weapon_properties_and_derives_physical_dps()
    {
        var item = Fixtures.Item("rare-crossbow-normal-hybrid");

        Assert.Equal(CopyKind.Normal, item.Copy);
        Assert.Equal(98m, item.Weapon!.PhysicalMin);
        Assert.Equal(210m, item.Weapon.PhysicalMax);
        Assert.Equal(1.65m, item.Weapon.AttacksPerSecond);
        // (98 + 210) / 2 × 1.65 — displayed numbers already include local mods and quality.
        Assert.Equal(254.1m, WeaponMath.PhysicalDps(item.Weapon));
    }

    [Fact]
    public void Item_level_and_required_level_are_distinct()
    {
        var item = Fixtures.Item("rare-crossbow-normal-hybrid");
        Assert.Equal(81, item.ItemLevel);
        Assert.Equal(79, item.Requirements.Level);
    }

    [Fact]
    public void Unique_flavour_text_is_not_a_modifier()
    {
        var item = Fixtures.Item("unique-ring");

        Assert.Equal(ItemRarity.Unique, item.Rarity);
        Assert.Equal("Sanity's Last Loop", item.Name);
        Assert.Equal(2, item.FlavourText.Count);
        Assert.Equal(2, item.ModsFrom(ModSource.Unique).Count());
        Assert.Single(item.ModsFrom(ModSource.Implicit));
    }

    [Fact]
    public void Currency_reads_stack_size()
    {
        var item = Fixtures.Item("currency-exalted");
        Assert.Equal(ItemRarity.Currency, item.Rarity);
        Assert.Equal("Exalted Orb", item.BaseType);
        Assert.Equal(7, item.StackSize);
        Assert.Equal(20, item.MaxStackSize);
        Assert.Empty(item.Mods);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello from a chat window")]
    [InlineData("Objet de classe: Arbalètes")]
    public void Unrelated_clipboard_text_is_rejected(string text)
    {
        var result = ItemTextParser.Parse(text, Fixtures.DemoNow, "x");
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void States_are_read()
    {
        var text = "Item Class: Rings\nRarity: Rare\nGloom Loop\nGold Ring\n--------\nItem Level: 70\n--------\n+30 to Intelligence\n--------\nCorrupted";
        var item = ItemTextParser.Parse(text, Fixtures.DemoNow, "x").Item!;
        Assert.True(item.States.HasFlag(ItemStates.Corrupted));
    }

    [Theory]
    [InlineData("+18% to Fire Resistance", "#% to Fire Resistance", 18)]
    [InlineData("-5% to Fire Resistance", "#% to Fire Resistance", -5)]
    [InlineData("+165 to Accuracy Rating", "# to Accuracy Rating", 165)]
    [InlineData("1.5% of Damage Leeched as Life", "#% of Damage Leeched as Life", 1.5)]
    [InlineData("+18% to Fire Resistance (rune)", "#% to Fire Resistance", 18)]
    public void Templates_drop_signs_and_tags(string line, string template, double value)
    {
        var parsed = StatText.Parse(line, advanced: false);
        Assert.Equal(template, parsed.Template);
        Assert.Equal((decimal)value, parsed.Components.Single().Value);
    }

    [Fact]
    public void Negative_advanced_range_parses()
    {
        var parsed = StatText.Parse("-12(-15--10)% to Cold Resistance", advanced: true);
        var c = parsed.Components.Single();
        Assert.Equal(-12m, c.Value);
        Assert.Equal(-15m, c.Min);
        Assert.Equal(-10m, c.Max);
    }

    [Fact]
    public void Advanced_number_without_range_is_fixed_by_the_game()
    {
        var parsed = StatText.Parse("+1 to Level of all Projectile Skills", advanced: true);
        Assert.Equal(RangeProvenance.FixedByGame, parsed.Components.Single().RangeSource);
    }
}
