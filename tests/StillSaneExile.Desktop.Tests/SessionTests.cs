using System.IO;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Reading;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Desktop.Tests;

public sealed class SessionTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly AppPaths _paths;
    private readonly Store _store;
    private readonly Settings _settings;

    public SessionTests()
    {
        _paths = new AppPaths(_dir.Sub("data"));
        _store = new Store(_paths.Database);
        _settings = new Settings(_store);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void A_new_player_gets_the_demo_with_synthetic_prices_and_a_fixed_clock()
    {
        var session = Session.Create(_store, _settings, _paths);

        Assert.True(session.IsDemo);
        Assert.Equal("demo", session.ProfileId);
        Assert.Equal(Settings.DemoLeague, session.League);
        Assert.True(session.Book.IsSynthetic);
        Assert.NotEmpty(session.Book.Quotes);
        Assert.NotNull(session.Pack);
        Assert.IsType<DemoTimeProvider>(session.Clock);
        Assert.InRange(session.Now - DemoTimeProvider.Start, TimeSpan.Zero, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void A_live_profile_without_a_confirmed_league_stays_on_the_demo()
    {
        _settings.Profile = ProfileKind.Live;

        Assert.True(Session.Create(_store, _settings, _paths).IsDemo);
    }

    [Fact]
    public void A_live_profile_never_sees_demo_prices_or_listings()
    {
        _settings.LiveLeague = "Standard";
        _settings.Profile = ProfileKind.Live;

        var session = Session.Create(_store, _settings, _paths);

        Assert.False(session.IsDemo);
        Assert.Equal("Standard", session.League);
        Assert.False(session.Book.IsSynthetic);
        Assert.Empty(session.Book.Quotes);
        Assert.Null(session.Pack);
        Assert.Null(session.Listings(Crossbow(session, out var worksheet), worksheet));
    }

    [Fact]
    public void The_demo_session_brings_its_synthetic_listings()
    {
        var session = Session.Create(_store, _settings, _paths);

        Assert.NotEmpty(session.Listings(Crossbow(session, out var worksheet), worksheet)!);
    }

    [Fact]
    public void The_guard_refuses_the_demo_price_book_in_a_live_profile()
    {
        var demo = Session.Demo(_store, _settings, _paths);

        Assert.Throws<InvalidOperationException>(() => PriceBookGuard.EnsureAllowed(ProfileKind.Live, demo.League, demo.Book));
        Assert.Throws<InvalidOperationException>(() => PriceBookGuard.EnsureAllowed(ProfileKind.Demo, "Standard", demo.Book));
        PriceBookGuard.EnsureAllowed(ProfileKind.Demo, Settings.DemoLeague, demo.Book);
    }

    [Fact]
    public void Prices_written_down_in_one_profile_never_show_in_the_other()
    {
        var now = DemoTimeProvider.Start;
        var ask = new Comparable("live-1", "Standard", 3m, "divine", now, null, null, null, null, ComparableOrigin.PlayerEntered, now.AddDays(3));
        _store.AddComparable("live", "test-set", "item text", ask, now);
        _settings.LiveLeague = "Standard";

        var demo = Session.Create(_store, _settings, _paths);
        _settings.Profile = ProfileKind.Live;
        var live = Session.Create(_store, _settings, _paths);

        Assert.Empty(demo.Comparables("test-set"));
        Assert.Equal("live-1", Assert.Single(live.Comparables("test-set")).Id);
    }

    private ItemSnapshot Crossbow(Session session, out ComparisonWorksheet worksheet)
    {
        var text = File.ReadAllText(Path.Combine(_paths.Fixtures, "items", "rare-crossbow-advanced.txt"));
        var item = ItemTextParser.Parse(text, session.Now, "test").Item!;
        worksheet = WorksheetFactory.Create(ItemReader.Read(item, session.Pack));
        return item;
    }
}
