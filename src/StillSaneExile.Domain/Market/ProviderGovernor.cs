using System.Globalization;
using System.Net;
using System.Text;

namespace StillSaneExile.Domain.Market;

/// <summary>Which provider a request goes to (<see cref="Integration"/>, e.g. <c>poe.ninja</c>) and which of
/// its endpoints (<see cref="Route"/>). Use one route per endpoint template, not per URL: the exchange
/// digest's hourly URLs are all one route, so what one hour taught us about its limits applies to the next.</summary>
public readonly record struct RouteKey(string Integration, string Route)
{
    public override string ToString() => $"{Integration} {Route}";
}

public enum HoldReason
{
    /// <summary>Not held: the request may go now.</summary>
    None,
    /// <summary>Waiting out a restriction (a 429, or a restricted state) on a shared policy or the whole integration.</summary>
    CoolingDown,
    /// <summary>The next hit would eat into the headroom kept in one of the policy's windows.</summary>
    RateWindow,
    /// <summary>The route has no usable rate-limit headers, so requests go one at a time with a gap between them.</summary>
    Conservative,
    /// <summary>The provider answered 5xx or could not be reached: capped exponential backoff.</summary>
    ServerBackoff,
    /// <summary>Repeated 4xx: nothing goes until the circuit lets one trial request through.</summary>
    CircuitOpen,
    /// <summary>401 or 403: the integration stays off until someone fixes it and calls <see cref="ProviderGovernor.Enable"/>.</summary>
    Disabled,
}

/// <summary>The governor's answer for one route: send now (<see cref="Ticket"/> is set when a slot was
/// taken), or why not and when to try again. <see cref="RetryAt"/> is null when no time will help
/// (<see cref="HoldReason.Disabled"/>). <see cref="Message"/> is plain English for the UI or a log.</summary>
public sealed record SendDecision(HoldReason Reason, DateTimeOffset? RetryAt, string Message, SendTicket? Ticket = null)
{
    public bool CanSend => Reason == HoldReason.None;

    /// <summary>Short, expected holds that pace a burst. Anything else means the provider asked us to stop.</summary>
    public bool IsPacing => Reason is HoldReason.RateWindow or HoldReason.Conservative;
}

/// <summary>Permission for one request. Hand it back exactly once, with <see cref="ProviderGovernor.Record(SendTicket, HttpResponseMessage)"/>,
/// <see cref="ProviderGovernor.RecordFailure"/> or <see cref="ProviderGovernor.Abandon"/>, so the governor
/// learns from the answer and frees the slot.</summary>
public sealed class SendTicket
{
    internal SendTicket(RouteKey route, DateTimeOffset sentAt, bool conservative, bool trial)
    {
        Route = route;
        SentAt = sentAt;
        Conservative = conservative;
        Trial = trial;
    }

    public RouteKey Route { get; }

    public DateTimeOffset SentAt { get; }

    internal bool Conservative { get; }

    internal bool Trial { get; }

    internal bool Settled { get; set; }
}

/// <summary>How careful the governor is. The defaults are deliberately slow: SSE refreshes prices hourly,
/// so it never needs to go near a provider's limits.</summary>
public sealed record GovernorOptions
{
    public static GovernorOptions Default { get; } = new();

    /// <summary>Share of each window kept unused, rounded up. Together with <see cref="MinHeadroomHits"/>:
    /// a 5-per-10 s window allows 4, a 45-per-60 s window allows 40.</summary>
    public double HeadroomFraction { get; init; } = 0.1;

    /// <summary>Hits always left unused in each window, so we stop before the last one.</summary>
    public int MinHeadroomHits { get; init; } = 1;

    /// <summary>Added after a window's oldest hit expires, for clock skew and latency between us and the server.</summary>
    public TimeSpan WindowMargin { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Gap between requests to a route whose limits are unknown. One request at a time, and this
    /// far apart, is below any limit a public API is likely to set.</summary>
    public TimeSpan ConservativeInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Cooldown after a 429 that says nothing about how long (no Retry-After, state or penalty).</summary>
    public TimeSpan FallbackCooldown { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Longest single wait taken from a header, so a broken value cannot park an integration for years.
    /// A real longer ban is simply learned again from the next 429.</summary>
    public TimeSpan MaxCooldown { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Consecutive 4xx answers (other than 401/403) that open the circuit.</summary>
    public int ClientErrorsToOpenCircuit { get; init; } = 3;

    /// <summary>How long the circuit stays open the first time; doubles each time a trial fails.</summary>
    public TimeSpan CircuitOpenFor { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan CircuitOpenCap { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Wait after the first 5xx or network failure; doubles with each one in a row, up to <see cref="BackoffCap"/>.</summary>
    public TimeSpan BackoffBase { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan BackoffCap { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Added once to the end of every cooldown and backoff, so routes, and other copies of the app,
    /// do not all come back at the same instant. Tests pass a fixed value.</summary>
    public Func<TimeSpan> Jitter { get; init; } = () => TimeSpan.FromMilliseconds(Random.Shared.Next(1_000, 5_001));
}

/// <summary>Decides, before each market request, whether it may go now, must wait until a known time, or
/// is refused, and learns from every answer. Routes that report the same rate-limit policy share its
/// buckets; a route without usable headers gets the conservative mode. The governor never sends or
/// retries anything itself: it only says when. With a store, every change is saved as absolute UTC
/// instants, so a restart does not forget a cooldown.</summary>
public sealed class ProviderGovernor
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly GovernorOptions _options;
    private readonly ICooldownStore? _store;
    private readonly Dictionary<string, IntegrationState> _integrations = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Integration, string Policy), Bucket> _buckets = [];
    private readonly Dictionary<RouteKey, string> _routes = [];

    public ProviderGovernor(TimeProvider time, GovernorOptions? options = null, ICooldownStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _options = options ?? GovernorOptions.Default;
        _store = store;
        if (store?.Load() is { } snapshot)
        {
            Restore(snapshot);
        }
    }

    public TimeProvider Time => _time;

    private DateTimeOffset Now => _time.GetUtcNow();

    /// <summary>What would happen if <paramref name="route"/> sent now, without taking a slot. For status displays.</summary>
    public SendDecision Check(RouteKey route)
    {
        lock (_gate)
        {
            return Evaluate(route, Now, reserve: false);
        }
    }

    /// <summary>Takes a slot for one request when it may go now; otherwise says why not and until when.</summary>
    public SendDecision TryAcquire(RouteKey route)
    {
        lock (_gate)
        {
            return Evaluate(route, Now, reserve: true);
        }
    }

    public void Record(SendTicket ticket, HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Record(ticket, response.StatusCode, ResponseHeaders.Of(response));
    }

    /// <summary>Learns from one answer: which policy the route reported and what its windows hold, any
    /// restriction (on a 429, for every route sharing the policy), a 401/403, the 4xx count behind the
    /// circuit and the 5xx backoff. Then saves, so a restart sees the same.</summary>
    /// <param name="header">Returns a response header's raw value, or null when absent.</param>
    public void Record(SendTicket ticket, HttpStatusCode status, Func<string, string?> header)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(header);
        lock (_gate)
        {
            if (!Settle(ticket, out var integration))
            {
                return;
            }

            var now = Now;
            var code = (int)status;
            var reading = RateLimitHeaders.Read(header);
            var bucket = Learn(ticket.Route, reading.Policy, now);

            var restriction = reading.Policy?.LongestRestriction ?? TimeSpan.Zero;
            if (code == 429)
            {
                var retryAfter = RateLimitHeaders.RetryAfter(header("Retry-After"), now) ?? TimeSpan.Zero;
                restriction = Max(restriction, retryAfter);
                if (restriction <= TimeSpan.Zero)
                {
                    var penalty = reading.Policy?.LongestPenalty ?? TimeSpan.Zero;
                    restriction = penalty > TimeSpan.Zero ? penalty : _options.FallbackCooldown;
                }
            }

            if (restriction > TimeSpan.Zero)
            {
                var end = now + Min(restriction, _options.MaxCooldown);
                if (bucket is not null)
                {
                    // Shared: every route that reported this policy is held.
                    if (bucket.RestrictedUntil is not { } current || end > current)
                    {
                        bucket.RestrictedUntil = end + _options.Jitter();
                    }
                }
                else if (integration.PausedUntil is not { } paused || end > paused)
                {
                    // A 429 without a readable policy: we cannot tell which routes share it, so hold them all.
                    integration.PausedUntil = end + _options.Jitter();
                }
            }

            if (code is 401 or 403)
            {
                integration.DisabledStatus = code;
                integration.DisabledAt = now;
            }
            else if (code is >= 400 and < 500)
            {
                CountClientError(integration, code, now);
            }
            else if (code >= 500)
            {
                CountServerError(integration, code, RateLimitHeaders.RetryAfter(header("Retry-After"), now), now);
            }
            else
            {
                integration.ClientErrors = 0;
                integration.CircuitTrips = 0;
                integration.CircuitOpenUntil = null;
            }

            if (code < 500)
            {
                integration.ServerErrors = 0;
            }

            Persist(now);
        }
    }

    /// <summary>The request failed without an answer (connection refused, reset, DNS). Backs off like a 5xx.</summary>
    public void RecordFailure(SendTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        lock (_gate)
        {
            if (Settle(ticket, out var integration))
            {
                var now = Now;
                CountServerError(integration, null, null, now);
                Persist(now);
            }
        }
    }

    /// <summary>The caller cancelled. The hit stays counted (it may have reached the server); the slot is freed.</summary>
    public void Abandon(SendTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        lock (_gate)
        {
            Settle(ticket, out _);
        }
    }

    /// <summary>Turns an integration back on after a 401/403, once whatever caused it is fixed.</summary>
    public void Enable(string integration)
    {
        lock (_gate)
        {
            var state = State(integration);
            state.DisabledStatus = null;
            state.DisabledAt = null;
            Persist(Now);
        }
    }

    /// <summary>Everything a restart must not forget, as absolute UTC instants.</summary>
    public GovernorSnapshot Snapshot()
    {
        lock (_gate)
        {
            return BuildSnapshot(Now);
        }
    }

    private SendDecision Evaluate(RouteKey route, DateTimeOffset now, bool reserve)
    {
        var name = route.Integration;
        var integration = State(name);
        if (integration.DisabledStatus is { } status)
        {
            return Hold(HoldReason.Disabled, null,
                $"{name} refused access ({Describe(status)}). It stays off until the problem is fixed and it is turned back on.");
        }

        var trial = false;
        if (integration.ClientErrors >= _options.ClientErrorsToOpenCircuit)
        {
            if (integration.CircuitOpenUntil is { } open && now < open)
            {
                return Hold(HoldReason.CircuitOpen, open,
                    $"{name} rejected {integration.ClientErrors} requests in a row (last: {Describe(integration.LastClientError)}). Paused until {At(open)}, then one trial request.");
            }

            if (integration.TrialInFlight)
            {
                return Hold(HoldReason.CircuitOpen, now + _options.ConservativeInterval,
                    $"{name} rejected {integration.ClientErrors} requests in a row. Waiting for the answer to one trial request.");
            }

            trial = true;
        }

        if (integration.BackoffUntil is { } backoff && now < backoff)
        {
            return Hold(HoldReason.ServerBackoff, backoff,
                $"{name} failed {integration.ServerErrors} time(s) in a row (last: {Describe(integration.LastServerError)}). Backing off until {At(backoff)}.");
        }

        if (integration.PausedUntil is { } paused && now < paused)
        {
            return Hold(HoldReason.CoolingDown, paused,
                $"{name} asked us to slow down. Every {name} request waits until {At(paused)}.");
        }

        if (_routes.TryGetValue(route, out var policy) && _buckets.TryGetValue((name, policy), out var bucket))
        {
            if (bucket.RestrictedUntil is { } until && now < until)
            {
                return Hold(HoldReason.CoolingDown, until,
                    $"{name} restricted its '{policy}' limit. Every route that shares it waits until {At(until)}.");
            }

            DateTimeOffset? free = null;
            foreach (var w in bucket.Windows)
            {
                w.Prune(now, _options.WindowMargin);
                var usable = Usable(w.MaxHits);
                if (w.Hits.Count >= usable)
                {
                    // When enough of the oldest hits have left the window for one more to fit under the headroom.
                    var at = w.Hits[w.Hits.Count - usable] + w.Period + _options.WindowMargin;
                    free = free is { } f && f > at ? f : at;
                }
            }

            if (free is { } next)
            {
                return Hold(HoldReason.RateWindow, next,
                    $"Keeping headroom in {name}'s '{policy}' limit. Next request at {At(next)}.");
            }

            if (!reserve)
            {
                return Clear;
            }

            foreach (var w in bucket.Windows)
            {
                w.Add(now);
            }

            return Grant(integration, new SendTicket(route, now, conservative: false, trial));
        }

        // Unknown limits: assume the worst. This route may share a policy that is cooling down.
        var restricted = _buckets.Values
            .Where(b => b.Integration == name && b.RestrictedUntil > now)
            .Select(b => b.RestrictedUntil!.Value)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        if (restricted > now)
        {
            return Hold(HoldReason.CoolingDown, restricted,
                $"{name} is cooling down and this route's limits are unknown, so it waits too, until {At(restricted)}.");
        }

        var gap = _options.ConservativeInterval;
        if (integration.ConservativeInFlight > 0)
        {
            return Hold(HoldReason.Conservative, now + gap,
                $"{name} has not published usable limits for {route.Route}, so requests go one at a time. Waiting for the one in flight.");
        }

        if (integration.LastConservativeSend is { } last && now < last + gap)
        {
            return Hold(HoldReason.Conservative, last + gap,
                $"{name} has not published usable limits for {route.Route}, so requests go one at a time, {gap.TotalSeconds:0} s apart. Next request at {At(last + gap)}.");
        }

        if (!reserve)
        {
            return Clear;
        }

        integration.ConservativeInFlight++;
        integration.LastConservativeSend = now;
        return Grant(integration, new SendTicket(route, now, conservative: true, trial));
    }

    private static readonly SendDecision Clear = new(HoldReason.None, null, "Clear to send.");

    private static SendDecision Hold(HoldReason reason, DateTimeOffset? retryAt, string message) => new(reason, retryAt, message);

    private static SendDecision Grant(IntegrationState integration, SendTicket ticket)
    {
        if (ticket.Trial)
        {
            integration.TrialInFlight = true;
        }

        return new SendDecision(HoldReason.None, null, "Clear to send.", ticket);
    }

    private int Usable(int maxHits)
    {
        var headroom = Math.Max(_options.MinHeadroomHits, (int)Math.Ceiling(maxHits * _options.HeadroomFraction));
        return Math.Max(1, maxHits - headroom);
    }

    private bool Settle(SendTicket ticket, out IntegrationState integration)
    {
        integration = State(ticket.Route.Integration);
        if (ticket.Settled)
        {
            return false;
        }

        ticket.Settled = true;
        if (ticket.Conservative && integration.ConservativeInFlight > 0)
        {
            integration.ConservativeInFlight--;
        }

        if (ticket.Trial)
        {
            integration.TrialInFlight = false;
        }

        return true;
    }

    /// <summary>Maps the route to the policy it reported and brings the policy's windows up to date. A response
    /// without a complete, readable set puts the route back into conservative mode.</summary>
    private Bucket? Learn(RouteKey route, RateLimitPolicy? policy, DateTimeOffset now)
    {
        if (policy is null)
        {
            _routes.Remove(route);
            return null;
        }

        var key = (route.Integration, policy.Name);
        if (!_buckets.TryGetValue(key, out var bucket))
        {
            bucket = new Bucket(route.Integration, policy.Name);
            _buckets[key] = bucket;
        }

        _routes[route] = policy.Name;
        foreach (var rule in policy.Rules)
        {
            var known = bucket.Rules.TryGetValue(rule.Name, out var old) ? old : [];
            var windows = new List<Window>();
            foreach (var reported in rule.Windows)
            {
                var w = known.FirstOrDefault(k => k.Period == reported.Period) ?? new Window(reported.Period);
                w.MaxHits = reported.MaxHits;
                w.Penalty = reported.Penalty;
                w.Prune(now, _options.WindowMargin);

                // The server also counts other clients on the same IP or account (the trade site, other tools):
                // when it has seen more than we sent, believe it. Those hits are dated now, the cautious guess.
                for (var i = w.Hits.Count; i < Math.Min(reported.CurrentHits, reported.MaxHits); i++)
                {
                    w.Add(now);
                }

                windows.Add(w);
            }

            bucket.Rules[rule.Name] = windows;
        }

        foreach (var gone in bucket.Rules.Keys.Where(k => !policy.Rules.Any(r => r.Name.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            bucket.Rules.Remove(gone);
        }

        return bucket;
    }

    private void CountClientError(IntegrationState integration, int code, DateTimeOffset now)
    {
        integration.ClientErrors++;
        integration.LastClientError = code;
        if (integration.ClientErrors >= _options.ClientErrorsToOpenCircuit)
        {
            integration.CircuitOpenUntil = now + Doubling(_options.CircuitOpenFor, integration.CircuitTrips, _options.CircuitOpenCap);
            integration.CircuitTrips++;
        }
    }

    private void CountServerError(IntegrationState integration, int? code, TimeSpan? retryAfter, DateTimeOffset now)
    {
        integration.ServerErrors++;
        integration.LastServerError = code;
        var wait = Doubling(_options.BackoffBase, integration.ServerErrors - 1, _options.BackoffCap);
        if (retryAfter is { } asked)
        {
            wait = Max(wait, Min(asked, _options.MaxCooldown));
        }

        integration.BackoffUntil = now + wait + _options.Jitter();
    }

    /// <summary><paramref name="start"/> doubled <paramref name="times"/> times, never past <paramref name="cap"/>.</summary>
    private static TimeSpan Doubling(TimeSpan start, int times, TimeSpan cap)
    {
        var span = start;
        for (var i = 0; i < times && span < cap; i++)
        {
            span += span;
        }

        return Min(span, cap);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private IntegrationState State(string name)
    {
        if (!_integrations.TryGetValue(name, out var s))
        {
            s = new IntegrationState();
            _integrations[name] = s;
        }

        return s;
    }

    private void Persist(DateTimeOffset now) => _store?.Save(BuildSnapshot(now));

    private GovernorSnapshot BuildSnapshot(DateTimeOffset now)
    {
        var policies = new List<PolicySnapshot>();
        foreach (var b in _buckets.Values)
        {
            var rules = new List<RuleSnapshot>();
            foreach (var (name, windows) in b.Rules)
            {
                var ws = new List<WindowSnapshot>();
                foreach (var w in windows)
                {
                    w.Prune(now, _options.WindowMargin);
                    ws.Add(new WindowSnapshot(w.MaxHits, (int)w.Period.TotalSeconds, (int)w.Penalty.TotalSeconds, [.. w.Hits]));
                }

                rules.Add(new RuleSnapshot(name, ws));
            }

            policies.Add(new PolicySnapshot(b.Integration, b.Policy, b.RestrictedUntil, rules));
        }

        var routes = _routes.Select(r => new RouteSnapshot(r.Key.Integration, r.Key.Route, r.Value)).ToList();
        var integrations = _integrations.Select(i => new IntegrationSnapshot(
            i.Key,
            i.Value.DisabledStatus,
            i.Value.DisabledAt,
            i.Value.ClientErrors,
            i.Value.LastClientError,
            i.Value.CircuitTrips,
            i.Value.CircuitOpenUntil,
            i.Value.ServerErrors,
            i.Value.LastServerError,
            i.Value.BackoffUntil,
            i.Value.PausedUntil,
            i.Value.LastConservativeSend)).ToList();
        return new GovernorSnapshot(GovernorSnapshot.CurrentVersion, now, policies, routes, integrations);
    }

    /// <summary>Rebuilds state from a snapshot, skipping anything unreadable. Requests in flight before the
    /// restart are gone, so their slots are not restored.</summary>
    private void Restore(GovernorSnapshot snapshot)
    {
        if (snapshot.Version != GovernorSnapshot.CurrentVersion)
        {
            return;
        }

        foreach (var p in snapshot.Policies ?? [])
        {
            if (p is null || string.IsNullOrEmpty(p.Integration) || string.IsNullOrEmpty(p.Policy))
            {
                continue;
            }

            var bucket = new Bucket(p.Integration, p.Policy) { RestrictedUntil = p.RestrictedUntil?.ToUniversalTime() };
            foreach (var r in p.Rules ?? [])
            {
                if (r is null || string.IsNullOrEmpty(r.Name))
                {
                    continue;
                }

                var windows = new List<Window>();
                foreach (var w in r.Windows ?? [])
                {
                    if (w is null || w.MaxHits < 1 || w.PeriodSeconds < 1 || windows.Any(x => x.Period.TotalSeconds == w.PeriodSeconds))
                    {
                        continue;
                    }

                    var window = new Window(TimeSpan.FromSeconds(w.PeriodSeconds)) { MaxHits = w.MaxHits, Penalty = TimeSpan.FromSeconds(Math.Max(0, w.PenaltySeconds)) };
                    foreach (var hit in (w.Hits ?? []).Select(h => h.ToUniversalTime()).Order().TakeLast(w.MaxHits))
                    {
                        window.Add(hit);
                    }

                    windows.Add(window);
                }

                if (windows.Count > 0)
                {
                    bucket.Rules[r.Name] = windows;
                }
            }

            if (bucket.Rules.Count > 0)
            {
                _buckets[(p.Integration, p.Policy)] = bucket;
            }
        }

        foreach (var r in snapshot.Routes ?? [])
        {
            if (r is not null && r.Integration is not null && r.Route is not null && r.Policy is not null
                && _buckets.ContainsKey((r.Integration, r.Policy)))
            {
                _routes[new RouteKey(r.Integration, r.Route)] = r.Policy;
            }
        }

        foreach (var i in snapshot.Integrations ?? [])
        {
            if (i is null || string.IsNullOrEmpty(i.Name))
            {
                continue;
            }

            var s = State(i.Name);
            s.DisabledStatus = i.DisabledStatus;
            s.DisabledAt = i.DisabledAt?.ToUniversalTime();
            s.ClientErrors = Math.Max(0, i.ClientErrors);
            s.LastClientError = i.LastClientError;
            s.CircuitTrips = Math.Max(0, i.CircuitTrips);
            s.CircuitOpenUntil = i.CircuitOpenUntil?.ToUniversalTime();
            s.ServerErrors = Math.Max(0, i.ServerErrors);
            s.LastServerError = i.LastServerError;
            s.BackoffUntil = i.BackoffUntil?.ToUniversalTime();
            s.PausedUntil = i.PausedUntil?.ToUniversalTime();
            s.LastConservativeSend = i.LastConservativeSend?.ToUniversalTime();
        }
    }

    private static string At(DateTimeOffset t) =>
        t.ToUniversalTime().ToString("yyyy-MM-dd HH':'mm':'ss 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>"404 Not Found", or "no answer" for a network failure.</summary>
    private static string Describe(int? status)
    {
        if (status is not { } code)
        {
            return "no answer";
        }

        var name = Enum.IsDefined((HttpStatusCode)code) ? ((HttpStatusCode)code).ToString() : null;
        if (name is null)
        {
            return code.ToString(CultureInfo.InvariantCulture);
        }

        var words = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && words.Length > 0)
            {
                words.Append(' ');
            }

            words.Append(c);
        }

        return $"{code} {words}";
    }

    private sealed class Window(TimeSpan period)
    {
        public TimeSpan Period { get; } = period;

        public int MaxHits { get; set; } = 1;

        public TimeSpan Penalty { get; set; }

        /// <summary>Send times of the hits still inside the window, oldest first.</summary>
        public List<DateTimeOffset> Hits { get; } = [];

        /// <summary>Drops hits that have left the window, counting the margin, so a hold ends exactly at its RetryAt.</summary>
        public void Prune(DateTimeOffset now, TimeSpan margin) => Hits.RemoveAll(h => h + Period + margin <= now);

        public void Add(DateTimeOffset at)
        {
            Hits.Add(at);
            if (Hits.Count > 1 && Hits[^2] > at)
            {
                Hits.Sort(); // the clock went backwards
            }
        }
    }

    private sealed class Bucket(string integration, string policy)
    {
        public string Integration { get; } = integration;

        public string Policy { get; } = policy;

        public Dictionary<string, List<Window>> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);

        public DateTimeOffset? RestrictedUntil { get; set; }

        public IEnumerable<Window> Windows => Rules.Values.SelectMany(w => w);
    }

    private sealed class IntegrationState
    {
        public int? DisabledStatus { get; set; }

        public DateTimeOffset? DisabledAt { get; set; }

        public int ClientErrors { get; set; }

        public int? LastClientError { get; set; }

        public int CircuitTrips { get; set; }

        public DateTimeOffset? CircuitOpenUntil { get; set; }

        public bool TrialInFlight { get; set; }

        public int ServerErrors { get; set; }

        public int? LastServerError { get; set; }

        public DateTimeOffset? BackoffUntil { get; set; }

        public DateTimeOffset? PausedUntil { get; set; }

        public int ConservativeInFlight { get; set; }

        public DateTimeOffset? LastConservativeSend { get; set; }
    }
}
