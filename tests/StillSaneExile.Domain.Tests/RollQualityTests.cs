using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Rolls;

namespace StillSaneExile.Domain.Tests;

public class RollQualityTests
{
    [Fact]
    public void Spec_example_125_in_110_140_is_half_filled_15_points_below_max()
    {
        var a = RollQuality.Assess(125, 110, 140, StatDirection.HigherIsBetter, "%");

        Assert.Equal(RollStatus.Scored, a.Status);
        Assert.Equal(0.5m, a.Fill);
        Assert.Equal(15m, a.DistanceFromBest);
        Assert.Equal("+125% · 50% of tier · 15 percentage points below max", RollQuality.Describe(a));
    }

    [Fact]
    public void Spec_example_175_in_170_179_is_about_56_percent_four_below_max()
    {
        var a = RollQuality.Assess(175, 170, 179, StatDirection.HigherIsBetter, "%");
        Assert.Equal("56%", RollQuality.Percent(a.Fill));
        Assert.Equal(4m, a.DistanceFromBest);
    }

    [Theory]
    [InlineData(170, 0)]
    [InlineData(179, 1)]
    public void Endpoints(decimal value, decimal fill)
    {
        var a = RollQuality.Assess(value, 170, 179, StatDirection.HigherIsBetter, "%");
        Assert.Equal(fill, a.Fill);
    }

    [Fact]
    public void Max_roll_reads_perfect()
    {
        var a = RollQuality.Assess(179, 170, 179, StatDirection.HigherIsBetter, "%");
        Assert.Equal("perfect roll", RollQuality.Distance(a));
    }

    [Fact]
    public void Lower_is_better_reverses_the_fill()
    {
        var a = RollQuality.Assess(12, 10, 20, StatDirection.LowerIsBetter, "%");
        Assert.Equal(0.8m, a.Fill);
        Assert.Equal(0.2m, a.Position);
        Assert.Equal(2m, a.DistanceFromBest);
        Assert.Contains("above min", RollQuality.Describe(a));
    }

    [Fact]
    public void Negative_range_is_ordered_and_scored()
    {
        var a = RollQuality.Assess(-12, -10, -15, StatDirection.HigherIsBetter, "%");
        Assert.Equal(RollStatus.Scored, a.Status);
        Assert.Equal(-15m, a.Min);
        Assert.Equal(0.6m, a.Fill);
    }

    [Fact]
    public void Decimal_values_work()
    {
        var a = RollQuality.Assess(1.5m, 1.0m, 2.0m, StatDirection.HigherIsBetter, "%");
        Assert.Equal(0.5m, a.Fill);
    }

    [Fact]
    public void Fixed_range_says_fixed()
    {
        var a = RollQuality.Assess(30, 30, 30, StatDirection.HigherIsBetter, "%", RangeProvenance.DataPack);
        Assert.Equal(RollStatus.Fixed, a.Status);
        Assert.False(a.HasBar);
    }

    [Fact]
    public void Out_of_range_is_a_mismatch_and_not_scored()
    {
        var a = RollQuality.Assess(181, 170, 179, StatDirection.HigherIsBetter, "%");
        Assert.Equal(RollStatus.RangeMismatch, a.Status);
        Assert.Null(a.Fill);
    }

    [Theory]
    [InlineData(StatDirection.Unknown)]
    [InlineData(StatDirection.BuildDependent)]
    public void Unverified_direction_gets_position_marker_only(StatDirection dir)
    {
        var a = RollQuality.Assess(15, 10, 20, dir, "");
        Assert.Equal(RollStatus.PositionOnly, a.Status);
        Assert.Null(a.Fill);
        Assert.Equal(0.5m, a.Position);
    }

    [Fact]
    public void No_range_means_no_bar()
    {
        var a = RollQuality.Assess(15, null, null, StatDirection.HigherIsBetter, "", RangeProvenance.None);
        Assert.Equal(RollStatus.NoRange, a.Status);
        Assert.False(a.HasBar);
    }
}
