using StillSaneExile.Domain.Data;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Reading;
using StillSaneExile.Domain.Rolls;

namespace StillSaneExile.Domain.Tests;

public class ItemReaderTests
{
    private static LineReading Line(ItemReading r, string template) => r.Lines.Single(l => l.Line.Template == template);

    [Fact]
    public void Hybrid_merge_in_normal_copy_is_ambiguous_and_draws_no_bar()
    {
        var reading = ItemReader.Read(Fixtures.Item("rare-crossbow-normal-hybrid"), Fixtures.Pack());

        var phys = Line(reading, "#% increased Physical Damage");
        Assert.Equal(LineStatus.Ambiguous, phys.Status);
        Assert.Contains(phys.Candidates, c => c.IsMerge);
        Assert.All(phys.Rolls, r => Assert.False(r.HasBar));

        var accuracy = Line(reading, "# to Accuracy Rating");
        Assert.Equal(LineStatus.Ambiguous, accuracy.Status);
        Assert.True(reading.HasAmbiguity);
    }

    [Fact]
    public void Item_level_excludes_tiers_the_item_cannot_have()
    {
        // ilvl 81: "Merciless" (170–179, level 82) is out, so 192% is never Merciless + "Squire's" (15–19).
        var reading = ItemReader.Read(Fixtures.Item("rare-crossbow-normal-hybrid"), Fixtures.Pack());
        var phys = Line(reading, "#% increased Physical Damage");
        Assert.DoesNotContain(phys.Candidates, c => !c.IsMerge);
        Assert.DoesNotContain(phys.Candidates, c => c.Parts.Contains(("crossbow.phys_pct", 1)));

        // The same line at item level 82 can be that pair.
        var text = Fixtures.Text("items/rare-crossbow-normal-hybrid.txt").Replace("Item Level: 81", "Item Level: 82", StringComparison.Ordinal);
        var at82 = Line(ItemReader.Read(ItemTextParser.Parse(text, Fixtures.DemoNow, "x").Item!, Fixtures.Pack()), "#% increased Physical Damage");
        Assert.Contains(at82.Candidates, c => c.Parts.Contains(("crossbow.phys_pct", 1)));
    }

    [Fact]
    public void Unique_explanation_in_normal_copy_resolves_with_pack_range()
    {
        var reading = ItemReader.Read(Fixtures.Item("rare-crossbow-normal-hybrid"), Fixtures.Pack());

        var speed = Line(reading, "#% increased Attack Speed"); // 18%: only "of Acclaim" (17–19) fits
        Assert.Equal(LineStatus.Resolved, speed.Status);
        Assert.Equal(1, speed.Tier);
        Assert.Equal("T1 of 5", speed.TierLabel); // ranged weapons stop at "of Acclaim"
        Assert.Equal("Range from the game's 0.5.5 data, not checked in game.", speed.RangeCaveat);
        var roll = speed.Rolls.Single();
        Assert.Equal(RollStatus.Scored, roll.Status);
        Assert.Equal(17m, roll.Min);
        Assert.Equal(19m, roll.Max);
    }

    [Fact]
    public void The_practice_pack_is_unverified_game_data_and_says_so()
    {
        var pack = Fixtures.Pack();
        Assert.False(pack.IsSynthetic);
        Assert.False(pack.IsVerified); // nobody has checked it in the game yet
        Assert.Equal(["0.5.5"], pack.CompatibleBuilds);

        // The physical damage family as the game has it in 0.5.5 (docs/GAME.md, 4.2).
        var phys = pack.Families.Single(f => f.Id == "crossbow.phys_pct");
        Assert.Equal(["Merciless", "Tyrannical", "Cruel", "Bloodthirsty", "Vicious", "Wicked", "Serrated", "Heavy"], phys.Tiers.Select(t => t.Name));
        Assert.Equal([82, 75, 60, 46, 33, 16, 8, 1], phys.Tiers.Select(t => t.Level));
        Assert.Equal((170m, 179m), phys.Tiers[0].Range(0));

        // What each kind of pack must say next to a range read from it.
        Assert.Equal("Demo range, not game data.", (pack with { Provenance = "synthetic" }).RangeCaveat);
        Assert.Null((pack with { VerifiedDate = "2026-12-12" }).RangeCaveat);
        Assert.Equal("Range from game data, not checked in game.", (pack with { CompatibleBuilds = [] }).RangeCaveat);
    }

    [Fact]
    public void Advanced_copy_uses_game_range_and_pack_direction()
    {
        var reading = ItemReader.Read(Fixtures.Item("rare-crossbow-advanced"), Fixtures.Pack());

        var phys = Line(reading, "#% increased Physical Damage");
        Assert.Equal(LineStatus.FromAdvancedCopy, phys.Status);
        Assert.Equal("T1 of 8", phys.TierLabel);
        Assert.Equal(StatDirection.HigherIsBetter, phys.Rolls.Single().Direction);
        Assert.Equal("56%", RollQuality.Percent(phys.Rolls.Single().Fill));

        var flat = Line(reading, "Adds # to # Physical Damage");
        Assert.Equal(2, flat.Rolls.Count);
        Assert.All(flat.Rolls, r => Assert.Equal(RollStatus.Scored, r.Status));
    }

    [Fact]
    public void Advanced_copy_without_pack_shows_position_marker_only()
    {
        var reading = ItemReader.Read(Fixtures.Item("rare-crossbow-advanced"), pack: null);
        var phys = Line(reading, "#% increased Physical Damage");
        Assert.Equal(RollStatus.PositionOnly, phys.Rolls.Single().Status);
    }

    [Fact]
    public void Advanced_range_that_disagrees_with_pack_keeps_game_range_and_says_so()
    {
        var text = Fixtures.Text("items/rare-crossbow-advanced.txt").Replace("175(170-179)", "175(168-179)", StringComparison.Ordinal);
        var item = ItemTextParser.Parse(text, Fixtures.DemoNow, "x").Item!;
        var phys = Line(ItemReader.Read(item, Fixtures.Pack()), "#% increased Physical Damage");
        Assert.Equal(168m, phys.Rolls.Single().Min);
        Assert.Contains("differs", phys.Note);
    }

    [Fact]
    public void Fixed_tier_reads_fixed()
    {
        var reading = ItemReader.Read(Fixtures.Item("rare-boots-normal"), Fixtures.Pack());
        var ms = Line(reading, "#% increased Movement Speed");
        Assert.Equal(LineStatus.Resolved, ms.Status);
        Assert.Equal(RollStatus.Fixed, ms.Rolls.Single().Status);
    }

    [Fact]
    public void Wording_outside_the_pack_is_not_in_pack()
    {
        var reading = ItemReader.Read(Fixtures.Item("rare-boots-normal"), Fixtures.Pack());
        var cold = Line(reading, "#% to Cold Resistance");
        Assert.Equal(LineStatus.NotInPack, cold.Status);
        Assert.False(cold.Rolls.Single().HasBar);
    }

    [Fact]
    public void Value_outside_every_tier_is_range_mismatch()
    {
        var text = Fixtures.Text("items/rare-boots-normal.txt").Replace("+104 to maximum Life", "+150 to maximum Life", StringComparison.Ordinal);
        var item = ItemTextParser.Parse(text, Fixtures.DemoNow, "x").Item!;
        var life = Line(ItemReader.Read(item, Fixtures.Pack()), "# to maximum Life");
        Assert.Equal(LineStatus.RangeMismatch, life.Status);
    }

    [Fact]
    public void Implicit_and_rune_lines_are_never_scored_from_explicit_families()
    {
        var reading = ItemReader.Read(Fixtures.Item("rare-crossbow-advanced"), Fixtures.Pack());
        var rune = reading.Lines.Single(l => l.Source == ModSource.Rune);
        Assert.Equal(LineStatus.NotInPack, rune.Status);
        Assert.All(rune.Rolls, r => Assert.False(r.HasBar));
    }

    [Fact]
    public void No_pack_means_no_bars_on_normal_copy()
    {
        var reading = ItemReader.Read(Fixtures.Item("rare-boots-normal"), pack: null);
        Assert.All(reading.Lines, l => Assert.Equal(LineStatus.NoPack, l.Status));
        Assert.All(reading.Lines.SelectMany(l => l.Rolls), r => Assert.False(r.HasBar));
    }

    [Fact]
    public void Pack_loader_rejects_bad_schema_and_range_count()
    {
        Assert.NotNull(DataPackLoader.Load("""{ "schemaVersion": 9, "id": "x", "title": "x", "provenance": "p", "rights": "r", "compatibleBuilds": [], "locales": [], "families": [] }""").Error);
        Assert.NotNull(DataPackLoader.Load("""
            { "schemaVersion": 1, "id": "x", "title": "x", "provenance": "p", "rights": "r", "compatibleBuilds": [], "locales": [],
              "families": [ { "id": "f", "group": "g", "side": "prefix", "itemClasses": [],
                "lines": [ { "template": "Adds # to # Physical Damage", "direction": "higherIsBetter", "unit": "", "components": 2 } ],
                "tiers": [ { "tier": 1, "name": "n", "level": 1, "ranges": [[1, 2]] } ] } ] }
            """).Error);
        Assert.NotNull(DataPackLoader.Load("""{ "schemaVersion": 1, "id": "x", "title": "x", "provenance": "", "rights": "", "compatibleBuilds": [], "locales": [], "families": [] }""").Error);
    }
}
