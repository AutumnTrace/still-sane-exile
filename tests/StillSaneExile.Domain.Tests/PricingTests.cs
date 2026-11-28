using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Domain.Tests;

public class PricingTests
{
    private static readonly DateTimeOffset Now = Fixtures.DemoNow;
    private const string League = "Test League";

    private static Quote Q(string item, decimal amount, string denom, DateTimeOffset? observedTo = null, decimal? volume = 1000, string league = League) =>
        new(item, null, amount, denom, ObservationKind.ExchangeEstimate, league, "test", (observedTo ?? Now.AddMinutes(-30)).AddHours(-1), observedTo ?? Now.AddMinutes(-30), Now, volume, "test");

    private static CurrencyConverter Converter(params Quote[] quotes) =>
        new(new PriceBook("s", League, null, "exalted", false, Now, quotes), StalenessPolicy.Hourly);

    [Fact]
    public void Spec_example_12_ex_at_120_ex_per_div_is_0_1_div_and_0_3_for_three()
    {
        var conv = Converter(Q("divine", 120, "exalted"));
        var one = conv.Convert(12, "exalted", "divine", Now);
        Assert.True(one.Ok);
        Assert.Equal(ConversionPath.Inverted, one.Path);
        Assert.Equal(0.1m, decimal.Round(one.Value, 10));
        Assert.Equal(0.3m, decimal.Round(conv.Convert(36, "exalted", "divine", Now).Value, 10));
    }

    [Fact]
    public void Direct_orientation_multiplies()
    {
        var conv = Converter(Q("divine", 120, "exalted"));
        Assert.Equal(240m, conv.Convert(2, "divine", "exalted", Now).Value);
    }

    [Fact]
    public void Reference_currency_change_uses_one_transparent_bridge()
    {
        var conv = Converter(Q("divine", 120, "exalted"), Q("chaos", 0.05m, "divine"));
        var c = conv.Convert(10, "chaos", "exalted", Now);
        Assert.True(c.Ok);
        Assert.Equal(ConversionPath.Bridge, c.Path);
        Assert.Equal(60m, c.Value);
        Assert.Contains("bridge", c.Explanation);
    }

    [Fact]
    public void Stale_bridge_leg_is_refused()
    {
        var conv = Converter(Q("divine", 120, "exalted", Now.AddHours(-5)), Q("chaos", 0.05m, "divine"));
        Assert.False(conv.Convert(10, "chaos", "exalted", Now).Ok);
    }

    [Fact]
    public void Illiquid_bridge_leg_is_refused()
    {
        var conv = Converter(Q("divine", 120, "exalted"), Q("chaos", 0.05m, "divine", volume: 3));
        Assert.False(conv.Convert(10, "chaos", "exalted", Now).Ok);
    }

    [Fact]
    public void Zero_rate_and_other_league_quotes_are_rejected()
    {
        Assert.False(Converter(Q("divine", 0, "exalted")).Convert(1, "divine", "exalted", Now).Ok);
        Assert.False(Converter(Q("divine", 120, "exalted", league: "Other")).Convert(1, "divine", "exalted", Now).Ok);
    }

    [Fact]
    public void No_rate_keeps_original_denomination()
    {
        var c = Converter().Convert(5, "regal", "divine", Now);
        Assert.False(c.Ok);
        Assert.Equal("regal", c.Denomination);
        Assert.Equal(5m, c.Value);
    }

    [Theory]
    [InlineData(30, Freshness.Fresh)]
    [InlineData(90, Freshness.Fresh)]
    [InlineData(126, Freshness.Stale)]
    [InlineData(23 * 60, Freshness.Stale)]
    [InlineData(25 * 60, Freshness.RankingSuppressed)]
    public void Hourly_staleness(int minutesOld, Freshness expected) =>
        Assert.Equal(expected, StalenessPolicy.Hourly.Classify(Now.AddMinutes(-minutesOld), Now));

    [Fact]
    public void Direct_stale_quote_converts_but_is_marked()
    {
        var c = Converter(Q("divine", 120, "exalted", Now.AddHours(-4))).Convert(1, "divine", "exalted", Now);
        Assert.True(c.Ok);
        Assert.Equal(Freshness.Stale, c.Freshness);
    }

    [Fact]
    public void Digest_rate_is_volume_b_over_volume_a_and_merges_by_summing()
    {
        Assert.Equal(120m, ExchangeDigestMath.UnitsOfBPerA(10, 1200));
        Assert.Null(ExchangeDigestMath.UnitsOfBPerA(0, 1200));
        Assert.Equal(125m, ExchangeDigestMath.Merge([(10, 1200), (10, 1300), (0, 50)]));
    }

    [Fact]
    public void Last_complete_hour_is_the_previous_utc_hour()
    {
        var t = new DateTimeOffset(2026, 11, 27, 13, 42, 0, TimeSpan.FromHours(2));
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero), ExchangeDigestMath.LastCompleteHour(t));
    }

    [Theory]
    [InlineData(0.004, "<0.01")]
    [InlineData(0.058, "0.06")]
    [InlineData(2.5, "2.5")]
    [InlineData(12.34, "12.3")]
    [InlineData(1234.5, "1,235")]
    public void Display_rounding_happens_last(double value, string expected) =>
        Assert.Equal(expected, ValueFormat.Amount((decimal)value));

    [Fact]
    public void Synthetic_prices_never_reach_a_live_profile()
    {
        var book = Fixtures.DemoBook();
        Assert.True(book.IsSynthetic);
        Assert.Throws<InvalidOperationException>(() => PriceBookGuard.EnsureAllowed(ProfileKind.Live, book.League, book));
        PriceBookGuard.EnsureAllowed(ProfileKind.Demo, book.League, book);
    }

    [Fact]
    public void Rates_never_carry_across_leagues()
    {
        var book = Fixtures.DemoBook();
        Assert.Throws<InvalidOperationException>(() => PriceBookGuard.EnsureAllowed(ProfileKind.Demo, "Standard", book));
    }

    [Fact]
    public void Stack_of_primary_currency_prices_through_the_converter()
    {
        var book = Fixtures.DemoBook();
        var conv = new CurrencyConverter(book, StalenessPolicy.Hourly);
        var line = ItemPricer.Price(Fixtures.Item("currency-exalted"), conv, "divine", [], book.League, Fixtures.DemoNow);
        Assert.Equal(PriceKind.Commodity, line.Kind);
        Assert.Equal("<0.01 div each · 0.06 div for 7", line.Headline);
    }

    [Fact]
    public void Unique_gets_its_baseline_as_the_price_alone()
    {
        var book = Fixtures.DemoBook();
        var conv = new CurrencyConverter(book, StalenessPolicy.Hourly);
        var line = ItemPricer.Price(Fixtures.Item("unique-ring"), conv, "exalted", [], book.League, Fixtures.DemoNow);
        Assert.Equal(PriceKind.UniqueBaseline, line.Kind);
        Assert.Equal("168 ex", line.Headline);
        Assert.DoesNotContain("baseline", line.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_name_on_a_different_base_is_not_the_same_unique()
    {
        var book = Fixtures.DemoBook();
        var text = Fixtures.Text("items/unique-ring.txt").Replace("Gold Ring", "Iron Ring", StringComparison.Ordinal);
        var item = Items.ItemTextParser.Parse(text, Fixtures.DemoNow, "x").Item!;
        Assert.Null(ItemPricer.Identify(item, book));
    }

    [Fact]
    public void Rare_without_comparables_says_compare_on_trade()
    {
        var book = Fixtures.DemoBook();
        var conv = new CurrencyConverter(book, StalenessPolicy.Hourly);
        var line = ItemPricer.Price(Fixtures.Item("rare-boots-normal"), conv, "divine", [], book.League, Fixtures.DemoNow);
        Assert.Equal("No evidence · Compare on trade", line.Headline);
    }
}
