using System.Net;
using System.Text.Json;
using StillSaneExile.Domain.Market;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Reading;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Domain.Tests;

/// <summary>The trade site's search API: URLs, bodies and answers, on synthetic fixtures in the shapes the
/// live site sent on 27 Nov 2026 (<c>fixtures/trade/</c>; made-up values, no real listings or names).</summary>
public class TradeApiTests
{
    private const string First = "aaaa0000000000000000000000000000000000000000000000000000000000a1";

    private static readonly Lazy<TradeData> Data = new(() => TradeData.Parse(Fixtures.DataText("trade/trade-data.json")));

    private static IReadOnlyList<TradeListing> Fetch(bool instantBuyoutSearch = true) =>
        TradeApi.ParseFetch(Fixtures.Text("trade/fetch.json"), instantBuyoutSearch);

    [Fact]
    public void The_search_goes_to_the_league_s_poe2_search()
    {
        Assert.Equal("https://www.pathofexile.com/api/trade2/search/poe2/Runes%20of%20Aldur", TradeApi.SearchUri("Runes of Aldur").AbsoluteUri);
        Assert.Equal("https://www.pathofexile.com/api/trade2/search/poe2/Standard", TradeApi.SearchUri("Standard").AbsoluteUri);
    }

    [Fact]
    public void The_search_body_is_the_query_sorted_cheapest_first()
    {
        const string query = """{"query":{"status":{"option":"securable"},"name":"Sanity's Last Loop","stats":[{"type":"and","filters":[{"id":"explicit.stat_210067635","value":{"min":15}}]}]}}""";

        var body = TradeApi.SearchBody(query);

        Assert.Equal("""{"query":{"status":{"option":"securable"},"name":"Sanity's Last Loop","stats":[{"type":"and","filters":[{"id":"explicit.stat_210067635","value":{"min":15}}]}]},"sort":{"price":"asc"}}""", body);
    }

    [Fact]
    public void The_search_answer_gives_its_id_the_listing_ids_and_the_total()
    {
        var answer = TradeApi.ParseSearch(Fixtures.Text("trade/search.json"));

        Assert.Equal("SyntheticQuery0001-made_up", answer.Id);
        Assert.Equal(137, answer.Total);
        Assert.Equal(12, answer.Ids.Count); // the one id that is not plain letters and digits is dropped
        Assert.Equal(First, answer.Ids[0]);
        Assert.DoesNotContain(answer.Ids, id => id.Contains('/', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"result":[]}""")]
    [InlineData("""{"error":{"code":2,"message":"Invalid query"}}""")]
    [InlineData("[]")]
    public void A_search_answer_without_an_id_cannot_be_read(string json) =>
        Assert.ThrowsAny<JsonException>(() => TradeApi.ParseSearch(json));

    [Fact]
    public void The_fetch_takes_up_to_ten_ids_with_the_search_s_id_and_the_realm()
    {
        var ids = Enumerable.Range(0, 10).Select(i => $"aaaa{i:D60}").ToList();

        var uri = TradeApi.FetchUri(ids, "Synthetic+Query/0001=");

        Assert.Equal($"https://www.pathofexile.com/api/trade2/fetch/{string.Join(',', ids)}?query=Synthetic%2BQuery%2F0001%3D&realm=poe2", uri.AbsoluteUri);
        Assert.Throws<ArgumentException>(() => TradeApi.FetchUri([.. ids, "aaaa1"], "q"));
        Assert.Throws<ArgumentException>(() => TradeApi.FetchUri(["../search"], "q"));
        Assert.Throws<ArgumentException>(() => TradeApi.FetchUri([], "q"));
    }

    [Fact]
    public void The_fetch_reads_price_time_level_and_mods()
    {
        var listings = Fetch();

        Assert.Equal(8, listings.Count); // a gone listing (null) and one without a price are left out
        var a = listings[0];
        Assert.Equal(First, a.Id);
        Assert.Equal((1m, "divine", "~b/o"), (a.Amount, a.Currency, a.PriceType));
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 11, 0, 0, TimeSpan.Zero), a.IndexedAt);
        Assert.Equal(82, a.ItemLevel);
        Assert.Equal(330.5m, a.PhysicalDps);
        Assert.Equal(160m, a.ValueOf("explicit.stat_1509134228"));
        Assert.Equal(28m, a.ValueOf("explicit.stat_1940865751")); // "Adds 20 to 36": the average, as the site's filter compares it
        Assert.Equal(16m, a.ValueOf("explicit.stat_210067635"));
        Assert.Equal("+170 to Accuracy Rating", a.Mods.Single(m => m.StatId == "explicit.stat_691932474").Text); // the site's markup removed
        Assert.Null(a.ValueOf("explicit.stat_3299347043"));

        var b = listings[1];
        Assert.Equal((300m, "exalted", "~price"), (b.Amount, b.Currency, b.PriceType));
        Assert.Null(listings.Single(l => l.Id.EndsWith("b0", StringComparison.Ordinal)).IndexedAt); // an unreadable time is no time
    }

    [Fact]
    public void Nothing_about_the_seller_is_kept()
    {
        foreach (var listing in Fetch())
        {
            var everything = listing.ToString() + string.Join(";", listing.Mods);
            Assert.DoesNotContain("ExampleSeller", everything, StringComparison.Ordinal);
            Assert.DoesNotContain("Synthetic tab", everything, StringComparison.Ordinal);
            Assert.DoesNotContain("Example Thirst", everything, StringComparison.Ordinal); // the item's own name isn't needed either
        }
    }

    [Fact]
    public void In_an_instant_buyout_search_every_listing_is_one_otherwise_only_those_with_the_gold_fee()
    {
        Assert.All(Fetch(instantBuyoutSearch: true), l => Assert.True(l.InstantBuyout));

        var any = Fetch(instantBuyoutSearch: false);
        Assert.False(any.Single(l => l.Currency == "exalted").InstantBuyout); // no merchant fee: in person
        Assert.Equal(7, any.Count(l => l.InstantBuyout));
    }

    [Fact]
    public void Plain_text_mods_take_their_stat_ids_from_the_hashes_in_order()
    {
        var older = Fetch().Single(l => l.Id.EndsWith("a9", StringComparison.Ordinal));

        Assert.Equal(170m, older.ValueOf("explicit.stat_1509134228"));
        Assert.Equal(18m, older.ValueOf("explicit.stat_210067635"));
    }

    [Fact]
    public void A_fractured_line_counts_for_its_explicit_stat()
    {
        var fractured = Fetch().Single(l => l.Id.EndsWith("b0", StringComparison.Ordinal));

        Assert.Equal(17m, fractured.ValueOf("explicit.stat_210067635"));
        Assert.Null(fractured.ValueOf("implicit.stat_210067635"));
    }

    [Fact]
    public void A_listing_s_numbers_land_on_the_worksheet_s_rows()
    {
        var w = WorksheetFactory.Create(ItemReader.Read(Fixtures.Item("rare-crossbow-advanced"), Fixtures.Pack()), trade: Data.Value);
        var listing = MarketSearch.FromTradeSite(Fetch()[0], w, Data.Value);

        Assert.Equal(330.5m, listing.Stats[ComparisonWorksheet.PhysicalDpsId]);
        Assert.Equal(16m, listing.Stats[$"{RowKind.Explicit}:{ComparisonWorksheet.LocalAttackSpeed}"]);
        Assert.Equal(160m, listing.Stats[$"{RowKind.Explicit}:{ComparisonWorksheet.LocalPhysPercent}"]);
        Assert.Equal(170m, listing.Stats[$"{RowKind.Explicit}:# to Accuracy Rating"]);
        Assert.False(listing.Stats.ContainsKey($"{RowKind.Explicit}:#% increased Critical Hit Chance"));
        Assert.True(listing.BuyNow);
        Assert.Equal(82, listing.ItemLevel);
        Assert.True(listing.HasListedAt);
        Assert.Equal(TimeSpan.FromMinutes(30), Fixtures.DemoNow - listing.ListedAt);
    }

    private static CurrencyConverter Converter() => new(Fixtures.DemoBook(), StalenessPolicy.Hourly);

    private static Listing L(string id, decimal amount, string currency = "divine", bool buyNow = true) =>
        new(id, amount, currency, new Dictionary<string, decimal>(), 82, buyNow, Fixtures.DemoNow.AddHours(-2));

    [Fact]
    public void The_price_runs_from_the_second_cheapest_instant_buyout_to_the_middle_one()
    {
        var cheapest = Enumerable.Range(1, 10).Select(i => L($"l{i}", i)).Reverse();

        var r = MarketSearch.Summarize(cheapest, 1605, Converter(), "divine", Fixtures.DemoNow);

        Assert.Equal(SearchConfidence.Good, r.Confidence);
        Assert.Equal((2m, 5.5m), (r.Low!.Value, r.High!.Value));
        Assert.Equal(1605, r.Count); // the site's count, not the ten it sent
        Assert.Equal(["l1", "l2", "l3"], r.Matches.Take(3).Select(m => m.Listing.Id));
        Assert.Equal("l1", r.Cheapest!.Listing.Id);
    }

    [Fact]
    public void An_ask_without_instant_buyout_at_the_bottom_never_sets_the_price()
    {
        List<Listing> cheapest = [L("unsure", 0.2m, buyNow: false), L("a", 2), L("b", 3), L("c", 4), L("d", 5)];

        var r = MarketSearch.Summarize(cheapest, 5, Converter(), "divine", Fixtures.DemoNow);

        Assert.Equal("unsure", r.Matches[0].Listing.Id); // shown, cheapest first
        Assert.Equal("a", r.Cheapest!.Listing.Id);
        Assert.Equal(SearchConfidence.Rough, r.Confidence);
        Assert.Equal(4, r.PricedCount);
        Assert.Equal((3m, 3.5m), (r.Low!.Value, r.High!.Value));
    }

    [Fact]
    public void One_or_two_instant_buyout_prices_are_too_few_and_show_the_cheapest()
    {
        var r = MarketSearch.Summarize([L("a", 3), L("b", 2), L("c", 0.5m, buyNow: false)], 3, Converter(), "divine", Fixtures.DemoNow);

        Assert.Equal(SearchConfidence.TooFew, r.Confidence);
        Assert.Equal((2m, 2m), (r.Low!.Value, r.High!.Value));
    }

    [Fact]
    public void Listings_none_of_which_can_be_bought_at_once_set_no_price()
    {
        var r = MarketSearch.Summarize([L("a", 3, buyNow: false), L("b", 2, buyNow: false)], 40, Converter(), "divine", Fixtures.DemoNow);

        Assert.Equal(SearchConfidence.NoBuyout, r.Confidence);
        Assert.Null(r.Low);
        Assert.Null(r.Cheapest);
        Assert.Equal(40, r.Count);
    }

    [Fact]
    public void Nothing_found_is_no_matches()
    {
        var r = MarketSearch.Summarize([], 0, Converter(), "divine", Fixtures.DemoNow);

        Assert.Equal(SearchConfidence.NoMatches, r.Confidence);
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void Asks_in_other_currencies_convert_with_the_session_s_rates_and_one_without_a_rate_is_left_out()
    {
        var rate = Converter().Convert(1m, "divine", "exalted", Fixtures.DemoNow);
        Assert.True(rate.Ok);
        List<Listing> cheapest = [L("ex", rate.Value / 2, "exalted"), L("div", 1), L("odd", 1, "made-up-orb"), L("div2", 2), L("div3", 3)];

        var r = MarketSearch.Summarize(cheapest, 5, Converter(), "divine", Fixtures.DemoNow);

        Assert.Equal(0.5m, Math.Round(r.Matches[0].Value, 6));
        Assert.Equal(1, r.ExcludedNoRate);
        Assert.DoesNotContain(r.Matches, m => m.Listing.Id == "odd");
    }

    [Fact]
    public void The_demo_search_leaves_in_person_asks_out_of_the_price_too()
    {
        var w = WorksheetFactory.Create(ItemReader.Read(Fixtures.Item("rare-crossbow-advanced"), Fixtures.Pack()));
        w.BuyNowOnly = false;
        var stats = new Dictionary<string, decimal> { [ComparisonWorksheet.PhysicalDpsId] = 720, [$"{RowKind.Explicit}:{ComparisonWorksheet.LocalAttackSpeed}"] = 16 };
        List<Listing> listings =
        [
            new("unsure", 0.5m, "divine", stats, 82, false, Fixtures.DemoNow),
            new("a", 2, "divine", stats, 82, true, Fixtures.DemoNow),
            new("b", 3, "divine", stats, 82, true, Fixtures.DemoNow),
            new("c", 4, "divine", stats, 82, true, Fixtures.DemoNow),
        ];

        var r = MarketSearch.Run(w, listings, Converter(), "divine", Fixtures.DemoNow);

        Assert.Equal(4, r.Count);
        Assert.Equal("a", r.Cheapest!.Listing.Id);
        Assert.True(r.Low >= 2, $"{r.Low}");
    }

    // The trade site's limits as it sent them on 27 Nov 2026 (values in seconds).
    private static readonly (string, string)[] SearchLimits = MarketTest.Limits("trade-search-request-limit", "5:10:60,15:60:300,30:300:1800", "1:10:0,1:60:0,1:300:0");
    private static readonly (string, string)[] FetchLimits = MarketTest.Limits("trade-fetch-request-limit", "12:4:10,16:12:300,50:300:300", "1:4:0,1:12:0,1:300:0");

    /// <summary>No gap between requests to a route whose limits are not known yet (the first search and
    /// fetch); the app's own gap is 5 s, which its handler waits out.</summary>
    private static readonly GovernorOptions Options = MarketTest.Options with { ConservativeInterval = TimeSpan.Zero };

    private static FakeProvider Site() => new();

    private static FakeProvider AnswerOnce(FakeProvider site) => site
        .Reply(HttpStatusCode.OK, Fixtures.Text("trade/search.json"), SearchLimits)
        .Reply(HttpStatusCode.OK, Fixtures.Text("trade/fetch.json"), FetchLimits);

    private const string Query = """{"query":{"status":{"option":"securable"},"type":"Siege Crossbow"}}""";

    [Fact]
    public async Task A_search_is_one_post_and_one_fetch_on_their_own_routes_and_the_governor_learns_both_limits()
    {
        var clock = MarketTest.Clock();
        var governor = new ProviderGovernor(clock, Options);
        var site = AnswerOnce(Site());
        using var http = MarketTest.Client(governor, site);

        var page = await new TradeClient(http).SearchAsync("Standard", Query, instantBuyoutOnly: true);

        Assert.Equal(137, page.Total);
        Assert.Equal(8, page.Listings.Count);
        Assert.Equal(2, site.Requests.Count);
        Assert.Equal(HttpMethod.Post, site.Requests[0].Method);
        Assert.Equal("https://www.pathofexile.com/api/trade2/search/poe2/Standard", site.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("application/json", site.Requests[0].Content!.Headers.ContentType!.ToString());
        Assert.Equal(HttpMethod.Get, site.Requests[1].Method);
        Assert.StartsWith($"https://www.pathofexile.com/api/trade2/fetch/{First},", site.Requests[1].RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith("?query=SyntheticQuery0001-made_up&realm=poe2", site.Requests[1].RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(10, site.Requests[1].RequestUri!.AbsolutePath.Split('/')[^1].Split(',').Length);
        Assert.Equal(new RouteKey(TradeApi.Integration, TradeApi.SearchRoute), MarketRequest.RouteOf(site.Requests[0]));
        Assert.Equal(new RouteKey(TradeApi.Integration, TradeApi.FetchRoute), MarketRequest.RouteOf(site.Requests[1]));

        var routes = governor.Snapshot().Routes.ToDictionary(r => r.Route, r => r.Policy);
        Assert.Equal("trade-search-request-limit", routes[TradeApi.SearchRoute]);
        Assert.Equal("trade-fetch-request-limit", routes[TradeApi.FetchRoute]);
    }

    [Fact]
    public async Task A_429_holds_the_next_search_without_a_request_and_says_how_long()
    {
        var clock = MarketTest.Clock();
        var governor = new ProviderGovernor(clock, Options);
        var site = Site().Reply(HttpStatusCode.TooManyRequests, """{"error":{"code":3,"message":"Rate limit exceeded"}}""",
            [.. MarketTest.Limits("trade-search-request-limit", "5:10:60,15:60:300,30:300:1800", "6:10:60,6:60:0,6:300:0"), ("Retry-After", "60")]);
        using var http = MarketTest.Client(governor, site);
        var client = new TradeClient(http);

        var refused = await Assert.ThrowsAsync<TradeSiteException>(() => client.SearchAsync("Standard", Query, true));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), refused.RetryAfter);
        Assert.Equal("Rate limit exceeded", refused.SiteMessage);

        clock.Advance(TimeSpan.FromSeconds(20));
        var held = await Assert.ThrowsAsync<ProviderHoldException>(() => client.SearchAsync("Standard", Query, true));

        Assert.Single(site.Requests); // nothing reached the site while it asked to wait
        Assert.Equal(HoldReason.CoolingDown, held.Decision.Reason);
        Assert.Equal("The trade site asks to wait 43 s.", TradeClient.HoldText(held.Decision, clock.GetUtcNow())); // 60 s + 3 s jitter, 20 s gone
    }

    [Fact]
    public async Task Headroom_is_kept_the_fifth_search_in_ten_seconds_waits()
    {
        var clock = MarketTest.Clock();
        var governor = new ProviderGovernor(clock, Options);
        var site = Site();
        for (var i = 1; i <= 4; i++)
        {
            site.Reply(HttpStatusCode.OK, Fixtures.Text("trade/search.json"), MarketTest.Limits("trade-search-request-limit", "5:10:60,15:60:300,30:300:1800", $"{i}:10:0,{i}:60:0,{i}:300:0"))
                .Reply(HttpStatusCode.OK, Fixtures.Text("trade/fetch.json"), MarketTest.Limits("trade-fetch-request-limit", "12:4:10,16:12:300,50:300:300", $"{i}:4:0,{i}:12:0,{i}:300:0"));
        }

        using var http = MarketTest.Client(governor, site);
        var client = new TradeClient(http);
        for (var i = 0; i < 4; i++)
        {
            await client.SearchAsync("Standard", Query, true);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        var held = await Assert.ThrowsAsync<ProviderHoldException>(() => client.SearchAsync("Standard", Query, true));

        Assert.Equal(8, site.Requests.Count);
        Assert.Equal(HoldReason.RateWindow, held.Decision.Reason);
        Assert.StartsWith("The trade site asks to wait ", TradeClient.HoldText(held.Decision, clock.GetUtcNow()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.2, "1 s")]
    [InlineData(12, "12 s")]
    [InlineData(99, "99 s")]
    [InlineData(100, "2 min")]
    [InlineData(1800, "30 min")]
    [InlineData(7200, "2 h")]
    public void Waits_read_in_seconds_minutes_or_hours_rounded_up(double seconds, string expected) =>
        Assert.Equal(expected, TradeClient.Wait(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Other_holds_say_what_happened()
    {
        var now = Fixtures.DemoNow;
        Assert.StartsWith("The trade site refused SSE's requests", TradeClient.HoldText(new SendDecision(HoldReason.Disabled, null, ""), now), StringComparison.Ordinal);
        Assert.Equal("The trade site turned down several searches in a row. Try again in 5 min.", TradeClient.HoldText(new SendDecision(HoldReason.CircuitOpen, now.AddMinutes(5), ""), now));
        Assert.Equal("The trade site had a problem or could not be reached. Try again in 10 s.", TradeClient.HoldText(new SendDecision(HoldReason.ServerBackoff, now.AddSeconds(10), ""), now));
    }
}
