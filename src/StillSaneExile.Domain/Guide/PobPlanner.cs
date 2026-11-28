namespace StillSaneExile.Domain.Guide;

/// <summary>A skill set and the stage SSE gave it. <see cref="Stage"/> is null when its title names no
/// stage and it is not the first set: its gems are then listed but never become steps.</summary>
public sealed record SkillSetStage(PobSkillSet Set, PlanStage? Stage, int Steps);

/// <summary>The gem steps a pasted build gives the guide, and where each came from.</summary>
public sealed record PobPlan(BuildPlan Plan, int FromNotes, int FromSkillSets, IReadOnlyList<SkillSetStage> Sets)
{
    public IEnumerable<SkillSetStage> Unstaged => Sets.Where(s => s.Stage is null);
}

/// <summary>Turns a pasted build into gem steps. Two sources, both the build author's own:
/// <list type="bullet">
/// <item>The build's notes, read by <see cref="PlanNotesParser"/> like the player's own notes were. A
/// line is kept only when every gem it names is in the build, so prose such as "move on to Act 2" never
/// turns into a step.</item>
/// <item>The skill sets. A set's stage comes from its title ("Act 2", "after ascendancy"); the first set
/// counts from the start when its title names none. For each set in order: cut each skill and support
/// not cut yet, socket each support into its group's skill, and move a support that sits in another
/// skill than before. A later set whose title names no stage gives no steps, so an endgame set never
/// shows up in Act 1.</item>
/// </list>
/// Steps run in stage order, the notes' first within a stage. Gem levels are not turned into upgrades:
/// Path of Building often sets every gem to its top level whatever the stage.</summary>
public static class PobPlanner
{
    public const string NotesOrigin = "the build's notes";

    public static PobPlan Plan(PobBuild build, RoutePack? route = null)
    {
        var gems = build.GemKeys;
        var fromNotes = PlanNotesParser.Parse(build.Notes, route).Plan.Milestones
            .Where(m => Names(m).All(g => gems.Contains(GemName.Key(g))))
            .Select(m => m with { Origin = NotesOrigin })
            .ToList();

        var cut = fromNotes.Where(m => m.MakesGem).Select(m => GemName.Key(m.Gem)).ToHashSet(StringComparer.Ordinal);
        var socketed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in fromNotes.Where(m => m.Action is GemAction.Socket or GemAction.MoveSupport))
        {
            socketed[GemName.Key(m.Gem)] = m.Target!;
        }

        var generated = new List<PlanMilestone>();
        var sets = new List<SkillSetStage>();
        PlanStage? last = null;
        foreach (var set in build.SkillSets)
        {
            var stage = PlanNotesParser.StageInTitle(set.Title, last ?? PlanStage.Start) ?? (last is null ? PlanStage.Start : null);
            if (stage is null)
            {
                sets.Add(new SkillSetStage(set, null, 0));
                continue;
            }

            last = stage;
            var before = generated.Count;
            var origin = set.Title is { } t ? $"skill set “{t}”" : "the build's skills";
            var thisSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in set.Groups.Where(g => g.Enabled && !g.FromItem))
            {
                var main = group.MainSkill;
                foreach (var skill in group.Skills)
                {
                    if (cut.Add(GemName.Key(skill.Name)))
                    {
                        generated.Add(Step(GemAction.Cut, skill.Name, stage, origin));
                    }
                }

                foreach (var support in group.Supports)
                {
                    var key = GemName.Key(support.Name);
                    if (cut.Add(key))
                    {
                        generated.Add(Step(GemAction.CutSupport, support.Name, stage, origin));
                    }

                    // A support goes into one skill at a time: the first group that has it in this set.
                    if (main is null || !thisSet.Add(key))
                    {
                        continue;
                    }

                    if (!socketed.TryGetValue(key, out var was))
                    {
                        generated.Add(Step(GemAction.Socket, support.Name, stage, origin) with { Target = main.Name });
                    }
                    else if (GemName.Key(was) != GemName.Key(main.Name))
                    {
                        generated.Add(Step(GemAction.MoveSupport, support.Name, stage, origin) with { From = was, Target = main.Name });
                    }

                    socketed[key] = main.Name;
                }
            }

            sets.Add(new SkillSetStage(set, stage, generated.Count - before));
        }

        // Stable: within a stage the notes keep their order ahead of the skill sets'.
        var steps = fromNotes.Concat(generated)
            .Select((m, i) => (m, i))
            .OrderBy(x => x.m.Stage.Ascendancies).ThenBy(x => x.m.Stage.Act).ThenBy(x => x.i)
            .Select(x => x.m);
        return new PobPlan(new BuildPlan(steps), fromNotes.Count, generated.Count, sets);
    }

    private static PlanMilestone Step(GemAction action, string gem, PlanStage stage, string origin) => new()
    {
        Line = 0,
        Source = origin,
        Stage = stage,
        Action = action,
        Gem = gem,
        Origin = origin,
    };

    /// <summary>The gem names a step relies on. A respec names none.</summary>
    private static IEnumerable<string> Names(PlanMilestone m) => m.Action == GemAction.Respec
        ? m.Needs
        : new[] { m.Gem, m.From, m.Target }.OfType<string>().Concat(m.Needs).Where(g => g.Length > 0);
}
