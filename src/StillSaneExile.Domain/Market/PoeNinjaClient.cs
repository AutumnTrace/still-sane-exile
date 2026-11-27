using System.Net;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Domain.Market;

/// <summary>What one refresh did: the book it built (null when poe.ninja had no prices at all for the
/// league), how many requests reached poe.ninja and how many were answered from the cache, and notes on
/// anything skipped, in plain words.</summary>
public sealed record PoeNinjaRefresh(PoeNinjaBookResult? Result, int NetworkRequests, int CachedAnswers, IReadOnlyList<string> Notes)
{
    public PriceBook? Book => Result?.Book;
}

/// <summary>Reads poe.ninja's public PoE2 economy API: the league list, and per league one overview per
/// configured category. Every request is tagged with its route, so the <see cref="MarketProviderHandler"/>
/// in front of the <see cref="HttpClient"/> can space, cache and hold it; this class never retries, waits
/// or opens a connection of its own. The app supplies the client, its handler chain and its User-Agent.
/// Requests carry only the league and category names: nothing about the player.</summary>
public sealed class PoeNinjaClient(HttpClient http)
{
    public const string Integration = "poe.ninja";

    public const string LeaguesRoute = "poe2/api/data/index-state";

    public const string ExchangeRoute = "poe2/api/economy/exchange/current/overview";

    public const string ItemsRoute = "poe2/api/economy/stash/current/item/overview";

    public const string ProjectUrl = "https://github.com/AutumnTrace/still-sane-exile";

    public static Uri Site { get; } = new("https://poe.ninja/");

    public static Uri LeaguesUri { get; } = new(Site, LeaguesRoute);

    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <summary>"StillSaneExile/0.3.0 (+https://github.com/AutumnTrace/still-sane-exile)": who is asking and
    /// where to find out more. Nothing about the player or their machine.</summary>
    public static string UserAgent(string version) => $"StillSaneExile/{version} (+{ProjectUrl})";

    /// <summary>The overview URL as poe.ninja's own pages build it (spaces as '+').</summary>
    public static Uri OverviewUri(PoeNinjaCategory category, string league)
    {
        ArgumentNullException.ThrowIfNull(category);
        return new Uri(Site, $"{RouteFor(category)}?league={WebUtility.UrlEncode(league)}&type={WebUtility.UrlEncode(category.Type)}");
    }

    public static string RouteFor(PoeNinjaCategory category) =>
        category.Overview == PoeNinjaOverview.Exchange ? ExchangeRoute : ItemsRoute;

    /// <summary>poe.ninja's current economy leagues. Throws <see cref="HttpRequestException"/> on a failed
    /// answer and <see cref="ProviderHoldException"/> while the governor holds poe.ninja.</summary>
    public async Task<IReadOnlyList<PoeNinjaLeague>> LeaguesAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LeaguesUri).ForRoute(Integration, LeaguesRoute);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return PoeNinjaLeagues.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Fetches every category in <paramref name="catalog"/> one after another and builds the
    /// league's price book. A category that answers 200 with no lines, or 4xx other than 429, is skipped
    /// with a note (types change between leagues). Anything else ends the refresh at once: a 429 or 5xx as
    /// <see cref="HttpRequestException"/>, a hold as <see cref="ProviderHoldException"/>, so nothing is
    /// half-published and the caller keeps its last good snapshot.</summary>
    public async Task<PoeNinjaRefresh> RefreshAsync(string league, PoeNinjaCatalog catalog, TimeProvider time, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(league);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(time);

        var payloads = new List<PoeNinjaPayload>();
        var notes = new List<string>();
        int network = 0, cached = 0;
        foreach (var category in catalog.Categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, OverviewUri(category, league)).ForRoute(Integration, RouteFor(category));
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (MarketRequest.SourceOf(response) == ResponseSource.Cache)
            {
                cached++;
            }
            else
            {
                network++;
            }

            var code = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                payloads.Add(new PoeNinjaPayload(category, body, time.GetUtcNow()));
            }
            else if (code is >= 400 and < 500 && code != 429)
            {
                notes.Add($"{category.Type}: poe.ninja answered {code}, so it was skipped.");
            }
            else
            {
                throw new HttpRequestException($"poe.ninja answered {code} for {category.Type}.", null, response.StatusCode);
            }
        }

        var built = PoeNinjaBook.Build(league, payloads, time.GetUtcNow());
        notes.AddRange(built.Notes);
        return new PoeNinjaRefresh(built.HasPrices ? built : null, network, cached, notes);
    }
}
