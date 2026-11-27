using System.Net;
using StillSaneExile.Domain.Market;
using static StillSaneExile.Domain.Tests.MarketTest;

namespace StillSaneExile.Domain.Tests;

public class HttpCacheTests
{
    private const string Url = "https://poe.ninja.test/poe2/api/economy/exchange/current/overview?league=Test&type=Currency";
    private static readonly DateTimeOffset T0 = Fixtures.DemoNow;
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private sealed class Rig
    {
        public ManualClock Clock { get; } = MarketTest.Clock();

        public FakeProvider Provider { get; } = new();

        public HttpCache Cache { get; } = new();

        public HttpClient Client { get; }

        public Rig() => Client = MarketTest.Client(new ProviderGovernor(Clock, Options), Provider, Cache);

        public async Task<(HttpResponseMessage Response, string Body)> Get(string url = Url)
        {
            var response = await Client.SendAsync(MarketTest.Get(url, "overview", Ninja));
            return (response, await response.Content.ReadAsStringAsync());
        }

        public string? Sent(int index, string header) =>
            Provider.Requests[index].Headers.TryGetValues(header, out var values) ? string.Join(", ", values) : null;
    }

    [Fact]
    public async Task A_fresh_response_is_served_without_a_request()
    {
        var rig = new Rig();
        rig.Provider.Reply(HttpStatusCode.OK, "prices v1", ("Cache-Control", "max-age=60"), ("ETag", "\"v1\""));

        var (first, firstBody) = await rig.Get();
        Assert.Equal(ResponseSource.Network, MarketRequest.SourceOf(first));
        Assert.Equal("prices v1", firstBody);

        rig.Clock.Advance(S(30));
        var (second, secondBody) = await rig.Get();
        Assert.Equal(ResponseSource.Cache, MarketRequest.SourceOf(second));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("prices v1", secondBody);
        Assert.Equal("text/plain", second.Content.Headers.ContentType?.MediaType);
        Assert.Single(rig.Provider.Requests);
    }

    [Fact]
    public async Task A_304_reuses_the_cached_body_and_refreshes_its_freshness()
    {
        var rig = new Rig();
        rig.Provider
            .Reply(HttpStatusCode.OK, "prices v1", ("Cache-Control", "max-age=60"), ("ETag", "\"v1\""))
            .Reply(HttpStatusCode.NotModified, "", ("Cache-Control", "max-age=120"));
        await rig.Get();

        rig.Clock.Advance(S(61));
        var (revalidated, body) = await rig.Get();
        Assert.Equal("\"v1\"", rig.Sent(1, "If-None-Match"));
        Assert.Equal(HttpStatusCode.OK, revalidated.StatusCode);
        Assert.Equal(ResponseSource.Revalidated, MarketRequest.SourceOf(revalidated));
        Assert.Equal("prices v1", body);
        Assert.Equal("\"v1\"", revalidated.Headers.ETag?.Tag);

        // The 304's max-age restarts the clock: fresh for another 120 s, with no request.
        Assert.Equal(T0 + S(61 + 120), rig.Cache.Find(new Uri(Url))!.FreshUntil);
        rig.Clock.Advance(S(119));
        var (cached, cachedBody) = await rig.Get();
        Assert.Equal(ResponseSource.Cache, MarketRequest.SourceOf(cached));
        Assert.Equal("prices v1", cachedBody);
        Assert.Equal(2, rig.Provider.Requests.Count);
    }

    [Fact]
    public async Task Last_modified_is_revalidated_with_if_modified_since()
    {
        var rig = new Rig();
        const string modified = "Fri, 27 Nov 2026 10:00:00 GMT";
        rig.Provider
            .Reply(HttpStatusCode.OK, "digest hour", ("Last-Modified", modified))
            .Reply(HttpStatusCode.NotModified);
        await rig.Get();

        // No lifetime: stored for its validator, but checked every time. 5 s is the conservative gap.
        rig.Clock.Advance(S(5));
        var (response, body) = await rig.Get();
        Assert.Equal(modified, rig.Sent(1, "If-Modified-Since"));
        Assert.Null(rig.Sent(1, "If-None-Match"));
        Assert.Equal(ResponseSource.Revalidated, MarketRequest.SourceOf(response));
        Assert.Equal("digest hour", body);
        Assert.Equal(modified, response.Content.Headers.NonValidated["Last-Modified"].ToString());
    }

    [Fact]
    public async Task No_store_is_never_cached()
    {
        var rig = new Rig();
        rig.Provider
            .Reply(HttpStatusCode.OK, "v1", ("Cache-Control", "no-store, max-age=600"), ("ETag", "\"v1\""))
            .Reply(HttpStatusCode.OK, "v2", ("Cache-Control", "no-store"));
        await rig.Get();
        Assert.Equal(0, rig.Cache.Count);

        rig.Clock.Advance(S(5));
        var (response, body) = await rig.Get();
        Assert.Null(rig.Sent(1, "If-None-Match"));
        Assert.Equal(ResponseSource.Network, MarketRequest.SourceOf(response));
        Assert.Equal("v2", body);
    }

    [Fact]
    public async Task No_cache_is_revalidated_every_time()
    {
        var rig = new Rig();
        rig.Provider
            .Reply(HttpStatusCode.OK, "v1", ("Cache-Control", "no-cache, max-age=600"), ("ETag", "\"v1\""))
            .Reply(HttpStatusCode.NotModified);
        await rig.Get();

        rig.Clock.Advance(S(5));
        var (response, body) = await rig.Get();
        Assert.Equal("\"v1\"", rig.Sent(1, "If-None-Match"));
        Assert.Equal(ResponseSource.Revalidated, MarketRequest.SourceOf(response));
        Assert.Equal("v1", body);
    }

    [Fact]
    public async Task A_changed_resource_replaces_the_cached_copy()
    {
        var rig = new Rig();
        rig.Provider
            .Reply(HttpStatusCode.OK, "v1", ("Cache-Control", "max-age=60"), ("ETag", "\"v1\""))
            .Reply(HttpStatusCode.OK, "v2", ("Cache-Control", "max-age=60"), ("ETag", "\"v2\""));
        await rig.Get();

        rig.Clock.Advance(S(61));
        var (changed, body) = await rig.Get();
        Assert.Equal("\"v1\"", rig.Sent(1, "If-None-Match"));
        Assert.Equal(ResponseSource.Network, MarketRequest.SourceOf(changed));
        Assert.Equal("v2", body);
        Assert.Equal("\"v2\"", rig.Cache.Find(new Uri(Url))!.ETag);

        rig.Clock.Advance(S(30));
        Assert.Equal("v2", (await rig.Get()).Body);
        Assert.Equal(2, rig.Provider.Requests.Count);
    }

    [Fact]
    public async Task Expires_gives_freshness_when_there_is_no_max_age()
    {
        var rig = new Rig();
        rig.Provider
            .Reply(HttpStatusCode.OK, "v1", ("Expires", "Fri, 27 Nov 2026 11:31:00 GMT"))
            .Reply(HttpStatusCode.OK, "v2");
        await rig.Get();

        rig.Clock.Advance(S(59));
        Assert.Equal(ResponseSource.Cache, MarketRequest.SourceOf((await rig.Get()).Response));
        rig.Clock.Advance(S(1));
        Assert.Equal("v2", (await rig.Get()).Body);
    }

    [Fact]
    public async Task Age_counts_against_max_age()
    {
        var rig = new Rig();
        rig.Provider
            .Reply(HttpStatusCode.OK, "v1", ("Cache-Control", "max-age=60"), ("Age", "50"))
            .Reply(HttpStatusCode.OK, "v2");
        await rig.Get();

        rig.Clock.Advance(S(9));
        Assert.Equal("v1", (await rig.Get()).Body);
        rig.Clock.Advance(S(1));
        Assert.Equal("v2", (await rig.Get()).Body);
    }

    [Theory]
    [InlineData("max-age=10", true)]
    [InlineData("max-age=10, must-revalidate", false)]
    public async Task A_hold_offers_the_stale_copy_unless_it_must_be_revalidated(string cacheControl, bool offered)
    {
        var rig = new Rig();
        rig.Provider
            .Reply(HttpStatusCode.OK, "v1", ("Cache-Control", cacheControl), ("ETag", "\"v1\""))
            .Reply(HttpStatusCode.ServiceUnavailable);
        await rig.Get();

        rig.Clock.Advance(S(11));
        var (failed, _) = await rig.Get();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);

        var held = await Assert.ThrowsAsync<ProviderHoldException>(() => rig.Get());
        Assert.Equal(HoldReason.ServerBackoff, held.Decision.Reason);
        Assert.Equal(offered, held.Stale is not null);
        if (offered)
        {
            Assert.Equal("v1", System.Text.Encoding.UTF8.GetString(held.Stale!.Body));
        }
    }

    [Fact]
    public async Task Only_gets_are_cached()
    {
        var rig = new Rig();
        rig.Provider.Reply(HttpStatusCode.OK, "created", ("Cache-Control", "max-age=600"), ("ETag", "\"v1\""));
        using var request = new HttpRequestMessage(HttpMethod.Post, Url).ForRoute(Ninja, "overview");
        using var response = await rig.Client.SendAsync(request);
        Assert.Equal(0, rig.Cache.Count);
    }
}
