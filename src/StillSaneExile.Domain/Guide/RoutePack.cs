using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StillSaneExile.Domain.Guide;

/// <summary>How far a route pack's facts have been checked. Anything short of verified is labelled
/// wherever the route is shown, the same way the synthetic data pack is.</summary>
public enum RouteStatus
{
    /// <summary>Written from general knowledge; nobody has walked it in the live game yet.</summary>
    Unverified,

    /// <summary>Checked in the live game on <see cref="RoutePack.VerifiedDate"/>.</summary>
    Verified,

    /// <summary>Made up for tests. Not game data.</summary>
    Synthetic,
}

public enum StepKind
{
    /// <summary>What this route does in the zone. The player can still skip it.</summary>
    Objective,

    /// <summary>An optional extra the route suggests.</summary>
    Bonus,

    /// <summary>Talk to an NPC to hand something in or collect a reward.</summary>
    HandIn,
}

/// <summary>How a leg is reached from the leg before it. The loader checks each one against the
/// zone graph, so a route cannot ask for a walk between zones that do not touch.</summary>
public enum Travel
{
    Walk,
    Waypoint,
    Portal,
}

/// <summary>What a step gives. Permanent rewards (passive points, resistances and the like) are the
/// ones worth listing when missed; everything else is loot.</summary>
public sealed record RewardFact(string Text, bool Permanent = false);

public sealed record RouteStep(string Id, StepKind Kind, string Text, string? Npc = null, IReadOnlyList<RewardFact>? Rewards = null)
{
    public bool Optional => Kind == StepKind.Bonus;

    public IReadOnlyList<RewardFact> RewardFacts => Rewards ?? [];

    public bool HasPermanentReward => RewardFacts.Any(r => r.Permanent);

    /// <summary>The step as the HUD and the page say it.</summary>
    public string Describe() => Optional ? $"{Text} (optional)" : Text;
}

/// <summary>A place in the zone graph. <see cref="Waypoint"/> is null when nobody has checked it, so
/// the app never claims a waypoint the pack does not state.</summary>
public sealed record RouteZone(
    string Id,
    int Act,
    string Name,
    IReadOnlyList<string>? Aliases = null,
    bool Town = false,
    bool? Waypoint = null,
    IReadOnlyList<string>? Edges = null)
{
    public IReadOnlyList<string> EdgeList => Edges ?? [];

    public IEnumerable<string> Names => Aliases is null ? [Name] : [Name, .. Aliases];
}

/// <summary>One visit on the route. A zone can be visited more than once (a dead end and back, a
/// return to town), so steps belong to the visit rather than the zone.</summary>
public sealed record RouteLeg(string Zone, Travel Via = Travel.Walk, string? Hint = null, IReadOnlyList<RouteStep>? Steps = null)
{
    public IReadOnlyList<RouteStep> StepList => Steps ?? [];
}

public sealed record RouteAct(int Act, string Title);

/// <summary>A versioned campaign route for one patch and league: the zone graph (stable ids, aliases,
/// edges, waypoints) and the route through it (legs with objectives, bonuses, hand-ins and reward
/// facts). Like the data pack it states where it came from, and it is either loaded whole or not at all.</summary>
public sealed record RoutePack(
    int SchemaVersion,
    string Id,
    string Title,
    RouteStatus Status,
    string Game,
    string Patch,
    string League,
    string Source,
    string Rights,
    int Revision,
    IReadOnlyList<RouteAct> Acts,
    IReadOnlyList<RouteZone> Zones,
    IReadOnlyList<RouteLeg> Route,
    string? VerifiedDate = null)
{
    public const int SupportedSchemaVersion = 1;
    public const string SupportedGame = "Path of Exile 2";

    public bool IsVerified => Status == RouteStatus.Verified;

    public RouteZone? FindZone(string? id) => id is null ? null : Zones.FirstOrDefault(z => z.Id == id);

    public RouteZone Zone(string id) => FindZone(id) ?? throw new ArgumentException($"Unknown zone {id}.", nameof(id));

    /// <summary>Finds a zone by its name or an alias, ignoring case and a leading "the".</summary>
    public RouteZone? ResolveZone(string? name)
    {
        var wanted = NameKey(name);
        return wanted.Length == 0 ? null : Zones.FirstOrDefault(z => z.Names.Any(n => NameKey(n) == wanted));
    }

    public bool Connected(string a, string b) =>
        FindZone(a)?.EdgeList.Contains(b) == true || FindZone(b)?.EdgeList.Contains(a) == true;

    public IEnumerable<(int Leg, RouteStep Step)> AllSteps() =>
        Route.SelectMany((leg, i) => leg.StepList.Select(s => (i, s)));

    public RouteStep? FindStep(string id) => AllSteps().Select(x => x.Step).FirstOrDefault(s => s.Id == id);

    public bool HasPermanentRewards => AllSteps().Any(x => x.Step.HasPermanentReward);

    internal static string NameKey(string? name)
    {
        var s = (name ?? "").Trim().ToLowerInvariant();
        return s.StartsWith("the ", StringComparison.Ordinal) ? s[4..].Trim() : s;
    }
}

/// <summary><see cref="Pack"/> is the pack to use. When a replacement was rejected it is the one
/// already in use (possibly null) and <see cref="Error"/> says why.</summary>
public sealed record RouteLoadResult(RoutePack? Pack, string? Error);

public static partial class RoutePackLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Validates the whole pack before returning it. A pack that fails any check is not
    /// partially applied.</summary>
    public static RouteLoadResult Load(string json)
    {
        RoutePack? pack;
        try
        {
            pack = JsonSerializer.Deserialize<RoutePack>(json, Options);
        }
        catch (JsonException ex)
        {
            return new RouteLoadResult(null, $"Route pack is not valid: {ex.Message}");
        }

        if (pack is null)
        {
            return new RouteLoadResult(null, "Route pack is empty.");
        }

        var error = Validate(pack);
        return error is null ? new RouteLoadResult(Normalize(pack), null) : new RouteLoadResult(null, $"Route pack {pack.Id}: {error}");
    }

    /// <summary>Loads a replacement for the pack in use. A pack that fails a check, or an older revision
    /// of the pack in use, is not applied: the result keeps <paramref name="inUse"/> and says why.</summary>
    public static RouteLoadResult Replace(RoutePack? inUse, string json)
    {
        var loaded = Load(json);
        if (loaded.Pack is null)
        {
            return new RouteLoadResult(inUse, loaded.Error + (inUse is null ? "" : $" Still using revision {inUse.Revision}."));
        }

        if (inUse is not null && loaded.Pack.Id == inUse.Id && loaded.Pack.Revision < inUse.Revision)
        {
            return new RouteLoadResult(inUse, $"Route pack {inUse.Id}: revision {loaded.Pack.Revision} is older than revision {inUse.Revision} in use. Still using revision {inUse.Revision}.");
        }

        return loaded;
    }

    private static string? Validate(RoutePack pack)
    {
        if (pack.SchemaVersion != RoutePack.SupportedSchemaVersion)
        {
            return $"schema {pack.SchemaVersion} is not supported (expected {RoutePack.SupportedSchemaVersion}).";
        }

        if (pack.Game != RoutePack.SupportedGame)
        {
            return $"written for \"{pack.Game}\", not {RoutePack.SupportedGame}.";
        }

        if (!IdPattern().IsMatch(pack.Id))
        {
            return "the id must be lower-case letters, digits, dots, dashes or underscores.";
        }

        if (new[] { pack.Title, pack.Patch, pack.League, pack.Source, pack.Rights }.Any(string.IsNullOrWhiteSpace))
        {
            return "title, patch, league, source and rights must all be filled in.";
        }

        if (pack.Revision < 1)
        {
            return "revision must be 1 or higher.";
        }

        if (pack.Status == RouteStatus.Verified && string.IsNullOrWhiteSpace(pack.VerifiedDate))
        {
            return "a verified pack must say when it was verified.";
        }

        if (pack.Acts.Count == 0 || pack.Acts.Any(a => a.Act < 1 || string.IsNullOrWhiteSpace(a.Title)) || pack.Acts.DistinctBy(a => a.Act).Count() != pack.Acts.Count)
        {
            return "acts must be listed once each, numbered from 1, with a title.";
        }

        if (pack.Zones.Count == 0)
        {
            return "no zones.";
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var z in pack.Zones)
        {
            if (!IdPattern().IsMatch(z.Id) || !ids.Add(z.Id))
            {
                return $"zone id \"{z.Id}\" is not valid or is listed twice.";
            }

            if (pack.Acts.All(a => a.Act != z.Act))
            {
                return $"zone {z.Id} is in act {z.Act}, which the pack does not list.";
            }

            if (string.IsNullOrWhiteSpace(z.Name) || z.Names.Any(string.IsNullOrWhiteSpace))
            {
                return $"zone {z.Id} has an empty name or alias.";
            }

            foreach (var n in z.Names)
            {
                if (names.TryGetValue(RoutePack.NameKey(n), out var other) && other != z.Id)
                {
                    return $"the name \"{n}\" is used by both {other} and {z.Id}, so it cannot be told apart.";
                }

                names[RoutePack.NameKey(n)] = z.Id;
            }
        }

        foreach (var z in pack.Zones)
        {
            if (z.EdgeList.FirstOrDefault(e => e == z.Id || !ids.Contains(e)) is { } bad)
            {
                return $"zone {z.Id} connects to \"{bad}\", which is itself or not a zone in the pack.";
            }
        }

        if (pack.Route.Count == 0)
        {
            return "the route has no legs.";
        }

        var stepIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < pack.Route.Count; i++)
        {
            var leg = pack.Route[i];
            if (!ids.Contains(leg.Zone))
            {
                return $"leg {i + 1} goes to \"{leg.Zone}\", which is not a zone in the pack.";
            }

            if (i > 0 && TravelProblem(pack, pack.Route[i - 1].Zone, leg) is { } travel)
            {
                return $"leg {i + 1}: {travel}";
            }

            foreach (var s in leg.StepList)
            {
                if (!IdPattern().IsMatch(s.Id) || !stepIds.Add(s.Id))
                {
                    return $"step id \"{s.Id}\" is not valid or is used twice.";
                }

                if (string.IsNullOrWhiteSpace(s.Text))
                {
                    return $"step {s.Id} has no text.";
                }

                if (s.Kind == StepKind.HandIn && string.IsNullOrWhiteSpace(s.Npc))
                {
                    return $"hand-in {s.Id} must name the NPC.";
                }

                if (s.RewardFacts.Any(r => string.IsNullOrWhiteSpace(r.Text)))
                {
                    return $"step {s.Id} has a reward with no text.";
                }
            }
        }

        return null;
    }

    private static string? TravelProblem(RoutePack pack, string from, RouteLeg leg)
    {
        if (from == leg.Zone)
        {
            return $"two legs in a row in {leg.Zone}; merge them.";
        }

        var a = pack.Zone(from);
        var b = pack.Zone(leg.Zone);
        return leg.Via switch
        {
            Travel.Walk when !pack.Connected(from, leg.Zone) => $"walks from {from} to {leg.Zone}, but the graph has no edge between them.",
            Travel.Waypoint when a.Waypoint != true || b.Waypoint != true => $"takes a waypoint from {from} to {leg.Zone}, but the pack does not give both zones a waypoint.",
            Travel.Portal when !a.Town && !b.Town => $"takes a portal from {from} to {leg.Zone}; a portal goes to or from town.",
            _ => null,
        };
    }

    /// <summary>Edges are stored both ways, so the graph does not depend on which zone lists the edge.</summary>
    private static RoutePack Normalize(RoutePack pack) => pack with
    {
        Zones = pack.Zones
            .Select(z => z with
            {
                Edges = z.EdgeList
                    .Concat(pack.Zones.Where(o => o.EdgeList.Contains(z.Id)).Select(o => o.Id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
            })
            .ToList(),
    };

    [GeneratedRegex("^[a-z0-9][a-z0-9_.-]*$")]
    private static partial Regex IdPattern();
}
