using System.Text.Json.Nodes;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Domain.Tests;

public class RoutePackTests
{
    [Fact]
    public void Shipped_act1_pack_loads_and_says_it_is_an_unverified_draft()
    {
        var pack = TestRoutes.Act1();

        Assert.Equal(RouteStatus.Unverified, pack.Status);
        Assert.False(pack.IsVerified);
        Assert.Null(pack.VerifiedDate);
        Assert.Contains("to be checked after the 11 Dec 2026 full release", pack.Patch);
        Assert.Contains("no guide prose copied", pack.Source);
        Assert.Equal("The Riverbank", pack.Zone(pack.Route[0].Zone).Name);
        Assert.Equal("Defeat the Devourer", pack.FindStep("a1.mud_burrow.devourer")!.Text);
    }

    [Fact]
    public void Shipped_act1_pack_backtracks_out_of_its_dead_ends()
    {
        var zones = TestRoutes.Act1().Route.Select(l => l.Zone).ToList();

        Assert.Equal(["a1.riverbank", "a1.clearfell_encampment", "a1.clearfell", "a1.mud_burrow", "a1.clearfell", "a1.grelwood", "a1.red_vale", "a1.grelwood"], zones);
    }

    [Fact]
    public void Edges_are_stored_both_ways()
    {
        var pack = TestRoutes.Synthetic();

        Assert.Contains("t.field", pack.Zone("t.cave").EdgeList);
        Assert.Contains("t.cave", pack.Zone("t.field").EdgeList);
        Assert.True(pack.Connected("t.cave", "t.field"));
        Assert.False(pack.Connected("t.cave", "t.ridge"));
    }

    [Theory]
    [InlineData("The Field", "t.field")]
    [InlineData("field", "t.field")]
    [InlineData("  MEADOW ", "t.field")]
    [InlineData("the cave", "t.cave")]
    [InlineData("Nowhere", null)]
    [InlineData("", null)]
    public void Zones_resolve_by_name_or_alias_ignoring_case_and_a_leading_the(string name, string? id)
    {
        Assert.Equal(id, TestRoutes.Synthetic().ResolveZone(name)?.Id);
    }

    [Fact]
    public void Waypoints_are_unknown_unless_the_pack_states_them()
    {
        var pack = TestRoutes.Act1();

        Assert.True(pack.Zone("a1.clearfell_encampment").Waypoint);
        Assert.Null(pack.Zone("a1.clearfell").Waypoint);
    }

    [Fact]
    public void Wrong_schema_version_is_rejected()
    {
        var result = RoutePackLoader.Load(TestRoutes.SyntheticWith(p => p["schemaVersion"] = 2));

        Assert.Null(result.Pack);
        Assert.Contains("schema 2 is not supported", result.Error);
    }

    [Fact]
    public void A_pack_for_another_game_is_rejected()
    {
        var result = RoutePackLoader.Load(TestRoutes.SyntheticWith(p => p["game"] = "Path of Exile"));

        Assert.Null(result.Pack);
        Assert.Contains("not Path of Exile 2", result.Error);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("rights")]
    [InlineData("patch")]
    public void A_pack_must_say_where_it_came_from(string field)
    {
        var result = RoutePackLoader.Load(TestRoutes.SyntheticWith(p => p[field] = " "));

        Assert.Null(result.Pack);
        Assert.Contains("must all be filled in", result.Error);
    }

    [Fact]
    public void A_missing_required_field_is_rejected_not_defaulted()
    {
        var result = RoutePackLoader.Load(TestRoutes.SyntheticWith(p => p.Remove("zones")));

        Assert.Null(result.Pack);
        Assert.StartsWith("Route pack is not valid", result.Error);
    }

    [Fact]
    public void Unknown_status_is_rejected()
    {
        var result = RoutePackLoader.Load(TestRoutes.SyntheticWith(p => p["status"] = "probably-fine"));

        Assert.Null(result.Pack);
    }

    [Fact]
    public void A_verified_pack_must_say_when()
    {
        var result = RoutePackLoader.Load(TestRoutes.SyntheticWith(p => p["status"] = "verified"));

        Assert.Null(result.Pack);
        Assert.Contains("when it was verified", result.Error);
    }

    [Fact]
    public void Walking_between_zones_that_do_not_touch_is_rejected()
    {
        // Cave → Ridge has no edge.
        var json = TestRoutes.SyntheticWith(p => p["route"]!.AsArray().Insert(3, new JsonObject { ["zone"] = "t.ridge" }));
        var result = RoutePackLoader.Load(json);

        Assert.Null(result.Pack);
        Assert.Contains("no edge between them", result.Error);
    }

    [Fact]
    public void Waypoint_travel_needs_a_stated_waypoint_at_both_ends()
    {
        var json = TestRoutes.SyntheticWith(p => p["zones"]!.AsArray().Single(z => (string?)z!["id"] == "t.field")!.AsObject().Remove("waypoint"));
        var result = RoutePackLoader.Load(json);

        Assert.Null(result.Pack);
        Assert.Contains("does not give both zones a waypoint", result.Error);
    }

    [Fact]
    public void Two_legs_in_a_row_in_the_same_zone_are_rejected()
    {
        var json = TestRoutes.SyntheticWith(p => p["route"]!.AsArray().Insert(1, new JsonObject { ["zone"] = "t.town" }));

        Assert.Contains("two legs in a row", RoutePackLoader.Load(json).Error);
    }

    [Fact]
    public void Edges_to_unknown_zones_are_rejected()
    {
        var json = TestRoutes.SyntheticWith(p => p["zones"]![2]!["edges"] = new JsonArray("t.nowhere"));

        Assert.Contains("not a zone in the pack", RoutePackLoader.Load(json).Error);
    }

    [Fact]
    public void Step_ids_must_be_unique()
    {
        var json = TestRoutes.SyntheticWith(p => p["route"]![5]!["steps"]![0]!["id"] = "t.cave.boss");

        Assert.Contains("used twice", RoutePackLoader.Load(json).Error);
    }

    [Fact]
    public void A_hand_in_must_name_the_npc()
    {
        var json = TestRoutes.SyntheticWith(p => p["route"]![3]!["steps"]![0]!.AsObject().Remove("npc"));

        Assert.Contains("must name the NPC", RoutePackLoader.Load(json).Error);
    }

    [Fact]
    public void A_name_two_zones_share_is_rejected()
    {
        var json = TestRoutes.SyntheticWith(p => p["zones"]![2]!["aliases"] = new JsonArray("Meadow"));

        Assert.Contains("cannot be told apart", RoutePackLoader.Load(json).Error);
    }

    [Fact]
    public void Broken_json_is_rejected()
    {
        Assert.Null(RoutePackLoader.Load("{ \"schemaVersion\": 1, ").Pack);
        Assert.Null(RoutePackLoader.Load("null").Pack);
    }

    [Fact]
    public void A_broken_replacement_keeps_the_last_good_pack()
    {
        var good = TestRoutes.Synthetic();

        var result = RoutePackLoader.Replace(good, TestRoutes.SyntheticWith(p => p["schemaVersion"] = 3));

        Assert.Same(good, result.Pack);
        Assert.Contains("Still using revision 2", result.Error);
    }

    [Fact]
    public void An_older_revision_does_not_replace_a_newer_one()
    {
        var good = TestRoutes.Synthetic();

        var result = RoutePackLoader.Replace(good, TestRoutes.SyntheticWith(p => p["revision"] = 1));

        Assert.Same(good, result.Pack);
        Assert.Contains("older than revision 2", result.Error);
    }

    [Fact]
    public void A_newer_good_revision_replaces_the_pack()
    {
        var result = RoutePackLoader.Replace(TestRoutes.Synthetic(), TestRoutes.SyntheticWith(p => p["revision"] = 3));

        Assert.Null(result.Error);
        Assert.Equal(3, result.Pack!.Revision);
    }

    [Fact]
    public void With_nothing_in_use_a_broken_pack_leaves_nothing()
    {
        var result = RoutePackLoader.Replace(null, "not json");

        Assert.Null(result.Pack);
        Assert.NotNull(result.Error);
    }
}
