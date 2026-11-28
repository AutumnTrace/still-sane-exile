using System.IO;
using System.Net;
using System.Net.Http;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Domain.Market;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Desktop.Tests;

/// <summary>The trade-site-shaped answers in the repository's <c>fixtures/trade/</c>: synthetic values in
/// the shapes the site sent on 27 Nov 2026. They never ship with the app.</summary>
internal static class TradeFixtures
{
    private static readonly Lazy<string> Root = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "fixtures", "trade")))
        {
            dir = dir.Parent;
        }

        return dir is null ? throw new DirectoryNotFoundException("fixtures/trade not found above the test output.") : Path.Combine(dir.FullName, "fixtures", "trade");
    });

    public static string Text(string name) => File.ReadAllText(Path.Combine(Root.Value, $"{name}.json"));
}

/// <summary>Stands in for the trade site on the network: answers the search and the fetch from the
/// fixtures with the site's rate-limit headers (and the session cookie it hands out), and records every
/// request with its body. With <see cref="FailWith"/> set, it answers that instead.</summary>
internal sealed class FakeTradeSite : HttpMessageHandler
{
    private readonly Lock _gate = new();

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public HttpStatusCode? FailWith { get; set; }

    public (string Name, string Value)[] FailHeaders { get; set; } = [];

    public int Searches
    {
        get
        {
            lock (_gate)
            {
                return Requests.Count(r => r.Request.Method == HttpMethod.Post);
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            Requests.Add((request, body));
        }

        if (FailWith is { } status)
        {
            var failed = new HttpResponseMessage(status) { Content = new StringContent("""{"error":{"code":3,"message":"Rate limit exceeded"}}"""), RequestMessage = request };
            foreach (var (name, value) in FailHeaders)
            {
                failed.Headers.TryAddWithoutValidation(name, value);
            }

            return failed;
        }

        var search = request.Method == HttpMethod.Post;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(TradeFixtures.Text(search ? "search" : "fetch")),
            RequestMessage = request,
        };
        foreach (var (name, value) in search
            ? Limits("trade-search-request-limit", "5:10:60,15:60:300,30:300:1800", "1:10:0,1:60:0,1:300:0")
            : Limits("trade-fetch-request-limit", "12:4:10,16:12:300,50:300:300", "1:4:0,1:12:0,1:300:0"))
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        response.Headers.TryAddWithoutValidation("Set-Cookie", "POESESSID=synthetic-not-a-session; path=/; secure; HttpOnly");
        return response;
    }

    public static (string, string)[] Limits(string policy, string limits, string state) =>
    [
        ("X-Rate-Limit-Policy", policy),
        ("X-Rate-Limit-Rules", "Ip"),
        ("X-Rate-Limit-Ip", limits),
        ("X-Rate-Limit-Ip-State", state),
    ];
}

internal static class TradeRig
{
    public const string League = "Runes of Aldur";

    public static void Live(Settings s)
    {
        s.LiveLeague = League;
        s.Profile = ProfileKind.Live;
    }

    /// <summary>The app's trade search against <paramref name="site"/>: the real handler chain and a governor
    /// of its own, with no gap between requests to routes whose limits are not known yet.</summary>
    public static LiveListings Listings(FakeTradeSite site)
    {
        var governor = new ProviderGovernor(TimeProvider.System, new GovernorOptions { ConservativeInterval = TimeSpan.Zero, Jitter = () => TimeSpan.Zero });
        var http = LiveListings.HttpFor(governor, site, LivePricesRig.Version);
        return new LiveListings(new TradeClient(http), governor, http);
    }

    /// <summary>poe.ninja prices for the league (1 div = 400 ex), so exalted asks convert.</summary>
    public static void WithPrices(TestApp app)
    {
        new PriceBookArchive(app.Paths.Prices).Save(NinjaFixtures.Book(League, DateTimeOffset.UtcNow));
        app.Shell.ReloadSession();
    }

    /// <summary>Clicks Search and waits for the answer to land on the UI thread.</summary>
    public static void Search(ItemViewModel item)
    {
        item.SearchCommand.Execute(null);
        Sta.Until(() => !item.IsSearching, "the search");
        Sta.Drain();
    }
}

/// <summary>Search is explicit: nothing changes the price until the player clicks it, and only that click
/// asks the trade site. Against <see cref="FakeTradeSite"/>: nothing touches the network.</summary>
public class TradeSearchTests
{
    private const string Crossbow = "rare-crossbow-advanced.txt";
    private const string PressSearch = "Press Search to see listings";
    private static readonly Hotkey CtrlC = Hotkey.Parse("Ctrl+C")!;

    // Hand-written in the shape of a game copy; not one of the practice items.
    private const string GameBoots = "Item Class: Boots\nRarity: Rare\nGale Stride\nLeather Boots\n--------\nItem Level: 80\n--------\n25% increased Movement Speed\n+60 to maximum Life";

    [Fact]
    public void A_live_rare_waits_for_Search_and_changing_the_search_sends_nothing() => Sta.Run(() =>
    {
        Sta.UseDispatcherContext();
        using var app = new TestApp(TradeRig.Live);
        var site = new FakeTradeSite();
        using var listings = TradeRig.Listings(site);
        app.Shell.Listings = listings;

        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "hud")!;

        Assert.True(item.HasListings);
        Assert.True(item.CanSearch);
        Assert.Equal("No price yet", item.Answer.Big);
        Assert.Equal(PressSearch, item.PeekLine);
        Assert.Empty(item.Matches);

        item.SearchRows.First(r => !r.IsOn).IsOn = true;
        item.SearchRows.First(r => r.IsOn).IncreaseCommand.Execute(null);
        item.BuyNowOnly = false;
        item.SetStrictnessCommand.Execute("20");
        item.ResetCommand.Execute(null);
        Sta.Drain();

        Assert.Empty(site.Requests);
        Assert.False(item.IsStale); // no result to be old yet
        Assert.Equal(PressSearch, item.PeekLine);
    });

    [Fact]
    public void Search_asks_the_trade_site_once_and_the_price_and_listings_follow() => Sta.Run(() =>
    {
        Sta.UseDispatcherContext();
        using var app = new TestApp(TradeRig.Live);
        TradeRig.WithPrices(app);
        var site = new FakeTradeSite();
        using var listings = TradeRig.Listings(site);
        app.Shell.Listings = listings;
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "hud")!;

        TradeRig.Search(item);

        // One search, one fetch of the ten cheapest.
        Assert.Equal(2, site.Requests.Count);
        var (search, body) = site.Requests[0];
        Assert.Equal(HttpMethod.Post, search.Method);
        Assert.Equal("https://www.pathofexile.com/api/trade2/search/poe2/Runes%20of%20Aldur", search.RequestUri!.AbsoluteUri);
        Assert.Equal(TradeApi.SearchBody(item.Trade!.Json), body);
        Assert.Contains("\"status\":{\"option\":\"securable\"}", body, StringComparison.Ordinal);
        Assert.EndsWith(",\"sort\":{\"price\":\"asc\"}}", body, StringComparison.Ordinal);
        var fetch = site.Requests[1].Request;
        Assert.Equal(HttpMethod.Get, fetch.Method);
        Assert.StartsWith("https://www.pathofexile.com/api/trade2/fetch/", fetch.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith("?query=SyntheticQuery0001-made_up&realm=poe2", fetch.RequestUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.All(site.Requests, r =>
        {
            Assert.Equal($"StillSaneExile/{LivePricesRig.Version} (+https://github.com/AutumnTrace/still-sane-exile)", string.Join(" ", r.Request.Headers.GetValues("User-Agent")));
            Assert.False(r.Request.Headers.Contains("Cookie")); // the session cookie the site hands out is never sent back
            Assert.False(r.Request.Headers.Contains("Authorization"));
        });

        // Eight priced listings, all instant buyout: 300 ex (0.75 div), 1, 1.5, 2, 2, 2.5, 3 and 4 div.
        // The price runs from the second-cheapest to the middle one; the count is the site's.
        Assert.Equal("1–2 div", item.Answer.Big);
        Assert.Equal(137, item.Answer.Count);
        Assert.Equal("Good price guess — 137 for sale.", item.Answer.Note);
        Assert.Equal("Good price guess · cheapest 300 ex", item.PeekLine);
        Assert.Equal("Cheapest 8 of 137", item.ListingsTitle);
        Assert.Equal(8, item.Matches.Count);
        var cheapest = item.Matches[0];
        Assert.Equal("300 ex", cheapest.Price);
        Assert.Equal("Phys DPS 322 · Attack speed 15", cheapest.Stats);
        Assert.True(cheapest.BuyNow);
        Assert.EndsWith(" ago", cheapest.Age, StringComparison.Ordinal);
        Assert.Equal("", item.Matches[^1].Age); // the site's time could not be read
        Assert.DoesNotContain(item.Matches, m => m.ToString().Contains("ExampleSeller", StringComparison.Ordinal));
        Assert.False(item.IsStale);
        Assert.Null(item.SearchNote);
    });

    [Fact]
    public void A_changed_search_marks_the_result_old_until_the_next_Search() => Sta.Run(() =>
    {
        Sta.UseDispatcherContext();
        using var app = new TestApp(TradeRig.Live);
        TradeRig.WithPrices(app);
        var site = new FakeTradeSite();
        using var listings = TradeRig.Listings(site);
        app.Shell.Listings = listings;
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "hud")!;
        TradeRig.Search(item);
        var answer = item.Answer;

        item.SearchRows.First(r => r.IsOn).IsOn = false;

        Assert.Same(answer, item.Answer);
        Assert.True(item.IsStale);
        Assert.Equal(ItemViewModel.StaleText, item.PeekLine);
        Assert.StartsWith(ItemViewModel.StaleText, item.NoteLine, StringComparison.Ordinal);
        Assert.Equal(2, site.Requests.Count);

        TradeRig.Search(item);

        Assert.Equal(2, site.Searches);
        Assert.False(item.IsStale);
    });

    [Fact]
    public void An_hourly_price_refresh_keeps_the_search_and_its_listings_without_asking_again() => Sta.Run(() =>
    {
        Sta.UseDispatcherContext();
        using var app = new TestApp(TradeRig.Live);
        TradeRig.WithPrices(app);
        var site = new FakeTradeSite();
        using var listings = TradeRig.Listings(site);
        app.Shell.Listings = listings;
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "hud")!;
        item.SearchRows.First(r => !r.IsOn).IsOn = true;
        TradeRig.Search(item);

        Assert.True(app.Shell.ApplyLiveBook(NinjaFixtures.Book(TradeRig.League, DateTimeOffset.UtcNow)));

        var after = app.Shell.Inspect.Selected!;
        Assert.NotSame(item, after);
        Assert.Equal(item.Answer.Big, after.Answer.Big);
        Assert.Equal(item.Matches.Select(m => m.ToString()), after.Matches.Select(m => m.ToString()));
        Assert.Equal(item.OnCount, after.OnCount);
        Assert.Equal(item.TradeUrl, after.TradeUrl);
        Assert.False(after.IsStale);
        Assert.Equal(2, site.Requests.Count);
    });

    [Fact]
    public void While_the_trade_site_asks_to_wait_the_last_result_stays_and_the_card_says_how_long() => Sta.Run(() =>
    {
        Sta.UseDispatcherContext();
        using var app = new TestApp(TradeRig.Live);
        TradeRig.WithPrices(app);
        var site = new FakeTradeSite();
        using var listings = TradeRig.Listings(site);
        app.Shell.Listings = listings;
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "hud")!;
        TradeRig.Search(item);
        var (answer, matches) = (item.Answer, item.Matches);

        site.FailWith = HttpStatusCode.TooManyRequests;
        site.FailHeaders = [.. FakeTradeSite.Limits("trade-search-request-limit", "5:10:60,15:60:300,30:300:1800", "6:10:60,6:60:0,6:300:0"), ("Retry-After", "60")];
        TradeRig.Search(item);

        Assert.Same(answer, item.Answer);
        Assert.Same(matches, item.Matches);
        Assert.Equal("The trade site asks to wait 60 s.", item.SearchNote);
        Assert.Equal(item.SearchNote, item.PeekLine);
        Assert.True(item.PeekLineIsWarning);
        var sent = site.Requests.Count;

        TradeRig.Search(item); // held by the governor: nothing is sent

        Assert.Equal(sent, site.Requests.Count);
        Assert.Matches(@"^The trade site asks to wait \d+ s\.$", item.SearchNote);
        Assert.Same(answer, item.Answer);
    });

    [Fact]
    public void With_trade_search_off_Search_sends_nothing_and_points_to_the_trade_site() => Sta.Run(() =>
    {
        using var app = new TestApp(s =>
        {
            TradeRig.Live(s);
            s.TradeSearch = false;
        });
        var site = new FakeTradeSite();
        using var listings = TradeRig.Listings(site);
        app.Shell.Listings = listings;
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "hud")!;

        Assert.False(item.HasListings);
        Assert.Equal("Open the search on the trade site, then write down what you find.", item.Answer.Note);

        item.SearchCommand.Execute(null);
        Sta.Drain();

        Assert.Equal(ItemViewModel.SearchOffText, item.SearchNote);
        Assert.Empty(site.Requests);
    });

    [Fact]
    public void A_unique_the_site_s_filters_do_not_know_is_not_searched() => Sta.Run(() =>
    {
        using var app = new TestApp(TradeRig.Live);
        var site = new FakeTradeSite();
        using var listings = TradeRig.Listings(site);
        app.Shell.Listings = listings;
        var ring = app.Shell.Inspect.AddFromText(app.ItemText("unique-ring.txt"), "hud")!; // a made-up unique

        Assert.False(ring.Trade!.Identifies);
        ring.SearchCommand.Execute(null);
        Sta.Drain();

        Assert.Empty(site.Requests); // without its name the search would price every unique gold ring
        Assert.StartsWith("The trade site's filters don't know this item (Name (Sanity's Last Loop))", ring.SearchNote, StringComparison.Ordinal);
    });

    [Fact]
    public void The_Settings_switch_turns_trade_search_off_and_on() => Sta.Run(() =>
    {
        using var app = new TestApp(TradeRig.Live);
        using var listings = TradeRig.Listings(new FakeTradeSite());
        app.Shell.Listings = listings;
        app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "hud");
        Assert.True(app.Shell.SettingsPage.TradeSearchOn);
        Assert.Equal(PressSearch, app.Shell.Inspect.Selected!.PeekLine);

        app.Shell.SettingsPage.TradeSearchOn = false;

        Assert.False(app.Settings.TradeSearch);
        Assert.False(app.Shell.TradeSite.IsOn);
        Assert.Equal("No price yet · compare on trade", app.Shell.Inspect.Selected!.PeekLine);

        app.Shell.SettingsPage.TradeSearchOn = true;

        Assert.True(app.Shell.TradeSite.IsOn);
        Assert.Equal(PressSearch, app.Shell.Inspect.Selected!.PeekLine);
    });

    [Fact]
    public void The_demo_searches_a_practice_item_s_synthetic_listings_without_the_network() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var site = new FakeTradeSite();
        using var listings = TradeRig.Listings(site);
        app.Shell.Listings = listings;
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "practice")!;

        Assert.True(item.HasMatches); // the practice items open with a result, as before
        Assert.InRange(item.Matches.Count, 7, ItemViewModel.ListingRows);
        var before = item.Answer;

        item.SearchRows.First(r => r.IsOn).IsOn = false;
        Assert.Same(before, item.Answer);
        Assert.True(item.IsStale);

        item.SearchCommand.Execute(null);

        Assert.NotEqual(before, item.Answer);
        Assert.False(item.IsStale);
        Assert.Null(item.SearchNote);
        Assert.Empty(site.Requests);
    });

    [Fact]
    public void In_the_demo_an_item_from_the_game_has_no_real_price_and_Search_says_it_needs_a_live_league() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var site = new FakeTradeSite();
        using var listings = TradeRig.Listings(site);
        app.Shell.Listings = listings;
        var item = app.Shell.Inspect.AddFromText(GameBoots, "hud")!;

        Assert.Equal("No real price", item.Answer.Big);
        Assert.True(item.CanSearch);

        item.SearchCommand.Execute(null);
        Sta.Drain();

        Assert.Equal(ItemViewModel.NeedsLiveLeague, item.SearchNote);
        Assert.True(item.SearchNoteIsWarning);
        Assert.Equal("No real price", item.Answer.Big);
        Assert.Empty(site.Requests);
    });

    [Fact]
    public void A_key_press_and_ticks_on_the_pinned_card_send_nothing_only_its_Search_does() => Sta.Run(() =>
    {
        Sta.UseDispatcherContext();
        using var rig = new HudRig(TradeRig.Live);
        TradeRig.WithPrices(rig.App);
        var site = new FakeTradeSite();
        using var listings = TradeRig.Listings(site);
        rig.Shell.Listings = listings;
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        Assert.Single(rig.Peek.Shown);
        rig.Keys.Release(CtrlC);
        Sta.Drain();
        Assert.Empty(site.Requests);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Cursor.MoveTo(FakeCursor.OnCard);
        rig.Keys.Release(CtrlC);
        Sta.Drain();
        var card = rig.Peek.Shown[^1];
        Assert.True(card.IsPinned);
        Assert.True(card.ShowsSearch);
        Assert.Equal(PressSearch, card.Item!.PeekLine);

        card.Item.SearchRows.First(r => !r.IsOn).IsOn = true;
        card.Item.SearchRows.First(r => r.IsOn).DecreaseCommand.Execute(null);
        Sta.Drain();
        Assert.Empty(site.Requests);

        TradeRig.Search(card.Item);

        Assert.Equal(2, site.Requests.Count);
        Assert.Equal(1, site.Searches);
        Assert.Equal(2, rig.Clipboard.Reads); // one per press; Search reads nothing
        Assert.Equal("1–2 div", card.Answer!.Big);
        Assert.True(card.Item.HasMatches);
    });

    [Fact]
    public void The_connection_keeps_no_cookies()
    {
        using var network = LiveListings.Network();
        Assert.False(network.UseCookies);
    }
}
