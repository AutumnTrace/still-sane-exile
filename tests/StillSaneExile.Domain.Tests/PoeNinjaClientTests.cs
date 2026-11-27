using System.Net;
using StillSaneExile.Domain.Market;
using StillSaneExile.Domain.Pricing;
using static StillSaneExile.Domain.Tests.MarketTest;

namespace StillSaneExile.Domain.Tests;

/// <summary>The poe.ninja client behind the polite handler, against a fake provider: no test touches the network.</summary>
public class PoeNinjaClientTests
{
    private const string League = "Runes of Aldur";

    private static readonly PoeNinjaCatalog Three = new(
    [
        new PoeNinjaCategory(PoeNinjaOverview.Exchange, "Currency"),
        new PoeNinjaCategory(PoeNinjaOverview.Exchange, "Runes"),
        new PoeNinjaCategory(PoeNinjaOverview.Items, "UniqueAccessories"),
    ]);

    private sealed class Rig
    {
        public Rig(TimeSpan? spacing = null)
        {
            Governor = new ProviderGovernor(Clock, Options with { ConservativeInterval = spacing ?? TimeSpan.Zero });
            Ninja = new PoeNinjaClient(MarketTest.Client(Governor, Provider, Cache));
        }

        public ManualClock Clock { get; } = MarketTest.Clock();

        public FakeProvider Provider { get; } = new();

        public HttpCache Cache { get; } = new();

        public ProviderGovernor Governor { get; }

        public PoeNinjaClient Ninja { get; }

        public Rig Answer(string fixture, params (string, string)[] headers)
        {
            Provider.Reply(HttpStatusCode.OK, Fixtures.Text($"poeninja/{fixture}.json"), headers);
            return this;
        }

        public Task<PoeNinjaRefresh> Refresh(PoeNinjaCatalog? catalog = null) => Ninja.RefreshAsync(League, catalog ?? Three, Clock);
    }

    [Fact]
    public async Task A_refresh_asks_for_each_category_once_on_tagged_routes()
    {
        var rig = new Rig().Answer("exchange-currency").Answer("exchange-runes").Answer("items-unique-accessories");

        var refresh = await rig.Refresh();

        Assert.Equal(
        [
            "https://poe.ninja/poe2/api/economy/exchange/current/overview?league=Runes+of+Aldur&type=Currency",
            "https://poe.ninja/poe2/api/economy/exchange/current/overview?league=Runes+of+Aldur&type=Runes",
            "https://poe.ninja/poe2/api/economy/stash/current/item/overview?league=Runes+of+Aldur&type=UniqueAccessories",
        ], rig.Provider.Requests.Select(r => r.RequestUri!.AbsoluteUri));
        Assert.Equal(
        [
            new RouteKey("poe.ninja", PoeNinjaClient.ExchangeRoute),
            new RouteKey("poe.ninja", PoeNinjaClient.ExchangeRoute),
            new RouteKey("poe.ninja", PoeNinjaClient.ItemsRoute),
        ], rig.Provider.Requests.Select(MarketRequest.RouteOf));
        Assert.All(rig.Provider.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.Equal(3, refresh.NetworkRequests);
        Assert.Equal(League, refresh.Book!.League);
        Assert.NotNull(refresh.Book.Find("alch"));
        Assert.NotNull(refresh.Book.Find("unique.mageblood-utility-belt"));
    }

    [Fact]
    public async Task Without_rate_limit_headers_poe_ninja_gets_one_request_at_a_time_five_seconds_apart()
    {
        var rig = new Rig(spacing: TimeSpan.FromSeconds(5)).Answer("exchange-currency");

        await rig.Refresh(new PoeNinjaCatalog([Three.Categories[0]]));
        var next = rig.Governor.Check(new RouteKey("poe.ninja", PoeNinjaClient.ItemsRoute));

        Assert.Equal(HoldReason.Conservative, next.Reason);
        Assert.Equal(rig.Clock.GetUtcNow() + TimeSpan.FromSeconds(5), next.RetryAt);
    }

    [Fact]
    public async Task An_unknown_category_is_skipped_and_the_rest_still_load()
    {
        var rig = new Rig().Answer("exchange-currency");
        rig.Provider.Reply(HttpStatusCode.NotFound, "Not found");
        rig.Answer("items-unique-accessories");

        var refresh = await rig.Refresh(new PoeNinjaCatalog(
        [
            Three.Categories[0],
            new PoeNinjaCategory(PoeNinjaOverview.Items, "UniqueRelics"),
            Three.Categories[2],
        ]));

        Assert.Contains("UniqueRelics: poe.ninja answered 404, so it was skipped.", refresh.Notes);
        Assert.NotNull(refresh.Book!.Find("unique.mageblood-utility-belt"));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_server_error_or_a_429_ends_the_refresh_with_no_book(HttpStatusCode status)
    {
        var rig = new Rig().Answer("exchange-currency");
        rig.Provider.Reply(status, "slow down");

        await Assert.ThrowsAsync<HttpRequestException>(() => rig.Refresh());

        Assert.Equal(2, rig.Provider.Requests.Count); // the third category was never asked for
    }

    [Fact]
    public async Task While_poe_ninja_is_held_nothing_is_sent_and_the_hold_says_why()
    {
        var rig = new Rig();
        rig.Provider.Reply(HttpStatusCode.TooManyRequests, "", ("Retry-After", "120"));
        await Assert.ThrowsAsync<HttpRequestException>(() => rig.Refresh());

        var held = await Assert.ThrowsAsync<ProviderHoldException>(() => rig.Refresh());

        Assert.Single(rig.Provider.Requests);
        Assert.Equal(HoldReason.CoolingDown, held.Decision.Reason);
        Assert.Equal(rig.Clock.GetUtcNow() + TimeSpan.FromSeconds(120) + Jitter, held.Decision.RetryAt);
    }

    [Fact]
    public async Task Cache_headers_are_honoured_between_refreshes()
    {
        var rig = new Rig()
            .Answer("exchange-currency", ("Cache-Control", "public, max-age=1800"))
            .Answer("items-unique-accessories", ("Cache-Control", "no-cache"), ("ETag", "\"u1\""));
        var two = new PoeNinjaCatalog([Three.Categories[0], Three.Categories[2]]);
        await rig.Refresh(two);

        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        rig.Provider.Reply(HttpStatusCode.NotModified, "", ("ETag", "\"u1\""));
        var again = await rig.Refresh(two);

        Assert.Equal(3, rig.Provider.Requests.Count); // the exchange answer was still fresh
        Assert.Equal(1, again.CachedAnswers);
        Assert.Equal("\"u1\"", rig.Provider.Requests[2].Headers.IfNoneMatch.Single().Tag);
        Assert.NotNull(again.Book!.Find("unique.mageblood-utility-belt")); // the 304 reused the stored body
    }

    [Fact]
    public async Task A_league_with_no_prices_at_all_gives_no_book()
    {
        var rig = new Rig().Answer("exchange-empty").Answer("exchange-empty").Answer("exchange-empty");

        var refresh = await rig.Refresh();

        Assert.Null(refresh.Book);
        Assert.Contains(refresh.Notes, n => n.Contains("no prices", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Leagues_come_from_index_state()
    {
        var rig = new Rig().Answer("index-state");

        var leagues = await rig.Ninja.LeaguesAsync();

        Assert.Equal("https://poe.ninja/poe2/api/data/index-state", rig.Provider.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal(new RouteKey("poe.ninja", PoeNinjaClient.LeaguesRoute), MarketRequest.RouteOf(rig.Provider.Requests[0]));
        Assert.Equal(4, leagues.Count);
    }

    [Fact]
    public void The_user_agent_names_the_app_and_where_to_find_it_and_nothing_else()
    {
        Assert.Equal("StillSaneExile/1.2.3 (+https://github.com/AutumnTrace/still-sane-exile)", PoeNinjaClient.UserAgent("1.2.3"));
    }
}
