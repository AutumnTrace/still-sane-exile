using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Reading;

namespace StillSaneExile.Domain.Tests;

/// <summary>Shapes of real item copies the parser missed (docs/GAME.md §10.3), each in a small made-up copy.</summary>
public class CopyFormatTests
{
    private static ItemSnapshot Parse(string text) => ItemTextParser.Parse(text.Replace("\n", "\r\n", StringComparison.Ordinal), Fixtures.DemoNow, "t").Item!;

    [Fact]
    public void Thousands_separators_are_one_number()
    {
        var item = Parse("""
            Item Class: Crossbows
            Rarity: Rare
            Made Up Thing
            Siege Crossbow
            --------
            Physical Damage: 414-1,043 (augmented)
            Attacks per Second: 1.93 (augmented)
            --------
            Item Level: 82
            --------
            +1,250 to Accuracy Rating
            """);

        Assert.Equal((414m, 1043m), (item.Weapon!.PhysicalMin, item.Weapon.PhysicalMax));
        Assert.Equal(1406.0m, Math.Round(ItemReader.Read(item, null).PhysicalDps!.Value, 1)); // (414 + 1,043) / 2 × 1.93
        var accuracy = Assert.Single(item.Mods).Lines[0];
        Assert.Equal("# to Accuracy Rating", accuracy.Template);
        Assert.Equal(1250m, Assert.Single(accuracy.Components).Value);

        Assert.Equal(1043m, StatText.Parse("1,043(1,000-1,100) to Armour", advanced: true).Components[0].Value);
        Assert.Equal(1100m, StatText.Parse("1,043(1,000-1,100) to Armour", advanced: true).Components[0].Max);
    }

    [Fact]
    public void Sanctified_and_a_tiered_Unidentified_are_states_not_stats()
    {
        var item = Parse("""
            Item Class: Rings
            Rarity: Rare
            Gold Ring
            --------
            Item Level: 80
            --------
            Unidentified (Tier 4)
            --------
            Sanctified
            """);

        Assert.True(item.States.HasFlag(ItemStates.Unidentified));
        Assert.True(item.States.HasFlag(ItemStates.Sanctified));
        Assert.Empty(item.Mods);
    }

    [Fact]
    public void Unscalable_value_and_element_tags_do_not_stay_in_the_text()
    {
        Assert.Equal("Allocates Hallowed", StatText.StripTags("Allocates Hallowed — Unscalable Value").Text);
        Assert.Equal(["unscalable value"], StatText.StripTags("Allocates Hallowed — Unscalable Value").Tags);
        Assert.Equal("11-78", StatText.StripTags("11-78 (lightning)").Text);
    }

    [Fact]
    public void An_unmet_requirement_with_the_attributes_full_name_is_read()
    {
        var item = Parse("""
            Item Class: Wands
            Rarity: Rare
            Unidentified Thing
            Withered Wand
            --------
            Requires: Level 78, 78 (unmet) Intelligence
            --------
            Item Level: 80
            --------
            Unidentified
            """);

        Assert.Equal(78, item.Requirements.Level);
        Assert.Equal(78, item.Requirements.Intelligence);
    }
}
