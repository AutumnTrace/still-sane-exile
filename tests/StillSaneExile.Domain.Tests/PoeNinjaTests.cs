using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Domain.Tests;

/// <summary>poe.ninja's PoE2 answers turned into a price book. The fixtures under <c>fixtures/poeninja/</c>
/// have the exact shape of poe.ninja's answers with made-up numbers and a few lines each.</summary>
public class PoeNinjaTests
{
    private const string League = "Runes of Aldur";
    private static readonly DateTimeOffset T0 = Fixtures.DemoNow;

    private static readonly PoeNinjaCategory CurrencyType = new(PoeNinjaOverview.Exchange, "Currency");
    private static readonly PoeNinjaCategory Runes = new(PoeNinjaOverview.Exchange, "Runes");
    private static readonly PoeNinjaCategory Omens = new(PoeNinjaOverview.Exchange, "Omens");
    private static readonly PoeNinjaCategory Accessories = new(PoeNinjaOverview.Items, "UniqueAccessories");

    private static PoeNinjaPayload Payload(PoeNinjaCategory category, string file, DateTimeOffset? at = null) =>
        new(category, Fixtures.Text($"poeninja/{file}.json"), at ?? T0);

    private static PoeNinjaBookResult Everything() => PoeNinjaBook.Build(League,
    [
        Payload(CurrencyType, "exchange-currency"),
        Payload(Runes, "exchange-runes"),
        Payload(Omens, "exchange-empty"),
        Payload(Accessories, "items-unique-accessories"),
    ], T0);

    private static CurrencyConverter Converter(PriceBook book) => new(book, StalenessPolicy.Hourly);

    private static ItemSnapshot Copy(string text)
    {
        var result = ItemTextParser.Parse(text, T0, "poe-ninja-test");
        Assert.True(result.Ok, result.Error);
        return result.Item!;
    }

    private static ItemSnapshot Currency(string name, int stack) =>
        Copy($"Item Class: Stackable Currency\nRarity: Currency\n{name}\n--------\nStack Size: {stack}/20\n--------\nSynthetic description\n");

    private static ItemSnapshot Unique(string name, string baseType, bool corrupted = false) =>
        Copy($"Item Class: Belts\nRarity: Unique\n{name}\n{baseType}\n--------\nItem Level: 80\n--------\n+30 to Strength\n" + (corrupted ? "--------\nCorrupted\n" : ""));

    [Fact]
    public void Exchange_lines_become_exchange_estimates_in_divine_with_window_volume_and_provenance()
    {
        var book = Everything().Book;

        var alch = book.Find("alch")!;
        Assert.Equal(0.005m, alch.Amount);
        Assert.Equal("divine", alch.Denomination);
        Assert.Equal(ObservationKind.ExchangeEstimate, alch.Kind);
        Assert.Equal(League, alch.League);
        Assert.Equal("poe.ninja", alch.Source);
        Assert.Equal(T0, alch.FetchedAt);
        Assert.Equal(T0, alch.ObservedTo);
        Assert.Equal(T0 - TimeSpan.FromHours(1), alch.ObservedFrom);
        Assert.Equal(500m, alch.Volume); // 2.5 div traded at 0.005 div each
        Assert.Contains("Currency", alch.Provenance, StringComparison.Ordinal);

        Assert.Equal(League, book.League);
        Assert.Equal("divine", book.PrimaryCurrency);
        Assert.False(book.IsSynthetic);
        Assert.Equal(T0, book.CreatedAt);
        Assert.Equal("poe.ninja-20261127T113000Z", book.SnapshotId);
    }

    [Fact]
    public void Core_rates_give_exalted_and_chaos_both_ways_so_prices_show_in_ex_div_or_c()
    {
        var book = Everything().Book;
        var converter = Converter(book);

        Assert.Equal(400m, converter.Convert(1m, "divine", "exalted", T0).Value);
        Assert.Equal(0.0025m, converter.Convert(1m, "exalted", "divine", T0).Value);
        Assert.Equal(8m, converter.Convert(1m, "divine", "chaos", T0).Value);
        Assert.Equal(2m, converter.Convert(book.Find("alch")!.Amount, "divine", "exalted", T0).Value);

        var chaosInEx = converter.Convert(1m, "chaos", "exalted", T0);
        Assert.True(chaosInEx.Ok);
        Assert.Equal(ConversionPath.Bridge, chaosInEx.Path);
        Assert.Equal(50m, chaosInEx.Value);

        Assert.DoesNotContain(book.Quotes, q => q.ItemId == "divine" && q.Denomination == "divine");
        Assert.Equal(400m, Assert.Single(book.Quotes, q => q.ItemId == "divine" && q.Denomination == "exalted").Amount);
    }

    [Fact]
    public void Rates_come_from_the_Currency_answer_whatever_order_the_answers_arrive_in()
    {
        var book = PoeNinjaBook.Build(League, [Payload(Runes, "exchange-runes"), Payload(CurrencyType, "exchange-currency")], T0).Book;

        Assert.Equal(400m, Converter(book).Convert(1m, "divine", "exalted", T0).Value);
    }

    [Fact]
    public void Zero_nameless_and_repeated_lines_are_skipped_and_exponents_are_read()
    {
        var result = Everything();
        var book = result.Book;

        Assert.Null(book.Find("worthless"));
        Assert.Null(book.Find("nameless"));
        Assert.Contains(result.Notes, n => n.Contains("no item name", StringComparison.Ordinal));
        Assert.Equal(0.0125m, book.Find("regal")!.Amount);

        // "alch" is in Currency and (made up) in Runes too: the first category wins, by id and by name.
        Assert.Equal(0.005m, Assert.Single(book.Quotes, q => q.ItemId == "alch").Amount);
        Assert.Equal("Orb of Alchemy", Assert.Single(book.Identities!, i => i.Id == "alch").Name);
        Assert.Equal(0.02m, book.Find("greater-iron-rune")!.Amount);
    }

    [Fact]
    public void Empty_unknown_and_unreadable_answers_are_tolerated()
    {
        var result = PoeNinjaBook.Build(League,
        [
            Payload(Omens, "exchange-empty"),
            new PoeNinjaPayload(new PoeNinjaCategory(PoeNinjaOverview.Exchange, "Broken"), "<html>not json</html>", T0),
            new PoeNinjaPayload(new PoeNinjaCategory(PoeNinjaOverview.Exchange, "Shapeless"), "{\"lines\":[]}", T0),
            Payload(CurrencyType, "exchange-currency"),
        ], T0);

        Assert.Contains(result.Notes, n => n.StartsWith("Omens: no prices", StringComparison.Ordinal));
        Assert.Contains(result.Notes, n => n.StartsWith("Broken: the answer was not readable JSON", StringComparison.Ordinal));
        Assert.Contains(result.Notes, n => n.StartsWith("Shapeless: no reference currency", StringComparison.Ordinal));
        Assert.NotNull(result.Book.Find("alch"));
    }

    [Fact]
    public void Currency_is_identified_by_name_and_priced_from_the_book()
    {
        var book = Everything().Book;
        var converter = Converter(book);

        Assert.Equal("exalted", ItemPricer.Identify(Currency("Exalted Orb", 7), book)!.Id);
        Assert.Equal("divine", ItemPricer.Identify(Currency("Divine Orb", 1), book)!.Id);
        Assert.Equal(IdentityKind.Commodity, ItemPricer.Identify(Currency("Greater Iron Rune", 3), book)!.Kind);

        var divine = ItemPricer.Price(Currency("Divine Orb", 1), converter, "exalted", [], League, T0 + TimeSpan.FromMinutes(20));
        Assert.Equal(PriceKind.Commodity, divine.Kind);
        Assert.Equal("400 ex", divine.Headline);
        Assert.StartsWith("poe.ninja exchange estimate, 20 min ago", divine.Detail, StringComparison.Ordinal);

        var exalts = ItemPricer.Price(Currency("Exalted Orb", 7), converter, "divine", [], League, T0);
        Assert.Equal("<0.01 div each · 0.02 div for 7", exalts.Headline);

        var alchs = ItemPricer.Price(Currency("Orb of Alchemy", 10), converter, "exalted", [], League, T0);
        Assert.Equal("2 ex each · 20 ex for 10", alchs.Headline);
        Assert.Contains("volume 500", alchs.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Uniques_become_category_baselines_keyed_by_name_and_base()
    {
        var book = Everything().Book;

        var identity = ItemPricer.Identify(Unique("Mageblood", "Utility Belt"), book)!;
        Assert.Equal("unique.mageblood-utility-belt", identity.Id);
        Assert.Null(identity.Variant);

        var quote = book.Find(identity.Id)!;
        Assert.Equal(180m, quote.Amount); // the line with 40 listings, not the stray one with 2
        Assert.Equal("divine", quote.Denomination);
        Assert.Equal(ObservationKind.CategoryBaseline, quote.Kind);
        Assert.Equal(40m, quote.Volume);
        Assert.Contains("UniqueAccessories", quote.Provenance, StringComparison.Ordinal);

        var line = ItemPricer.Price(Unique("Mageblood", "Utility Belt"), Converter(book), "divine", [], League, T0 + TimeSpan.FromMinutes(5));
        Assert.Equal(PriceKind.UniqueBaseline, line.Kind);
        Assert.Equal("180 div", line.Headline);
        Assert.Equal("poe.ninja · 5 min ago · 40 listed", line.Detail);
        Assert.False(line.UncorruptedStandIn);
        Assert.Equal(T0, line.ObservedTo);

        Assert.Equal("72,000 ex", ItemPricer.Price(Unique("Mageblood", "Utility Belt"), Converter(book), "exalted", [], League, T0).Headline);
    }

    [Fact]
    public void A_unique_is_never_identified_by_its_name_alone()
    {
        var book = Everything().Book;

        Assert.Null(ItemPricer.Identify(Unique("Mageblood", "Heavy Belt"), book));
        Assert.DoesNotContain(book.Identities!, i => i.Name == "Synthetic Nameless Band"); // its line had no base type
        Assert.Contains(Everything().Notes, n => n.Contains("no base type", StringComparison.Ordinal));
    }

    [Fact]
    public void Corrupted_lines_stay_apart_from_clean_ones()
    {
        var book = Everything().Book;
        var converter = Converter(book);

        var corrupted = ItemPricer.Identify(Unique("Mageblood", "Utility Belt", corrupted: true), book)!;
        Assert.Equal(UniqueVariant.Corrupted, corrupted.Variant);
        Assert.Equal(150m, book.Find(corrupted.Id, corrupted.Variant)!.Amount);
        var corruptedLine = ItemPricer.Price(Unique("Mageblood", "Utility Belt", corrupted: true), converter, "divine", [], League, T0);
        Assert.Equal("150 div", corruptedLine.Headline);
        Assert.False(corruptedLine.UncorruptedStandIn); // it has its own line
        Assert.Equal("180 div", ItemPricer.Price(Unique("Mageblood", "Utility Belt"), converter, "divine", [], League, T0).Headline);
    }

    [Fact]
    public void A_corrupted_copy_with_no_corrupted_line_gets_the_clean_baseline_and_is_told_so()
    {
        var book = Everything().Book;

        var line = ItemPricer.Price(Unique("Sanity's Last Loop", "Gold Ring", corrupted: true), Converter(book), "exalted", [], League, T0);

        Assert.Equal("200 ex", line.Headline);
        Assert.True(line.UncorruptedStandIn);
    }

    [Fact]
    public void Variants_that_cannot_be_told_apart_are_not_guessed()
    {
        var book = Everything().Book;

        Assert.Equal(2, book.Identities!.Count(i => i.Name == "Synthetic Twin Charm"));
        Assert.Null(ItemPricer.Identify(Unique("Synthetic Twin Charm", "Gold Ring"), book));
    }

    [Fact]
    public void Live_quotes_go_stale_after_two_missed_hours_and_stop_ranking_after_a_day_with_their_age()
    {
        var book = Everything().Book;
        var item = Unique("Mageblood", "Utility Belt");

        var fresh = ItemPricer.Price(item, Converter(book), "divine", [], League, T0 + TimeSpan.FromMinutes(90));
        var stale = ItemPricer.Price(item, Converter(book), "divine", [], League, T0 + TimeSpan.FromHours(2.5));
        var old = ItemPricer.Price(item, Converter(book), "divine", [], League, T0 + TimeSpan.FromHours(25));

        Assert.Equal(Freshness.Fresh, fresh.Freshness);
        Assert.Equal(Freshness.Stale, stale.Freshness);
        Assert.Equal("poe.ninja · 2 h ago · 40 listed · may be out of date", stale.Detail);
        Assert.Equal(Freshness.RankingSuppressed, old.Freshness);
        Assert.Equal("poe.ninja · 1 d ago · 40 listed · may be out of date", old.Detail);
    }

    [Fact]
    public void A_live_book_passes_the_guard_for_its_own_league_only()
    {
        var book = Everything().Book;

        PriceBookGuard.EnsureAllowed(ProfileKind.Live, League, book);
        Assert.Throws<InvalidOperationException>(() => PriceBookGuard.EnsureAllowed(ProfileKind.Live, "HC Runes of Aldur", book));
        Assert.Throws<InvalidOperationException>(() => PriceBookGuard.EnsureAllowed(ProfileKind.Demo, League, book));
    }

    [Fact]
    public void A_book_round_trips_through_its_snapshot_json()
    {
        var book = Everything().Book;

        var back = PriceBook.FromJson(book.ToJson());

        Assert.Equal(book.SnapshotId, back.SnapshotId);
        Assert.Equal(book.League, back.League);
        Assert.Equal(book.CreatedAt, back.CreatedAt);
        Assert.Equal(book.Quotes, back.Quotes);
        Assert.Equal(book.Identities, back.Identities);
    }

    [Fact]
    public void Leagues_are_the_current_public_indexed_economy_leagues()
    {
        var leagues = PoeNinjaLeagues.Parse(Fixtures.Text("poeninja/index-state.json"));

        Assert.Equal(["Runes of Aldur", "HC Runes of Aldur", "Standard", "Hardcore"], leagues.Select(l => l.Name));
        Assert.True(leagues[1].Hardcore);
        Assert.Equal("Hardcore Runes of Aldur", leagues[1].DisplayName);
        Assert.Empty(PoeNinjaLeagues.Parse("{}"));
    }

    [Fact]
    public void The_shipped_category_list_is_the_checked_one_with_Currency_first()
    {
        var shipped = PoeNinjaCatalog.Parse(Fixtures.DataText("poe-ninja.json"));

        Assert.Equal(PoeNinjaCatalog.Default.Categories, shipped.Categories);
        Assert.Equal(CurrencyType, shipped.Categories[0]);
        Assert.Equal(14, shipped.Categories.Count(c => c.Overview == PoeNinjaOverview.Exchange));
        Assert.Equal(9, shipped.Categories.Count(c => c.Overview == PoeNinjaOverview.Items));
    }

    [Fact]
    public void A_category_list_without_types_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => PoeNinjaCatalog.Parse("{\"exchange\": [], \"items\": [\" \"]}"));
        Assert.Throws<InvalidDataException>(() => PoeNinjaCatalog.Parse("not json"));
        Assert.Equal(2, PoeNinjaCatalog.Parse("{\"exchange\": [\"Currency\", \"currency\", \"Runes\"]}").Categories.Count);
    }
}
