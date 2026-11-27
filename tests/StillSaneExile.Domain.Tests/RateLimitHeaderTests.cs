using StillSaneExile.Domain.Market;
using static StillSaneExile.Domain.Tests.MarketTest;

namespace StillSaneExile.Domain.Tests;

public class RateLimitHeaderTests
{
    private static readonly DateTimeOffset Now = Fixtures.DemoNow;

    [Fact]
    public void Reads_the_policy_every_rule_and_every_window()
    {
        var reading = RateLimitHeaders.Read(Headers(
            ("X-Rate-Limit-Policy", "trade-search-request-limit"),
            ("X-Rate-Limit-Rules", "Ip,Account"),
            ("X-Rate-Limit-Ip", "5:10:60, 30:300:1800"),
            ("X-Rate-Limit-Ip-State", "30:300:0,2:10:0"),
            ("X-Rate-Limit-Account", "45:60:120"),
            ("X-Rate-Limit-Account-State", "3:60:17")));

        Assert.Equal(RateLimitHeaderStatus.Valid, reading.Status);
        var policy = reading.Policy!;
        Assert.Equal("trade-search-request-limit", policy.Name);
        Assert.Equal(["Ip", "Account"], policy.Rules.Select(r => r.Name));

        // State entries are matched to their window by period, not by position.
        Assert.Equal(
            [new RateLimitWindow(5, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), 2, TimeSpan.Zero),
             new RateLimitWindow(30, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(1800), 30, TimeSpan.Zero)],
            policy.Rules[0].Windows);
        Assert.Equal(new RateLimitWindow(45, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), 3, TimeSpan.FromSeconds(17)), policy.Rules[1].Windows.Single());
        Assert.Equal(TimeSpan.FromSeconds(17), policy.LongestRestriction);
        Assert.Equal(TimeSpan.FromSeconds(1800), policy.LongestPenalty);
    }

    [Fact]
    public void No_rate_limit_headers_read_as_absent()
    {
        var reading = RateLimitHeaders.Read(Headers(("Content-Type", "application/json")));
        Assert.Equal(RateLimitHeaderStatus.Absent, reading.Status);
        Assert.Null(reading.Policy);
    }

    [Theory]
    [InlineData(null, "Ip", "5:10:60", "1:10:0")] // rules without a policy
    [InlineData("", "Ip", "5:10:60", "1:10:0")]
    [InlineData("bad policy", "Ip", "5:10:60", "1:10:0")]
    [InlineData("p", null, "5:10:60", "1:10:0")] // policy without rules
    [InlineData("p", "", "5:10:60", "1:10:0")]
    [InlineData("p", " , ", "5:10:60", "1:10:0")]
    [InlineData("p", "Ip", null, "1:10:0")] // limits missing
    [InlineData("p", "Ip", "5:10:60", null)] // state missing
    [InlineData("p", "Ip", "", "")]
    [InlineData("p", "Ip", "5:10", "1:10:0")] // two fields
    [InlineData("p", "Ip", "5:10:60:9", "1:10:0")] // four fields
    [InlineData("p", "Ip", "five:10:60", "1:10:0")]
    [InlineData("p", "Ip", "5:10:60", "1:10:zero")]
    [InlineData("p", "Ip", "-5:10:60", "1:10:0")]
    [InlineData("p", "Ip", "5.5:10:60", "1:10:0")]
    [InlineData("p", "Ip", "0:10:60", "0:10:0")] // no hits allowed
    [InlineData("p", "Ip", "5:0:60", "1:0:0")] // zero-length window
    [InlineData("p", "Ip", "99999999999:10:60", "1:10:0")] // overflow
    [InlineData("p", "Ip", "50000:10:60", "1:10:0")] // out of range
    [InlineData("p", "Ip", "5:10:60", "1:10:99999999")]
    [InlineData("p", "Ip", "5:10:60,30:300:1800", "1:10:0")] // state is missing a window
    [InlineData("p", "Ip", "5:10:60", "1:20:0")] // state is for another window
    [InlineData("p", "Ip", "5:10:60,6:10:60", "1:10:0,1:10:0")] // the same window twice
    [InlineData("p", "Ip,Ip", "5:10:60", "1:10:0")] // the same rule twice
    [InlineData("p", "Ip,Account", "5:10:60", "1:10:0")] // second rule has no headers
    [InlineData("p", "Ip Account", "5:10:60", "1:10:0")]
    public void Malformed_or_partial_headers_never_throw_and_read_as_malformed(string? policy, string? rules, string? limits, string? state)
    {
        var headers = new List<(string, string)>();
        if (policy is not null)
        {
            headers.Add(("X-Rate-Limit-Policy", policy));
        }

        if (rules is not null)
        {
            headers.Add(("X-Rate-Limit-Rules", rules));
        }

        if (limits is not null)
        {
            headers.Add(("X-Rate-Limit-Ip", limits));
        }

        if (state is not null)
        {
            headers.Add(("X-Rate-Limit-Ip-State", state));
        }

        var reading = RateLimitHeaders.Read(Headers([.. headers]));
        Assert.Equal(RateLimitHeaderStatus.Malformed, reading.Status);
        Assert.Null(reading.Policy);
        Assert.NotEmpty(reading.Detail);
    }

    [Theory]
    [InlineData("120", 120)]
    [InlineData(" 7 ", 7)]
    [InlineData("0", 0)]
    [InlineData("Fri, 27 Nov 2026 11:32:00 GMT", 120)] // IMF-fixdate, two minutes after Now
    [InlineData("Friday, 27-Nov-26 11:31:00 GMT", 60)] // obsolete RFC 850 form
    [InlineData("Fri Nov 27 11:30:30 2026", 30)] // obsolete asctime form
    [InlineData("Fri, 27 Nov 2026 11:00:00 GMT", 0)] // already past
    public void Retry_after_reads_seconds_and_http_dates(string value, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RateLimitHeaders.RetryAfter(value, Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("27/11/2026 11:32")]
    public void Unreadable_retry_after_is_null(string? value) =>
        Assert.Null(RateLimitHeaders.RetryAfter(value, Now));

    [Fact]
    public void Huge_retry_after_does_not_overflow() =>
        Assert.Equal(TimeSpan.FromSeconds(int.MaxValue), RateLimitHeaders.RetryAfter("99999999999999", Now));

    [Theory]
    [InlineData("max-age=600", false, false, false, 600)]
    [InlineData("public, max-age=\"60\"", false, false, false, 60)]
    [InlineData("MAX-AGE=60, max-age=30", false, false, false, 30)] // repeated: the shortest wins
    [InlineData("max-age=soon", false, false, false, 0)] // unreadable: stale at once
    [InlineData("no-store", true, false, false, null)]
    [InlineData("no-cache", false, true, false, null)]
    [InlineData("no-cache=\"Set-Cookie\", max-age=60", false, true, false, 60)]
    [InlineData("private, must-revalidate, max-age=0", false, false, true, 0)]
    [InlineData(",,, ,", false, false, false, null)]
    [InlineData("", false, false, false, null)]
    public void Cache_control_directives(string value, bool noStore, bool noCache, bool mustRevalidate, int? maxAge)
    {
        var d = CacheDirectives.Parse(value);
        Assert.Equal(new CacheDirectives(noStore, noCache, mustRevalidate, maxAge is { } s ? TimeSpan.FromSeconds(s) : null), d);
    }
}
