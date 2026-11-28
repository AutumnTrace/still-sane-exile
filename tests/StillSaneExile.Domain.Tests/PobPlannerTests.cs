using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Domain.Tests;

/// <summary>What a pasted build gives the guide: gem steps from its notes and its skill sets, gated by
/// stage. The practice build is synthetic (fixtures/builds/practice-witch.xml).</summary>
public class PobPlannerTests
{
    private static readonly PobPlan Practice = PobPlanner.Plan(PobCodeTests.Practice());

    private static readonly HashSet<string> None = [];

    private static PlanPosition At(int act, int ascendancies = 0) => new(act, ascendancies, _ => true);

    private static HashSet<string> Done(BuildPlan plan, params string[] readings) =>
        plan.Entries.Where(e => readings.Contains(e.Milestone.Describe())).Select(e => e.Key).ToHashSet();

    private static string Xml(string skills, string notes = "") =>
        $"""<PathOfBuilding2><Build level="10" className="Witch"/><Skills>{skills}</Skills><Notes>{notes}</Notes></PathOfBuilding2>""";

    private static PobPlan PlanOf(string skills, string notes = "") => PobPlanner.Plan(PobCodeTests.Read(PobCodeTests.Encode(Xml(skills, notes))));

    private static string Gem(string name, bool support = false) =>
        support
            ? $"""<Gem nameSpec="{name}" skillId="Support{name.Replace(" ", "", StringComparison.Ordinal)}Player" gemId="Metadata/Items/Gem/SupportGem{name.Replace(" ", "", StringComparison.Ordinal)}"/>"""
            : $"""<Gem nameSpec="{name}" skillId="{name.Replace(" ", "", StringComparison.Ordinal)}Player" gemId="Metadata/Items/Gems/SkillGem{name.Replace(" ", "", StringComparison.Ordinal)}"/>""";

    [Fact]
    public void The_practice_build_gives_its_notes_steps_then_its_skill_sets_steps_in_stage_order()
    {
        Assert.Equal(
        [
            "Act 1 · Cut Entangle",
            "Act 1 · Cut support Zenith I",
            "Act 1 · Socket Zenith I into Entangle",
            "Act 1 · Cut Contagion",
            "Act 2 · Cut Essence Drain",
            "Act 2 · Move Zenith I: Entangle → Essence Drain",
            "Act 2 · Cut support Controlled Destruction",
            "Act 2 · Socket Controlled Destruction into Essence Drain",
            "Act 2 · after ascendancy · Respec: the passive tree for the endgame",
        ], Practice.Plan.Milestones.Select(m => $"{m.Stage.Label} · {m.Describe()}"));
        Assert.Equal(3, Practice.FromNotes);
        Assert.Equal(6, Practice.FromSkillSets);
    }

    [Fact]
    public void Each_step_says_where_in_the_build_it_came_from()
    {
        var steps = Practice.Plan.Milestones.ToList();

        Assert.Equal(PobPlanner.NotesOrigin, steps.Single(m => m.Describe() == "Cut Entangle").Origin);
        Assert.Equal("Mud Burrow", steps.Single(m => m.Describe() == "Cut Entangle").Where);
        Assert.Equal("high", steps.Single(m => m.Describe() == "Cut Essence Drain").Priority);
        Assert.Equal("skill set “Act 2”", steps.Single(m => m.Action == GemAction.MoveSupport).Origin);
        Assert.Equal(PlanMilestone.NoPriority, steps.Single(m => m.Action == GemAction.MoveSupport).PriorityText);
    }

    [Fact]
    public void Prose_in_the_notes_never_becomes_a_step()
    {
        // "Move on to the Grelwood once the Devourer is down." reads like a move, but names no gem in the build.
        Assert.DoesNotContain(Practice.Plan.Milestones, m => m.Describe().Contains("Grelwood", StringComparison.Ordinal));
        Assert.DoesNotContain(Practice.Plan.Milestones, m => m.Gem == "on");
    }

    [Fact]
    public void Acceptance_journey_cuts_entangle_then_contagion_and_moves_zenith_only_once_essence_drain_exists()
    {
        var plan = Practice.Plan;
        string? Next(PlanPosition at, HashSet<string> done) => PlanGate.Next(plan, at, done)?.Milestone.Describe();

        Assert.Equal("Cut Entangle", Next(At(1), None));
        var done = Done(plan, "Cut Entangle", "Cut support Zenith I", "Socket Zenith I into Entangle");
        Assert.Equal("Cut Contagion", Next(At(1), done));

        done = Done(plan, "Cut Entangle", "Cut support Zenith I", "Socket Zenith I into Entangle", "Cut Contagion");
        Assert.Null(Next(At(1), done)); // nothing from Act 2 leaks into Act 1

        var move = plan.Entries.Single(e => e.Milestone.Action == GemAction.MoveSupport);
        Assert.Equal("Cut Essence Drain", Next(At(2), done));
        Assert.Equal(MilestoneStatus.WaitingForGem, PlanGate.Check(plan, move, At(2), done).Status);
        Assert.Equal(MilestoneStatus.LaterStage, PlanGate.Check(plan, move, At(1), done).Status);

        done = Done(plan, "Cut Entangle", "Cut support Zenith I", "Socket Zenith I into Entangle", "Cut Contagion", "Cut Essence Drain");
        Assert.Equal("Move Zenith I: Entangle → Essence Drain", Next(At(2), done));
    }

    [Fact]
    public void A_later_skill_set_whose_title_names_no_stage_gives_no_steps()
    {
        var endgame = Practice.Sets.Single(s => s.Set.Title == "Endgame");

        Assert.Null(endgame.Stage);
        Assert.Equal(0, endgame.Steps);
        Assert.DoesNotContain(Practice.Plan.Milestones, m => m.Gem == "Arcane Surge");
        Assert.Equal(["Endgame"], Practice.Unstaged.Select(s => s.Set.Title));
    }

    [Fact]
    public void A_build_with_one_set_counts_it_from_the_start()
    {
        var plan = PlanOf($"""<SkillSet id="1" title="Endgame"><Skill enabled="true">{Gem("Spark")}{Gem("Arcane Surge", support: true)}</Skill></SkillSet>""");

        Assert.Equal(new[] { "Cut Spark", "Cut support Arcane Surge", "Socket Arcane Surge into Spark" }, plan.Plan.Milestones.Select(m => m.Describe()));
        Assert.All(plan.Plan.Milestones, m => Assert.Equal(PlanStage.Start, m.Stage));
    }

    [Fact]
    public void Switched_off_groups_and_skills_from_items_are_not_cut_and_lone_supports_are_cut_but_not_socketed()
    {
        var plan = PlanOf($"""
            <SkillSet id="1" title="Act 1">
              <Skill enabled="false">{Gem("Fireball")}</Skill>
              <Skill enabled="true" source="Item:1:Practice Bane">{Gem("Granted Bolt")}</Skill>
              <Skill enabled="true">{Gem("Arcane Surge", support: true)}</Skill>
            </SkillSet>
            """);

        Assert.Equal(new[] { "Cut support Arcane Surge" }, plan.Plan.Milestones.Select(m => m.Describe()));
    }

    [Fact]
    public void A_support_in_two_groups_of_one_set_goes_into_the_first_only()
    {
        var plan = PlanOf($"""
            <SkillSet id="1" title="Act 1">
              <Skill enabled="true">{Gem("Spark")}{Gem("Arcane Surge", support: true)}</Skill>
              <Skill enabled="true">{Gem("Flame Wall")}{Gem("Arcane Surge", support: true)}</Skill>
            </SkillSet>
            """);

        Assert.Single(plan.Plan.Milestones, m => m.Action is GemAction.Socket or GemAction.MoveSupport);
    }

    [Fact]
    public void The_notes_steps_count_only_when_their_gems_are_in_the_build()
    {
        var plan = PlanOf(
            $"""<SkillSet id="1"><Skill enabled="true">{Gem("Spark")}</Skill></SkillSet>""",
            "cut Spark\ncut Fireball\nupgrade Spark to level 5\nrespec at the Encampment");

        Assert.Equal(new[] { "Cut Spark", "Upgrade Spark", "Respec" }, plan.Plan.Milestones.Select(m => m.Describe()));
        Assert.Equal(3, plan.FromNotes);
        Assert.Equal(0, plan.FromSkillSets); // Spark was already cut by the notes
    }

    [Theory]
    [InlineData("Act 2", 2, 0)]
    [InlineData("Acts 4-6", 4, 0)]
    [InlineData("Leveling (Act 1)", 1, 0)]
    [InlineData("Act III", 3, 0)]
    [InlineData("After Ascendancy", 1, 1)]
    [InlineData("Act 3, post-ascendancy", 3, 1)]
    [InlineData("Second ascendancy", 1, 2)]
    public void A_skill_set_title_names_its_stage(string title, int act, int ascendancies) =>
        Assert.Equal(new PlanStage(act, ascendancies), PlanNotesParser.StageInTitle(title, PlanStage.Start));

    [Theory]
    [InlineData("Endgame")]
    [InlineData("Level 12")]
    [InlineData("Active skills")]
    [InlineData("Exact copy")]
    [InlineData(null)]
    public void A_title_that_names_no_stage_gets_none(string? title) => Assert.Null(PlanNotesParser.StageInTitle(title, PlanStage.Start));

    [Fact]
    public void Titles_only_move_the_stage_forward()
    {
        Assert.Equal(new PlanStage(3, 1), PlanNotesParser.StageInTitle("Act 1", new PlanStage(3, 1)));
        Assert.Equal(new PlanStage(3, 1), PlanNotesParser.StageInTitle("Before ascendancy", new PlanStage(3, 1)));
    }

    [Fact]
    public void A_saved_plan_keeps_where_its_steps_came_from_and_old_plans_still_open()
    {
        var again = BuildPlan.FromJson(Practice.Plan.ToJson());
        var old = BuildPlan.FromJson("""[{"line":1,"source":"cut Spark","stage":{"act":1,"ascendancies":0},"action":"cut","gem":"Spark"}]""");

        Assert.Equal(Practice.Plan.Milestones.Select(m => m.Origin), again.Milestones.Select(m => m.Origin));
        Assert.Null(old.Milestones.Single().Origin);
    }
}
