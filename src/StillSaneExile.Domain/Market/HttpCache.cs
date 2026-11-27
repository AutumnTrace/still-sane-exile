using System.Globalization;

namespace StillSaneExile.Domain.Market;

/// <summary>The <c>Cache-Control</c> directives a private client cache acts on. Anything else is ignored.</summary>
public sealed record CacheDirectives(bool NoStore, bool NoCache, bool MustRevalidate, TimeSpan? MaxAge)
{
    public static CacheDirectives None { get; } = new(false, false, false, null);

    /// <summary>Never throws. An unreadable <c>max-age</c> counts as zero (stale at once), as RFC 9111 asks;
    /// <c>no-cache="field"</c> counts as plain <c>no-cache</c>; when <c>max-age</c> repeats, the shortest wins.</summary>
    public static CacheDirectives Parse(string? cacheControl)
    {
        if (string.IsNullOrWhiteSpace(cacheControl))
        {
            return None;
        }

        bool noStore = false, noCache = false, mustRevalidate = false;
        TimeSpan? maxAge = null;
        foreach (var part in cacheControl.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            var name = (eq < 0 ? part : part[..eq]).Trim();
            var value = eq < 0 ? null : part[(eq + 1)..].Trim().Trim('"');
            if (name.Equals("no-store", StringComparison.OrdinalIgnoreCase))
            {
                noStore = true;
            }
            else if (name.Equals("no-cache", StringComparison.OrdinalIgnoreCase))
            {
                noCache = true;
            }
            else if (name.Equals("must-revalidate", StringComparison.OrdinalIgnoreCase))
            {
                mustRevalidate = true;
            }
            else if (name.Equals("max-age", StringComparison.OrdinalIgnoreCase))
            {
                var age = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? TimeSpan.FromSeconds(s) : TimeSpan.Zero;
                maxAge = maxAge is { } m && m < age ? m : age;
            }
        }

        return new CacheDirectives(noStore, noCache, mustRevalidate, maxAge);
    }
}

/// <summary>One stored 200 response. <see cref="FreshUntil"/> is when it must be revalidated; until then it
/// is served without a request. Validators (<see cref="ETag"/>, <see cref="LastModified"/>) make the
/// revalidation a conditional request that a 304 answers without a body.</summary>
public sealed record CachedResponse(
    Uri Uri,
    byte[] Body,
    IReadOnlyList<KeyValuePair<string, string>> ContentHeaders,
    CacheDirectives Directives,
    string? ETag,
    string? LastModified,
    string? Expires,
    DateTimeOffset ValidatedAt,
    DateTimeOffset FreshUntil)
{
    public bool IsFresh(DateTimeOffset now) => !Directives.NoCache && now < FreshUntil;

    /// <summary>Whether a stale copy may still be shown while the provider cannot be asked (cooling down,
    /// circuit open). <c>no-cache</c> and <c>must-revalidate</c> forbid it.</summary>
    public bool MayServeStale => !Directives.NoCache && !Directives.MustRevalidate;

    public bool CanRevalidate => ETag is not null || LastModified is not null;
}

/// <summary>An in-memory private cache for GET responses, keyed by absolute URL. It stores only what
/// <c>Cache-Control</c> allows, serves it while fresh, and otherwise revalidates with
/// <c>If-None-Match</c> / <c>If-Modified-Since</c>. It does not vary by request headers, so it suits one
/// client talking to public market endpoints. Thread-safe.</summary>
public sealed class HttpCache(int capacity = 256)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CachedResponse> _entries = new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public CachedResponse? Find(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        lock (_gate)
        {
            return _entries.GetValueOrDefault(uri.AbsoluteUri);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    internal void Remove(Uri uri)
    {
        lock (_gate)
        {
            _entries.Remove(uri.AbsoluteUri);
        }
    }

    /// <summary>A 200 is worth storing when <c>no-store</c> is absent and it is either fresh for a while or
    /// carries a validator to revalidate with.</summary>
    internal static bool IsStorable(Func<string, string?> header, DateTimeOffset now)
    {
        var directives = CacheDirectives.Parse(header("Cache-Control"));
        if (directives.NoStore || header("Vary")?.Trim() == "*")
        {
            return false;
        }

        return header("ETag") is not null || header("Last-Modified") is not null
            || Lifetime(directives, header("Age"), header("Expires"), now) > TimeSpan.Zero;
    }

    internal CachedResponse Store(Uri uri, Func<string, string?> header, byte[] body, IReadOnlyList<KeyValuePair<string, string>> contentHeaders, DateTimeOffset now)
    {
        var directives = CacheDirectives.Parse(header("Cache-Control"));
        var expires = header("Expires");
        var entry = new CachedResponse(uri, body, contentHeaders, directives, header("ETag"), header("Last-Modified"), expires,
            now, now + Lifetime(directives, header("Age"), expires, now));
        lock (_gate)
        {
            _entries[uri.AbsoluteUri] = entry;
            if (_entries.Count > capacity)
            {
                var oldest = _entries.Values.MinBy(e => e.ValidatedAt);
                if (oldest is not null)
                {
                    _entries.Remove(oldest.Uri.AbsoluteUri);
                }
            }
        }

        return entry;
    }

    /// <summary>A 304 confirmed <paramref name="entry"/>: keep its body, take any new validators and
    /// directives from the 304 (falling back to the stored ones), and restart its freshness from now.</summary>
    internal CachedResponse Refresh(CachedResponse entry, Func<string, string?> header, DateTimeOffset now)
    {
        var cacheControl = header("Cache-Control");
        var directives = cacheControl is null ? entry.Directives : CacheDirectives.Parse(cacheControl);
        var expires = header("Expires") ?? entry.Expires;
        var refreshed = entry with
        {
            Directives = directives,
            ETag = header("ETag") ?? entry.ETag,
            LastModified = header("Last-Modified") ?? entry.LastModified,
            Expires = expires,
            ValidatedAt = now,
            FreshUntil = now + Lifetime(directives, header("Age"), expires, now),
        };

        lock (_gate)
        {
            if (directives.NoStore)
            {
                _entries.Remove(entry.Uri.AbsoluteUri);
            }
            else
            {
                _entries[entry.Uri.AbsoluteUri] = refreshed;
            }
        }

        return refreshed;
    }

    /// <summary>How long a response stays fresh: <c>max-age</c> less <c>Age</c>, else <c>Expires</c>, else zero.
    /// No heuristic freshness: prices change, so without an explicit lifetime we revalidate every time.</summary>
    internal static TimeSpan Lifetime(CacheDirectives directives, string? age, string? expires, DateTimeOffset now)
    {
        if (directives.NoCache)
        {
            return TimeSpan.Zero;
        }

        if (directives.MaxAge is { } maxAge)
        {
            var aged = int.TryParse(age?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? TimeSpan.FromSeconds(s) : TimeSpan.Zero;
            return maxAge > aged ? maxAge - aged : TimeSpan.Zero;
        }

        // An unreadable Expires means "already expired".
        return HttpDates.Parse(expires) is { } at && at > now ? at - now : TimeSpan.Zero;
    }
}
