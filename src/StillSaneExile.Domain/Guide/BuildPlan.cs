using System.Text.Json;
using System.Text.Json.Serialization;

namespace StillSaneExile.Domain.Guide;

/// <summary>What a build-plan step does with a gem.</summary>
public enum GemAction
{
    Cut,
    Upgrade,
    CutSupport,
    Socket,
    MoveSupport,
    Reserve,
    Respec,
}

public enum UncutKind
{
    Skill,
    Support,
    Spirit,
}

/// <summary>The uncut gem a step uses, as the player wrote it. The level is null when not given.</summary>
public sealed record UncutGem(UncutKind Kind, int? Level);

/// <summary>When a step can come up: from an act on, and after a number of ascendancies. Stages only
/// open on what the player tells SSE (the act they are in, the ascendancies they have done), so a
/// later loadout can never show up early.</summary>
public sealed record PlanStage(int Act, int Ascendancies)
{
    public static readonly PlanStage Start = new(1, 0);

    public bool ReachedBy(int act, int ascendancies) => act >= Act && ascendancies >= Ascendancies;

    public bool IsBefore(PlanStage other) => Act < other.Act || Ascendancies < other.Ascendancies;

    [JsonIgnore]
    public string Label => Ascendancies switch
    {
        0 => $"Act {Act}",
        _ when Act == 1 => $"After {Ordinal(Ascendancies)}ascendancy",
        _ => $"Act {Act} · after {Ordinal(Ascendancies)}ascendancy",
    };

    private static string Ordinal(int n) => n switch
    {
        1 => "",
        2 => "2nd ",
        3 => "3rd ",
        _ => $"{n}th ",
    };
}

public enum MilestoneTrigger
{
    /// <summary>Comes up as soon as its stage is reached and the gems it needs are in hand.</summary>
    Stage,

    /// <summary>Also waits until the player reaches the zone the step names.</summary>
    Zone,
}

/// <summary>One gem step from the player's notes. Every field comes from what the player typed (or
/// pasted from their own copy of a guide); nothing is fetched.</summary>
public sealed record PlanMilestone
{
    public const string NoPriority = "Author did not specify";

    /// <summary>The line it came from in the notes, from 1.</summary>
    public required int Line { get; init; }

    /// <summary>The note line as written.</summary>
    public required string Source { get; init; }

    public required PlanStage Stage { get; init; }

    public required GemAction Action { get; init; }

    /// <summary>The gem acted on: the skill to cut or upgrade, or the support to cut, socket or move.
    /// For a respec, whatever followed "respec" (may be empty).</summary>
    public required string Gem { get; init; }

    /// <summary>Move support: the skill the support comes out of, when written.</summary>
    public string? From { get; init; }

    /// <summary>Socket and move support: the skill the support goes into.</summary>
    public string? Target { get; init; }

    public UncutGem? Uncut { get; init; }

    /// <summary>The zone or NPC the step names, as written.</summary>
    public string? Where { get; init; }

    /// <summary>Set when <see cref="Where"/> names a zone in the route pack. The step then waits until
    /// the player reaches that zone.</summary>
    public string? ZoneId { get; init; }

    /// <summary>Gems the player wrote this step needs, on top of the ones it acts on.</summary>
    public IReadOnlyList<string> Needs { get; init; } = [];

    /// <summary>The guide author's priority as the player copied it; null when they gave none.</summary>
    public string? Priority { get; init; }

    [JsonIgnore]
    public MilestoneTrigger Trigger => ZoneId is null ? MilestoneTrigger.Stage : MilestoneTrigger.Zone;

    [JsonIgnore]
    public string PriorityText => Priority ?? NoPriority;

    /// <summary>The step in a few words: "Upgrade Entangle", "Move Zenith I: Entangle → Essence Drain".</summary>
    public string Describe() => Action switch
    {
        GemAction.Cut => $"Cut {Gem}",
        GemAction.Upgrade => $"Upgrade {Gem}",
        GemAction.CutSupport => $"Cut support {Gem}",
        GemAction.Socket => $"Socket {Gem} into {Target}",
        GemAction.MoveSupport when From is null => $"Move {Gem} → {Target}",
        GemAction.MoveSupport => $"Move {Gem}: {From} → {Target}",
        GemAction.Reserve => $"Reserve {Gem}",
        _ => Gem.Length == 0 ? "Respec" : $"Respec: {Gem}",
    };

    /// <summary>The gems this step acts on, which must be in hand first.</summary>
    public IEnumerable<string> GemsUsed() => Action switch
    {
        GemAction.Upgrade or GemAction.Reserve => [Gem],
        GemAction.Socket => [Gem, Target!],
        GemAction.MoveSupport => From is null ? [Gem, Target!] : [Gem, From, Target!],
        _ => [],
    };

    [JsonIgnore]
    public bool MakesGem => Action is GemAction.Cut or GemAction.CutSupport;

    /// <summary>What identifies the step across revisions, before duplicates are numbered.</summary>
    internal string BaseKey => string.Join('|', Action.ToString().ToLowerInvariant(), GemName.Key(Gem), GemName.Key(From), GemName.Key(Target), Uncut?.Level?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
}

/// <summary>A milestone with its stable key and every gem it needs.</summary>
public sealed record PlanEntry(string Key, PlanMilestone Milestone, IReadOnlyList<string> Requires);

/// <summary>An ordered build plan. Keys stay the same when the plan is re-saved, so ticks carry over to
/// a new revision for every step that did not change.</summary>
public sealed class BuildPlan
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
    };

    private readonly HashSet<string> _made;

    public BuildPlan(IEnumerable<PlanMilestone> milestones)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var entries = new List<PlanEntry>();
        foreach (var m in milestones)
        {
            var n = seen[m.BaseKey] = seen.GetValueOrDefault(m.BaseKey) + 1;
            var requires = m.GemsUsed().Concat(m.Needs).Where(g => g.Length > 0).DistinctBy(GemName.Key).ToList();
            entries.Add(new PlanEntry(n == 1 ? m.BaseKey : $"{m.BaseKey}#{n}", m, requires));
        }

        Entries = entries;
        _made = entries.Where(e => e.Milestone.MakesGem).Select(e => GemName.Key(e.Milestone.Gem)).ToHashSet(StringComparer.Ordinal);
    }

    public static BuildPlan Empty { get; } = new([]);

    public IReadOnlyList<PlanEntry> Entries { get; }

    public IEnumerable<PlanMilestone> Milestones => Entries.Select(e => e.Milestone);

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>True when the plan has a step that cuts this gem. Steps that need it wait for that cut
    /// to be ticked. Gems the plan never cuts are taken as already in hand.</summary>
    public bool Makes(string gem) => _made.Contains(GemName.Key(gem));

    /// <summary>True once a step that cuts this gem is ticked, or when the plan never cuts it.</summary>
    public bool InHand(string gem, IReadOnlySet<string> done) =>
        !Makes(gem) || Entries.Any(e => e.Milestone.MakesGem && GemName.Key(e.Milestone.Gem) == GemName.Key(gem) && done.Contains(e.Key));

    /// <summary>Saved with each revision, so a saved plan never changes when the notes parser does.</summary>
    public string ToJson() => JsonSerializer.Serialize(Milestones.ToList(), Json);

    public static BuildPlan FromJson(string json) => new(JsonSerializer.Deserialize<List<PlanMilestone>>(json, Json) ?? []);
}

/// <summary>Gem names compare without case, extra spaces or surrounding quotes.</summary>
public static class GemName
{
    public static string Key(string? name) =>
        string.Join(' ', (name ?? "").Trim().Trim('"', '\'', '“', '”').ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
