using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Wealth;

namespace StillSaneExile.Domain.Tests;

public class WealthLedgerTests
{
    private const string Demo = "Demo League (synthetic)";
    private static readonly DateTimeOffset Now = Fixtures.DemoNow;

    private static HoldingEntry H(string id, long quantity, int minutesAgo, string tab = "Currency", string? variant = null, string league = Demo) =>
        new(league, tab, id, variant, quantity, Now.AddMinutes(-minutesAgo), HoldingOrigin.Manual);

    private static IReadOnlyList<HoldingEntry> Example() =>
        WealthCsv.Read(Fixtures.Text("wealth/example.csv"), "example.csv", Demo, Now).Entries;

    [Fact]
    public void Current_is_the_latest_count_per_tab_item_and_variant()
    {
        var current = WealthLedger.Current([
            H("divine", 11, 90), H("divine", 14, 30), H("divine", 2, 60, tab: "Dump"),
            H("chaos", 5, 10, variant: "a"), H("chaos", 6, 20),
        ]);

        Assert.Equal(
            [("Currency", "chaos", (string?)null, 6L), ("Currency", "chaos", "a", 5L), ("Currency", "divine", (string?)null, 14L), ("Dump", "divine", (string?)null, 2L)],
            current.Select(h => (h.Tab, h.ItemId, h.Variant, h.Quantity)));
    }

    [Fact]
    public void A_zero_count_clears_the_holding_but_an_older_zero_does_not()
    {
        var current = WealthLedger.Current([H("divine", 14, 60), H("divine", 0, 30), H("regal", 0, 60), H("regal", 3, 30)]);

        Assert.Equal(["regal"], current.Select(h => h.ItemId));
    }

    [Fact]
    public void Example_file_current_holdings_use_the_latest_divine_count()
    {
        var current = WealthLedger.Current(Example());

        Assert.Equal(7, current.Count);
        Assert.Equal(14, current.Single(h => h.ItemId == "divine").Quantity);
    }

    [Fact]
    public void Import_check_sorts_rows_into_new_already_stored_and_conflict()
    {
        var stored = new[] { H("divine", 14, 30), H("chaos", 40, 30), H("regal", 9, 5) };
        var checks = WealthLedger.Check([H("divine", 14, 30), H("chaos", 41, 30), H("exalted", 100, 30), H("regal", 8, 60)], stored);

        Assert.Equal([ImportStatus.AlreadyStored, ImportStatus.Conflict, ImportStatus.New, ImportStatus.New], checks.Select(c => c.Status));
        Assert.Equal("Already stored as 40 for this tab, item and time; this file says 41.", checks[1].Error);
        Assert.Null(checks[0].Error);
        Assert.Equal([false, false, false, true], checks.Select(c => c.OlderThanStored));
    }

    [Fact]
    public void Re_importing_the_same_file_changes_nothing()
    {
        var entries = Example();

        Assert.All(WealthLedger.Check(entries, entries), c => Assert.Equal(ImportStatus.AlreadyStored, c.Status));
    }

    [Fact]
    public void Same_instant_written_with_another_offset_is_the_same_observation()
    {
        var stored = new[] { H("divine", 14, 30) };
        var shifted = stored[0] with { SnapshotAt = stored[0].SnapshotAt.ToOffset(TimeSpan.FromHours(2)) };

        Assert.Equal(ImportStatus.AlreadyStored, Assert.Single(WealthLedger.Check([shifted], stored)).Status);
    }

    [Fact]
    public void Manual_entry_is_recorded_now()
    {
        var key = new HoldingKey("Currency", "divine", null);

        Assert.Equal(Now, WealthLedger.ManualTime(Now, key, [H("divine", 3, 30), H("divine", 9, -60, tab: "Dump")]));
    }

    [Fact]
    public void Manual_entry_lands_after_a_later_stored_count_so_it_becomes_current()
    {
        // The demo clock restarts at the same instant every run; a count saved 10 minutes into an earlier
        // run is "in the future" of this one.
        var later = H("divine", 3, -10);
        var at = WealthLedger.ManualTime(Now, later.Key, [later]);

        Assert.Equal(later.SnapshotAt.AddSeconds(1), at);
        Assert.Equal(5, WealthLedger.Current([later, later with { Quantity = 5, SnapshotAt = at }]).Single().Quantity);
    }

    [Fact]
    public void Demo_book_values_quoted_items_and_its_primary_currency_and_leaves_the_rest_unpriced()
    {
        var book = Fixtures.DemoBook();
        var converter = new CurrencyConverter(book, StalenessPolicy.Hourly);
        HoldingValue V(string id, long q) => HoldingValuation.Value(H(id, q, 30), book, converter, "divine", Now);

        Assert.Equal(14m, V("divine", 14).Value);
        Assert.Equal(6m, V("chaos", 120).Value);
        Assert.Equal(1.4m, V("unique.sanitys-last-loop", 1).Value);
        Assert.Equal(3m, decimal.Round(V("exalted", 360).Value, 10));
        Assert.Equal(0.45m, decimal.Round(V("greater-iron-rune", 18).Value, 10));
        Assert.All(new[] { V("divine", 1), V("exalted", 1) }, v => Assert.Equal("divine", v.Denomination));

        var alchemy = V("alchemy", 47);
        Assert.False(alchemy.Priced);
        Assert.Equal("Not in this league's price book.", alchemy.Note);
        Assert.True(HoldingValuation.IsPriced(book, "exalted", null));
        Assert.False(HoldingValuation.IsPriced(book, "exalted", "foil"));
        Assert.False(HoldingValuation.IsPriced(book, "alchemy", null));
    }

    [Fact]
    public void Empty_live_book_prices_nothing_not_even_exalted()
    {
        var book = PriceBook.Empty("Standard");
        var converter = new CurrencyConverter(book, StalenessPolicy.Hourly);

        var value = HoldingValuation.Value(H("exalted", 10, 30, league: "Standard"), book, converter, "exalted", Now);

        Assert.False(value.Priced);
        Assert.Equal("No prices in this league yet.", value.Note);
        Assert.False(HoldingValuation.IsPriced(book, "exalted", null));
    }

    [Fact]
    public void A_holding_from_another_league_is_never_valued_with_this_book()
    {
        var book = Fixtures.DemoBook();
        var value = HoldingValuation.Value(H("divine", 10, 30, league: "Standard"), book, new CurrencyConverter(book, StalenessPolicy.Hourly), "exalted", Now);

        Assert.False(value.Priced);
    }

    [Fact]
    public void Names_come_from_the_price_book_identities()
    {
        var book = Fixtures.DemoBook();

        Assert.Equal("Divine Orb", HoldingValuation.Name(book, "divine", null));
        Assert.Equal("Sanity's Last Loop", HoldingValuation.Name(book, "unique.sanitys-last-loop", null));
        Assert.Null(HoldingValuation.Name(book, "alchemy", null));
    }
}
