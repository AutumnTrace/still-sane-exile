using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Domain.Tests;

public class PlanGateTests
{
    /// <summary>Our own test plan for the issue's acceptance journey (written for SSE, not copied from
    /// any guide): cut Entangle, then Contagion, and the Zenith I move only once Essence Drain exists.</summary>
    private const string Notes = """
        Act 1
        - cut Entangle
        - cut Contagion
        - cut support Zenith I
        - socket Zenith I into Entangle
        Act 2
        - cut Essence Drain
        - move Zenith I: Entangle → Essence Drain
        After ascendancy
        - respec into the ascendancy's side of the tree
        - upgrade Essence Drain
        """;

    private static readonly BuildPlan Plan = PlanNotesParser.Parse(Notes).Plan;

    private static readonly HashSet<string> None = [];

    private static PlanPosition At(int act, int ascendancies = 0) => new(act, ascendancies, _ => true);

    private static HashSet<string> Done(params string[] readings) =>
        Plan.Entries.Where(e => readings.Contains(e.Milestone.Describe())).Select(e => e.Key).ToHashSet();

    private static MilestoneStatus Status(string reading, PlanPosition at, HashSet<string> done) =>
        PlanGate.Check(Plan, Plan.Entries.Single(e => e.Milestone.Describe() == reading), at, done).Status;

    private static string? Next(PlanPosition at, HashSet<string> done) => PlanGate.Next(Plan, at, done)?.Milestone.Describe();

    [Fact]
    public void Acceptance_journey_cuts_entangle_then_contagion_and_moves_zenith_only_once_essence_drain_exists()
    {
        var done = Done();
        Assert.Equal("Cut Entangle", Next(At(1), done));

        done = Done("Cut Entangle");
        Assert.Equal("Cut Contagion", Next(At(1), done));

        done = Done("Cut Entangle", "Cut Contagion", "Cut support Zenith I", "Socket Zenith I into Entangle");
        Assert.Null(Next(At(1), done)); // nothing for Act 1 left, and nothing later leaks in

        // Entering Act 2 does not bring the move forward: Essence Drain does not exist yet.
        Assert.Equal("Cut Essence Drain", Next(At(2), done));
        Assert.Equal(MilestoneStatus.WaitingForGem, Status("Move Zenith I: Entangle → Essence Drain", At(2), done));

        done = Done("Cut Entangle", "Cut Contagion", "Cut support Zenith I", "Socket Zenith I into Entangle", "Cut Essence Drain");
        Assert.Equal("Move Zenith I: Entangle → Essence Drain", Next(At(2), done));
    }

    [Fact]
    public void The_move_never_comes_up_before_essence_drain_is_cut_whatever_the_stage()
    {
        var everythingElse = Done("Cut Entangle", "Cut Contagion", "Cut support Zenith I", "Socket Zenith I into Entangle");
        foreach (var at in new[] { At(1), At(2), At(3), At(5, 2) })
        {
            Assert.NotEqual(MilestoneStatus.Due, Status("Move Zenith I: Entangle → Essence Drain", at, everythingElse));
        }
    }

    [Fact]
    public void Entering_act_2_keeps_the_pre_ascendancy_plan_until_the_ascendancy_is_done()
    {
        var done = Done("Cut Entangle");

        // Act 1 steps still open stay due in Act 2 and Act 3.
        Assert.Equal(MilestoneStatus.Due, Status("Cut Contagion", At(2), done));
        Assert.Equal(MilestoneStatus.Due, Status("Cut Contagion", At(3), done));

        // After-ascendancy steps stay hidden in any act until the player says the ascendancy is done.
        Assert.Equal(MilestoneStatus.LaterStage, Status("Respec: into the ascendancy's side of the tree", At(2), done));
        Assert.Equal(MilestoneStatus.LaterStage, Status("Respec: into the ascendancy's side of the tree", At(4), done));
        Assert.Equal(MilestoneStatus.Due, Status("Respec: into the ascendancy's side of the tree", At(2, 1), done));
    }

    [Fact]
    public void Later_loadouts_never_leak_into_early_prompts()
    {
        var early = PlanGate.CheckAll(Plan, At(1), Done());

        var hidden = early.Where(c => !c.Visible).Select(c => c.Entry.Milestone.Describe()).ToList();
        Assert.Equal(["Cut Essence Drain", "Move Zenith I: Entangle → Essence Drain", "Respec: into the ascendancy's side of the tree", "Upgrade Essence Drain"], hidden);
        Assert.DoesNotContain(early.Where(c => c.Status == MilestoneStatus.Due), c => c.Entry.Milestone.Stage != PlanStage.Start);
    }

    [Fact]
    public void A_socket_waits_for_the_support_and_names_what_is_missing()
    {
        var check = PlanGate.Check(Plan, Plan.Entries.Single(e => e.Milestone.Action == GemAction.Socket), At(1), Done("Cut Entangle"));

        Assert.Equal(MilestoneStatus.WaitingForGem, check.Status);
        Assert.Equal(["Zenith I"], check.MissingGems);
    }

    [Fact]
    public void Gems_the_plan_never_cuts_are_taken_as_in_hand()
    {
        var plan = PlanNotesParser.Parse("upgrade Fireball\nsocket Zenith I into Fireball").Plan;

        Assert.Equal("Upgrade Fireball", PlanGate.Next(plan, At(1), None)?.Milestone.Describe());
        Assert.Equal(MilestoneStatus.Due, PlanGate.Check(plan, plan.Entries[1], At(1), None).Status);
    }

    [Fact]
    public void A_step_tied_to_a_zone_waits_until_the_player_reaches_it()
    {
        var route = TestRoutes.Act1();
        var plan = PlanNotesParser.Parse("upgrade Entangle at Mud Burrow\ncut Contagion", route).Plan;
        var progress = new CampaignProgress(route);

        var before = PlanPosition.From(progress, 0);
        Assert.Equal(MilestoneStatus.WaitingForZone, PlanGate.Check(plan, plan.Entries[0], before, None).Status);
        Assert.Equal("Cut Contagion", PlanGate.Next(plan, before, None)?.Milestone.Describe());

        progress.Enter("a1.mud_burrow");
        Assert.Equal("Upgrade Entangle", PlanGate.Next(plan, PlanPosition.From(progress, 0), None)?.Milestone.Describe());

        progress.Next(); // backtracking out to Clearfell keeps it due: the zone was reached
        Assert.Equal(MilestoneStatus.Due, PlanGate.Check(plan, plan.Entries[0], PlanPosition.From(progress, 0), None).Status);
    }

    [Fact]
    public void Past_the_route_the_act_the_player_sets_opens_the_next_stage()
    {
        var route = TestRoutes.Act1();
        var progress = new CampaignProgress(route);
        progress.SetAct(2);

        var at = PlanPosition.From(progress, 0);

        Assert.Equal(2, at.Act);
        Assert.Equal(MilestoneStatus.Due, Status("Cut Essence Drain", at, Done("Cut Entangle")));
    }

    [Fact]
    public void Ticked_steps_are_done_even_if_their_stage_has_not_come_yet()
    {
        var done = Done("Upgrade Essence Drain");

        Assert.Equal(MilestoneStatus.Done, Status("Upgrade Essence Drain", At(1), done));
    }
}
