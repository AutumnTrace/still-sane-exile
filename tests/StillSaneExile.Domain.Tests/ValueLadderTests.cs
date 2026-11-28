using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Domain.Tests;

/// <summary>Where each colour of the price label starts, as the player sets it.</summary>
public class ValueLadderTests
{
    private static readonly ValueLadder LeagueStart = ValueLadder.Default with
    {
        S = new ValueStep(1m, "divine"),
        A = new ValueStep(40m, "exalted"),
    };

    private static PriceText Text(ValueLadder? ladder = null) =>
        new(new CurrencyConverter(Fixtures.DemoBook(), StalenessPolicy.Hourly), "divine", Fixtures.DemoNow, ladder); // 1 div = 120 ex

    [Fact]
    public void The_default_starts_white_and_red_at_10_div_and_red_at_1_div()
    {
        Assert.Equal("10 div;1 div;30 ex;6 ex;1 ex", ValueLadder.Default.Format());
        Assert.Same(ValueLadder.Default, Text().Ladder);
        Assert.Equal(ValueTier.A, Text().Tier(2m));
    }

    [Theory]
    [InlineData(4, ValueTier.S)]
    [InlineData(1, ValueTier.S)] // white and red from 1 div
    [InlineData(0.5, ValueTier.A)] // 60 ex: red from 40 ex
    [InlineData(0.25, ValueTier.B)] // 30 ex: orange as before
    [InlineData(0.001, ValueTier.E)]
    public void A_players_ladder_colours_each_price_by_its_own_steps(double divines, ValueTier tier) =>
        Assert.Equal(tier, Text(LeagueStart).Tier((decimal)divines));

    [Fact]
    public void Settings_store_the_ladder_as_text_and_read_back_only_what_they_wrote()
    {
        Assert.Equal("1 div;40 ex;30 ex;6 ex;1 ex", LeagueStart.Format());
        Assert.Equal(LeagueStart, ValueLadder.Parse(LeagueStart.Format()));
        Assert.Equal(new ValueStep(1.5m, "divine"), ValueLadder.Parse("1.5 div;1 div;30 ex;6 ex;1 ex")!.S);

        Assert.Null(ValueLadder.Parse(null));
        Assert.Null(ValueLadder.Parse("10 div;1 div;30 ex;6 ex")); // a step short
        Assert.Null(ValueLadder.Parse("10 div;1 div;30 ex;6 ex;0 ex")); // nothing starts at nothing
        Assert.Null(ValueLadder.Parse("10 div;1 div;30 ex;6 ex;1 c")); // Divine or Exalted only
        Assert.Null(ValueLadder.Parse("10 div;1 div;30 ex;6 ex;-1 ex"));
    }

    [Fact]
    public void Each_step_has_to_start_below_the_one_above_it()
    {
        Assert.Null(ValueLadder.Default.OutOfOrder(120m));
        Assert.Null(LeagueStart.OutOfOrder(120m));

        // Same currency: no rate needed.
        Assert.Equal(ValueTier.A, (ValueLadder.Default with { A = new ValueStep(10m, "divine") }).OutOfOrder(null));
        Assert.Equal(ValueTier.C, (ValueLadder.Default with { C = new ValueStep(30m, "exalted") }).OutOfOrder(null));

        // Red from 0.2 div is 24 ex at 120 ex, under orange's 30 ex: orange is out of order.
        var cheapRed = ValueLadder.Default with { A = new ValueStep(0.2m, "divine") };
        Assert.Equal(ValueTier.B, cheapRed.OutOfOrder(120m));
        Assert.Null(cheapRed.OutOfOrder(null)); // without a rate, Divine and Exalted can't be compared
    }
}
