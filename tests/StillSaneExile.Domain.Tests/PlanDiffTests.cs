using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Domain.Tests;

public class PlanDiffTests
{
    private const string Revision1 = """
        Act 1
        cut Entangle
        upgrade Entangle
        cut support Zenith I
        socket Zenith I into Entangle
        Act 2
        cut Essence Drain
        """;

    private static BuildPlan Plan(string notes) => PlanNotesParser.Parse(notes).Plan;

    [Fact]
    public void The_same_notes_change_nothing()
    {
        var diff = PlanDiff.Between(Plan(Revision1), Plan(Revision1 + "\n\n# a comment"));

        Assert.True(diff.IsEmpty);
        Assert.Equal(5, diff.Unchanged);
    }

    [Fact]
    public void Added_and_removed_steps_are_listed()
    {
        var edited = Revision1.Replace("upgrade Entangle", "# dropped", StringComparison.Ordinal) + "\nmove Zenith I: Entangle → Essence Drain";

        var diff = PlanDiff.Between(Plan(Revision1), Plan(edited));

        Assert.Equal(["Move Zenith I: Entangle → Essence Drain"], diff.Added.Select(m => m.Describe()));
        Assert.Equal(["Upgrade Entangle"], diff.Removed.Select(m => m.Describe()));
        Assert.Empty(diff.Changed);
    }

    [Fact]
    public void A_new_target_reads_as_a_change_not_a_remove_and_add()
    {
        var diff = PlanDiff.Between(Plan(Revision1), Plan(Revision1.Replace("into Entangle", "into Contagion", StringComparison.Ordinal)));

        var change = Assert.Single(diff.Changed);
        Assert.Equal(["into: Entangle → Contagion"], change.What);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void A_step_moved_to_another_stage_is_a_change()
    {
        var edited = """
            Act 1
            cut Entangle
            upgrade Entangle
            cut support Zenith I
            Act 2
            socket Zenith I into Entangle
            cut Essence Drain
            """;

        var change = Assert.Single(PlanDiff.Between(Plan(Revision1), Plan(edited)).Changed);

        Assert.Equal("Socket Zenith I into Entangle", change.New.Describe());
        Assert.Equal(["stage: Act 1 → Act 2"], change.What);
    }

    [Fact]
    public void A_new_priority_or_place_is_a_change()
    {
        var diff = PlanDiff.Between(Plan(Revision1), Plan(Revision1.Replace("cut Essence Drain", "cut Essence Drain at the blacksmith (priority: high)", StringComparison.Ordinal)));

        var change = Assert.Single(diff.Changed);
        Assert.Equal(["where: none → the blacksmith", "priority: Author did not specify → high"], change.What);
    }

    [Fact]
    public void Ticks_carry_over_to_steps_that_did_not_change()
    {
        var old = Plan(Revision1);
        var edited = Plan(Revision1 + "\nmove Zenith I: Entangle → Essence Drain");
        var done = old.Entries.Take(2).Select(e => e.Key).ToHashSet();

        Assert.Equal(2, edited.Entries.Count(e => done.Contains(e.Key)));
    }
}
