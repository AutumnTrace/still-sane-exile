namespace StillSaneExile.Domain.Guide;

/// <summary>How far along the character is, as the player told SSE: the act (from the zone they picked,
/// or set by hand past the route), the ascendancies they have done, and which zones they have reached.</summary>
public sealed record PlanPosition(int Act, int Ascendancies, Func<string, bool> ZoneReached)
{
    public static PlanPosition From(CampaignProgress? progress, int ascendancies) =>
        new(progress?.Act ?? 1, ascendancies, zone => progress?.Reached(zone) == true);
}

public enum MilestoneStatus
{
    Done,

    /// <summary>Its stage is reached, the gems it needs are in hand and its zone (if any) reached.</summary>
    Due,

    /// <summary>Its stage is reached but a gem it needs has not been cut yet.</summary>
    WaitingForGem,

    /// <summary>Its stage is reached but the zone it names has not been reached yet.</summary>
    WaitingForZone,

    /// <summary>A later stage. Never shown as a prompt and never counted as next.</summary>
    LaterStage,
}

public sealed record MilestoneCheck(PlanEntry Entry, MilestoneStatus Status, IReadOnlyList<string> MissingGems)
{
    /// <summary>On the checklist now: everything except later stages.</summary>
    public bool Visible => Status != MilestoneStatus.LaterStage;
}

/// <summary>Stage gating. A step comes up only once its stage is reached (act and ascendancies), every
/// gem it acts on is in hand, and the zone it names is reached. Entering a later act therefore keeps the
/// pre-ascendancy steps going until the player says the ascendancy is done, and a step that moves a
/// support onto a skill waits until that skill has been cut.</summary>
public static class PlanGate
{
    public static MilestoneCheck Check(BuildPlan plan, PlanEntry entry, PlanPosition position, IReadOnlySet<string> done)
    {
        var m = entry.Milestone;
        if (done.Contains(entry.Key))
        {
            return new MilestoneCheck(entry, MilestoneStatus.Done, []);
        }

        if (!m.Stage.ReachedBy(position.Act, position.Ascendancies))
        {
            return new MilestoneCheck(entry, MilestoneStatus.LaterStage, []);
        }

        var missing = entry.Requires.Where(g => !plan.InHand(g, done)).ToList();
        if (missing.Count > 0)
        {
            return new MilestoneCheck(entry, MilestoneStatus.WaitingForGem, missing);
        }

        if (m.ZoneId is { } zone && !position.ZoneReached(zone))
        {
            return new MilestoneCheck(entry, MilestoneStatus.WaitingForZone, []);
        }

        return new MilestoneCheck(entry, MilestoneStatus.Due, []);
    }

    public static IReadOnlyList<MilestoneCheck> CheckAll(BuildPlan plan, PlanPosition position, IReadOnlySet<string> done) =>
        plan.Entries.Select(e => Check(plan, e, position, done)).ToList();

    /// <summary>The first due step in plan order: the HUD's "Next gem" line.</summary>
    public static PlanEntry? Next(BuildPlan plan, PlanPosition position, IReadOnlySet<string> done) =>
        plan.Entries.FirstOrDefault(e => Check(plan, e, position, done).Status == MilestoneStatus.Due);
}
