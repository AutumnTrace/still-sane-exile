using System.IO;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Desktop.Tests;

/// <summary>The guide page's one input: a pasted Path of Building code. The practice build is synthetic
/// (fixtures/builds/practice-witch.xml).</summary>
public class GuideBuildTests
{
    /// <summary>The practice build's code, or a code for the practice XML after <paramref name="change"/>.</summary>
    internal static string PracticeCode(TestApp app, Func<string, string>? change = null) => change is null
        ? File.ReadAllText(Path.Combine(app.Paths.Fixtures, "builds", "practice-witch.pob.txt"))
        : PobCode.Encode(change(File.ReadAllText(Path.Combine(app.Paths.Fixtures, "builds", "practice-witch.xml"))));

    private static ViewModels.GuideViewModel Character(TestApp app)
    {
        var guide = app.Shell.Guide;
        guide.NewCharacterName = "Practice exile";
        guide.AddCharacterCommand.Execute(null);
        Assert.True(guide.HasCharacter);
        return guide;
    }

    [Fact]
    public void A_pasted_code_gives_the_gem_steps_the_skills_the_notes_and_the_items() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var guide = Character(app);
        var plan = guide.Plan;
        Assert.False(plan.HasPlan);

        plan.CodeDraft = PracticeCode(app);
        plan.ReadCodeCommand.Execute(null);

        Assert.Equal("Read Blood Mage (Witch), level 38: 9 gem steps.", plan.Message);
        Assert.Equal("", plan.CodeDraft);
        Assert.True(plan.HasBuild);
        Assert.Equal("Blood Mage (Witch)", plan.BuildTitle);
        Assert.StartsWith("Level 38 · Path of Building, game version 0.5 · pasted ", plan.BuildFacts, StringComparison.Ordinal);
        Assert.Equal("Life 812 · Energy Shield 406 · Mana 590 · Spirit 30", plan.StatsLine);
        Assert.Equal("3 steps from the build's notes, 6 steps from its skill sets.", plan.StepsSource);
        Assert.Contains("“Endgame”", plan.UnstagedText, StringComparison.Ordinal);

        // Act 1 at the start of the route: Act 2 steps stay hidden, "Cut Entangle" waits for the Mud Burrow.
        Assert.Equal(new[] { "Cut Entangle", "Cut support Zenith I", "Socket Zenith I into Entangle", "Cut Contagion" }, plan.Checklist.Select(s => s.Text));
        Assert.StartsWith("Waits until Mud Burrow", plan.Checklist[0].Detail, StringComparison.Ordinal);
        Assert.Contains("from the build's notes", plan.Checklist[0].Detail, StringComparison.Ordinal);
        Assert.Contains("from skill set “Act 1”", plan.Checklist[1].Detail, StringComparison.Ordinal);
        Assert.Equal(5, plan.HiddenCount);
        Assert.Equal("Cut support Zenith I", plan.NextGem);
        Assert.Equal("Next gem", guide.HudThenLabel);
        Assert.Equal("Cut support Zenith I", guide.HudThen);

        // The skill set for the stage reached is shown; the author had Act 2 open.
        Assert.Equal("Act 1", plan.SkillSets.Single(s => s.IsShown).Title);
        Assert.Equal("active", plan.SkillSets.Single(s => s.IsShown).Tag);
        Assert.Equal("Entangle, supports Zenith I, Opener, level 3 in Path of Building, Practice note: cast it first.", plan.Skills[0].ToString());
        plan.ShowSkillSetCommand.Execute(plan.SkillSets.Single(s => s.Title == "Endgame"));
        Assert.Equal("Its title names no stage, so it gives no steps.", plan.ShownSetStage);
        Assert.Equal("Essence Drain", plan.Skills.Single().Skill);

        Assert.StartsWith("Practice build written for SSE's tests.", plan.NotesText, StringComparison.Ordinal);
        Assert.Equal("Campaign", plan.ItemSetTitle);
        Assert.Equal(new[] { "Weapon 1: Practice Bane, Withered Wand", "Ring 1: Practice Ring of the Test" }, plan.Items.Select(i => i.ToString()));
    });

    [Fact]
    public void Reaching_the_zone_and_the_act_brings_the_steps_forward_and_the_zenith_move_waits_for_essence_drain() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var guide = Character(app);
        var plan = guide.Plan;
        plan.CodeDraft = PracticeCode(app);
        plan.ReadCodeCommand.Execute(null);

        guide.SelectedZone = guide.Zones.Single(z => z.Name == "Mud Burrow");
        Assert.Equal("Cut Entangle", plan.NextGem);
        foreach (var step in plan.Checklist.ToList())
        {
            plan.ToggleStepCommand.Execute(plan.Checklist.Single(s => s.Key == step.Key));
        }

        Assert.Null(plan.NextGem);
        for (var i = 0; i < 50 && !guide.PastRoute; i++)
        {
            guide.NextCommand.Execute(null);
        }

        guide.ActUpCommand.Execute(null); // Act 2, past the end of the Act 1 draft
        Assert.Equal("Cut Essence Drain", plan.NextGem);
        Assert.Equal("Act 2", plan.SkillSets.Single(s => s.IsShown).Title);
        var move = plan.Checklist.Single(s => s.Text == "Move Zenith I: Entangle → Essence Drain");
        Assert.True(move.IsWaiting);
        Assert.StartsWith("Waits for Essence Drain", move.Detail, StringComparison.Ordinal);

        plan.ToggleStepCommand.Execute(plan.Checklist.Single(s => s.Text == "Cut Essence Drain"));
        Assert.Equal("Move Zenith I: Entangle → Essence Drain", plan.NextGem);
    });

    [Fact]
    public void Text_that_is_not_a_code_or_a_link_to_a_build_site_is_refused_and_nothing_is_saved() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var plan = Character(app).Plan;

        plan.CodeDraft = "https://pobb.in/AbCdEf12";
        plan.ReadCodeCommand.Execute(null);
        Assert.Contains("SSE does not open build sites", plan.Message, StringComparison.Ordinal);

        plan.CodeDraft = "https://mobalytics.gg/poe-2/builds/practice";
        plan.ReadCodeCommand.Execute(null);
        Assert.StartsWith("That is a Mobalytics link.", plan.Message, StringComparison.Ordinal);

        var code = PracticeCode(app).Trim();
        plan.CodeDraft = code[..(code.Length / 2)];
        plan.ReadCodeCommand.Execute(null);
        Assert.Equal(PobCode.Damaged, plan.Message);

        Assert.False(plan.HasBuild);
        Assert.Empty(app.Store.GuidePlans("Practice exile"));
        Assert.NotEqual("", plan.CodeDraft); // kept, so the player can see what they pasted
    });

    [Fact]
    public void A_newer_code_that_changes_the_steps_waits_until_accepted_and_ticks_carry_over() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var plan = Character(app).Plan;
        plan.CodeDraft = PracticeCode(app);
        plan.ReadCodeCommand.Execute(null);
        plan.ToggleStepCommand.Execute(plan.Checklist.Single(s => s.Text == "Cut Contagion"));

        plan.CodeDraft = PracticeCode(app, xml => xml.Replace("title=\"Endgame\"", "title=\"After ascendancy\"", StringComparison.Ordinal));
        plan.ReadCodeCommand.Execute(null);

        Assert.Equal("Read as revision 2. The steps in use stay until you accept the changes.", plan.Message);
        Assert.True(plan.HasPending);
        // The renamed set comes after "Act 2", so it counts from Act 2 after the ascendancy.
        Assert.Equal(new[] { "Added: Cut support Arcane Surge, Act 2 · after ascendancy", "Added: Socket Arcane Surge into Essence Drain, Act 2 · after ascendancy" }, plan.PendingChanges.Select(c => c.ToString()));
        Assert.Contains("“Endgame”", plan.UnstagedText, StringComparison.Ordinal); // revision 1 is still the one in use

        plan.AcceptPendingCommand.Execute(null);

        Assert.False(plan.HasPending);
        Assert.False(plan.HasUnstaged);
        Assert.True(plan.Checklist.Single(s => s.Text == "Cut Contagion").IsDone);
        var rows = app.Store.GuidePlans("Practice exile");
        Assert.Equal(2, Assert.Single(rows).Revision);
        Assert.NotNull(rows[0].Code);
    });

    [Fact]
    public void A_newer_code_with_the_same_steps_replaces_the_build_at_once() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var plan = Character(app).Plan;
        plan.CodeDraft = PracticeCode(app);
        plan.ReadCodeCommand.Execute(null);

        plan.CodeDraft = PracticeCode(app, xml => xml.Replace("Practice Bane", "Practice Doom", StringComparison.Ordinal));
        plan.ReadCodeCommand.Execute(null);

        Assert.Equal("Build updated: Blood Mage (Witch), level 38. No gem steps changed.", plan.Message);
        Assert.False(plan.HasPending);
        Assert.Equal("Weapon 1: Practice Doom, Withered Wand", plan.Items[0].ToString());
    });

    [Fact]
    public void Steps_written_before_builds_could_be_pasted_stay_in_use_until_a_build_is() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var written = PlanNotesParser.Parse("cut Spark\ncut support Arcane Surge").Plan;
        app.Store.AddGuideCharacter("Old exile", "sse-act1-opening", new CampaignPosition(0, "a1.riverbank", 1), DateTimeOffset.UtcNow);
        app.Store.SaveGuidePlan("Old exile", GuidePlanRow.Active, "cut Spark\ncut support Arcane Surge", written.ToJson(), DateTimeOffset.UtcNow);
        app.Store.SaveGuideBuild("Old exile", new GuideBuildRow("https://mobalytics.gg/poe-2/builds/old", 1), DateTimeOffset.UtcNow);
        var guide = app.Shell.Guide;
        guide.SelectedCharacter = "Old exile";
        var plan = guide.Plan;

        Assert.True(plan.HasWrittenPlan);
        Assert.False(plan.HasBuild);
        Assert.Equal(1, plan.Ascendancies);
        Assert.Equal("Cut Spark", plan.NextGem);
        Assert.StartsWith("Your written steps from an earlier version", plan.StepsSource, StringComparison.Ordinal);

        plan.CodeDraft = PracticeCode(app);
        plan.ReadCodeCommand.Execute(null);
        Assert.True(plan.HasPending); // the written steps stay until the player accepts the build's
        plan.AscendancyUpCommand.Execute(null);
        Assert.Equal("https://mobalytics.gg/poe-2/builds/old", app.Store.GuideBuild("Old exile").Link); // an old saved link is kept, not shown
    });

    [Fact]
    public void Removing_a_character_removes_their_build_codes_too() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var guide = Character(app);
        guide.Plan.CodeDraft = PracticeCode(app);
        guide.Plan.ReadCodeCommand.Execute(null);
        guide.Plan.CodeDraft = PracticeCode(app, xml => xml.Replace("title=\"Endgame\"", "title=\"Act 3\"", StringComparison.Ordinal));
        guide.Plan.ReadCodeCommand.Execute(null);
        Assert.Equal(2, CodeRows(app.Paths));
        guide.Plan.DiscardPendingCommand.Execute(null);
        Assert.NotNull(Assert.Single(app.Store.GuidePlans("Practice exile")).Code);
        Assert.Equal(1, CodeRows(app.Paths));

        guide.RemoveCharacterCommand.Execute(null);
        guide.RemoveCharacterCommand.Execute(null);

        Assert.Empty(app.Store.GuidePlans("Practice exile"));
        Assert.True(app.Store.AddGuideCharacter("Practice exile", "sse-act1-opening", new CampaignPosition(0, "a1.riverbank", 1), DateTimeOffset.UtcNow));
        Assert.Empty(app.Store.GuidePlans("Practice exile"));
        Assert.Equal(0, CodeRows(app.Paths));
    });

    private static long CodeRows(AppPaths paths)
    {
        using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={paths.Database}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM guide_plan_codes";
        return (long)cmd.ExecuteScalar()!;
    }
}
