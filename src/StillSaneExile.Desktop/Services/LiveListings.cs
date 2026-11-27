using System.Net;
using System.Net.Http;
using StillSaneExile.Domain.Market;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Desktop.Services;

/// <summary>What the price check searches the trade site through. The app's is <see cref="LiveListings"/>
/// behind the Settings switch; tests and snapshots have none, so nothing they do reaches the site.</summary>
public interface ITradeSite
{
    /// <summary>"Search the trade site from SSE" is on and there is a client to search with.</summary>
    bool IsOn { get; }

    /// <summary>One search of <paramref name="league"/>: the site's count and its cheapest listings.</summary>
    Task<TradePage> SearchAsync(string league, string queryJson, bool buyNowOnly, CancellationToken cancellationToken = default);
}

/// <summary>Searches the official trade site when the player clicks Search, and never otherwise: one
/// search request, then one fetch for the ten cheapest listings. Every request goes through the same
/// <see cref="ProviderGovernor"/> as live prices, under its own integration and routes ("trade": search
/// and fetch), so the site's <c>X-Rate-Limit-*</c> headers are honoured, headroom is kept and a 429 or a
/// remembered cooldown holds it. No cookies, no login, no account data: the requests carry the search and
/// SSE's User-Agent. Nothing here runs on a key press; the HUD has no reference to it.</summary>
public sealed class LiveListings(TradeClient client, ProviderGovernor governor, IDisposable? owned = null) : IDisposable
{
    /// <summary>Longest pacing wait the handler sits out before giving up with a hold the player sees. Long
    /// enough for the 5 s gap of the very first search, before the site has told SSE its limits.</summary>
    public static readonly TimeSpan MaxPacingWait = TimeSpan.FromSeconds(6);

    /// <summary>The app's instance, sharing <paramref name="governor"/> (and its cooldown file) with live prices.</summary>
    public static LiveListings Create(ProviderGovernor governor, string version)
    {
        var http = HttpFor(governor, Network(), version);
        return new LiveListings(new TradeClient(http), governor, http);
    }

    /// <summary>The connection: compressed answers welcome, and cookies off, so the session cookie the site
    /// hands out is never stored or sent back.</summary>
    public static SocketsHttpHandler Network() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        UseCookies = false,
    };

    /// <summary>The chain every trade request takes: the governor in front of <paramref name="network"/> (no
    /// cache: every Search asks afresh), and the same User-Agent as live prices.</summary>
    public static HttpClient HttpFor(ProviderGovernor governor, HttpMessageHandler network, string version)
    {
        var http = new HttpClient(new MarketProviderHandler(governor, cache: null, MaxPacingWait) { InnerHandler = network })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", PoeNinjaClient.UserAgent(version));
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        return http;
    }

    public Task<TradePage> SearchAsync(string league, string queryJson, bool buyNowOnly, CancellationToken cancellationToken = default) =>
        client.SearchAsync(league, queryJson, buyNowOnly, TradeApi.FetchBatch, cancellationToken);

    /// <summary>Turns the trade site back on after it refused access (401/403). Only on the player's say-so:
    /// switching "Search the trade site from SSE" off and on.</summary>
    public void Retry() => governor.Enable(TradeApi.Integration);

    public void Dispose() => owned?.Dispose();
}
