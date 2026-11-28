using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Desktop.Tests;

/// <summary>The tier and roll bar under each stat, on the card over the game and in the price check.</summary>
public class RollViewTests
{
    private const string Crossbow = "rare-crossbow-advanced.txt";
    private const string Phys = "175% increased Physical Damage";

    private static SearchRowViewModel Row(ItemViewModel item, string text) => item.SearchRows.First(r => r.Text == text);

    [Fact]
    public void Each_stat_shows_its_tier_and_where_it_rolled_in_the_tiers_range() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "test")!;

        var phys = Row(item, Phys).Roll!;
        Assert.Equal("T1", phys.Tier);
        Assert.True(phys.HasBar);
        Assert.True(phys.Scored); // the demo pack says which end is better
        Assert.Equal("170", phys.Low);
        Assert.Equal("179", phys.High);
        Assert.Equal(5.0 / 9, phys.Position, 3);
        Assert.Equal("Tier 1, rolls 170 to 179", phys.Spoken);
        Assert.Equal($"{Phys}, Tier 1, rolls 170 to 179", Row(item, Phys).ToString());

        // Two numbers in one line: both ends show both.
        var added = Row(item, "Adds 24 to 39 Physical Damage").Roll!;
        Assert.Equal("T2", added.Tier);
        Assert.Equal("20/34", added.Low);
        Assert.Equal("27/45", added.High);
        Assert.Equal("Tier 2, rolls 20 to 27 and 34 to 45", added.Spoken);

        Assert.Null(item.SearchRows.First(r => r.Row.Id == ComparisonWorksheet.PhysicalDpsId).Roll); // worked out, not rolled
        Assert.Null(item.AdvancedCopyHint);
    });

    [Fact]
    public void Without_a_data_pack_the_bar_only_marks_the_roll_between_the_games_own_numbers() => Sta.Run(() =>
    {
        using var app = new TestApp(TradeRig.Live);
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "hud")!;

        var phys = Row(item, Phys).Roll!;
        Assert.Equal("T1", phys.Tier); // as the game printed it
        Assert.True(phys.HasBar);
        Assert.False(phys.Scored); // which end is better is not known: no fill
        Assert.Equal("170", phys.Low);
        Assert.Equal("179", phys.High);
        Assert.Equal(5.0 / 9, phys.Position, 3);
    });

    [Fact]
    public void A_normal_copy_without_a_data_pack_points_to_the_advanced_copy() => Sta.Run(() =>
    {
        using var app = new TestApp(TradeRig.Live);
        var rare = app.Shell.Inspect.AddFromText(app.ItemText("rare-boots-normal.txt"), "hud")!;
        var unique = app.Shell.Inspect.AddFromText(app.ItemText("unique-ring.txt"), "hud")!;

        Assert.All(rare.SearchRows, r => Assert.Null(r.Roll));
        Assert.Equal("Ctrl+Alt+C for tiers", rare.AdvancedCopyHint);
        Assert.Equal("Ctrl+Alt+C for ranges", unique.AdvancedCopyHint); // uniques have no tiers
    });

    [Fact]
    public void The_card_shows_the_roll_under_every_stat_and_keeps_the_right_for_the_search() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "test")!;
        var card = PeekViewModel.From(item, demo: true, "Let go to hide", "F3");

        var phys = card.Rows.First(r => r.Text == Phys);
        Assert.False(phys.On);
        Assert.Equal("", phys.Right); // the tier sits under the stat now
        Assert.Equal("T1", phys.Roll!.Tier);
        Assert.Equal($"{Phys}, not searched, Tier 1, rolls 170 to 179", phys.ToString());

        var speed = card.Rows.First(r => r.Text == "17% increased Attack Speed");
        Assert.True(speed.On);
        Assert.Equal("≥ 15", speed.Right);
        Assert.Equal("T3", speed.Roll!.Tier);

        Assert.Equal("Rune", card.Rows.First(r => r.Text == "+18% to Fire Resistance").Right);
        Assert.Null(card.Rows.First(r => r.Text.StartsWith("Physical DPS", StringComparison.Ordinal)).Roll);
    });
}
