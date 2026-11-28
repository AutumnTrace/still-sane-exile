using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Reading;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Domain.Tests;

public class PriceTextTests
{
    private static PriceText Text(string display) =>
        new(new CurrencyConverter(Fixtures.DemoBook(), StalenessPolicy.Hourly), display, Fixtures.DemoNow);

    [Fact]
    public void Divine_keeps_one_decimal_and_cheap_items_show_in_whole_exalted()
    {
        var t = Text("divine");
        Assert.Equal("2.1 div", t.One(2.13m));
        Assert.Equal("42 ex", t.One(0.35m));
        Assert.Equal("2.1–2.8 div", t.Range(2.13m, 2.79m));
        Assert.Equal("60–95 ex", t.Range(0.5m, 95m / 120m));
        Assert.Equal("95 ex – 1.2 div", t.Range(95m / 120m, 1.2m));
        Assert.Equal("2 div", t.Range(2m, 2m));
    }

    [Fact]
    public void Other_display_currencies_are_shown_as_they_are()
    {
        Assert.Equal("252 ex", Text("exalted").One(252.4m));
        Assert.Equal("0.4 ex", Text("exalted").One(0.4m));
        Assert.Equal("12.5 c", Text("chaos").One(12.5m));
    }

    [Fact]
    public void Divine_rate_line_comes_from_the_price_book()
    {
        Assert.Equal("1 div = 120 ex", Text("divine").DivineRate());
        var empty = new PriceText(new CurrencyConverter(PriceBook.Empty("Some League"), StalenessPolicy.Hourly), "divine", Fixtures.DemoNow);
        Assert.Null(empty.DivineRate());
        Assert.Equal("0.5 div", empty.One(0.5m)); // no rate: never invent an Exalted amount
    }

    [Theory]
    [InlineData(12, ValueTier.S)]
    [InlineData(10, ValueTier.S)]
    [InlineData(2.4, ValueTier.A)]
    [InlineData(1, ValueTier.A)]
    [InlineData(0.35, ValueTier.B)] // 42 ex
    [InlineData(0.25, ValueTier.B)] // 30 ex
    [InlineData(0.075, ValueTier.C)] // 9 ex
    [InlineData(0.05, ValueTier.C)] // 6 ex
    [InlineData(0.025, ValueTier.D)] // 3 ex
    [InlineData(0.001, ValueTier.E)]
    public void A_price_is_coloured_by_what_it_is_worth_in_Divine_and_Exalted(double divines, ValueTier tier)
    {
        Assert.Equal(tier, Text("divine").Tier((decimal)divines));
        Assert.Equal(tier, Text("exalted").Tier((decimal)divines * 120m)); // the same value shown in Exalted
    }

    [Fact]
    public void Without_a_rate_a_price_has_no_colour_step()
    {
        var empty = new PriceText(new CurrencyConverter(PriceBook.Empty("Some League"), StalenessPolicy.Hourly), "divine", Fixtures.DemoNow);
        Assert.Null(empty.Tier(3m));
    }
}

public class WorksheetSearchTests
{
    private static ComparisonWorksheet Crossbow() =>
        WorksheetFactory.Create(ItemReader.Read(Fixtures.Item("rare-crossbow-advanced"), Fixtures.Pack()));

    [Fact]
    public void Starting_bounds_follow_the_chosen_strictness_and_keep_the_selection()
    {
        var w = Crossbow();
        var pdps = w.Row(ComparisonWorksheet.PhysicalDpsId)!;
        Assert.Equal(682m, pdps.Min); // ⌊758.5 × 0.9⌋

        w.SetStartingBounds(0m);
        Assert.Equal(758m, pdps.Min);
        w.SetStartingBounds(0.2m);
        Assert.Equal(606m, pdps.Min);
        Assert.True(pdps.Selected);
    }

    [Fact]
    public void Copying_the_search_from_a_fresh_worksheet_resets_every_choice()
    {
        var w = Crossbow();
        foreach (var r in w.Rows)
        {
            r.Selected = !r.Selected;
            r.Min = 1;
        }

        w.BuyNowOnly = false;
        w.MinItemLevel = 80;
        w.CopySearchFrom(Crossbow());

        Assert.Equal(2, w.Selected.Count());
        Assert.Equal(682m, w.Row(ComparisonWorksheet.PhysicalDpsId)!.Min);
        Assert.True(w.BuyNowOnly);
        Assert.Null(w.MinItemLevel);
    }

    [Fact]
    public void Suggested_item_level_rounds_down_for_rares_and_is_exact_for_crafting_bases_but_no_search_uses_it()
    {
        Assert.Equal(80, Crossbow().SuggestedItemLevel);
        Assert.Null(Crossbow().MinItemLevel);
        var magic = WorksheetFactory.Create(ItemReader.Read(Fixtures.Item("magic-crossbow-normal"), Fixtures.Pack()));
        Assert.Equal(82, magic.SuggestedItemLevel);
        Assert.Null(magic.MinItemLevel); // the app offers no item level choice for now, so none is searched unseen
    }

    [Fact]
    public void A_line_that_could_be_two_stats_stays_out_of_the_starting_search()
    {
        var reading = ItemReader.Read(Fixtures.Item("rare-crossbow-normal-hybrid"), Fixtures.Pack());
        var ambiguous = reading.Lines.Where(l => l.Status == LineStatus.Ambiguous).Select(l => l.Line.Template).ToList();
        Assert.NotEmpty(ambiguous);

        // Listed so they can be ticked by hand, but unticked at the start. (No fixture has an ambiguous
        // line that a class preset asks for, so this checks the guarantee rather than the preset path.)
        var w = WorksheetFactory.Create(reading);
        Assert.DoesNotContain(w.Selected, r => ambiguous.Contains(r.Template));
        Assert.Contains(w.Rows, r => ambiguous.Contains(r.Template));
    }

    [Fact]
    public void Buy_now_choice_is_part_of_the_copied_search()
    {
        var w = Crossbow();
        Assert.Contains("Buy-now listings only", w.ToManualInstructions("Demo League (synthetic)"));
        w.BuyNowOnly = false;
        w.MinItemLevel = 80;
        var text = w.ToManualInstructions("Demo League (synthetic)");
        Assert.DoesNotContain("Buy-now", text);
        Assert.Contains("Item level ≥ 80", text);
    }
}
