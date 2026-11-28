using StillSaneExile.Desktop.ViewModels;

namespace StillSaneExile.Desktop.Tests;

/// <summary>The craft simulator's page: orbs picked up like in game (or clicked to use), what clicks cost,
/// undo, and no made-up data in a live profile.</summary>
public class CraftViewModelTests
{
    private static OrbViewModel Orb(CraftViewModel craft, string id) => craft.Groups.SelectMany(g => g.Orbs).First(o => o.Id == id);

    private static CraftViewModel Bench(TestApp app, bool shift = false)
    {
        var craft = new CraftViewModel(app.Shell, () => shift);
        craft.Restart(new Random(3));
        return craft;
    }

    [Fact]
    public void Like_in_game_an_orb_is_picked_up_then_used_on_the_item() => Sta.Run(() =>
    {
        using var app = new TestApp();
        Assert.True(app.Settings.CraftPickUp); // the default
        var craft = Bench(app);
        var transmute = Orb(craft, "transmute");

        transmute.ClickCommand.Execute(null);

        Assert.Same(transmute, craft.Held);
        Assert.True(transmute.IsHeld);
        Assert.True(craft.IsEmpty); // nothing used yet
        Assert.Equal("Use Orb of Transmutation on the item", craft.UseOnItemName);
        Assert.True(craft.UseHeldCommand.CanExecute(null));

        craft.UseHeldCommand.Execute(null);

        Assert.Null(craft.Held);
        Assert.False(transmute.IsHeld);
        Assert.Equal(1, craft.Clicks);
        Assert.False(craft.IsEmpty);
        Assert.StartsWith("Orb of Transmutation: Now Magic:", craft.Message, StringComparison.Ordinal);
        Assert.False(craft.UseHeldCommand.CanExecute(null)); // the item is no button with nothing in hand
    });

    [Fact]
    public void Shift_keeps_the_orb_in_hand_and_right_click_or_Esc_puts_it_back() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var craft = Bench(app, shift: true);
        Orb(craft, "alch").ClickCommand.Execute(null);
        craft.UseHeldCommand.Execute(null); // a Rare with 4 stats; Alchemy can't be used again
        Assert.Null(craft.Held);

        var exalted = Orb(craft, "exalted");
        exalted.ClickCommand.Execute(null);
        craft.UseHeldCommand.Execute(null);
        Assert.Same(exalted, craft.Held); // Shift: still in hand
        Assert.Equal(2, craft.Clicks);

        craft.DropCommand.Execute(null);
        Assert.Null(craft.Held);
        Assert.False(exalted.IsHeld);
        Assert.Equal("Put back.", craft.Message);
    });

    [Fact]
    public void An_orb_that_cannot_be_used_says_why_and_stays_on_the_bench() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var craft = Bench(app);
        var regal = Orb(craft, "regal");
        Assert.False(regal.CanUse);

        regal.ClickCommand.Execute(null);

        Assert.Null(craft.Held);
        Assert.True(craft.MessageIsError);
        Assert.Equal("Regal Orb: Regal only works on a Magic (blue) item. Use Transmutation first.", craft.Message);
        Assert.Contains("Regal only works on a Magic", regal.Tip, StringComparison.Ordinal);
    });

    [Fact]
    public void Clicking_to_use_is_the_other_setting() => Sta.Run(() =>
    {
        using var app = new TestApp();
        app.Shell.SettingsPage.SetCraftUseCommand.Execute("click");
        Assert.False(app.Settings.CraftPickUp);
        var craft = Bench(app);

        Orb(craft, "transmute").ClickCommand.Execute(null);

        Assert.Null(craft.Held);
        Assert.Equal(1, craft.Clicks);
    });

    [Fact]
    public void Clicks_are_priced_from_the_price_book_and_undo_takes_them_back() => Sta.Run(() =>
    {
        using var app = new TestApp(); // the demo book prices Exalted, Divine, Chaos and Regal Orbs
        app.Shell.SettingsPage.SetCraftUseCommand.Execute("click");
        var craft = Bench(app);

        Orb(craft, "transmute").ClickCommand.Execute(null); // not in the book
        Orb(craft, "regal").ClickCommand.Execute(null); // 0.4 ex
        Orb(craft, "exalted").ClickCommand.Execute(null); // 1 ex

        Assert.Equal(3, craft.Clicks);
        Assert.Equal("1 ex", craft.Spent); // 1.4 ex in Divine shows in whole Exalted
        Assert.Equal("3 clicks · 1 not priced · demo prices", craft.SpentNote);
        Assert.Equal([3, 2, 1], craft.History.Select(h => h.Number));

        craft.UndoCommand.Execute(null);

        Assert.Equal(2, craft.Clicks);
        Assert.Equal("2 clicks · 1 not priced · demo prices", craft.SpentNote);
        Assert.Equal(Domain.Items.ItemRarity.Rare, craft.Rarity);

        craft.GoBackCommand.Execute(1);
        Assert.True(craft.IsEmpty);
        Assert.False(craft.UndoCommand.CanExecute(null));
    });

    [Fact]
    public void Omens_are_used_up_by_the_orb_they_change_and_cost_their_price() => Sta.Run(() =>
    {
        using var app = new TestApp();
        app.Shell.SettingsPage.SetCraftUseCommand.Execute("click");
        var craft = Bench(app);
        Orb(craft, "alch").ClickCommand.Execute(null);
        var sinistral = craft.Omens.First(o => o.Info.Omen == Domain.Craft.Omen.SinistralExaltation);
        sinistral.ToggleCommand.Execute(null);

        Orb(craft, "chaos").ClickCommand.Execute(null); // not the orb it changes
        Assert.True(sinistral.IsOn);
        Orb(craft, "exalted").ClickCommand.Execute(null);

        Assert.False(sinistral.IsOn);
        Assert.StartsWith("Exalted Orb + Omen of Sinistral Exaltation", craft.History[0].Label, StringComparison.Ordinal);
        Assert.Contains("not priced", craft.SpentNote, StringComparison.Ordinal); // the demo book has no omens
    });

    [Fact]
    public void Wanted_stats_give_odds_and_the_pool_marks_them() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var craft = Bench(app);

        Assert.Equal(["#% increased Physical Damage", "#% increased Attack Speed"], craft.Wants.Select(w => w.Label));
        var odds = Assert.Single(craft.Odds);
        Assert.Equal("Next Transmutation", odds.Label);
        Assert.Equal("exact", odds.Kind);

        var crit = craft.Pool.SelectMany(s => s.Rows).First(r => r.Label == "#% increased Critical Hit Chance");
        crit.IsWanted = true;
        Assert.Contains(craft.Wants, w => w.Label == "#% increased Critical Hit Chance");

        craft.Wants.First(w => w.Label == "#% increased Physical Damage").Remove.Execute(null);
        Assert.DoesNotContain(craft.Wants, w => w.Label == "#% increased Physical Damage");
    });

    [Fact]
    public void A_live_profile_has_no_made_up_stats_to_craft_with() => Sta.Run(() =>
    {
        using var app = new TestApp(TradeRig.Live);

        var craft = app.Shell.Craft;

        Assert.True(craft.NoData);
        Assert.Empty(craft.Groups);
        Assert.Contains("Only the demo has some yet, and it is made up", craft.NoDataText, StringComparison.Ordinal);
    });
}
