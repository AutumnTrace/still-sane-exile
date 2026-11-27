using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Tests;

public class InspectViewModelTests
{
    private const string Crossbow = "rare-crossbow-advanced.txt";

    [Fact]
    public void Item_text_is_parsed_kept_and_selected() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var inspect = app.Shell.Inspect;

        var item = inspect.AddFromText(app.ItemText(Crossbow), "test");

        Assert.NotNull(item);
        Assert.Equal("Doom Thirst", item.Title);
        Assert.Same(item, Assert.Single(inspect.Recent));
        Assert.Same(item, inspect.Selected);
        Assert.Null(inspect.Message);
    });

    // Hand-written in the shape of a game copy; not one of the practice items.
    private const string GameUnique = "Item Class: Belts\nRarity: Unique\nMageblood\nHeavy Belt\n--------\nItem Level: 84\n--------\n+25 to Strength (implicit)\n--------\n+30 to Dexterity\n--------\nCorrupted";
    private const string GameCurrency = "Item Class: Stackable Currency\nRarity: Currency\nDivine Orb\n--------\nStack Size: 3/20\n--------\nRandomises the numeric values of the random modifiers on an item";

    [Fact]
    public void The_demo_prices_its_practice_items() => Sta.Run(() =>
    {
        using var app = new TestApp();

        var practice = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "practice")!;

        Assert.True(practice.HasListings);
        Assert.NotEqual("No real price", practice.Answer.Big);
    });

    [Theory]
    [InlineData(GameUnique)]
    [InlineData(GameCurrency)]
    public void The_demo_never_invents_a_price_for_an_item_copied_from_the_game(string text) => Sta.Run(() =>
    {
        using var app = new TestApp();

        var item = app.Shell.Inspect.AddFromText(text, "hud")!;

        Assert.True(app.Shell.Session.IsDemo);
        Assert.False(item.HasListings);
        Assert.Equal("No real price", item.Answer.Big);
        Assert.Null(item.Answer.Count);
        Assert.Contains("practice items", item.Answer.Line);
    });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hunter2")]
    [InlineData("Meet at the hideout at 9, bring the map.")]
    public void Text_that_is_not_an_item_is_discarded(string? text) => Sta.Run(() =>
    {
        using var app = new TestApp();
        var inspect = app.Shell.Inspect;
        var kept = inspect.AddFromText(app.ItemText(Crossbow), "test");

        Assert.Null(inspect.AddFromText(text, "test"));

        Assert.Same(kept, Assert.Single(inspect.Recent));
        Assert.Same(kept, inspect.Selected);
        Assert.False(string.IsNullOrEmpty(inspect.Message));
    });

    [Fact]
    public void Paste_reads_the_clipboard_once_and_keeps_nothing_that_is_not_an_item() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var inspect = app.Shell.Inspect;
        app.Clipboard.Text = "hunter2";

        inspect.PasteFromClipboardCommand.Execute(null);

        Assert.Equal(1, app.Clipboard.Reads);
        Assert.Empty(inspect.Recent);
        Assert.Null(inspect.Selected);
        Assert.False(string.IsNullOrEmpty(inspect.Message));
    });

    [Fact]
    public void Paste_keeps_an_item() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var inspect = app.Shell.Inspect;
        app.Clipboard.Text = app.ItemText(Crossbow);

        inspect.PasteFromClipboardCommand.Execute(null);

        Assert.Equal(1, app.Clipboard.Reads);
        Assert.Equal("Doom Thirst", Assert.Single(inspect.Recent).Title);
    });

    [Fact]
    public void Recent_keeps_the_newest_twenty() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var inspect = app.Shell.Inspect;
        var text = app.ItemText(Crossbow);

        for (var i = 0; i < 25; i++)
        {
            inspect.AddFromText(text, "test");
        }

        Assert.Equal(20, inspect.Recent.Count);
        Assert.Equal("test-25", inspect.Recent[0].Item.CaptureId);
        Assert.Equal("test-6", inspect.Recent[^1].Item.CaptureId);
    });

    /// <summary>The <c>q</c> parameter of a trade site link, decoded back to the query.</summary>
    private static string Query(string url) => Uri.UnescapeDataString(url[(url.IndexOf("?q=", StringComparison.Ordinal) + 3)..]);

    [Fact]
    public void The_demo_links_its_search_in_standard_and_says_what_to_set_by_hand() => Sta.Run(() =>
    {
        using var app = new TestApp();
        Assert.False(app.Shell.Session.Trade.IsEmpty); // the shipped data file reached the app folder

        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "practice")!;

        Assert.StartsWith("https://www.pathofexile.com/trade2/search/Standard?q=", item.TradeUrl);
        Assert.Contains("\"explicit.stat_210067635\"", Query(item.TradeUrl));
        Assert.Contains("\"pdps\":{\"min\":319}", Query(item.TradeUrl));
        Assert.Contains("Standard", item.TradeTooltip);
        Assert.Contains("made up", item.TradeTooltip);
        Assert.Equal("Set by hand: Item base (Expert Bombard Crossbow)", item.ByHand);
        Assert.True(item.HasByHand);

        // "Copy search" carries the same link, then the filters as text, for the same league.
        Assert.StartsWith($"Trade site link: {item.TradeUrl}", item.Instructions);
        Assert.Contains("League: Standard", item.Instructions);

        item.BuyNowOnly = false;
        Assert.Contains("\"status\":{\"option\":\"available\"}", Query(item.TradeUrl));
        item.BuyNowOnly = true;
        Assert.Contains("\"status\":{\"option\":\"securable\"}", Query(item.TradeUrl));
    });

    [Fact]
    public void A_live_profile_links_its_search_in_the_confirmed_league() => Sta.Run(() =>
    {
        using var app = new TestApp(s =>
        {
            s.LiveLeague = "Runes of Aldur";
            s.Profile = ProfileKind.Live;
        });

        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "hud")!;

        Assert.StartsWith("https://www.pathofexile.com/trade2/search/Runes%20of%20Aldur?q=", item.TradeUrl);
        Assert.Equal("Opens this search on the official trade site in your browser, in Runes of Aldur.", item.TradeTooltip);
        Assert.Contains("League: Runes of Aldur", item.Instructions);
    });

    [Fact]
    public void A_ticked_stat_the_link_cannot_carry_is_listed_under_the_button() => Sta.Run(() =>
    {
        using var app = new TestApp();
        const string armour = "Item Class: Body Armours\nRarity: Rare\nDoom Shell\nLeather Vest\n--------\nItem Level: 80\n--------\n+30 to Spirit\n+50 to maximum Life";
        var item = app.Shell.Inspect.AddFromText(armour, "hud")!;
        Assert.Null(item.ByHand);

        var spirit = item.SearchRows.Single(r => r.Row.Template == "# to Spirit");
        Assert.Equal("Not in the trade site link: set it by hand there", spirit.Hint);
        spirit.IsOn = true;

        Assert.Equal("Set by hand: +30 to Spirit", item.ByHand);
        Assert.DoesNotContain("Spirit", Query(item.TradeUrl));
    });

    [Fact]
    public void A_session_change_keeps_the_items_and_the_selection() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var inspect = app.Shell.Inspect;
        var first = inspect.AddFromText(app.ItemText(Crossbow), "test")!;
        inspect.AddFromText(app.ItemText("unique-ring.txt"), "test");
        inspect.Selected = first;

        app.Settings.SetDisplayCurrency(app.Shell.Session.Kind, "exalted");
        app.Shell.ReloadSession();

        Assert.Equal(2, inspect.Recent.Count);
        Assert.Equal(first.Item.CaptureId, inspect.Selected?.Item.CaptureId);
        Assert.NotSame(first, inspect.Selected);
    });
}
