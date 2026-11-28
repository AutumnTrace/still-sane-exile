using StillSaneExile.Domain.Reading;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Domain.Tests;

public class WorksheetTests
{
    private static ComparisonWorksheet Crossbow() =>
        WorksheetFactory.Create(ItemReader.Read(Fixtures.Item("rare-crossbow-advanced"), Fixtures.Pack()));

    [Fact]
    public void Attack_weapon_preset_selects_physical_dps_and_attack_speed()
    {
        var w = Crossbow();
        var selected = w.Selected.Select(r => r.Id).ToList();
        Assert.Equal(2, selected.Count);
        Assert.Contains(ComparisonWorksheet.PhysicalDpsId, selected);
        Assert.Contains($"{RowKind.Explicit}:{ComparisonWorksheet.LocalAttackSpeed}", selected);
        Assert.Equal(758.5m, w.Row(ComparisonWorksheet.PhysicalDpsId)!.Actual); // (205 + 581) / 2 × 1.93
    }

    [Fact]
    public void Boots_preset_selects_movement_speed_and_life()
    {
        var w = WorksheetFactory.Create(ItemReader.Read(Fixtures.Item("rare-boots-normal"), Fixtures.Pack()));
        Assert.Equal(["#% increased Movement Speed", "# to maximum Life"], w.Selected.Select(r => r.Template));
    }

    [Fact]
    public void Every_line_gets_a_row_and_sources_stay_distinct()
    {
        var w = Crossbow();
        Assert.Contains(w.Rows, r => r.Kind == RowKind.Rune && r.Template == "Adds # to # Fire Damage");
        Assert.Contains(w.Rows, r => r.Kind == RowKind.Explicit && r.Template == "# to Accuracy Rating");
        Assert.False(w.Rows.Single(r => r.Kind == RowKind.Rune).Supported);
    }

    [Fact]
    public void Pseudo_total_and_its_contributors_warn_about_double_counting()
    {
        var w = Crossbow();
        w.Rows.Single(r => r.Template == ComparisonWorksheet.LocalPhysPercent).Selected = true;
        Assert.Contains(w.Warnings(), x => x.Contains("already includes", StringComparison.Ordinal));
    }

    [Fact]
    public void Clearing_everything_is_allowed_and_warned()
    {
        var w = Crossbow();
        foreach (var r in w.Rows)
        {
            r.Selected = false;
        }

        Assert.Contains(w.Warnings(), x => x.StartsWith("No stats selected", StringComparison.Ordinal));
    }

    [Fact]
    public void Relaxation_is_offered_one_change_at_a_time_and_never_applied_silently()
    {
        var w = Crossbow();
        var before = w.Row(ComparisonWorksheet.PhysicalDpsId)!.Min;
        var suggestions = w.SuggestRelaxations();
        Assert.NotEmpty(suggestions);
        Assert.Equal(before, w.Row(ComparisonWorksheet.PhysicalDpsId)!.Min);

        suggestions.First(s => s.RowId == ComparisonWorksheet.PhysicalDpsId && s.Description.StartsWith("Lower", StringComparison.Ordinal)).Apply(w);
        Assert.True(w.Row(ComparisonWorksheet.PhysicalDpsId)!.Min < before);
    }

    [Fact]
    public void Manual_instructions_list_groups_and_market_controls()
    {
        var w = Crossbow();
        w.Row($"{RowKind.Explicit}:{ComparisonWorksheet.LocalAttackSpeed}")!.Group = FilterGroup.Optional;
        var text = w.ToManualInstructions("Demo League (synthetic)");
        Assert.Contains("Item base: Siege Crossbow", text);
        Assert.Contains("Not corrupted", text);
        Assert.Contains("Stat filters (AND):", text);
        Assert.Contains("Optional (COUNT ≥ 1):", text);
    }
}
