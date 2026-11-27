using System.Net;
using StillSaneExile.Domain.Market;

namespace StillSaneExile.Domain.Tests;

/// <summary>A clock that only moves when the test says so. Timers (and so <c>Task.Delay</c> with this
/// provider) fire during <see cref="Advance"/>.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public int PendingTimers
    {
        get
        {
            lock (_lock)
            {
                return _timers.Count;
            }
        }
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_lock)
        {
            _now += by;
            due = [.. _timers.Where(t => t.Due <= _now).OrderBy(t => t.Due)];
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._lock)
            {
                clock._timers.Remove(this);
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    Due = clock._now + dueTime;
                    clock._timers.Add(this);
                }
            }

            return true;
        }

        public void Fire()
        {
            lock (clock._lock)
            {
                if (!clock._timers.Remove(this))
                {
                    return;
                }
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (clock._lock)
            {
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Stands in for a market provider: answers from a queue of canned replies and records every
/// request it saw. A request with no reply left fails the test, so an unexpected retry is caught.</summary>
internal sealed class FakeProvider : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _replies = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public FakeProvider Reply(HttpStatusCode status, string body = "", params (string Name, string Value)[] headers)
    {
        _replies.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            foreach (var (name, value) in headers)
            {
                if (!response.Headers.TryAddWithoutValidation(name, value))
                {
                    response.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }

            return response;
        });
        return this;
    }

    public FakeProvider Fail(Exception error)
    {
        _replies.Enqueue(_ => throw error);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Assert.True(_replies.Count > 0, $"Unexpected request to {request.RequestUri}: no canned reply left.");
        var response = _replies.Dequeue()(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

/// <summary>A cooldown file in its own temporary folder, deleted afterwards.</summary>
internal sealed class TempCooldownFile : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "sse-tests", Guid.NewGuid().ToString("N"));

    public TempCooldownFile()
    {
        Store = new JsonFileCooldownStore(FilePath);
    }

    public string FilePath => Path.Combine(_folder, "cooldowns.json");

    public JsonFileCooldownStore Store { get; }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}

/// <summary>Shared builders for the market tests.</summary>
internal static class MarketTest
{
    public const string Ggg = "ggg";
    public const string Ninja = "poe.ninja";

    /// <summary>A fixed jitter, so cooldown ends are exact.</summary>
    public static readonly TimeSpan Jitter = TimeSpan.FromSeconds(3);

    public static GovernorOptions Options { get; } = new() { Jitter = () => Jitter };

    public static ManualClock Clock() => new(Fixtures.DemoNow);

    public static RouteKey Route(string route, string integration = Ggg) => new(integration, route);

    /// <summary>GGG's headers for one rule.</summary>
    public static (string, string)[] Limits(string policy, string limits, string state, string rule = "Ip") =>
    [
        ("X-Rate-Limit-Policy", policy),
        ("X-Rate-Limit-Rules", rule),
        ($"X-Rate-Limit-{rule}", limits),
        ($"X-Rate-Limit-{rule}-State", state),
    ];

    public static Func<string, string?> Headers(params (string Name, string Value)[] headers)
    {
        var map = headers.ToDictionary(h => h.Name, h => h.Value, StringComparer.OrdinalIgnoreCase);
        return name => map.GetValueOrDefault(name);
    }

    public static HttpRequestMessage Get(string url, string? route = null, string integration = Ggg)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        return route is null ? request : request.ForRoute(integration, route);
    }

    public static HttpClient Client(ProviderGovernor governor, FakeProvider provider, HttpCache? cache = null, TimeSpan? maxPacingWait = null) =>
        new(new MarketProviderHandler(governor, cache, maxPacingWait ?? TimeSpan.Zero) { InnerHandler = provider });

    /// <summary>Sends one request through the governor directly, as if the provider answered <paramref name="status"/>.</summary>
    public static void Answer(ProviderGovernor governor, RouteKey route, HttpStatusCode status, params (string Name, string Value)[] headers)
    {
        var decision = governor.TryAcquire(route);
        Assert.True(decision.CanSend, decision.Message);
        governor.Record(decision.Ticket!, status, Headers(headers));
    }
}
