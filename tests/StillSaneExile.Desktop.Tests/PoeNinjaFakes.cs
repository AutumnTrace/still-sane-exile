using System.IO;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Windows.Threading;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Market;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Tests;

/// <summary>The poe.ninja-shaped answers in the repository's <c>fixtures/poeninja/</c> (they never ship with the app).</summary>
internal static class NinjaFixtures
{
    private static readonly Lazy<string> Root = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "fixtures", "poeninja")))
        {
            dir = dir.Parent;
        }

        return dir is null ? throw new DirectoryNotFoundException("fixtures/poeninja not found above the test output.") : Path.Combine(dir.FullName, "fixtures", "poeninja");
    });

    public static string Text(string name) => File.ReadAllText(Path.Combine(Root.Value, $"{name}.json"));

    /// <summary>The three categories the fake answers with data; a full refresh in these tests.</summary>
    public static readonly PoeNinjaCatalog Catalog = new(
    [
        new PoeNinjaCategory(PoeNinjaOverview.Exchange, "Currency"),
        new PoeNinjaCategory(PoeNinjaOverview.Exchange, "Runes"),
        new PoeNinjaCategory(PoeNinjaOverview.Items, "UniqueAccessories"),
    ]);

    /// <summary>A book for <paramref name="league"/> built from the fixtures, as a refresh would.</summary>
    public static PriceBook Book(string league, DateTimeOffset at) => PoeNinjaBook.Build(league,
    [
        new PoeNinjaPayload(Catalog.Categories[0], Text("exchange-currency"), at),
        new PoeNinjaPayload(Catalog.Categories[2], Text("items-unique-accessories"), at),
    ], at).Book;
}

/// <summary>Stands in for poe.ninja on the network: answers each overview from the fixtures by its
/// <c>type</c>, the league list from <c>index-state.json</c>, and records every request. With
/// <see cref="Holding"/> on, answers wait for <see cref="Release"/>, ignoring cancellation, like an
/// answer that arrives after the player switched league.</summary>
internal sealed class FakeNinja : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly List<TaskCompletionSource> _waiting = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public bool Holding { get; set; }

    /// <summary>When set, every request fails with this status (and these headers) instead.</summary>
    public HttpStatusCode? FailWith { get; set; }

    public (string Name, string Value)[] FailHeaders { get; set; } = [];

    /// <summary>When set, every request fails as if the network were down.</summary>
    public bool Offline { get; set; }

    public int Waiting
    {
        get
        {
            lock (_gate)
            {
                return _waiting.Count;
            }
        }
    }

    public int Answered { get; private set; }

    public void Release()
    {
        List<TaskCompletionSource> go;
        lock (_gate)
        {
            go = [.. _waiting];
            _waiting.Clear();
        }

        foreach (var g in go)
        {
            g.TrySetResult();
        }
    }

    /// <summary>Requests that asked for <paramref name="league"/>'s prices.</summary>
    public int For(string league)
    {
        lock (_gate)
        {
            return Requests.Count(r => HttpUtility.ParseQueryString(r.RequestUri!.Query)["league"] == league);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        TaskCompletionSource? wait = null;
        lock (_gate)
        {
            Requests.Add(request);
            if (Holding)
            {
                wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting.Add(wait);
            }
        }

        if (wait is not null)
        {
            await wait.Task.ConfigureAwait(false); // deliberately deaf to cancellation: a late answer
        }

        if (Offline)
        {
            throw new HttpRequestException("No such host is known.");
        }

        Answered++;
        if (FailWith is { } status)
        {
            var failed = new HttpResponseMessage(status) { Content = new StringContent(""), RequestMessage = request };
            foreach (var (name, value) in FailHeaders)
            {
                failed.Headers.TryAddWithoutValidation(name, value);
            }

            return failed;
        }

        var path = request.RequestUri!.AbsolutePath;
        var type = HttpUtility.ParseQueryString(request.RequestUri.Query)["type"];
        var body = path.EndsWith("index-state", StringComparison.Ordinal) ? NinjaFixtures.Text("index-state")
            : type switch
            {
                "Currency" => NinjaFixtures.Text("exchange-currency"),
                "Runes" => NinjaFixtures.Text("exchange-runes"),
                "UniqueAccessories" => NinjaFixtures.Text("items-unique-accessories"),
                _ => NinjaFixtures.Text("exchange-empty"),
            };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body), RequestMessage = request };
    }
}

internal static class LivePricesRig
{
    public const string Version = "9.9.9";

    /// <summary>The app's live prices against <paramref name="ninja"/>: the real handler chain and archive,
    /// with no spacing between requests and a first refresh far enough off that only
    /// <see cref="LivePrices.RefreshNow"/> starts one.</summary>
    public static LivePrices For(TestApp app, FakeNinja ninja, TimeSpan? startDelay = null)
    {
        var governor = new ProviderGovernor(TimeProvider.System, new GovernorOptions { ConservativeInterval = TimeSpan.Zero, Jitter = () => TimeSpan.Zero });
        var http = LivePrices.HttpFor(governor, ninja, Version);
        return new LivePrices(new PoeNinjaClient(http), governor, new PriceBookArchive(app.Paths.Prices), NinjaFixtures.Catalog,
            TimeProvider.System, Dispatcher.CurrentDispatcher, () => startDelay ?? TimeSpan.FromHours(5), () => TimeSpan.Zero, http);
    }
}
