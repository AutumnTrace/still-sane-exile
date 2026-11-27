using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Domain.Tests;

public class PlanNotesParserTests
{
    private static PlanMilestone One(string line, RoutePack? route = null)
    {
        var draft = PlanNotesParser.Parse(line, route);
        var parsed = Assert.Single(draft.Lines);
        Assert.True(parsed.Kind == NoteLineKind.Step, $"{line}: {parsed.Problem}");
        return parsed.Milestone!;
    }

    [Theory]
    [InlineData("cut Entangle", GemAction.Cut, "Cut Entangle")]
    [InlineData("Cut: Entangle.", GemAction.Cut, "Cut Entangle")]
    [InlineData("upgrade Entangle", GemAction.Upgrade, "Upgrade Entangle")]
    [InlineData("level up Contagion", GemAction.Upgrade, "Upgrade Contagion")]
    [InlineData("cut support Zenith I", GemAction.CutSupport, "Cut support Zenith I")]
    [InlineData("cut support gem Zenith I", GemAction.CutSupport, "Cut support Zenith I")]
    [InlineData("socket Zenith I into Entangle", GemAction.Socket, "Socket Zenith I into Entangle")]
    [InlineData("socket Zenith I in Entangle", GemAction.Socket, "Socket Zenith I into Entangle")]
    [InlineData("Move Zenith I: Entangle → Essence Drain", GemAction.MoveSupport, "Move Zenith I: Entangle → Essence Drain")]
    [InlineData("move Zenith I: Entangle -> Essence Drain", GemAction.MoveSupport, "Move Zenith I: Entangle → Essence Drain")]
    [InlineData("move Zenith I: Entangle to Essence Drain", GemAction.MoveSupport, "Move Zenith I: Entangle → Essence Drain")]
    [InlineData("move support Zenith I from Entangle to Essence Drain", GemAction.MoveSupport, "Move Zenith I: Entangle → Essence Drain")]
    [InlineData("move Zenith I to Essence Drain", GemAction.MoveSupport, "Move Zenith I → Essence Drain")]
    [InlineData("reserve Herald of Ash", GemAction.Reserve, "Reserve Herald of Ash")]
    [InlineData("reserve spirit for Herald of Ash", GemAction.Reserve, "Reserve Herald of Ash")]
    [InlineData("respec", GemAction.Respec, "Respec")]
    [InlineData("Respec: the left side of the tree", GemAction.Respec, "Respec: the left side of the tree")]
    public void Recognises_each_gem_action(string line, GemAction action, string reading)
    {
        var m = One(line);

        Assert.Equal(action, m.Action);
        Assert.Equal(reading, m.Describe());
        Assert.Equal(line, m.Source);
    }

    [Fact]
    public void Move_keeps_the_support_the_old_skill_and_the_new_one_apart()
    {
        var m = One("move Zenith I: Entangle → Essence Drain");

        Assert.Equal("Zenith I", m.Gem);
        Assert.Equal("Entangle", m.From);
        Assert.Equal("Essence Drain", m.Target);
    }

    [Fact]
    public void A_cutthroat_line_is_not_a_cut()
    {
        var line = Assert.Single(PlanNotesParser.Parse("cutthroat tactics").Lines);

        Assert.Equal(NoteLineKind.NotUnderstood, line.Kind);
        Assert.Equal(PlanNotesParser.NotAStep, line.Problem);
    }

    [Fact]
    public void Bullets_numbering_and_tick_boxes_are_ignored()
    {
        var draft = PlanNotesParser.Parse("- cut Entangle\n* cut Contagion\n3. upgrade Entangle\n4) cut support Zenith I\n[ ] respec\n• cut Essence Drain");

        Assert.Equal(6, draft.Steps);
        Assert.Empty(draft.Problems);
    }

    [Fact]
    public void Blank_lines_and_comments_are_kept_but_not_steps()
    {
        var draft = PlanNotesParser.Parse("# my notes\n\n// from the guide I follow\ncut Entangle");

        Assert.Equal([NoteLineKind.Comment, NoteLineKind.Blank, NoteLineKind.Comment, NoteLineKind.Step], draft.Lines.Select(l => l.Kind));
        Assert.Equal(4, draft.Lines[3].Number);
    }

    [Fact]
    public void Stage_labels_apply_from_there_down()
    {
        var draft = PlanNotesParser.Parse("cut Entangle\nAct 2:\ncut Essence Drain\nmove Zenith I: Entangle → Essence Drain\nAfter ascendancy\nrespec");
        var steps = draft.Plan.Milestones.ToList();

        Assert.Equal(new PlanStage(1, 0), steps[0].Stage);
        Assert.Equal(new PlanStage(2, 0), steps[1].Stage);
        Assert.Equal(new PlanStage(2, 0), steps[2].Stage);
        Assert.Equal(new PlanStage(2, 1), steps[3].Stage);
        Assert.Equal(NoteLineKind.Stage, draft.Lines[1].Kind);
        Assert.Equal("From here on: Act 2 · after ascendancy", draft.Lines[4].Reading);
    }

    [Theory]
    [InlineData("Act 2: cut Essence Drain", 2, 0)]
    [InlineData("act II - cut Essence Drain", 2, 0)]
    [InlineData("Act 3, after ascendancy: respec", 3, 1)]
    [InlineData("after the first ascendancy: respec", 1, 1)]
    [InlineData("after 2nd ascendancy: respec", 1, 2)]
    [InlineData("after ascendancy 3: respec", 1, 3)]
    [InlineData("post-ascendancy: respec", 1, 1)]
    [InlineData("before ascendancy: cut Entangle", 1, 0)]
    public void A_stage_label_can_lead_the_step_line(string line, int act, int ascendancies)
    {
        Assert.Equal(new PlanStage(act, ascendancies), One(line).Stage);
    }

    [Fact]
    public void Stages_never_go_back_so_later_steps_cannot_show_up_early()
    {
        var draft = PlanNotesParser.Parse("Act 2:\ncut Essence Drain\nAct 1:\ncut Entangle");

        Assert.Equal(new PlanStage(2, 0), draft.Lines[3].Stage);
        Assert.Contains("only move forward", draft.Lines[2].Problem);
    }

    [Fact]
    public void An_act_label_after_the_ascendancy_label_keeps_the_ascendancy()
    {
        var draft = PlanNotesParser.Parse("after ascendancy:\nrespec\nAct 3:\ncut Essence Drain");

        Assert.Equal(new PlanStage(3, 1), draft.Lines[3].Stage);
        Assert.Null(draft.Lines[2].Problem);
    }

    [Fact]
    public void An_act_out_of_range_is_flagged()
    {
        var draft = PlanNotesParser.Parse("Act 12:\ncut Entangle");

        Assert.Contains("not one SSE knows", draft.Lines[0].Problem);
        Assert.Equal(PlanStage.Start, draft.Lines[1].Stage);
    }

    [Theory]
    [InlineData("cut Contagion (level 3 uncut)", UncutKind.Skill, 3)]
    [InlineData("cut Contagion with a level 3 uncut skill gem", UncutKind.Skill, 3)]
    [InlineData("cut Contagion lvl 3", UncutKind.Skill, 3)]
    [InlineData("cut support Zenith I (lvl 2)", UncutKind.Support, 2)]
    [InlineData("upgrade Entangle to level 5", UncutKind.Skill, 5)]
    [InlineData("reserve Herald of Ash (uncut spirit gem level 4)", UncutKind.Spirit, 4)]
    public void Reads_the_uncut_gem_type_and_level(string line, UncutKind kind, int level)
    {
        var m = One(line);

        Assert.Equal(new UncutGem(kind, level), m.Uncut);
        Assert.DoesNotContain("level", m.Gem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_uncut_gem_unless_the_notes_say_so()
    {
        Assert.Null(One("cut Entangle").Uncut);
    }

    [Theory]
    [InlineData("cut Contagion (priority: high)", "high")]
    [InlineData("cut Contagion (high priority)", "high")]
    [InlineData("cut Contagion (optional)", "optional")]
    public void Keeps_the_authors_priority_as_written(string line, string priority)
    {
        Assert.Equal(priority, One(line).PriorityText);
    }

    [Fact]
    public void Without_a_priority_it_says_the_author_did_not_specify()
    {
        Assert.Equal("Author did not specify", One("cut Contagion").PriorityText);
        Assert.Null(One("cut Contagion").Priority);
    }

    [Fact]
    public void Reads_written_prerequisites()
    {
        var m = One("respec (needs Essence Drain and Contagion)");

        Assert.Equal(["Essence Drain", "Contagion"], m.Needs);
    }

    [Fact]
    public void A_zone_the_route_knows_becomes_a_zone_trigger()
    {
        var m = One("upgrade Entangle at Mud Burrow", TestRoutes.Act1());

        Assert.Equal("Entangle", m.Gem);
        Assert.Equal("Mud Burrow", m.Where);
        Assert.Equal("a1.mud_burrow", m.ZoneId);
        Assert.Equal(MilestoneTrigger.Zone, m.Trigger);
    }

    [Fact]
    public void An_npc_or_unknown_place_is_kept_as_written_without_a_trigger()
    {
        var m = One("cut Contagion at the blacksmith", TestRoutes.Act1());

        Assert.Equal("the blacksmith", m.Where);
        Assert.Null(m.ZoneId);
        Assert.Equal(MilestoneTrigger.Stage, m.Trigger);
    }

    [Fact]
    public void In_only_names_a_place_when_the_route_has_that_zone()
    {
        Assert.Equal("a1.clearfell", One("cut Contagion in Clearfell", TestRoutes.Act1()).ZoneId);
        Assert.Equal("Cut Contagion in Clearfell", One("cut Contagion in Clearfell").Describe());
    }

    [Fact]
    public void Lines_it_cannot_read_say_what_to_write_instead()
    {
        var draft = PlanNotesParser.Parse("cut Entangle\nget a better weapon\nsocket Zenith I\nmove Zenith I\nmove Zenith I: Entangle → entangle");

        Assert.Equal(1, draft.Steps);
        Assert.Equal(4, draft.Problems.Count);
        Assert.Equal(PlanNotesParser.NotAStep, draft.Lines[1].Problem);
        Assert.Contains("which skill it goes into", draft.Lines[2].Problem);
        Assert.Contains("where it goes", draft.Lines[3].Problem);
        Assert.Contains("same skill", draft.Lines[4].Problem);
    }

    [Fact]
    public void The_preview_reading_spells_out_every_field()
    {
        var line = PlanNotesParser.Parse("Act 1: upgrade Entangle at Mud Burrow (level 4 uncut) (priority: high)", TestRoutes.Act1()).Lines.Single();

        Assert.Equal("Act 1 · Upgrade Entangle · uncut skill, level 4 · once you reach Mud Burrow · priority: high", line.Reading);
    }

    [Fact]
    public void Repeated_steps_get_their_own_keys_and_keys_survive_lines_added_above()
    {
        var first = PlanNotesParser.Parse("cut Entangle\nupgrade Entangle\nupgrade Entangle").Plan;
        var edited = PlanNotesParser.Parse("# notes\ncut Entangle\ncut Contagion\nupgrade Entangle\nupgrade Entangle").Plan;

        Assert.Equal(3, first.Entries.Select(e => e.Key).Distinct().Count());
        Assert.Subset(edited.Entries.Select(e => e.Key).ToHashSet(), first.Entries.Select(e => e.Key).ToHashSet());
    }

    [Fact]
    public void Gem_names_match_without_case_or_extra_spaces()
    {
        var plan = PlanNotesParser.Parse("cut  essence drain\nmove Zenith I: Entangle → Essence Drain").Plan;

        Assert.True(plan.Makes("Essence Drain"));
        Assert.False(plan.Makes("Entangle"));
    }

    [Fact]
    public void A_saved_plan_round_trips_through_json()
    {
        var plan = PlanNotesParser.Parse("Act 2: upgrade Entangle at Mud Burrow (level 4 uncut) (priority: high) (needs Contagion)\nmove Zenith I: Entangle → Essence Drain", TestRoutes.Act1()).Plan;

        var back = BuildPlan.FromJson(plan.ToJson());

        IReadOnlyList<string> none = [];
        Assert.Equal(plan.Entries, back.Entries, (a, b) => a.Key == b.Key && a.Milestone with { Needs = none } == b.Milestone with { Needs = none } && a.Milestone.Needs.SequenceEqual(b.Milestone.Needs));
        Assert.Equal(["Contagion"], back.Entries[0].Milestone.Needs);
        Assert.DoesNotContain("priorityText", plan.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_notes_make_an_empty_plan()
    {
        Assert.True(PlanNotesParser.Parse(null).Plan.IsEmpty);
        Assert.True(PlanNotesParser.Parse("  \n\n").Plan.IsEmpty);
    }
}
