using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Tests;

/// <summary>What the price block says for a unique priced by its baseline alone.</summary>
public class PriceAnswerTests
{
    private static readonly DateTimeOffset Observed = new(2026, 11, 28, 2, 0, 0, TimeSpan.Zero);

    private static PriceLine Mageblood(Freshness freshness, bool standIn = false) => new(
        PriceKind.UniqueBaseline, "220 ex", "poe.ninja · 10 h ago · 3,812 listed", freshness, true,
        ObservedTo: Observed, Amount: 220m, Denomination: "exalted", UncorruptedStandIn: standIn);

    [Fact]
    public void The_label_holds_the_price_alone_and_one_short_line_says_what_moves_it()
    {
        var answer = PriceAnswer.Baseline(Mageblood(Freshness.Fresh), Observed + TimeSpan.FromHours(10));

        Assert.Equal("Usually sells for about", answer.Lead);
        Assert.Equal("220 ex", answer.Big);
        Assert.DoesNotContain("baseline", answer.Big, StringComparison.Ordinal);
        Assert.Equal("Your rolls can change it.", answer.Note);
        Assert.False(answer.NoteIsWarning);
        Assert.Equal("poe.ninja · 10 h ago · 3,812 listed", answer.Line);
        Assert.Equal("Your rolls can change it · 10 h ago", answer.PeekNote);
    }

    [Fact]
    public void An_old_price_says_it_may_be_out_of_date()
    {
        var answer = PriceAnswer.Baseline(Mageblood(Freshness.Stale), Observed + TimeSpan.FromHours(10));

        Assert.True(answer.NoteIsWarning);
        Assert.Equal("Your rolls can change it · 10 h ago · may be out of date", answer.PeekNote);
    }

    [Fact]
    public void Under_1_div_a_unique_shows_in_Exalted_like_every_other_price() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var session = app.Shell.Session; // the demo: 1 div = 120 ex, prices in Divine
        var cheap = new PriceLine(PriceKind.UniqueBaseline, "0.5 div", "1 h ago · 12 listed", Freshness.Fresh, true, Amount: 0.5m, Denomination: "divine");

        Assert.Equal("60 ex", PriceAnswer.Baseline(cheap, null, session.PriceText()).Big);
        Assert.Equal("1.4 div", PriceAnswer.Baseline(cheap with { Amount = 1.4m, Headline = "1.4 div" }, null, session.PriceText()).Big);
        Assert.Equal("0.5 div", PriceAnswer.Baseline(cheap).Big); // no rates to hand: the line as it came
    });

    [Fact]
    public void A_stack_of_Exalted_Orbs_shown_in_Divine_says_its_own_amount_once() => Sta.Run(() =>
    {
        using var app = new TestApp();

        var stack = app.Shell.Inspect.AddFromText(app.ItemText("currency-exalted.txt"), "test")!; // 7 Exalted Orbs

        Assert.Equal("7 ex", stack.Answer.Big); // not "7 ex ≈ 0.06 div"
    });

    [Fact]
    public void A_corrupted_copy_priced_as_a_clean_one_says_so()
    {
        var answer = PriceAnswer.Baseline(Mageblood(Freshness.Fresh, standIn: true));

        Assert.Equal("220 ex", answer.Big);
        Assert.Equal("Price of an uncorrupted one: corruption can change it.", answer.Note);
        Assert.True(answer.NoteIsWarning);
        Assert.Equal("Uncorrupted price", answer.PeekNote); // the demo's clock is made up: no age
    }
}
