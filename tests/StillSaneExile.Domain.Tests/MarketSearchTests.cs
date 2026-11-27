using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Reading;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Domain.Tests;

public class MarketSearchTests
{
    private static readonly string AttackSpeedId = $"{RowKind.Explicit}:{ComparisonWorksheet.LocalAttackSpeed}";

    private static ComparisonWorksheet Crossbow() =>
        WorksheetFactory.Create(ItemReader.Read(Fixtures.Item("rare-crossbow-advanced"), Fixtures.Pack()));

    private static CurrencyConverter Converter() => new(Fixtures.DemoBook(), StalenessPolicy.Hourly);

    private static Listing L(string id, decimal amount, string currency, decimal pdps, decimal? attackSpeed = null, bool buyNow = true, int level = 82)
    {
        var stats = new Dictionary<string, decimal> { [ComparisonWorksheet.PhysicalDpsId] = pdps };
        if (attackSpeed is { } a)
        {
            stats[AttackSpeedId] = a;
        }

        return new Listing(id, amount, currency, stats, level, buyNow, Fixtures.DemoNow.AddMinutes(-30));
    }

    [Fact]
    public void Selected_rows_must_be_present_and_reach_the_bound()
    {
        var w = Crossbow(); // preset: physical DPS ≥ 319 and attack speed ≥ 15
        Assert.True(MarketSearch.Matches(w, L("a", 2, "divine", 330, 16)));
        Assert.False(MarketSearch.Matches(w, L("b", 2, "divine", 300, 16)));
        Assert.False(MarketSearch.Matches(w, L("c", 2, "divine", 330)));
    }

    [Fact]
    public void Unticked_rows_and_buy_now_and_item_level_filter_as_chosen()
    {
        var w = Crossbow();
        w.Row(AttackSpeedId)!.Selected = false;
        Assert.True(MarketSearch.Matches(w, L("a", 2, "divine", 330)));
        Assert.False(MarketSearch.Matches(w, L("b", 2, "divine", 330, buyNow: false)));

        w.BuyNowOnly = false;
        w.MinItemLevel = w.SuggestedItemLevel;
        Assert.Equal(80, w.MinItemLevel);
        Assert.True(MarketSearch.Matches(w, L("c", 2, "divine", 330, buyNow: false)));
        Assert.False(MarketSearch.Matches(w, L("d", 2, "divine", 330, level: 78)));
    }

    [Fact]
    public void An_ask_without_instant_buyout_never_sets_the_price_by_default()
    {
        // A seller asking 1 div without instant buyout is often unsure of the price: by default the
        // price comes from instant-buyout listings only, so the cheapest ask here is left out entirely.
        var w = Crossbow();
        Assert.True(w.BuyNowOnly);
        List<Listing> listings =
        [
            L("unsure", 1, "divine", 330, 16, buyNow: false),
            L("a", 2, "divine", 330, 16),
            L("b", 3, "divine", 330, 16),
            L("c", 4, "divine", 330, 16),
        ];

        var r = MarketSearch.Run(w, listings, Converter(), "divine", Fixtures.DemoNow);

        Assert.Equal(["a", "b", "c"], r.Matches.Select(m => m.Listing.Id));
        Assert.Equal("a", r.Cheapest!.Listing.Id);
        Assert.True(r.Low >= 2, $"{r.Low}");

        // Unticking "Buy-now listings only" is the player asking for in-person asks too.
        w.BuyNowOnly = false;
        Assert.Equal(4, MarketSearch.Run(w, listings, Converter(), "divine", Fixtures.DemoNow).Count);
    }

    [Fact]
    public void Results_are_sorted_cheapest_first_in_the_display_currency()
    {
        var w = Crossbow();
        var r = MarketSearch.Run(w, [L("a", 2, "divine", 330, 16), L("b", 150, "exalted", 330, 16), L("c", 1, "divine", 330, 16)], Converter(), "divine", Fixtures.DemoNow);
        Assert.Equal(["c", "b", "a"], r.Matches.Select(m => m.Listing.Id));
        Assert.Equal(1.25m, Math.Round(r.Matches[1].Value, 6));
        Assert.Equal(SearchConfidence.Rough, r.Confidence);
    }

    [Theory]
    [InlineData(0, SearchConfidence.NoMatches)]
    [InlineData(2, SearchConfidence.TooFew)]
    [InlineData(3, SearchConfidence.Rough)]
    [InlineData(7, SearchConfidence.Rough)]
    [InlineData(8, SearchConfidence.Good)]
    public void Confidence_follows_how_many_are_for_sale(int count, SearchConfidence expected)
    {
        var listings = Enumerable.Range(1, count).Select(i => L($"l{i}", i, "divine", 330, 16));
        var r = MarketSearch.Run(Crossbow(), listings, Converter(), "divine", Fixtures.DemoNow);
        Assert.Equal(expected, r.Confidence);
        Assert.Equal(count, r.Count);
    }

    [Fact]
    public void Too_few_shows_the_cheapest_and_none_shows_no_band()
    {
        var two = MarketSearch.Run(Crossbow(), [L("a", 3, "divine", 330, 16), L("b", 2, "divine", 330, 16)], Converter(), "divine", Fixtures.DemoNow);
        Assert.Equal((2m, 2m), (two.Low!.Value, two.High!.Value));

        var none = MarketSearch.Run(Crossbow(), [], Converter(), "divine", Fixtures.DemoNow);
        Assert.Null(none.Low);
        Assert.Null(none.Cheapest);
    }

    [Fact]
    public void Demo_market_is_deterministic_and_loosening_the_search_finds_more()
    {
        var w = Crossbow();
        var a = DemoMarket.For(w, "Rare|Expert Bombard Crossbow|Doom Thirst", 300, 82, 120, Fixtures.DemoNow);
        var b = DemoMarket.For(w, "Rare|Expert Bombard Crossbow|Doom Thirst", 300, 82, 120, Fixtures.DemoNow);
        Assert.Equal(a.Select(l => (l.Amount, l.Currency, l.Stats.Count)), b.Select(l => (l.Amount, l.Currency, l.Stats.Count)));
        Assert.Equal(DemoMarket.DefaultCount, a.Count);

        var strict = MarketSearch.Run(w, a, Converter(), "divine", Fixtures.DemoNow).Count;
        w.SetStartingBounds(0.2m);
        var loose = MarketSearch.Run(w, a, Converter(), "divine", Fixtures.DemoNow).Count;
        Assert.True(loose > strict, $"{loose} > {strict}");
    }

    [Fact]
    public void Demo_market_prices_the_practice_crossbow_near_its_anchor()
    {
        var w = Crossbow();
        var listings = DemoMarket.For(w, "Rare|Expert Bombard Crossbow|Doom Thirst", 300, 82, 120, Fixtures.DemoNow);
        var r = MarketSearch.Run(w, listings, Converter(), "divine", Fixtures.DemoNow);
        Assert.Equal(SearchConfidence.Good, r.Confidence);
        Assert.InRange(r.Low!.Value, 1.2m, 3m);
        Assert.InRange(r.High!.Value, 1.6m, 4m);
    }
}
