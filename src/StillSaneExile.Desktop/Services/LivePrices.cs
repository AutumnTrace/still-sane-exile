using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Market;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Services;

/// <summary>Keeps the live league's prices from poe.ninja up to date: a few seconds after start (with
/// jitter), unless the last snapshot is less than an hour old, then hourly. Every request goes through
/// the polite market handler (one at a time, at least 5 s apart, cache headers honoured, holds obeyed and
/// remembered across restarts). Each finished refresh is saved as an immutable snapshot and offered to
/// the shell, only if it is still for the league being tracked: a league switch cancels the refresh in
/// flight and anything that still comes back for the old league is dropped. Nothing here runs on a key
/// press; the HUD only reads the session's book and has no network client.</summary>
public sealed partial class LivePrices : ObservableObject, IDisposable
{
    public static readonly TimeSpan Cadence = TimeSpan.FromHours(1);

    /// <summary>Wait after a refresh that failed without a hold (offline, a 5xx): the governor's own backoff
    /// is shorter, but there is no point asking for twenty categories again sooner.</summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(10);

    private static readonly RouteKey Exchange = new(PoeNinjaClient.Integration, PoeNinjaClient.ExchangeRoute);

    private readonly PoeNinjaClient _client;
    private readonly ProviderGovernor _governor;
    private readonly PriceBookArchive _archive;
    private readonly PoeNinjaCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly Dispatcher _dispatcher;
    private readonly Func<TimeSpan> _startDelay;
    private readonly Func<TimeSpan> _jitter;
    private readonly IDisposable? _owned;
    private ITimer? _timer;
    private CancellationTokenSource? _inFlight;
    private int _generation;
    private bool _disposed;

    /// <param name="startDelay">How long after start (or a league switch) the first refresh waits. Default 5–15 s.</param>
    /// <param name="jitter">Added to each hourly wait so copies of the app do not line up. Default 0–60 s.</param>
    /// <param name="owned">Disposed with this object (the HttpClient <see cref="Create"/> made).</param>
    public LivePrices(PoeNinjaClient client, ProviderGovernor governor, PriceBookArchive archive, PoeNinjaCatalog catalog,
        TimeProvider time, Dispatcher dispatcher, Func<TimeSpan>? startDelay = null, Func<TimeSpan>? jitter = null, IDisposable? owned = null)
    {
        _client = client;
        _governor = governor;
        _archive = archive;
        _catalog = catalog;
        _time = time;
        _dispatcher = dispatcher;
        _startDelay = startDelay ?? (() => TimeSpan.FromMilliseconds(Random.Shared.Next(5_000, 15_001)));
        _jitter = jitter ?? (() => TimeSpan.FromSeconds(Random.Shared.Next(0, 61)));
        _owned = owned;
    }

    /// <summary>The one governor every market request of the app goes through (poe.ninja and the trade
    /// site), with its cooldowns kept in the data folder. One instance, so the file has one writer.</summary>
    public static ProviderGovernor Governor(AppPaths paths) =>
        new(TimeProvider.System, store: new JsonFileCooldownStore(paths.ProviderState));

    /// <summary>The app's instance: poe.ninja over HTTPS, through <paramref name="governor"/>.</summary>
    public static LivePrices Create(AppPaths paths, string version, Dispatcher dispatcher, ProviderGovernor governor)
    {
        var time = governor.Time;
        var network = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            UseCookies = false,
        };
        var http = HttpFor(governor, network, version);
        return new LivePrices(new PoeNinjaClient(http), governor, new PriceBookArchive(paths.Prices), LoadCatalog(paths), time, dispatcher, owned: http);
    }

    /// <summary>The chain every poe.ninja request takes: governor and cache in front of <paramref name="network"/>,
    /// and a User-Agent that names the app and its home page, nothing about the player.</summary>
    public static HttpClient HttpFor(ProviderGovernor governor, HttpMessageHandler network, string version)
    {
        var http = new HttpClient(new MarketProviderHandler(governor, new HttpCache()) { InnerHandler = network })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", PoeNinjaClient.UserAgent(version));
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        return http;
    }

    /// <summary>The shipped category list, or the built-in one if the file is missing or broken.</summary>
    public static PoeNinjaCatalog LoadCatalog(AppPaths paths)
    {
        try
        {
            return PoeNinjaCatalog.Parse(File.ReadAllText(paths.PoeNinjaCategories));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return PoeNinjaCatalog.Default;
        }
    }

    /// <summary>A refresh for the tracked league finished with prices. Raised on the UI thread.</summary>
    public event Action<PriceBook>? BookReady;

    /// <summary>The league being kept up to date, or null when nothing is fetched (demo, or switched off).</summary>
    [ObservableProperty]
    public partial string? League { get; private set; }

    /// <summary>When the newest book for <see cref="League"/> was fetched.</summary>
    [ObservableProperty]
    public partial DateTimeOffset? LastRefresh { get; private set; }

    [ObservableProperty]
    public partial int LastCount { get; private set; }

    [ObservableProperty]
    public partial DateTimeOffset? NextRefresh { get; private set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; private set; }

    /// <summary>Why poe.ninja is not being asked right now, in plain words, or null.</summary>
    [ObservableProperty]
    public partial string? Hold { get; private set; }

    /// <summary>What went wrong with the last refresh, in plain words, or null.</summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>One per category: what a full refresh costs.</summary>
    public int RequestsPerRefresh => _catalog.Categories.Count;

    /// <summary>Starts, moves or stops the schedule. Same league: nothing changes. Another league (or none):
    /// the refresh in flight is cancelled and whatever it still returns is dropped.</summary>
    /// <param name="current">The book the session already has, so a fresh snapshot is not fetched again at start.</param>
    public void Track(string? league, PriceBook? current)
    {
        if (_disposed || string.Equals(league, League, StringComparison.Ordinal))
        {
            return;
        }

        Stop();
        League = league;
        Hold = null;
        Problem = null;
        var known = league is not null && current is { IsSynthetic: false, Quotes.Count: > 0 } && string.Equals(current.League, league, StringComparison.Ordinal) ? current : null;
        LastRefresh = known?.CreatedAt;
        LastCount = known?.Quotes.Count ?? 0;
        if (league is null)
        {
            NextRefresh = null;
            return;
        }

        var now = _time.GetUtcNow();
        var due = now + _startDelay();
        if (known is not null && known.CreatedAt + Cadence > due)
        {
            due = known.CreatedAt + Cadence; // restarted within the hour: the snapshot on disk is still current
        }

        // A hold remembered from before a restart (a cooldown, an open circuit) is waited out, not retried.
        var decision = _governor.Check(Exchange);
        if (!decision.CanSend && !decision.IsPacing)
        {
            Hold = Plain(decision, now);
            if (decision.RetryAt is not { } retry)
            {
                NextRefresh = null;
                return;
            }

            if (retry > due)
            {
                due = retry;
            }
        }

        Schedule(due);
    }

    /// <summary>Refreshes the tracked league now, unless one is already running. For tests; the app only
    /// refreshes on its schedule.</summary>
    public void RefreshNow()
    {
        if (League is not null && !IsRefreshing)
        {
            _timer?.Dispose();
            _timer = null;
            Begin(_generation);
        }
    }

    /// <summary>Turns poe.ninja back on after it refused access (401/403). Only on the player's say-so:
    /// switching live prices off and on.</summary>
    public void Retry() => _governor.Enable(PoeNinjaClient.Integration);

    /// <summary>poe.ninja's economy leagues, for the league picker. One request, cached for poe.ninja's max-age.</summary>
    public Task<IReadOnlyList<PoeNinjaLeague>> LeaguesAsync(CancellationToken cancellationToken = default) => _client.LeaguesAsync(cancellationToken);

    /// <summary>A governor hold in plain words.</summary>
    public static string Plain(SendDecision decision, DateTimeOffset now)
    {
        var at = decision.RetryAt is { } t ? Session.LocalTime(t, now) : "later";
        return decision.Reason switch
        {
            HoldReason.CoolingDown => $"poe.ninja asked SSE to slow down, so SSE waits until {at} before asking again.",
            HoldReason.ServerBackoff => $"poe.ninja had a problem or could not be reached, so SSE waits until {at} before trying again.",
            HoldReason.CircuitOpen => $"poe.ninja turned down several requests in a row, so SSE pauses until {at}, then tries once.",
            HoldReason.Disabled => "poe.ninja refused SSE's requests, so SSE stopped asking. Switch live prices off and on to try again.",
            _ => decision.Message,
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _owned?.Dispose();
    }

    private void Stop()
    {
        _generation++;
        _timer?.Dispose();
        _timer = null;
        _inFlight?.Cancel(); // not disposed: the refresh may still be reading its token
        _inFlight = null;
        IsRefreshing = false;
    }

    private void Schedule(DateTimeOffset due)
    {
        _timer?.Dispose();
        NextRefresh = due;
        var generation = _generation;
        var delay = due - _time.GetUtcNow();
        _timer = _time.CreateTimer(_ => _dispatcher.BeginInvoke(() => Begin(generation)), null,
            delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private void Begin(int generation)
    {
        if (_disposed || generation != _generation || League is not { } league || IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        Problem = null;
        var cts = new CancellationTokenSource();
        _inFlight = cts;
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            Outcome outcome;
            try
            {
                var refresh = await _client.RefreshAsync(league, _catalog, _time, token).ConfigureAwait(false);
                if (refresh.Book is { } book)
                {
                    _archive.Save(book); // its own league's folder, even if the player has moved on
                }

                outcome = new Outcome(refresh, null, null);
            }
            catch (ProviderHoldException e)
            {
                outcome = new Outcome(null, e.Decision, null);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return; // the league changed or the app is closing; nothing to report
            }
            catch (Exception e)
            {
                outcome = new Outcome(null, null, e);
            }

            _ = _dispatcher.BeginInvoke(() => Finish(generation, league, outcome));
        });
    }

    private void Finish(int generation, string league, Outcome outcome)
    {
        if (_disposed || generation != _generation || !string.Equals(league, League, StringComparison.Ordinal))
        {
            return; // a late answer for a league the player has left: dropped
        }

        _inFlight = null;
        IsRefreshing = false;
        var now = _time.GetUtcNow();
        if (outcome.Refresh is { } refresh)
        {
            Hold = null;
            if (refresh.Book is { } book)
            {
                LastRefresh = book.CreatedAt;
                LastCount = book.Quotes.Count;
                BookReady?.Invoke(book);
            }
            else
            {
                Problem = $"poe.ninja has no prices for \"{league}\". Check the league name above.";
            }

            Schedule(now + Cadence + _jitter());
            return;
        }

        var decision = outcome.Held ?? _governor.Check(Exchange);
        if (outcome.Error is { } error)
        {
            var what = error switch
            {
                TaskCanceledException => "poe.ninja took too long to answer.",
                HttpRequestException { StatusCode: { } code } => $"poe.ninja answered {(int)code} ({code}).",
                _ => $"Could not reach poe.ninja ({error.GetBaseException().Message.TrimEnd('.')}).",
            };
            Problem = $"{what} The prices you have stay until the next refresh.";
        }

        Hold = !decision.CanSend && !decision.IsPacing ? Plain(decision, now) : null;
        if (decision.Reason == HoldReason.Disabled)
        {
            NextRefresh = null;
            return;
        }

        var due = now + (outcome.Error is null ? TimeSpan.FromMinutes(1) : RetryAfterFailure);
        if (!decision.CanSend && decision.RetryAt is { } retry && retry > due)
        {
            due = retry;
        }

        Schedule(due);
    }

    private sealed record Outcome(PoeNinjaRefresh? Refresh, SendDecision? Held, Exception? Error);
}
