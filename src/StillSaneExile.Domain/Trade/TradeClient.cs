using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StillSaneExile.Domain.Market;

namespace StillSaneExile.Domain.Trade;

/// <summary>The trade site turned a request down (4xx or 5xx). <see cref="SiteMessage"/> is the site's
/// own reason when it gave one ("Invalid query").</summary>
public sealed class TradeSiteException(HttpStatusCode status, string? siteMessage, TimeSpan? retryAfter = null)
    : HttpRequestException(siteMessage is null ? $"The trade site answered {(int)status}." : $"The trade site answered {(int)status}: {siteMessage}", null, status)
{
    public string? SiteMessage { get; } = siteMessage;

    /// <summary>On a 429: how long the site asked to wait (its <c>Retry-After</c>, or the longest restriction
    /// in its rate-limit state), when it said.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>The site's <c>{"error":{"message":…}}</c>, if the body has one.</summary>
    public static string? MessageIn(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Searches the official trade site for one query: one search request, then one fetch per ten
/// listings wanted. Every request is tagged with its route (<see cref="TradeApi.Integration"/>, search or
/// fetch), so the <see cref="MarketProviderHandler"/> in front of the <see cref="HttpClient"/> spaces and
/// holds it by the site's own <c>X-Rate-Limit-*</c> headers. This class never retries, waits or logs in:
/// the requests carry the query and nothing else. The app supplies the client and its User-Agent.</summary>
public sealed class TradeClient(HttpClient http)
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <summary>Searches <paramref name="league"/> with <paramref name="queryJson"/> (from <see cref="TradeQuery"/>),
    /// cheapest first, and fetches the first <paramref name="count"/> listings. Throws
    /// <see cref="ProviderHoldException"/> while the governor holds the site (nothing was sent),
    /// <see cref="TradeSiteException"/> when the site turns a request down, <see cref="HttpRequestException"/>
    /// when it cannot be reached and <see cref="JsonException"/> when its answer has another shape.</summary>
    /// <param name="instantBuyoutOnly">The query asks for instant buyout only ("securable").</param>
    public async Task<TradePage> SearchAsync(string league, string queryJson, bool instantBuyoutOnly, int count = TradeApi.FetchBatch, CancellationToken cancellationToken = default)
    {
        var content = new StringContent(TradeApi.SearchBody(queryJson), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        string body;
        using (var request = new HttpRequestMessage(HttpMethod.Post, TradeApi.SearchUri(league)) { Content = content }.ForRoute(TradeApi.Integration, TradeApi.SearchRoute))
        {
            body = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var answer = TradeApi.ParseSearch(body);
        var listings = new List<TradeListing>();
        foreach (var batch in answer.Ids.Take(Math.Max(0, count)).Chunk(TradeApi.FetchBatch))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, TradeApi.FetchUri(batch, answer.Id)).ForRoute(TradeApi.Integration, TradeApi.FetchRoute);
            listings.AddRange(TradeApi.ParseFetch(await SendAsync(request, cancellationToken).ConfigureAwait(false), instantBuyoutOnly));
        }

        return new TradePage(answer.Id, answer.Total, listings);
    }

    /// <summary>A governor hold in the words the price check shows ("The trade site asks to wait 12 s.").</summary>
    public static string HoldText(SendDecision decision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var wait = decision.RetryAt is { } at ? Wait(at - now) : "a while";
        return decision.Reason switch
        {
            HoldReason.Disabled => "The trade site refused SSE's requests, so SSE stopped asking. Switch \"Search the trade site from SSE\" off and on in Settings to try again.",
            HoldReason.CircuitOpen => $"The trade site turned down several searches in a row. Try again in {wait}.",
            HoldReason.ServerBackoff => $"The trade site had a problem or could not be reached. Try again in {wait}.",
            _ => $"The trade site asks to wait {wait}.",
        };
    }

    /// <summary>"12 s", "3 min", "2 h": rounded up, so the player never tries too early.</summary>
    public static string Wait(TimeSpan span)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(span.TotalSeconds));
        return seconds < 100 ? $"{seconds} s"
            : seconds < 2 * 3600 ? $"{(int)Math.Ceiling(seconds / 60.0)} min"
            : $"{(int)Math.Ceiling(seconds / 3600.0)} h";
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new TradeSiteException(response.StatusCode, TradeSiteException.MessageIn(body), response.StatusCode == HttpStatusCode.TooManyRequests ? AskedWait(response) : null);
        }

        return body;
    }

    /// <summary>The longer of a 429's <c>Retry-After</c> and its rate-limit restriction, or null.</summary>
    private static TimeSpan? AskedWait(HttpResponseMessage response)
    {
        string? Header(string name) => response.Headers.NonValidated.TryGetValues(name, out var values) ? string.Join(", ", values) : null;
        var retry = RateLimitHeaders.RetryAfter(Header("Retry-After"), response.Headers.Date ?? DateTimeOffset.UtcNow) ?? TimeSpan.Zero;
        var restricted = RateLimitHeaders.Read(Header).Policy?.LongestRestriction ?? TimeSpan.Zero;
        var wait = retry > restricted ? retry : restricted;
        return wait > TimeSpan.Zero ? wait : null;
    }
}
