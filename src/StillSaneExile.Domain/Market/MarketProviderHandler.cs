using System.Net;
using System.Net.Http.Headers;

namespace StillSaneExile.Domain.Market;

/// <summary>Where a response handed back by <see cref="MarketProviderHandler"/> came from.</summary>
public enum ResponseSource
{
    /// <summary>A real answer from the provider.</summary>
    Network,
    /// <summary>A fresh cached copy: no request was made.</summary>
    Cache,
    /// <summary>The provider answered 304 Not Modified, so this is the cached body, confirmed current.</summary>
    Revalidated,
}

/// <summary>Tags a request with its integration and route. Untagged requests use the host as the integration
/// and the path as the route, which is wrong for templated URLs (each digest hour would look like a new
/// route with unknown limits), so callers should tag.</summary>
public static class MarketRequest
{
    public static HttpRequestOptionsKey<string> IntegrationOption { get; } = new("StillSaneExile.Market.Integration");

    public static HttpRequestOptionsKey<string> RouteOption { get; } = new("StillSaneExile.Market.Route");

    public static HttpRequestOptionsKey<ResponseSource> SourceOption { get; } = new("StillSaneExile.Market.Source");

    public static HttpRequestMessage ForRoute(this HttpRequestMessage request, string integration, string route)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(IntegrationOption, integration);
        request.Options.Set(RouteOption, route);
        return request;
    }

    public static RouteKey RouteOf(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uri = request.RequestUri is { IsAbsoluteUri: true } u ? u : null;
        var integration = request.Options.TryGetValue(IntegrationOption, out var i) ? i : uri?.Host ?? "";
        var route = request.Options.TryGetValue(RouteOption, out var r) ? r : uri?.AbsolutePath ?? "";
        return new RouteKey(integration, route);
    }

    /// <summary>Network, cache or revalidated; null when the response did not pass through the handler.</summary>
    public static ResponseSource? SourceOf(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.RequestMessage?.Options.TryGetValue(SourceOption, out var s) == true ? s : null;
    }
}

/// <summary>Thrown by <see cref="MarketProviderHandler"/> instead of sending. Nothing reached the provider.
/// <see cref="Decision"/> says why (cooling down, circuit open, disabled after 401/403...) and when to try
/// again; <see cref="Stale"/> is the last cached copy when its headers allow showing it while we wait.</summary>
public sealed class ProviderHoldException(RouteKey route, SendDecision decision, CachedResponse? stale = null)
    : Exception(decision.Message)
{
    public RouteKey Route { get; } = route;

    public SendDecision Decision { get; } = decision;

    public CachedResponse? Stale { get; } = stale;
}

/// <summary>Puts <see cref="ProviderGovernor"/> and <see cref="HttpCache"/> in front of any HttpClient. Fresh
/// GETs come from the cache; stale ones are revalidated; everything else asks the governor first. Short
/// pacing holds (headroom, conservative spacing) are waited out, up to <c>maxPacingWait</c>; any other
/// hold throws <see cref="ProviderHoldException"/> at once rather than sleeping through a cooldown. It
/// never retries: a 4xx or 5xx goes back to the caller as it came, and the governor spaces the next try.</summary>
public sealed class MarketProviderHandler(ProviderGovernor governor, HttpCache? cache = null, TimeSpan? maxPacingWait = null)
    : DelegatingHandler
{
    public static TimeSpan DefaultMaxPacingWait { get; } = TimeSpan.FromSeconds(30);

    // Only reached if the governor ever returned a hold time that has already passed: avoids a busy loop.
    private static readonly TimeSpan MinimumPause = TimeSpan.FromMilliseconds(100);

    private readonly ProviderGovernor _governor = governor ?? throw new ArgumentNullException(nameof(governor));
    private readonly TimeSpan _maxPacingWait = maxPacingWait ?? DefaultMaxPacingWait;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var route = MarketRequest.RouteOf(request);
        var time = _governor.Time;

        // Caller-made conditional requests are passed through untouched: they manage their own copy.
        var store = cache is not null && request.Method == HttpMethod.Get && !HasConditionals(request) ? cache : null;
        var uri = request.RequestUri is { IsAbsoluteUri: true } u ? u : null;
        CachedResponse? stored = null;
        if (store is not null && uri is not null)
        {
            stored = store.Find(uri);
            if (stored is not null && stored.IsFresh(time.GetUtcNow()))
            {
                return Replay(request, stored, ResponseSource.Cache);
            }

            AddValidators(request, stored);
        }

        var ticket = await AcquireAsync(route, stored, cancellationToken).ConfigureAwait(false);
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _governor.Abandon(ticket);
            throw;
        }
        catch
        {
            _governor.RecordFailure(ticket);
            throw;
        }

        _governor.Record(ticket, response);
        request.Options.Set(MarketRequest.SourceOption, ResponseSource.Network);
        if (store is null || uri is null)
        {
            return response;
        }

        var now = time.GetUtcNow();
        var header = ResponseHeaders.Of(response);
        if (stored is not null && response.StatusCode == HttpStatusCode.NotModified)
        {
            var refreshed = store.Refresh(stored, header, now);
            response.Dispose();
            return Replay(request, refreshed, ResponseSource.Revalidated);
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return response;
        }

        if (!HttpCache.IsStorable(header, now))
        {
            store.Remove(uri);
            return response;
        }

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var contentHeaders = CopyableContentHeaders(response.Content.Headers);
        store.Store(uri, header, body, contentHeaders, now);

        var buffered = new ByteArrayContent(body);
        foreach (var (name, value) in contentHeaders)
        {
            buffered.Headers.TryAddWithoutValidation(name, value);
        }

        CopyValidators(response.Content.Headers, buffered.Headers);
        response.Content.Dispose();
        response.Content = buffered;
        return response;
    }

    private async Task<SendTicket> AcquireAsync(RouteKey route, CachedResponse? stored, CancellationToken cancellationToken)
    {
        var time = _governor.Time;
        var deadline = time.GetUtcNow() + _maxPacingWait;
        while (true)
        {
            var decision = _governor.TryAcquire(route);
            if (decision.Ticket is { } ticket)
            {
                return ticket;
            }

            if (!decision.IsPacing || decision.RetryAt is not { } at || at > deadline)
            {
                throw new ProviderHoldException(route, decision, stored is { MayServeStale: true } ? stored : null);
            }

            var delay = at - time.GetUtcNow();
            await Task.Delay(delay > TimeSpan.Zero ? delay : MinimumPause, time, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool HasConditionals(HttpRequestMessage request) =>
        request.Headers.NonValidated.Contains("If-None-Match") || request.Headers.NonValidated.Contains("If-Modified-Since");

    private static void AddValidators(HttpRequestMessage request, CachedResponse? stored)
    {
        if (stored?.ETag is { } tag)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", tag);
        }

        if (stored?.LastModified is { } date)
        {
            request.Headers.TryAddWithoutValidation("If-Modified-Since", date);
        }
    }

    private static HttpResponseMessage Replay(HttpRequestMessage request, CachedResponse entry, ResponseSource source)
    {
        var content = new ByteArrayContent(entry.Body);
        foreach (var (name, value) in entry.ContentHeaders)
        {
            content.Headers.TryAddWithoutValidation(name, value);
        }

        if (entry.LastModified is { } lastModified)
        {
            content.Headers.TryAddWithoutValidation("Last-Modified", lastModified);
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request };
        if (entry.ETag is { } tag)
        {
            response.Headers.TryAddWithoutValidation("ETag", tag);
        }

        request.Options.Set(MarketRequest.SourceOption, source);
        return response;
    }

    /// <summary>Content headers worth replaying (type, encoding, language...). Length is recomputed from the
    /// stored body; validators and expiry are kept on the entry itself, so a 304 can update them.</summary>
    private static List<KeyValuePair<string, string>> CopyableContentHeaders(HttpContentHeaders headers)
    {
        var copy = new List<KeyValuePair<string, string>>();
        foreach (var (name, values) in headers.NonValidated)
        {
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Last-Modified", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Expires", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in values)
            {
                copy.Add(new KeyValuePair<string, string>(name, value));
            }
        }

        return copy;
    }

    private static void CopyValidators(HttpContentHeaders from, HttpContentHeaders to)
    {
        foreach (var name in (string[])["Last-Modified", "Expires"])
        {
            if (from.NonValidated.TryGetValues(name, out var values))
            {
                foreach (var value in values)
                {
                    to.TryAddWithoutValidation(name, value);
                }
            }
        }
    }
}

/// <summary>Raw header lookup over a response's own and content headers, without validation, so the
/// parsers see exactly what the server sent, malformed values included.</summary>
internal static class ResponseHeaders
{
    public static Func<string, string?> Of(HttpResponseMessage response) => name =>
        Join(response.Headers.NonValidated, name) ?? Join(response.Content.Headers.NonValidated, name);

    private static string? Join(HttpHeadersNonValidated headers, string name) =>
        headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;
}
