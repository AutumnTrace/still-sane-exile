namespace StillSaneExile.Domain.Guide;

public enum StepState
{
    Open,
    Done,
    Skipped,
}

/// <summary>One change the player made to a step. Progress is the replay of these marks in order, so
/// undo is "drop the last mark" and resuming after a restart is "replay the saved marks".</summary>
public sealed record StepMark(long Seq, string StepId, StepState State, DateTimeOffset At);

/// <summary>Where the player says they are: a leg of the route, the zone they picked (null when they
/// are past the end of the route) and the act, which only matters past the route.</summary>
public sealed record CampaignPosition(int Leg, string? Zone, int Act);

public enum MissReason
{
    /// <summary>The player pressed Skip.</summary>
    Skipped,

    /// <summary>Still open on a leg the player has already moved past.</summary>
    LeftBehind,
}

public sealed record MissedReward(int Leg, RouteZone Zone, RouteStep Step, RewardFact Reward, MissReason Reason);

/// <summary>A step as the page lists it. <see cref="EarlierVisit"/> marks a step left open on an
/// earlier visit to the same zone.</summary>
public sealed record StepView(int Leg, RouteStep Step, StepState State, bool EarlierVisit);

/// <summary>The two HUD lines. Line 1: the zone and what to do there. Line 2: a label and what comes
/// next (the route's next stop here; the build plan's next gem step once a plan is active).</summary>
public sealed record HudLines(string Zone, string Now, string ThenLabel, string Then);

/// <summary>One character's progress through a route pack. Everything changes only on the player's
/// say-so: picking a zone or pressing next/back moves the position and never marks a step done.</summary>
public sealed class CampaignProgress
{
    public const int MaxAct = 10;

    private readonly List<StepMark> _marks = [];
    private readonly Dictionary<string, StepState> _states = new(StringComparer.Ordinal);
    private long _lastSeq;
    private int _act;

    /// <param name="marks">Saved marks, in any order. Marks for steps this pack no longer has (an older
    /// revision's) are kept out of the state but still count for numbering.</param>
    public CampaignProgress(RoutePack pack, IEnumerable<StepMark>? marks = null, CampaignPosition? position = null)
    {
        Pack = pack;
        foreach (var m in (marks ?? []).OrderBy(m => m.Seq))
        {
            _lastSeq = Math.Max(_lastSeq, m.Seq);
            if (pack.FindStep(m.StepId) is not null)
            {
                _marks.Add(m);
                _states[m.StepId] = m.State;
            }
        }

        // A saved zone the pack no longer has (renamed in a later revision) falls back to the leg's zone.
        Leg = Math.Clamp(position?.Leg ?? 0, 0, pack.Route.Count - 1);
        ZoneId = position is { Zone: null } ? null : pack.FindZone(position?.Zone)?.Id ?? pack.Route[Leg].Zone;
        _act = Math.Clamp(position?.Act ?? pack.Zone(pack.Route[Leg].Zone).Act, 1, MaxAct);
    }

    public RoutePack Pack { get; }

    /// <summary>Index into <see cref="RoutePack.Route"/>.</summary>
    public int Leg { get; private set; }

    /// <summary>The zone the player says they are in; null past the end of the route.</summary>
    public string? ZoneId { get; private set; }

    public RouteLeg CurrentLeg => Pack.Route[Leg];

    public RouteZone? Zone => Pack.FindZone(ZoneId);

    public int Act => Zone?.Act ?? _act;

    /// <summary>In the zone of the current leg (not off in a zone the route does not visit here).</summary>
    public bool OnRoute => ZoneId == CurrentLeg.Zone;

    public bool PastRoute => ZoneId is null;

    public CampaignPosition Position => new(Leg, ZoneId, Act);

    public IReadOnlyList<StepMark> Marks => _marks;

    public StepMark? LastMark => _marks.Count > 0 ? _marks[^1] : null;

    public StepState StateOf(string stepId) => _states.GetValueOrDefault(stepId, StepState.Open);

    /// <summary>Records a change to one step. Returns the new mark to save, or null when the step was
    /// already in that state.</summary>
    public StepMark? Mark(string stepId, StepState state, DateTimeOffset at)
    {
        if (Pack.FindStep(stepId) is null)
        {
            throw new ArgumentException($"Unknown step {stepId}.", nameof(stepId));
        }

        if (StateOf(stepId) == state)
        {
            return null;
        }

        var mark = new StepMark(++_lastSeq, stepId, state, at);
        _marks.Add(mark);
        _states[stepId] = state;
        return mark;
    }

    /// <summary>Takes back the last mark. Returns it so the caller can delete it from storage, or null
    /// when there is nothing to undo. Moving between zones is not undone: next and back do that.</summary>
    public StepMark? Undo()
    {
        if (_marks.Count == 0)
        {
            return null;
        }

        var last = _marks[^1];
        _marks.RemoveAt(_marks.Count - 1);
        var previous = _marks.LastOrDefault(m => m.StepId == last.StepId);
        if (previous is null)
        {
            _states.Remove(last.StepId);
        }
        else
        {
            _states[last.StepId] = previous.State;
        }

        return last;
    }

    public bool CanGoNext => !PastRoute;

    public bool CanGoBack => PastRoute || !OnRoute || Leg > 0;

    /// <summary>The next leg. From the last leg it steps past the end of the route.</summary>
    public bool Next()
    {
        if (PastRoute)
        {
            return false;
        }

        if (Leg + 1 >= Pack.Route.Count)
        {
            _act = Act;
            ZoneId = null;
            return true;
        }

        Leg++;
        ZoneId = CurrentLeg.Zone;
        return true;
    }

    /// <summary>The previous leg. Past the route or off it, back returns to the current leg first.</summary>
    public bool Back()
    {
        if (PastRoute || !OnRoute)
        {
            ZoneId = CurrentLeg.Zone;
            return true;
        }

        if (Leg == 0)
        {
            return false;
        }

        Leg--;
        ZoneId = CurrentLeg.Zone;
        return true;
    }

    /// <summary>The zone picker. Looks ahead first (the next visit to that zone from here on), then
    /// back (the latest visit behind you); a zone the route never visits keeps the leg where it is.
    /// Never marks anything done.</summary>
    public void Enter(string zoneId)
    {
        var zone = Pack.FindZone(zoneId) ?? throw new ArgumentException($"Unknown zone {zoneId}.", nameof(zoneId));
        if (zone.Id == ZoneId)
        {
            return;
        }

        Leg = FindLeg(zone.Id, Leg, Pack.Route.Count, 1) ?? FindLeg(zone.Id, Leg - 1, -1, -1) ?? Leg;
        ZoneId = zone.Id;
    }

    private int? FindLeg(string zoneId, int from, int stop, int step)
    {
        for (var i = from; i != stop; i += step)
        {
            if (Pack.Route[i].Zone == zoneId)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Past the end of the route, the player says which act they are in. Used for build-plan
    /// stages when the pack does not cover that act yet.</summary>
    public void SetAct(int act)
    {
        act = Math.Clamp(act, 1, MaxAct);
        if (!PastRoute && Act == act)
        {
            return;
        }

        _act = act;
        ZoneId = null;
    }

    /// <summary>What the page lists for the zone the player is in: this visit's steps, then anything
    /// left open on earlier visits to the same zone.</summary>
    public IReadOnlyList<StepView> StepsHere()
    {
        if (ZoneId is null)
        {
            return [];
        }

        var list = new List<StepView>();
        if (OnRoute)
        {
            list.AddRange(CurrentLeg.StepList.Select(s => new StepView(Leg, s, StateOf(s.Id), false)));
        }

        for (var i = 0; i < Leg; i++)
        {
            if (Pack.Route[i].Zone == ZoneId)
            {
                list.AddRange(Pack.Route[i].StepList.Where(s => StateOf(s.Id) == StepState.Open).Select(s => new StepView(i, s, StepState.Open, true)));
            }
        }

        return list;
    }

    public RouteStep? NextHere => StepsHere().FirstOrDefault(s => s.State == StepState.Open)?.Step;

    /// <summary>The leg after the current one, if any.</summary>
    public RouteLeg? NextLeg => !PastRoute && Leg + 1 < Pack.Route.Count ? Pack.Route[Leg + 1] : null;

    /// <summary>The route's HUD lines, before any build plan is consulted.</summary>
    public HudLines Lines()
    {
        if (PastRoute)
        {
            return new HudLines($"Act {Act}", "past the end of this route", "Then", "carry on in game");
        }

        var zone = Zone!;
        var now = NextHere?.Describe() ?? (OnRoute ? CurrentLeg.Hint : null) ?? "nothing left to do here";
        if (!OnRoute)
        {
            return new HudLines(zone.Name, now, "Then", $"back to {Pack.Zone(CurrentLeg.Zone).Name}");
        }

        if (NextLeg is not { } next)
        {
            return new HudLines(zone.Name, now, "Then", "end of this route");
        }

        var nextZone = Pack.Zone(next.Zone).Name;
        var nextStep = next.StepList.FirstOrDefault(s => StateOf(s.Id) == StepState.Open);
        return new HudLines(zone.Name, now, "Then", nextStep is null ? nextZone : $"{nextZone}: {nextStep.Describe()}");
    }

    /// <summary>Permanent rewards the player skipped, or left open on a leg they have moved past.</summary>
    public IReadOnlyList<MissedReward> MissedRewards()
    {
        var list = new List<MissedReward>();
        foreach (var (leg, step) in Pack.AllSteps())
        {
            var state = StateOf(step.Id);
            MissReason? reason = state switch
            {
                StepState.Skipped => MissReason.Skipped,
                StepState.Open when leg < Leg || PastRoute => MissReason.LeftBehind,
                _ => null,
            };
            if (reason is { } r)
            {
                list.AddRange(step.RewardFacts.Where(f => f.Permanent).Select(f => new MissedReward(leg, Pack.Zone(Pack.Route[leg].Zone), step, f, r)));
            }
        }

        return list;
    }

    public IReadOnlyList<StepView> Skipped() =>
        Pack.AllSteps().Where(x => StateOf(x.Step.Id) == StepState.Skipped).Select(x => new StepView(x.Leg, x.Step, StepState.Skipped, false)).ToList();

    /// <summary>True once the player is in the zone or has moved past its first visit on the route, or
    /// is in a later act. Build-plan steps tied to a zone wait for this.</summary>
    public bool Reached(string zoneId)
    {
        if (ZoneId == zoneId)
        {
            return true;
        }

        var last = PastRoute ? Pack.Route.Count - 1 : Leg;
        for (var i = 0; i <= last; i++)
        {
            if (Pack.Route[i].Zone == zoneId)
            {
                return true;
            }
        }

        return Pack.FindZone(zoneId) is { } z && z.Act < Act;
    }
}
