using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Domain.Tests;

public class EvidenceTests
{
    private const string League = "Test League";
    private static readonly DateTimeOffset Now = Fixtures.DemoNow;
    private static int _id;

    private static CurrencyConverter Conv() =>
        new(new PriceBook("s", League, null, "exalted", false, Now,
            [new Quote("divine", null, 100, "exalted", ObservationKind.ExchangeEstimate, League, "t", Now.AddHours(-1), Now.AddMinutes(-20), Now, 1000, "t")]),
            StalenessPolicy.Hourly);

    private static Comparable C(decimal amount, string? seller, string currency = "divine", string? listing = null, int minutesAgo = 10, string league = League, DateTimeOffset? expires = null) =>
        new($"c{Interlocked.Increment(ref _id)}", league, amount, currency, Now.AddMinutes(-minutesAgo), seller, listing, null, null, ComparableOrigin.PlayerEntered, expires);

    private static EvidenceSummary Sum(params Comparable[] cs) => EvidencePolicy.Summarize(cs, League, "divine", Conv(), Now);

    [Fact]
    public void No_listings_says_compare_on_trade()
    {
        var s = Sum();
        Assert.Equal(EvidenceLevel.None, s.Level);
        Assert.Equal("No evidence · Compare on trade", s.Headline);
    }

    [Fact]
    public void One_listing_is_one_ask()
    {
        var s = Sum(C(2, "a"));
        Assert.Equal(EvidenceLevel.OneAsk, s.Level);
        Assert.Equal("One ask · 2 div", s.Headline);
    }

    [Fact]
    public void Two_to_four_sellers_is_a_thin_market_span()
    {
        var s = Sum(C(2, "a"), C(3, "b"), C(4, "c"));
        Assert.Equal(EvidenceLevel.ThinMarket, s.Level);
        Assert.Equal(2m, s.Low);
        Assert.Equal(4m, s.High);
    }

    [Fact]
    public void Five_sellers_give_median_and_middle_half()
    {
        var s = Sum(C(1, "a"), C(2, "b"), C(3, "c"), C(4, "d"), C(5, "e"));
        Assert.Equal(EvidenceLevel.Range, s.Level);
        Assert.Equal(3m, s.Median);
        Assert.Equal(2m, s.Low);
        Assert.Equal(4m, s.High);
    }

    [Fact]
    public void Repeated_seller_counts_once_with_their_latest_ask()
    {
        var s = Sum(C(2, "a", minutesAgo: 60), C(2.6m, "A ", minutesAgo: 5), C(3, "b"));
        Assert.Equal(2, s.IndependentSellers);
        Assert.Equal(1, s.DuplicatesRemoved);
        Assert.Contains(s.Points, p => p.Seller == "A" && p.Value == 2.6m);
    }

    [Fact]
    public void Duplicate_listing_id_is_removed()
    {
        var s = Sum(C(2, "a", listing: "L1"), C(2, "b", listing: "L1"));
        Assert.Equal(1, s.DuplicatesRemoved);
        Assert.Equal(EvidenceLevel.OneAsk, s.Level);
    }

    [Fact]
    public void Unknown_sellers_cannot_raise_confidence()
    {
        var s = Sum(C(1, null), C(2, null), C(3, null), C(4, null), C(5, null), C(6, null));
        Assert.Equal(EvidenceLevel.OneAsk, s.Level);
        Assert.Equal(1, s.IndependentSellers);
        Assert.Equal(6, s.ListingsUsed);
    }

    [Fact]
    public void Exclusions_are_counted_not_hidden()
    {
        var s = Sum(
            C(2, "a", league: "Other"),
            C(2, "b", expires: Now.AddMinutes(-1)),
            C(2, "c", minutesAgo: 60 * 24 * 30),
            C(2, "d", currency: "mirror"),
            C(2, "e"));
        Assert.Equal(1, s.ExcludedOtherLeague);
        Assert.Equal(1, s.ExcludedExpired);
        Assert.Equal(1, s.ExcludedOutsideWindow);
        Assert.Equal(1, s.ExcludedNoRate);
        Assert.Equal(EvidenceLevel.OneAsk, s.Level);
    }

    [Fact]
    public void Asks_in_other_currencies_are_normalized_before_comparing()
    {
        var s = Sum(C(250, "a", currency: "exalted"), C(3, "b"));
        Assert.Equal(2.5m, s.Low);
        Assert.Equal(3m, s.High);
    }

    [Fact]
    public void Wide_dispersion_and_clustered_low_asks_are_flagged()
    {
        var s = Sum(C(0.2m, "a"), C(0.25m, "b"), C(3, "c"), C(3.2m, "d"), C(9, "e"), C(10, "f"));
        Assert.True(s.WideDispersion);
        Assert.True(s.ClusteredLowAsks);
        Assert.NotEmpty(s.Warnings());
    }

    [Fact]
    public void Missing_provenance_has_its_own_count()
    {
        var s = Sum(C(2, "a", listing: "L1"), C(3, "b"));
        Assert.Equal(1, s.MissingProvenance);
    }

    [Fact]
    public void Demo_comparables_for_the_practice_crossbow_are_a_thin_market()
    {
        var book = Fixtures.DemoBook();
        var conv = new CurrencyConverter(book, StalenessPolicy.Hourly);
        var t = book.League;
        Comparable D(decimal a, string cur, string? seller, string? listing, int h, int m) =>
            new($"{seller}{listing}", t, a, cur, new DateTimeOffset(2026, 11, 27, h, m, 0, TimeSpan.Zero), seller, listing, null, null, ComparableOrigin.PlayerEntered, null);

        var s = EvidencePolicy.Summarize(
            [D(2.5m, "divine", "demo_seller_a", "demo-listing-1", 9, 40), D(3, "divine", "demo_seller_b", "demo-listing-2", 10, 10),
             D(280, "exalted", "demo_seller_a", "demo-listing-3", 11, 0), D(2.2m, "divine", null, null, 10, 45)],
            t, "divine", conv, Fixtures.DemoNow);

        Assert.Equal(EvidenceLevel.ThinMarket, s.Level);
        Assert.Equal(3, s.IndependentSellers);
        Assert.Equal("Thin market · 2.2–3 div · 3 sellers", s.Headline);
    }
}
