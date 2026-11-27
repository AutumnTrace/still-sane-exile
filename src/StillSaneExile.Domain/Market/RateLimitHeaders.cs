using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace StillSaneExile.Domain.Market;

/// <summary>One window of one rule, as the server reported it: at most <see cref="MaxHits"/> requests per
/// <see cref="Period"/>, or a <see cref="Penalty"/> restriction if exceeded. <see cref="CurrentHits"/> is
/// what the server has counted in the current period (from every client on the same IP or account, not
/// only this one); <see cref="Restricted"/> is the time left on an active restriction.</summary>
public sealed record RateLimitWindow(int MaxHits, TimeSpan Period, TimeSpan Penalty, int CurrentHits, TimeSpan Restricted);

/// <summary>A named rule (GGG uses names such as <c>Ip</c> and <c>Account</c>) and its windows.</summary>
public sealed record RateLimitRule(string Name, IReadOnlyList<RateLimitWindow> Windows);

/// <summary>A rate-limit policy. Every route that answers with the same policy name draws on the same
/// buckets, so a restriction earned on one of them holds all of them.</summary>
public sealed record RateLimitPolicy(string Name, IReadOnlyList<RateLimitRule> Rules)
{
    public IEnumerable<RateLimitWindow> Windows => Rules.SelectMany(r => r.Windows);

    /// <summary>The longest active restriction across every window of every rule.</summary>
    public TimeSpan LongestRestriction => Windows.Select(w => w.Restricted).DefaultIfEmpty(TimeSpan.Zero).Max();

    /// <summary>The harshest penalty any window would give: the fallback length of a 429 that says nothing else.</summary>
    public TimeSpan LongestPenalty => Windows.Select(w => w.Penalty).DefaultIfEmpty(TimeSpan.Zero).Max();
}

public enum RateLimitHeaderStatus
{
    /// <summary>No rate-limit headers at all: the provider does not publish its limits for this route.</summary>
    Absent,
    /// <summary>A complete, well-formed set: policy, rules, and limits plus state for every rule.</summary>
    Valid,
    /// <summary>Some headers are there but not all, or a value cannot be read. Treated like absent.</summary>
    Malformed,
}

/// <summary>What one response said about its rate limits. <see cref="Policy"/> is set only when
/// <see cref="Status"/> is <see cref="RateLimitHeaderStatus.Valid"/>; <see cref="Detail"/> says what was
/// wrong otherwise, in words fit for a log.</summary>
public sealed record RateLimitReading(RateLimitHeaderStatus Status, RateLimitPolicy? Policy, string Detail)
{
    public static RateLimitReading Absent { get; } = new(RateLimitHeaderStatus.Absent, null, "no rate-limit headers");

    public static RateLimitReading Malformed(string detail) => new(RateLimitHeaderStatus.Malformed, null, detail);
}

/// <summary>Reads GGG's documented rate-limit headers: <c>X-Rate-Limit-Policy</c>, <c>X-Rate-Limit-Rules</c>,
/// and per rule <c>X-Rate-Limit-&lt;Rule&gt;</c> (<c>hits:period:penalty,…</c>) and
/// <c>X-Rate-Limit-&lt;Rule&gt;-State</c> (<c>hits:period:restricted,…</c>), all in seconds. Never throws:
/// anything it cannot read in full comes back as <see cref="RateLimitHeaderStatus.Malformed"/>, because a
/// half-understood limit is more dangerous than an unknown one, and unknown limits get the conservative
/// mode.</summary>
public static class RateLimitHeaders
{
    public const string PolicyHeader = "X-Rate-Limit-Policy";
    public const string RulesHeader = "X-Rate-Limit-Rules";

    // Far above anything GGG publishes, low enough that a broken header cannot make us allocate or wait absurdly.
    private const int MaxHits = 10_000;
    private const int MaxSeconds = 1_000_000;
    private const int MaxRules = 8;
    private const int MaxWindows = 8;
    private const int MaxNameLength = 64;

    /// <param name="header">Returns a header's raw value (several values joined by commas), or null when absent.</param>
    public static RateLimitReading Read(Func<string, string?> header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var policy = header(PolicyHeader)?.Trim();
        var rules = header(RulesHeader)?.Trim();
        if (policy is null && rules is null)
        {
            return RateLimitReading.Absent;
        }

        if (!IsName(policy))
        {
            return RateLimitReading.Malformed($"{PolicyHeader} is missing or unreadable ('{Shorten(policy)}')");
        }

        if (string.IsNullOrEmpty(rules))
        {
            return RateLimitReading.Malformed($"{RulesHeader} is missing for policy '{policy}'");
        }

        var names = rules.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length is 0 or > MaxRules)
        {
            return RateLimitReading.Malformed($"{RulesHeader} '{Shorten(rules)}' does not list between 1 and {MaxRules} rules");
        }

        var parsed = new List<RateLimitRule>();
        foreach (var name in names)
        {
            if (!IsName(name))
            {
                return RateLimitReading.Malformed($"rule name '{Shorten(name)}' is unreadable");
            }

            if (parsed.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return RateLimitReading.Malformed($"rule '{name}' is listed twice");
            }

            var limitsHeader = $"X-Rate-Limit-{name}";
            var stateHeader = $"{limitsHeader}-State";
            var limits = header(limitsHeader);
            var state = header(stateHeader);
            if (limits is null || state is null)
            {
                return RateLimitReading.Malformed($"{(limits is null ? limitsHeader : stateHeader)} is missing");
            }

            if (!TryTriples(limits, out var limitTriples))
            {
                return RateLimitReading.Malformed($"{limitsHeader} '{Shorten(limits)}' is not hits:period:penalty");
            }

            if (!TryTriples(state, out var stateTriples))
            {
                return RateLimitReading.Malformed($"{stateHeader} '{Shorten(state)}' is not hits:period:restricted");
            }

            if (limitTriples.Count != stateTriples.Count)
            {
                return RateLimitReading.Malformed($"{limitsHeader} has {limitTriples.Count} windows but its state has {stateTriples.Count}");
            }

            var windows = new List<RateLimitWindow>();
            foreach (var (hits, period, penalty) in limitTriples)
            {
                if (hits is < 1 or > MaxHits || period is < 1 or > MaxSeconds || penalty > MaxSeconds)
                {
                    return RateLimitReading.Malformed($"{limitsHeader} window {hits}:{period}:{penalty} is out of range");
                }

                if (windows.Any(w => w.Period.TotalSeconds == period))
                {
                    return RateLimitReading.Malformed($"{limitsHeader} lists the {period} s window twice");
                }

                var matching = stateTriples.Where(s => s.B == period).ToList();
                if (matching.Count != 1)
                {
                    return RateLimitReading.Malformed($"{stateHeader} has no single entry for the {period} s window");
                }

                var (current, _, restricted) = matching[0];
                if (current > MaxHits || restricted > MaxSeconds)
                {
                    return RateLimitReading.Malformed($"{stateHeader} entry {current}:{period}:{restricted} is out of range");
                }

                windows.Add(new RateLimitWindow(hits, Seconds(period), Seconds(penalty), current, Seconds(restricted)));
            }

            parsed.Add(new RateLimitRule(name, windows));
        }

        return new RateLimitReading(RateLimitHeaderStatus.Valid, new RateLimitPolicy(policy, parsed), "ok");
    }

    /// <summary>How long a <c>Retry-After</c> value asks us to wait from <paramref name="now"/>: delta seconds or
    /// an HTTP date. A date in the past means zero. Null when absent or unreadable.</summary>
    public static TimeSpan? RetryAfter(string? value, DateTimeOffset now)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v))
        {
            return null;
        }

        if (long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(Math.Min(seconds, int.MaxValue));
        }

        return HttpDates.Parse(v) is { } date ? (date > now ? date - now : TimeSpan.Zero) : null;
    }

    private static bool TryTriples(string value, out List<(int A, int B, int C)> triples)
    {
        triples = [];
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > MaxWindows)
        {
            return false;
        }

        foreach (var part in parts)
        {
            var n = part.Split(':', StringSplitOptions.TrimEntries);
            if (n.Length != 3 || !Int(n[0], out var a) || !Int(n[1], out var b) || !Int(n[2], out var c))
            {
                return false;
            }

            triples.Add((a, b, c));
        }

        return true;
    }

    private static bool Int(string s, out int value) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static TimeSpan Seconds(int s) => TimeSpan.FromSeconds(s);

    /// <summary>Policy and rule names become header names and dictionary keys, so only plain tokens pass.</summary>
    private static bool IsName([NotNullWhen(true)] string? s) =>
        !string.IsNullOrEmpty(s) && s.Length <= MaxNameLength && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    private static string Shorten(string? s) => s is null ? "" : s.Length <= 60 ? s : s[..57] + "...";
}

/// <summary>HTTP-date parsing for <c>Retry-After</c> and <c>Expires</c>: IMF-fixdate plus the two obsolete
/// forms RFC 9110 still asks recipients to accept.</summary>
internal static class HttpDates
{
    private static readonly string[] Formats =
    [
        "r",
        "ddd, d MMM yyyy HH':'mm':'ss 'GMT'",
        "dddd, dd'-'MMM'-'yy HH':'mm':'ss 'GMT'",
        "ddd MMM d HH':'mm':'ss yyyy",
    ];

    public static DateTimeOffset? Parse(string? value) =>
        DateTimeOffset.TryParseExact(value?.Trim(), Formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowInnerWhite | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date
            : null;
}
