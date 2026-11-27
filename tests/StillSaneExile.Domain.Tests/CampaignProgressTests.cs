using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Domain.Tests;

public class CampaignProgressTests
{
    private static readonly DateTimeOffset T0 = Fixtures.DemoNow;

    [Fact]
    public void A_new_character_starts_on_the_first_leg_with_nothing_done()
    {
        var p = new CampaignProgress(TestRoutes.Act1());

        Assert.Equal(0, p.Leg);
        Assert.Equal("a1.riverbank", p.ZoneId);
        Assert.True(p.OnRoute);
        Assert.Equal(1, p.Act);
        Assert.All(p.Pack.AllSteps(), s => Assert.Equal(StepState.Open, p.StateOf(s.Step.Id)));
        Assert.False(p.CanGoBack);
    }

    [Fact]
    public void Entering_zones_never_marks_a_step_done()
    {
        var p = new CampaignProgress(TestRoutes.Act1());

        p.Next();
        p.Next();
        p.Enter("a1.mud_burrow");
        p.Enter("a1.red_vale");
        p.Back();

        Assert.Empty(p.Marks);
        Assert.All(p.Pack.AllSteps(), s => Assert.Equal(StepState.Open, p.StateOf(s.Step.Id)));
    }

    [Fact]
    public void Next_and_back_walk_the_legs_including_the_way_back_out_of_a_dead_end()
    {
        var p = new CampaignProgress(TestRoutes.Act1());
        var visited = new List<string> { p.ZoneId! };
        while (p.Next() && !p.PastRoute)
        {
            visited.Add(p.ZoneId!);
        }

        Assert.Equal(p.Pack.Route.Select(l => l.Zone), visited);
        Assert.True(p.PastRoute);

        Assert.True(p.Back());
        Assert.Equal("a1.grelwood", p.ZoneId);
        Assert.Equal(7, p.Leg);
        p.Back();
        Assert.Equal("a1.red_vale", p.ZoneId);
    }

    [Fact]
    public void The_zone_picker_looks_ahead_first_then_back()
    {
        var p = new CampaignProgress(TestRoutes.Act1());

        p.Enter("a1.clearfell");
        Assert.Equal(2, p.Leg); // the first visit, ahead of the Riverbank

        p.Enter("a1.mud_burrow");
        p.Enter("a1.clearfell");
        Assert.Equal(4, p.Leg); // coming back out of the Mud Burrow is the second visit, not the first

        p.Enter("a1.riverbank");
        Assert.Equal(0, p.Leg); // only behind
    }

    [Fact]
    public void A_zone_the_route_skips_here_keeps_the_leg_and_next_carries_on()
    {
        var p = new CampaignProgress(TestRoutes.Synthetic());
        p.Next(); // the Field

        p.Enter("t.grove");

        Assert.False(p.OnRoute);
        Assert.Equal(1, p.Leg);
        Assert.Equal("Grove", p.Lines().Zone);
        Assert.Equal("back to The Field", p.Lines().Then);

        p.Next();
        Assert.Equal("t.cave", p.ZoneId);
    }

    [Fact]
    public void Back_from_a_side_zone_returns_to_the_current_leg()
    {
        var p = new CampaignProgress(TestRoutes.Synthetic());
        p.Next();
        p.Enter("t.grove");

        p.Back();

        Assert.Equal("t.field", p.ZoneId);
        Assert.Equal(1, p.Leg);
    }

    [Fact]
    public void Skip_then_undo_reopens_the_step()
    {
        var p = new CampaignProgress(TestRoutes.Act1());

        var mark = p.Mark("a1.clearfell.beira", StepState.Skipped, T0);
        Assert.NotNull(mark);
        Assert.Equal(StepState.Skipped, p.StateOf("a1.clearfell.beira"));

        Assert.Equal(mark, p.Undo());
        Assert.Equal(StepState.Open, p.StateOf("a1.clearfell.beira"));
        Assert.Null(p.Undo());
    }

    [Fact]
    public void Undo_goes_back_to_the_steps_previous_state_not_to_open()
    {
        var p = new CampaignProgress(TestRoutes.Act1());
        p.Mark("a1.riverbank.miller", StepState.Done, T0);
        p.Mark("a1.riverbank.miller", StepState.Skipped, T0.AddMinutes(1));

        p.Undo();

        Assert.Equal(StepState.Done, p.StateOf("a1.riverbank.miller"));
    }

    [Fact]
    public void Marking_a_step_with_the_state_it_already_has_records_nothing()
    {
        var p = new CampaignProgress(TestRoutes.Act1());
        p.Mark("a1.riverbank.miller", StepState.Done, T0);

        Assert.Null(p.Mark("a1.riverbank.miller", StepState.Done, T0));
        Assert.Single(p.Marks);
    }

    [Fact]
    public void Marking_an_unknown_step_throws()
    {
        var p = new CampaignProgress(TestRoutes.Act1());

        Assert.Throws<ArgumentException>(() => p.Mark("a9.nowhere", StepState.Done, T0));
    }

    [Fact]
    public void Marks_for_steps_a_newer_revision_dropped_are_ignored_but_keep_numbering()
    {
        var saved = new[] { new StepMark(4, "a1.old.step", StepState.Done, T0), new StepMark(2, "a1.riverbank.miller", StepState.Done, T0) };
        var p = new CampaignProgress(TestRoutes.Act1(), saved);

        Assert.Single(p.Marks);
        Assert.Equal(5, p.Mark("a1.clearfell.beira", StepState.Skipped, T0)!.Seq);
    }

    [Fact]
    public void A_skipped_permanent_reward_is_listed_as_missed()
    {
        var p = new CampaignProgress(TestRoutes.Synthetic());
        p.Next();
        p.Mark("t.field.shrine", StepState.Skipped, T0);

        var missed = Assert.Single(p.MissedRewards());
        Assert.Equal(MissReason.Skipped, missed.Reason);
        Assert.Equal("+1 passive skill point", missed.Reward.Text);
        Assert.Equal("The Field", missed.Zone.Name);
    }

    [Fact]
    public void An_open_permanent_reward_counts_as_left_behind_only_once_you_move_on()
    {
        var p = new CampaignProgress(TestRoutes.Synthetic());
        p.Enter("t.field");
        Assert.Empty(p.MissedRewards());

        p.Next();
        var missed = Assert.Single(p.MissedRewards());
        Assert.Equal(MissReason.LeftBehind, missed.Reason);

        p.Mark("t.field.shrine", StepState.Done, T0);
        Assert.Empty(p.MissedRewards());
    }

    [Fact]
    public void Only_permanent_rewards_are_listed_as_missed()
    {
        var p = new CampaignProgress(TestRoutes.Synthetic());
        p.Mark("t.town.tess", StepState.Skipped, T0);

        var missed = Assert.Single(p.MissedRewards());
        Assert.Equal("+10% to all resistances", missed.Reward.Text);
    }

    [Fact]
    public void A_step_left_open_on_an_earlier_visit_shows_up_when_you_come_back()
    {
        var p = new CampaignProgress(TestRoutes.Synthetic());
        p.Enter("t.cave");
        p.Next(); // town, by portal
        p.Next(); // the Field again: the shrine from the first visit is still open

        var here = Assert.Single(p.StepsHere());
        Assert.Equal("t.field.shrine", here.Step.Id);
        Assert.True(here.EarlierVisit);
        Assert.Equal("Touch the shrine (optional)", p.Lines().Now);
    }

    [Fact]
    public void Past_the_route_the_player_sets_the_act()
    {
        var p = new CampaignProgress(TestRoutes.Act1());
        p.Enter("a1.grelwood");
        p.Enter("a1.red_vale");
        p.Enter("a1.grelwood");
        Assert.Equal(7, p.Leg);

        Assert.True(p.Next());
        Assert.True(p.PastRoute);
        Assert.False(p.CanGoNext);
        Assert.Equal(1, p.Act);

        p.SetAct(2);
        Assert.Equal(2, p.Act);
        Assert.Equal("Act 2", p.Lines().Zone);
        Assert.Empty(p.StepsHere());

        p.Back();
        Assert.Equal("a1.grelwood", p.ZoneId);
        Assert.Equal(1, p.Act);
    }

    [Fact]
    public void A_zone_counts_as_reached_once_you_are_there_or_past_it()
    {
        var p = new CampaignProgress(TestRoutes.Act1());
        Assert.False(p.Reached("a1.mud_burrow"));

        p.Enter("a1.mud_burrow");
        Assert.True(p.Reached("a1.mud_burrow"));

        p.Next();
        Assert.True(p.Reached("a1.mud_burrow"));
        Assert.False(p.Reached("a1.red_vale"));

        p.Next();
        p.Next();
        p.Next();
        p.Next();
        Assert.True(p.PastRoute);
        Assert.True(p.Reached("a1.red_vale"));
    }

    [Fact]
    public void The_hud_lines_name_the_zone_the_objective_and_what_comes_next()
    {
        var p = new CampaignProgress(TestRoutes.Act1());
        Assert.Equal(new HudLines("The Riverbank", "Defeat the Bloated Miller", "Then", "Clearfell Encampment"), p.Lines());

        p.Mark("a1.riverbank.miller", StepState.Done, T0);
        Assert.Equal("Follow the river to the camp.", p.Lines().Now);

        p.Enter("a1.clearfell");
        Assert.Equal(new HudLines("Clearfell", "Defeat Beira of the Rotten Pack (optional)", "Then", "Mud Burrow: Defeat the Devourer"), p.Lines());

        p.Next();
        Assert.Equal(("Mud Burrow", "Defeat the Devourer"), (p.Lines().Zone, p.Lines().Now));
    }

    /// <summary>The issue's "done when": an early-campaign route runs offline, including a skipped
    /// reward, backtracking and a resume.</summary>
    [Fact]
    public void Early_campaign_journey_with_a_skip_a_backtrack_and_a_resume()
    {
        var pack = TestRoutes.Act1();
        var p = new CampaignProgress(pack);

        p.Mark("a1.riverbank.miller", StepState.Done, T0);
        p.Next();                                                   // Clearfell Encampment
        p.Next();                                                   // Clearfell
        p.Mark("a1.clearfell.beira", StepState.Skipped, T0.AddMinutes(9));
        p.Next();                                                   // Mud Burrow
        p.Mark("a1.mud_burrow.devourer", StepState.Done, T0.AddMinutes(15));
        p.Next();                                                   // back out to Clearfell
        Assert.Equal("a1.clearfell", p.ZoneId);
        Assert.Equal(4, p.Leg);
        Assert.Equal("Back in Clearfell. On to the Grelwood.", p.Lines().Now);
        Assert.Equal(["a1.clearfell.beira"], p.Skipped().Select(s => s.Step.Id));

        // Restart: only the saved marks and position come back.
        var resumed = new CampaignProgress(pack, p.Marks.Reverse().ToList(), p.Position);

        Assert.Equal(p.Position, resumed.Position);
        Assert.Equal(p.Lines(), resumed.Lines());
        Assert.Equal(StepState.Skipped, resumed.StateOf("a1.clearfell.beira"));
        Assert.Equal(StepState.Done, resumed.StateOf("a1.mud_burrow.devourer"));

        // Undo still works after the restart: it takes back the Devourer, the last mark.
        Assert.Equal("a1.mud_burrow.devourer", resumed.Undo()!.StepId);
        resumed.Mark("a1.clearfell.beira", StepState.Done, T0.AddMinutes(30)); // changed their mind and went back for her
        Assert.Empty(resumed.Skipped());
    }
}
