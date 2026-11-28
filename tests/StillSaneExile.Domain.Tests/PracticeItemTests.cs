using StillSaneExile.Domain.Items;

namespace StillSaneExile.Domain.Tests;

/// <summary>The practice items are made up, but what they show must be the game's: their property lines follow
/// from the real base and their own stats, as Path of Building's Item.lua computes them (docs/GAME.md, 1.3).</summary>
public class PracticeItemTests
{
    // The Siege Crossbow's critical hit chance and reload time; its damage and speed come from the pack.
    private const decimal BaseCrit = 5m;
    private const decimal BaseReload = 0.75m;

    [Theory]
    [InlineData("rare-crossbow-advanced")]
    [InlineData("rare-crossbow-normal-hybrid")]
    [InlineData("magic-crossbow-normal")]
    public void A_practice_crossbows_properties_follow_from_its_base_and_its_stats(string name)
    {
        var item = Fixtures.Item(name);
        var siege = Fixtures.Pack().Bases!.Single(b => b.Name == "Siege Crossbow");
        Assert.EndsWith(siege.Name, item.BaseType, StringComparison.Ordinal); // a magic name ends in its base

        decimal Sum(string template, int component = 0) => item.Mods
            .Where(m => m.Source is ModSource.Explicit or ModSource.Rune)
            .SelectMany(m => m.Lines)
            .Where(l => l.Template == template)
            .Sum(l => l.Components[component].Value);

        var increased = Sum("#% increased Physical Damage") / 100;
        var quality = (item.Quality ?? 0) / 100m;
        var speed = Sum("#% increased Attack Speed") / 100;
        var low = Math.Round((siege.Physical[0] + Sum("Adds # to # Physical Damage")) * (1 + increased) * (1 + quality), MidpointRounding.AwayFromZero);
        var high = Math.Round((siege.Physical[1] + Sum("Adds # to # Physical Damage", 1)) * (1 + increased) * (1 + quality), MidpointRounding.AwayFromZero);

        Assert.Equal((low, high), (item.Weapon!.PhysicalMin, item.Weapon.PhysicalMax));
        Assert.Equal(Math.Round(siege.AttacksPerSecond * (1 + speed), 2, MidpointRounding.AwayFromZero), item.Weapon.AttacksPerSecond);
        Assert.Equal(Math.Round(BaseReload / (1 + speed), 2, MidpointRounding.AwayFromZero), item.Weapon.ReloadTime); // attack speed shortens the reload
        Assert.Equal(BaseCrit + Sum("#% to Critical Hit Chance"), item.Weapon.CriticalHitChance);
        Assert.Equal(79, item.Requirements.Level);
        Assert.Equal((89, 89), (item.Requirements.Strength, item.Requirements.Dexterity));
    }

    [Fact]
    public void The_helmets_defences_follow_from_the_Gladiatorial_Helm_and_its_stat()
    {
        // Gladiatorial Helm: Armour 206, Evasion 187, Requires Level 80, 63 Str, 63 Dex; "Gladiator's" 60%.
        var item = Fixtures.Item("rare-helmet-corrupted-advanced");

        Assert.Equal("Gladiatorial Helm", item.BaseType);
        Assert.Equal("330", item.Properties["Armour"]); // 206 × 1.6
        Assert.Equal("299", item.Properties["Evasion Rating"]); // 187 × 1.6
        Assert.Equal((80, 63, 63), (item.Requirements.Level, item.Requirements.Strength, item.Requirements.Dexterity));
        Assert.True(item.ItemLevel >= 80); // the base drops from level 80
    }

    [Fact]
    public void Every_practice_item_names_a_base_of_the_current_game()
    {
        // "Expert …" and "Advanced …" bases were renamed in 0.2.0; the trade site's list is the game's own.
        var bases = TradeDataBases();
        foreach (var name in new[] { "rare-crossbow-advanced", "rare-crossbow-normal-hybrid", "rare-boots-normal", "rare-helmet-corrupted-advanced", "unique-ring" })
        {
            Assert.Contains(Fixtures.Item(name).BaseType, bases);
        }

        Assert.Contains("Siege Crossbow", bases); // the magic crossbow's base, inside its name
    }

    private static HashSet<string> TradeDataBases() =>
        Trade.TradeData.Parse(Fixtures.DataText("trade/trade-data.json")).Bases.ToHashSet(StringComparer.Ordinal);
}
