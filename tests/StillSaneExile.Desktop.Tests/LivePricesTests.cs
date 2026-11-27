using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Web;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Market;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Tests;

/// <summary>Live prices from poe.ninja end to end, against <see cref="FakeNinja"/>: nothing touches the network.</summary>
public class LivePricesTests
{
    private const string League = "Runes of Aldur";
    private const string Other = "HC Runes of Aldur";

    private const string Mageblood = "Item Class: Belts\nRarity: Unique\nMageblood\nUtility Belt\n--------\nItem Level: 80\n--------\n+30 to Strength\n";
    private const string Divine = "Item Class: Stackable Currency\nRarity: Currency\nDivine Orb\n--------\nStack Size: 1/20\n--------\nSynthetic description\n";

    private static TestApp LiveApp(string league = League, bool livePrices = true) => new(s =>
    {
        s.LiveLeague = league;
        s.Profile = ProfileKind.Live;
        s.LivePrices = livePrices;
    });

    private static void Refresh(TestApp app, LivePrices prices)
    {
        var before = app.Shell.Session;
        prices.RefreshNow();
        Sta.Until(() => !prices.IsRefreshing, "the refresh to finish");
        Sta.Until(() => !ReferenceEquals(app.Shell.Session, before) || prices.Problem is not null || prices.Hold is not null, "the new book");
    }

    [Fact]
    public void A_live_profile_shows_exchange_estimates_and_unique_baselines_with_their_age() => Sta.Run(() =>
    {
        using var app = LiveApp();
        var ninja = new FakeNinja();
        using var prices = LivePricesRig.For(app, ninja);
        app.Shell.Prices = prices;

        Assert.Equal(League, prices.League);
        Assert.Empty(ninja.Requests); // nothing until the schedule says so
        Refresh(app, prices);

        var book = app.Shell.Session.Book;
        Assert.Equal(League, book.League);
        Assert.False(book.IsSynthetic);
        Assert.Equal(3, ninja.For(League));
        Assert.Single(new PriceBookArchive(app.Paths.Prices).Files(League));

        var unique = app.Shell.Inspect.AddFromText(Mageblood, "test")!;
        Assert.Equal("≈ 180 div baseline · check rolls", unique.Answer.Big);
        Assert.StartsWith("poe.ninja category baseline, just now · 40 listed.", unique.Answer.Note, StringComparison.Ordinal);
        Assert.Equal("Typical price · rolls can change it · just now", unique.Answer.PeekNote);

        var divine = app.Shell.Inspect.AddFromText(Divine, "test")!;
        Assert.Equal("1 div", divine.Answer.Big);
        Assert.StartsWith("1 div = 400 ex · checked just now", divine.Answer.Note, StringComparison.Ordinal);

        Assert.StartsWith("Prices from poe.ninja, fetched ", app.Shell.FooterStatus, StringComparison.Ordinal);
        Assert.StartsWith("Last refresh ", app.Shell.SettingsPage.LiveStatus, StringComparison.Ordinal);
        Assert.Contains("Next refresh about", app.Shell.SettingsPage.LiveStatus, StringComparison.Ordinal);
        Assert.InRange(prices.NextRefresh!.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));
    });

    [Fact]
    public void A_rare_in_a_live_league_still_has_no_price_and_points_to_the_trade_site() => Sta.Run(() =>
    {
        using var app = LiveApp();
        using var prices = LivePricesRig.For(app, new FakeNinja());
        app.Shell.Prices = prices;
        Refresh(app, prices);

        var rare = app.Shell.Inspect.AddFromText(app.ItemText("rare-crossbow-advanced.txt"), "test")!;

        Assert.False(rare.HasListings);
        Assert.Equal("No price yet", rare.Answer.Big);
        Assert.Equal("Open the search on the trade site, then write down what you find.", rare.Answer.Note);
        Assert.Equal("No price yet · compare on trade", rare.Answer.PeekNote);
    });

    [Fact]
    public void Switching_league_mid_refresh_never_shows_the_previous_league_s_prices() => Sta.Run(() =>
    {
        using var app = LiveApp();
        var ninja = new FakeNinja { Holding = true };
        using var prices = LivePricesRig.For(app, ninja);
        var offered = new List<string>();
        prices.BookReady += b => offered.Add(b.League);
        app.Shell.Prices = prices;

        prices.RefreshNow();
        Sta.Until(() => ninja.Waiting > 0, "the first request to be in flight");
        app.Shell.SettingsPage.LeagueDraft = Other;
        app.Shell.SettingsPage.ConfirmLeagueCommand.Execute(null);
        ninja.Holding = false;
        ninja.Release(); // the old league's answer arrives after the switch
        Sta.Until(() => ninja.Answered > 0, "the late answer");
        for (var i = 0; i < 20; i++)
        {
            Sta.Drain();
            Thread.Sleep(5);
        }

        Assert.Equal(Other, app.Shell.Session.League);
        Assert.Empty(app.Shell.Session.Book.Quotes);
        Assert.Empty(offered);
        Assert.Equal(1, ninja.For(League)); // the refresh in flight stopped at the switch
        Assert.Empty(new PriceBookArchive(app.Paths.Prices).Files(League));
        Assert.Equal(Other, prices.League);

        Refresh(app, prices);

        Assert.Equal([Other], offered);
        Assert.Equal(Other, app.Shell.Session.Book.League);
        Assert.All(app.Shell.Session.Book.Quotes, q => Assert.Equal(Other, q.League));
    });

    [Fact]
    public void A_late_book_for_a_league_the_player_left_is_dropped() => Sta.Run(() =>
    {
        using var app = LiveApp(Other);
        var before = app.Shell.Session;

        Assert.False(app.Shell.ApplyLiveBook(NinjaFixtures.Book(League, DateTimeOffset.UtcNow)));
        Assert.Same(before, app.Shell.Session);

        Assert.True(app.Shell.ApplyLiveBook(NinjaFixtures.Book(Other, DateTimeOffset.UtcNow)));
        Assert.Equal(Other, app.Shell.Session.Book.League);
    });

    [Fact]
    public void Nothing_is_fetched_for_the_demo_or_with_live_prices_off() => Sta.Run(() =>
    {
        var ninja = new FakeNinja();
        using (var demo = new TestApp())
        {
            using var prices = LivePricesRig.For(demo, ninja);
            demo.Shell.Prices = prices;
            prices.RefreshNow();
            Sta.Drain();

            Assert.Null(prices.League);
            Assert.Null(prices.NextRefresh);
            Assert.Equal("Starts once you confirm a live league under League and prices.", demo.Shell.SettingsPage.LiveStatus);
        }

        using (var off = LiveApp(livePrices: false))
        {
            using var prices = LivePricesRig.For(off, ninja);
            off.Shell.Prices = prices;
            prices.RefreshNow();
            Sta.Drain();

            Assert.Null(prices.League);
            Assert.Equal("Live prices are off: switch them on in Settings", off.Shell.FooterStatus);
            Assert.Equal("Off: SSE asks poe.ninja for nothing.", off.Shell.SettingsPage.LiveStatus);
        }

        Assert.Empty(ninja.Requests);
    });

    [Fact]
    public void Turning_live_prices_off_stops_fetching_and_hides_poe_ninja_prices_until_turned_back_on() => Sta.Run(() =>
    {
        using var app = LiveApp();
        using var prices = LivePricesRig.For(app, new FakeNinja());
        app.Shell.Prices = prices;
        Refresh(app, prices);
        var snapshot = app.Shell.Session.Book.SnapshotId;

        app.Shell.SettingsPage.LivePricesOn = false;

        Assert.False(app.Settings.LivePrices);
        Assert.Null(prices.League);
        Assert.Null(prices.NextRefresh);
        Assert.Empty(app.Shell.Session.Book.Quotes);

        app.Shell.SettingsPage.LivePricesOn = true;

        Assert.Equal(League, prices.League);
        Assert.Equal(snapshot, app.Shell.Session.Book.SnapshotId); // back from the snapshot on disk, no request needed
    });

    [Fact]
    public void At_start_the_newest_snapshot_loads_offline_and_a_fresh_one_is_not_fetched_again_within_the_hour() => Sta.Run(() =>
    {
        using var app = LiveApp();
        var saved = NinjaFixtures.Book(League, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));
        new PriceBookArchive(app.Paths.Prices).Save(saved);
        new PriceBookArchive(app.Paths.Prices).Save(NinjaFixtures.Book(Other, DateTimeOffset.UtcNow));
        app.Shell.ReloadSession();

        Assert.Equal(saved.SnapshotId, app.Shell.Session.Book.SnapshotId);
        Assert.Equal(League, app.Shell.Session.Book.League);

        using var prices = LivePricesRig.For(app, new FakeNinja(), startDelay: TimeSpan.FromSeconds(3));
        app.Shell.Prices = prices;

        Assert.Equal(saved.CreatedAt + LivePrices.Cadence, prices.NextRefresh);
        Assert.Equal(saved.CreatedAt, prices.LastRefresh);
    });

    [Fact]
    public void Without_a_snapshot_the_first_refresh_comes_a_few_seconds_after_start() => Sta.Run(() =>
    {
        using var app = LiveApp();
        using var prices = LivePricesRig.For(app, new FakeNinja(), startDelay: TimeSpan.FromMinutes(30));
        var before = DateTimeOffset.UtcNow;
        app.Shell.Prices = prices;

        Assert.InRange(prices.NextRefresh!.Value, before + TimeSpan.FromMinutes(30), DateTimeOffset.UtcNow + TimeSpan.FromMinutes(30));
        Assert.Equal("No prices for Runes of Aldur yet. Next refresh about " + Session.LocalTime(prices.NextRefresh.Value, DateTimeOffset.UtcNow) + ".",
            app.Shell.SettingsPage.LiveStatus);
    });

    [Fact]
    public void A_hold_is_reported_in_plain_words_and_the_prices_already_shown_stay() => Sta.Run(() =>
    {
        using var app = LiveApp();
        var saved = NinjaFixtures.Book(League, DateTimeOffset.UtcNow - TimeSpan.FromHours(3));
        new PriceBookArchive(app.Paths.Prices).Save(saved);
        app.Shell.ReloadSession();
        var ninja = new FakeNinja { FailWith = HttpStatusCode.TooManyRequests, FailHeaders = [("Retry-After", "1200")] };
        using var prices = LivePricesRig.For(app, ninja);
        app.Shell.Prices = prices;

        prices.RefreshNow();
        Sta.Until(() => !prices.IsRefreshing, "the refresh to fail");

        Assert.Single(ninja.Requests); // a 429 stops the refresh at once
        Assert.StartsWith("poe.ninja asked SSE to slow down, so SSE waits until ", prices.Hold, StringComparison.Ordinal);
        Assert.Equal(prices.Hold, app.Shell.SettingsPage.LiveHold);
        Assert.Contains("poe.ninja answered 429", app.Shell.SettingsPage.LiveProblem, StringComparison.Ordinal);
        Assert.Equal(saved.SnapshotId, app.Shell.Session.Book.SnapshotId);
        Assert.True(prices.NextRefresh >= DateTimeOffset.UtcNow + TimeSpan.FromMinutes(19));

        var unique = app.Shell.Inspect.AddFromText(Mageblood, "test")!;
        Assert.Contains("3 h ago · 40 listed · stale.", unique.Answer.Note, StringComparison.Ordinal);
        Assert.EndsWith("· 3 h ago · may be out of date", unique.Answer.PeekNote, StringComparison.Ordinal);
    });

    [Fact]
    public void Offline_the_last_prices_stay_and_the_next_try_waits() => Sta.Run(() =>
    {
        using var app = LiveApp();
        using var prices = LivePricesRig.For(app, new FakeNinja { Offline = true });
        app.Shell.Prices = prices;

        prices.RefreshNow();
        Sta.Until(() => prices.Problem is not null, "the failure");

        Assert.StartsWith("Could not reach poe.ninja", prices.Problem, StringComparison.Ordinal);
        Assert.True(prices.NextRefresh >= DateTimeOffset.UtcNow + LivePrices.RetryAfterFailure - TimeSpan.FromSeconds(30));
        Assert.Empty(app.Shell.Session.Book.Quotes);
        Assert.Equal("No prices yet: they load from poe.ninja (see Settings)", app.Shell.FooterStatus);
    });

    [Fact]
    public void Requests_name_the_app_and_carry_nothing_about_the_player() => Sta.Run(() =>
    {
        using var app = LiveApp();
        var ninja = new FakeNinja();
        using var prices = LivePricesRig.For(app, ninja);
        app.Shell.Prices = prices;
        Refresh(app, prices);

        Assert.All(ninja.Requests, r =>
        {
            Assert.Equal("https", r.RequestUri!.Scheme);
            Assert.Equal("poe.ninja", r.RequestUri.Host);
            Assert.Equal($"StillSaneExile/{LivePricesRig.Version} (+https://github.com/AutumnTrace/still-sane-exile)", string.Join(" ", r.Headers.GetValues("User-Agent")));
            Assert.False(r.Headers.Contains("Cookie"));
            Assert.False(r.Headers.Contains("Authorization"));
            Assert.Equal<string?>(["league", "type"], HttpUtility.ParseQueryString(r.RequestUri.Query).AllKeys.AsEnumerable());
        });
    });

    [Fact]
    public void The_HUD_has_no_way_to_reach_the_network()
    {
        var reachable = new[]
        {
            typeof(HttpClient), typeof(HttpMessageHandler), typeof(LivePrices), typeof(PoeNinjaClient), typeof(ProviderGovernor),
            typeof(LiveListings), typeof(ITradeSite), typeof(Domain.Trade.TradeClient),
        };
        var fields = typeof(HudController).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.DoesNotContain(fields, f => reachable.Any(t => t.IsAssignableFrom(f.FieldType)));
    }
}
