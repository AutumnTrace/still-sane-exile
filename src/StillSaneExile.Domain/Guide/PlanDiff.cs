namespace StillSaneExile.Domain.Guide;

public sealed record MilestoneChange(PlanMilestone Old, PlanMilestone New, IReadOnlyList<string> What);

/// <summary>What a new revision of a plan changes. The old revision stays in use until the player
/// accepts the new one, so this is what they read before deciding.</summary>
public sealed record PlanDiff(IReadOnlyList<PlanMilestone> Added, IReadOnlyList<PlanMilestone> Removed, IReadOnlyList<MilestoneChange> Changed, int Unchanged)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    /// <summary>Pairs steps by key first (same action, gems and level), then by action and gem in
    /// order, so a step whose target or level changed reads as changed rather than removed and added.</summary>
    public static PlanDiff Between(BuildPlan old, BuildPlan @new)
    {
        var changed = new List<MilestoneChange>();
        var unchanged = 0;
        var oldLeft = old.Entries.ToList();
        var newLeft = new List<PlanEntry>();

        foreach (var n in @new.Entries)
        {
            var match = oldLeft.FirstOrDefault(o => o.Key == n.Key);
            if (match is null)
            {
                newLeft.Add(n);
                continue;
            }

            oldLeft.Remove(match);
            var what = Differences(match.Milestone, n.Milestone);
            if (what.Count == 0)
            {
                unchanged++;
            }
            else
            {
                changed.Add(new MilestoneChange(match.Milestone, n.Milestone, what));
            }
        }

        var added = new List<PlanMilestone>();
        foreach (var n in newLeft)
        {
            var match = oldLeft.FirstOrDefault(o => o.Milestone.Action == n.Milestone.Action && GemName.Key(o.Milestone.Gem) == GemName.Key(n.Milestone.Gem));
            if (match is null)
            {
                added.Add(n.Milestone);
                continue;
            }

            oldLeft.Remove(match);
            changed.Add(new MilestoneChange(match.Milestone, n.Milestone, Differences(match.Milestone, n.Milestone)));
        }

        return new PlanDiff(added, oldLeft.Select(o => o.Milestone).ToList(), changed, unchanged);
    }

    private static List<string> Differences(PlanMilestone a, PlanMilestone b)
    {
        var what = new List<string>();
        void Compare(string label, string? x, string? y)
        {
            if (!string.Equals(x ?? "", y ?? "", StringComparison.Ordinal))
            {
                what.Add($"{label}: {x ?? "none"} → {y ?? "none"}");
            }
        }

        Compare("from", a.From, b.From);
        Compare("into", a.Target, b.Target);
        Compare("stage", a.Stage.Label, b.Stage.Label);
        Compare("where", a.Where, b.Where);
        Compare("uncut gem", Uncut(a.Uncut), Uncut(b.Uncut));
        Compare("needs", a.Needs.Count == 0 ? null : string.Join(", ", a.Needs), b.Needs.Count == 0 ? null : string.Join(", ", b.Needs));
        Compare("priority", a.PriorityText, b.PriorityText);
        return what;
    }

    internal static string? Uncut(UncutGem? u) => u is null ? null : u.Level is { } l ? $"{u.Kind.ToString().ToLowerInvariant()}, level {l}" : u.Kind.ToString().ToLowerInvariant();
}
