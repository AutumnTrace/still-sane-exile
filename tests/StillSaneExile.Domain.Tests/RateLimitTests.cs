using System.Net;
using StillSaneExile.Domain.Market;
using static StillSaneExile.Domain.Tests.MarketTest;

namespace StillSaneExile.Domain.Tests;

public class RateLimitTests
{
    private const string Url = "https://api.ggg.test/trade/fetch";
    private static readonly DateTimeOffset T0 = Fixtures.DemoNow;
    private static readonly HttpStatusCode TooManyRequests = (HttpStatusCode)429;
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    // ---- Headroom and windows ----

    [Fact]
    public void Headroom_stops_before_the_last_hit()
    {
        var clock = Clock();
        var governor = new ProviderGovernor(clock, Options);
        var route = Route("fetch");
        for (var i = 1; i <= 4; i++)
        {
            Answer(governor, route, HttpStatusCode.OK, Limits("fetch", "5:10:60", $"{i}:10:0"));
        }

        // 5 per 10 s: the fifth hit is the last one, so it is kept back.
        var held = governor.TryAcquire(route);
        Assert.Equal(HoldReason.RateWindow, held.Reason);
        Assert.Null(held.Ticket);
        Assert.Equal(T0 + S(11), held.RetryAt); // the oldest hit leaves the window, plus a 1 s margin
        Assert.Contains("headroom", held.Message);

        clock.Advance(S(10));
        Assert.Equal(HoldReason.RateWindow, governor.Check(route).Reason);
        clock.Advance(S(1));
        Assert.True(governor.Check(route).CanSend);
    }

    [Fact]
    public void The_tighter_window_governs_until_the_long_window_fills()
    {
        var clock = Clock();
        var governor = new ProviderGovernor(clock, Options);
        var route = Route("search");
        const string limits = "5:10:60,30:300:1800";
        Answer(governor, route, HttpStatusCode.OK, Limits("search", limits, "1:10:0,1:300:0"));
        var sent = 1;

        // Usable: 4 per 10 s and 27 per 300 s. Bursts of four, each held by the 10 s window, until the 300 s one is full.
        while (true)
        {
            var decision = governor.TryAcquire(route);
            if (decision.Ticket is { } ticket)
            {
                governor.Record(ticket, HttpStatusCode.OK, Headers(Limits("search", limits, "0:10:0,0:300:0")));
                sent++;
                continue;
            }

            Assert.Equal(HoldReason.RateWindow, decision.Reason);
            if (decision.RetryAt == T0 + S(301))
            {
                break;
            }

            Assert.Equal(clock.GetUtcNow() + S(11), decision.RetryAt);
            Assert.True(sent < 100, "the long window never took over");
            clock.Advance(decision.RetryAt!.Value - clock.GetUtcNow());
        }

        Assert.Equal(27, sent);
        Assert.Equal(T0 + S(66), clock.GetUtcNow());
        clock.Advance(T0 + S(301) - clock.GetUtcNow());
        Assert.True(governor.Check(route).CanSend);
    }

    [Fact]
    public void Hits_the_server_counted_from_elsewhere_are_respected()
    {
        var governor = new ProviderGovernor(Clock(), Options);
        var route = Route("fetch");

        // One request from us, but the server has counted four on this IP (the trade site, another tool).
        Answer(governor, route, HttpStatusCode.OK, Limits("fetch", "5:10:60", "4:10:0"));

        var held = governor.Check(route);
        Assert.Equal(HoldReason.RateWindow, held.Reason);
        Assert.Equal(T0 + S(11), held.RetryAt);
    }

    // ---- 429 and shared policies ----

    [Fact]
    public async Task A_429_on_one_route_pauses_every_route_that_shares_its_policy()
    {
        var clock = Clock();
        var governor = new ProviderGovernor(clock, Options);
        var provider = new FakeProvider();
        using var client = Client(governor, provider);
        const string limits = "5:10:60,30:300:1800";

        // Route b learns the policy. Route a has not been seen yet, so it waits the conservative gap first.
        provider.Reply(HttpStatusCode.OK, "{}", Limits("trade-fetch", limits, "1:10:0,1:300:0"));
        (await client.SendAsync(Get(Url + "/b", "b"))).Dispose();
        clock.Advance(S(5));

        // a trips both windows. Retry-After says 60 s, but the 300 s window is restricted for 120 s.
        provider.Reply(TooManyRequests, "", [.. Limits("trade-fetch", limits, "6:10:60,12:300:120"), ("Retry-After", "60")]);
        using var limited = await client.SendAsync(Get(Url + "/a", "a"));
        Assert.Equal(TooManyRequests, limited.StatusCode); // handed back as it came, not retried
        Assert.Equal(2, provider.Requests.Count);

        var end = clock.GetUtcNow() + S(120) + Jitter; // the longest restriction, then jitter
        var shared = await Assert.ThrowsAsync<ProviderHoldException>(() => client.SendAsync(Get(Url + "/b", "b")));
        Assert.Equal(HoldReason.CoolingDown, shared.Decision.Reason);
        Assert.Equal(end, shared.Decision.RetryAt);
        Assert.Contains("trade-fetch", shared.Message);

        // A route whose limits are unknown might share the policy, so it waits too.
        var unknown = await Assert.ThrowsAsync<ProviderHoldException>(() => client.SendAsync(Get(Url + "/c", "c")));
        Assert.Equal(HoldReason.CoolingDown, unknown.Decision.Reason);
        Assert.Equal(end, unknown.Decision.RetryAt);
        Assert.Equal(2, provider.Requests.Count); // nothing reached the provider

        // Another provider is not affected.
        provider.Reply(HttpStatusCode.OK, "[]");
        using var ninja = await client.SendAsync(Get("https://poe.ninja.test/api/overview", "overview", Ninja));
        Assert.Equal(HttpStatusCode.OK, ninja.StatusCode);

        clock.Advance(end - clock.GetUtcNow() - S(1));
        Assert.Equal(HoldReason.CoolingDown, governor.Check(Route("b")).Reason);
        clock.Advance(S(1));
        provider.Reply(HttpStatusCode.OK, "{}", Limits("trade-fetch", limits, "1:10:0,13:300:0"));
        using var again = await client.SendAsync(Get(Url + "/b", "b"));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(4, provider.Requests.Count);
    }

    [Fact]
    public void A_429_without_a_readable_policy_pauses_the_whole_integration()
    {
        var clock = Clock();
        var governor = new ProviderGovernor(clock, Options);

        // An HTTP date one minute ahead, and no rate-limit headers to say which routes share the limit.
        Answer(governor, Route("a"), TooManyRequests, ("Retry-After", "Fri, 27 Nov 2026 11:31:00 GMT"));

        var held = governor.Check(Route("b"));
        Assert.Equal(HoldReason.CoolingDown, held.Reason);
        Assert.Equal(T0 + S(60) + Jitter, held.RetryAt);
        Assert.True(governor.Check(Route("b", Ninja)).CanSend);
    }

    [Fact]
    public void A_429_that_says_nothing_still_cools_down()
    {
        var governor = new ProviderGovernor(Clock(), Options);
        Answer(governor, Route("a"), TooManyRequests);
        Assert.Equal(T0 + Options.FallbackCooldown + Jitter, governor.Check(Route("a")).RetryAt);
    }

    // ---- Conservative mode ----

    [Theory]
    [InlineData(null, null, null, null)] // headers missing altogether
    [InlineData("trade-fetch", null, null, null)]
    [InlineData("trade-fetch", "Ip", null, null)]
    [InlineData("trade-fetch", "Ip", "45:60:120", null)]
    [InlineData("trade-fetch", "Ip", "45:60", "1:60:0")]
    [InlineData("trade-fetch", "Ip", "45:60:120", "1:60")]
    [InlineData("trade-fetch", "Ip", "lots", "some")]
    [InlineData("trade-fetch", "Ip,Account", "45:60:120", "1:60:0")]
    public async Task Malformed_or_missing_headers_fall_back_to_serialized_conservative_requests(string? policy, string? rules, string? limits, string? state)
    {
        var clock = Clock();
        var governor = new ProviderGovernor(clock, Options);
        var provider = new FakeProvider();
        using var client = Client(governor, provider);

        // The route first shows good headers, so its next request needs no gap.
        provider.Reply(HttpStatusCode.OK, "{}", Limits("trade-fetch", "45:60:120", "1:60:0"));
        (await client.SendAsync(Get(Url, "fetch"))).Dispose();

        var bad = new List<(string, string)>();
        if (policy is not null)
        {
            bad.Add(("X-Rate-Limit-Policy", policy));
        }

        if (rules is not null)
        {
            bad.Add(("X-Rate-Limit-Rules", rules));
        }

        if (limits is not null)
        {
            bad.Add(("X-Rate-Limit-Ip", limits));
        }

        if (state is not null)
        {
            bad.Add(("X-Rate-Limit-Ip-State", state));
        }

        provider.Reply(HttpStatusCode.OK, "{}", [.. bad]);
        using var response = await client.SendAsync(Get(Url, "fetch"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // never throws on bad headers

        // Back to one at a time, 5 s apart.
        var held = await Assert.ThrowsAsync<ProviderHoldException>(() => client.SendAsync(Get(Url, "fetch")));
        Assert.Equal(HoldReason.Conservative, held.Decision.Reason);
        Assert.Equal(T0 + S(5), held.Decision.RetryAt);
        Assert.Equal(2, provider.Requests.Count);
    }

    [Fact]
    public void Conservative_mode_sends_one_request_at_a_time()
    {
        var clock = Clock();
        var governor = new ProviderGovernor(clock, Options);
        var first = governor.TryAcquire(Route("a", Ninja));
        Assert.NotNull(first.Ticket);

        // However long the first takes, a second route of the same provider waits for it.
        clock.Advance(S(30));
        var second = governor.TryAcquire(Route("b", Ninja));
        Assert.Equal(HoldReason.Conservative, second.Reason);
        Assert.Contains("in flight", second.Message);

        governor.Record(first.Ticket!, HttpStatusCode.OK, Headers());
        var third = governor.TryAcquire(Route("b", Ninja));
        Assert.NotNull(third.Ticket);

        var fourth = governor.TryAcquire(Route("c", Ninja));
        Assert.Equal(HoldReason.Conservative, fourth.Reason);
        Assert.Equal(T0 + S(35), fourth.RetryAt);
    }

    [Fact]
    public async Task Short_pacing_holds_are_waited_out_inside_the_handler()
    {
        var clock = Clock();
        var governor = new ProviderGovernor(clock, Options);
        var provider = new FakeProvider().Reply(HttpStatusCode.OK, "one").Reply(HttpStatusCode.OK, "two");
        using var client = Client(governor, provider, maxPacingWait: S(30));

        (await client.SendAsync(Get(Url, "fetch"))).Dispose();
        var second = client.SendAsync(Get(Url, "fetch"));
        Assert.False(second.IsCompleted);
        Assert.Equal(1, clock.PendingTimers);
        Assert.Single(provider.Requests);

        clock.Advance(S(5));
        using var response = await second;
        Assert.Equal("two", await response.Content.ReadAsStringAsync());
        Assert.Equal(2, provider.Requests.Count);
    }

    // ---- 4xx, 401/403 and the circuit ----

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Refused_access_disables_the_integration_until_it_is_turned_back_on(int status)
    {
        var clock = Clock();
        using var file = new TempCooldownFile();
        var governor = new ProviderGovernor(clock, Options, file.Store);
        var provider = new FakeProvider().Reply((HttpStatusCode)status);
        using var client = Client(governor, provider);

        using var refused = await client.SendAsync(Get(Url, "fetch"));
        Assert.Equal(status, (int)refused.StatusCode);

        var held = await Assert.ThrowsAsync<ProviderHoldException>(() => client.SendAsync(Get(Url + "/other", "other")));
        Assert.Equal(HoldReason.Disabled, held.Decision.Reason);
        Assert.Null(held.Decision.RetryAt); // waiting does not fix it
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), held.Message);
        Assert.Single(provider.Requests);

        clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(HoldReason.Disabled, governor.Check(Route("fetch")).Reason);
        Assert.Equal(HoldReason.Disabled, new ProviderGovernor(clock, Options, file.Store).Check(Route("fetch")).Reason);
        Assert.True(governor.Check(Route("fetch", Ninja)).CanSend);

        governor.Enable(Ggg);
        Assert.True(governor.Check(Route("fetch")).CanSend);
        Assert.True(new ProviderGovernor(clock, Options, file.Store).Check(Route("fetch")).CanSend);
    }

    [Fact]
    public void Repeated_4xx_open_the_circuit_then_one_trial_request_decides()
    {
        var clock = Clock();
        var governor = new ProviderGovernor(clock, Options);
        var limits = Limits("fetch", "45:60:120", "1:60:0");
        for (var i = 0; i < 3; i++)
        {
            Answer(governor, Route("fetch"), HttpStatusCode.NotFound, limits);
        }

        var open = governor.Check(Route("other"));
        Assert.Equal(HoldReason.CircuitOpen, open.Reason);
        Assert.Equal(T0 + TimeSpan.FromMinutes(5), open.RetryAt);
        Assert.Contains("404 Not Found", open.Message);

        // Half-open: one trial, and nothing else while it is out.
        clock.Advance(TimeSpan.FromMinutes(5));
        var trial = governor.TryAcquire(Route("fetch"));
        Assert.NotNull(trial.Ticket);
        Assert.Equal(HoldReason.CircuitOpen, governor.TryAcquire(Route("fetch")).Reason);

        // The trial fails: open again, for twice as long.
        governor.Record(trial.Ticket!, HttpStatusCode.BadRequest, Headers(limits));
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromMinutes(10), governor.Check(Route("fetch")).RetryAt);

        // The next trial succeeds: closed.
        clock.Advance(TimeSpan.FromMinutes(10));
        Answer(governor, Route("fetch"), HttpStatusCode.OK, limits);
        Assert.True(governor.Check(Route("fetch")).CanSend);
        Answer(governor, Route("fetch"), HttpStatusCode.NotFound, limits);
        Assert.True(governor.Check(Route("fetch")).CanSend);
    }

    [Fact]
    public void A_success_resets_the_4xx_count()
    {
        var governor = new ProviderGovernor(Clock(), Options);
        var limits = Limits("fetch", "45:60:120", "1:60:0");
        foreach (var status in (HttpStatusCode[])[HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.NotFound])
        {
            Answer(governor, Route("fetch"), status, limits);
        }

        Assert.True(governor.Check(Route("fetch")).CanSend);
    }

    // ---- 5xx and network failures ----

    [Fact]
    public void Server_errors_back_off_exponentially_up_to_the_cap()
    {
        var clock = Clock();
        var governor = new ProviderGovernor(clock, Options);
        var limits = Limits("fetch", "45:60:120", "1:60:0");
        foreach (var seconds in (int[])[10, 20, 40, 80, 160, 320, 640, 900, 900])
        {
            Answer(governor, Route("fetch"), HttpStatusCode.ServiceUnavailable, limits);
            var held = governor.Check(Route("fetch"));
            Assert.Equal(HoldReason.ServerBackoff, held.Reason);
            Assert.Equal(clock.GetUtcNow() + S(seconds) + Jitter, held.RetryAt);
            clock.Advance(held.RetryAt!.Value - clock.GetUtcNow());
        }

        // 5xx never open the circuit, and one good answer starts the ladder again.
        Answer(governor, Route("fetch"), HttpStatusCode.OK, limits);
        Answer(governor, Route("fetch"), HttpStatusCode.InternalServerError, limits);
        Assert.Equal(clock.GetUtcNow() + S(10) + Jitter, governor.Check(Route("fetch")).RetryAt);
    }

    [Theory]
    [InlineData("120", 120)] // longer than the first step: honoured
    [InlineData("2", 10)] // shorter: the backoff step wins
    public void Retry_after_on_a_503_is_honoured_when_longer(string retryAfter, int expectedSeconds)
    {
        var governor = new ProviderGovernor(Clock(), Options);
        Answer(governor, Route("fetch"), HttpStatusCode.ServiceUnavailable, ("Retry-After", retryAfter));
        Assert.Equal(T0 + S(expectedSeconds) + Jitter, governor.Check(Route("fetch")).RetryAt);
    }

    [Fact]
    public async Task A_network_failure_backs_off_like_a_server_error()
    {
        var governor = new ProviderGovernor(Clock(), Options);
        var provider = new FakeProvider().Fail(new HttpRequestException("connection refused"));
        using var client = Client(governor, provider);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(Get(Url, "fetch")));
        var held = await Assert.ThrowsAsync<ProviderHoldException>(() => client.SendAsync(Get(Url, "fetch")));
        Assert.Equal(HoldReason.ServerBackoff, held.Decision.Reason);
        Assert.Equal(T0 + S(10) + Jitter, held.Decision.RetryAt);
        Assert.Contains("no answer", held.Message);
        Assert.Single(provider.Requests);
    }

    [Fact]
    public async Task Errors_are_handed_back_once_and_never_retried()
    {
        var clock = Clock();
        var governor = new ProviderGovernor(clock, Options);
        var limits = Limits("fetch", "45:60:120", "1:60:0");
        var provider = new FakeProvider()
            .Reply(HttpStatusCode.NotFound, "", limits)
            .Reply(HttpStatusCode.BadGateway, "", limits);
        using var client = Client(governor, provider);

        using var notFound = await client.SendAsync(Get(Url, "fetch"));
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Single(provider.Requests);

        using var badGateway = await client.SendAsync(Get(Url, "fetch"));
        Assert.Equal(HttpStatusCode.BadGateway, badGateway.StatusCode);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal(ResponseSource.Network, MarketRequest.SourceOf(badGateway));
    }

    // ---- Persistence ----

    [Fact]
    public async Task A_restart_during_a_cooldown_keeps_requests_blocked_until_it_really_ends()
    {
        var clock = Clock();
        using var file = new TempCooldownFile();
        const string limits = "5:10:60,30:300:1800";

        var before = new ProviderGovernor(clock, Options, file.Store);
        var provider = new FakeProvider()
            .Reply(HttpStatusCode.OK, "{}", Limits("trade-fetch", limits, "1:10:0,1:300:0"))
            .Reply(TooManyRequests, "", [.. Limits("trade-fetch", limits, "6:10:60,7:300:0"), ("Retry-After", "60")]);
        using (var client = Client(before, provider))
        {
            (await client.SendAsync(Get(Url + "/b", "b"))).Dispose();
            clock.Advance(S(5));
            (await client.SendAsync(Get(Url + "/a", "a"))).Dispose();
        }

        var end = T0 + S(5) + S(60) + Jitter;
        Assert.Contains("\"restrictedUntil\": \"2026-11-27T11:31:08+00:00\"", File.ReadAllText(file.FilePath)); // an instant, not "60 s left"

        // The app closes and starts again 20 s later.
        clock.Advance(S(20));
        var after = new ProviderGovernor(clock, Options, file.Store);
        var fresh = new FakeProvider();
        using var restarted = Client(after, fresh);

        foreach (var route in (string[])["a", "b"])
        {
            var held = await Assert.ThrowsAsync<ProviderHoldException>(() => restarted.SendAsync(Get(Url + "/" + route, route)));
            Assert.Equal(HoldReason.CoolingDown, held.Decision.Reason);
            Assert.Equal(end, held.Decision.RetryAt);
        }

        clock.Advance(end - clock.GetUtcNow() - S(1));
        Assert.Equal(HoldReason.CoolingDown, after.Check(Route("a")).Reason);
        Assert.Empty(fresh.Requests);

        clock.Advance(S(1));
        fresh.Reply(HttpStatusCode.OK, "{}", Limits("trade-fetch", limits, "1:10:0,8:300:0"));
        using var response = await restarted.SendAsync(Get(Url + "/a", "a"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void A_restart_remembers_the_hits_inside_each_window()
    {
        var clock = Clock();
        using var file = new TempCooldownFile();
        var before = new ProviderGovernor(clock, Options, file.Store);
        for (var i = 1; i <= 4; i++)
        {
            Answer(before, Route("fetch"), HttpStatusCode.OK, Limits("fetch", "5:10:60", $"{i}:10:0"));
        }

        clock.Advance(S(2));
        var after = new ProviderGovernor(clock, Options, file.Store);
        var held = after.Check(Route("fetch"));
        Assert.Equal(HoldReason.RateWindow, held.Reason);
        Assert.Equal(T0 + S(11), held.RetryAt);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"version\": 99, \"policies\": [{\"integration\": \"ggg\"}]}")]
    [InlineData("{\"version\": 1, \"policies\": [null, {\"integration\": \"ggg\", \"policy\": \"p\", \"rules\": [{\"name\": \"Ip\", \"windows\": [{\"maxHits\": 0, \"periodSeconds\": 10}]}]}], \"routes\": [{\"integration\": \"ggg\", \"route\": \"fetch\", \"policy\": \"p\"}]}")]
    public void An_unreadable_cooldown_file_starts_in_conservative_mode(string json)
    {
        using var file = new TempCooldownFile();
        Directory.CreateDirectory(Path.GetDirectoryName(file.FilePath)!);
        File.WriteAllText(file.FilePath, json);

        var governor = new ProviderGovernor(Clock(), Options, file.Store);
        var first = governor.TryAcquire(Route("fetch"));
        Assert.NotNull(first.Ticket);
        Assert.Equal(HoldReason.Conservative, governor.Check(Route("fetch")).Reason);
    }
}
