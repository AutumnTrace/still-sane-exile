using System.Text.Json.Nodes;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Domain.Tests;

/// <summary>Route packs for the guide tests: the shipped Act 1 draft, and a small synthetic route with
/// the things the draft leaves out (permanent rewards, a hand-in, waypoint and portal travel, a zone
/// the route never visits).</summary>
internal static class TestRoutes
{
    public const string SyntheticJson = """
        {
          "schemaVersion": 1,
          "id": "test-route",
          "title": "Test route",
          "status": "synthetic",
          "game": "Path of Exile 2",
          "patch": "test",
          "league": "any",
          "source": "made up for unit tests",
          "rights": "test data",
          "revision": 2,
          "acts": [ { "act": 1, "title": "Act 1" }, { "act": 2, "title": "Act 2" } ],
          "zones": [
            { "id": "t.town", "act": 1, "name": "Test Town", "town": true, "waypoint": true, "edges": ["t.field"] },
            { "id": "t.field", "act": 1, "name": "The Field", "aliases": ["Meadow"], "waypoint": true, "edges": ["t.cave", "t.ridge", "t.grove"] },
            { "id": "t.cave", "act": 1, "name": "Cave" },
            { "id": "t.grove", "act": 1, "name": "Grove" },
            { "id": "t.ridge", "act": 1, "name": "Ridge", "edges": ["t.town2"] },
            { "id": "t.town2", "act": 2, "name": "Second Town", "town": true, "waypoint": true }
          ],
          "route": [
            { "zone": "t.town", "hint": "Start here." },
            { "zone": "t.field", "steps": [
              { "id": "t.field.shrine", "kind": "bonus", "text": "Touch the shrine", "rewards": [ { "text": "+1 passive skill point", "permanent": true } ] } ] },
            { "zone": "t.cave", "hint": "Dead end.", "steps": [
              { "id": "t.cave.boss", "kind": "objective", "text": "Defeat the Cave Boss" } ] },
            { "zone": "t.town", "via": "portal", "steps": [
              { "id": "t.town.tess", "kind": "handIn", "npc": "Tess", "text": "Talk to Tess", "rewards": [ { "text": "+10% to all resistances", "permanent": true }, { "text": "a ring" } ] } ] },
            { "zone": "t.field", "via": "waypoint" },
            { "zone": "t.ridge", "steps": [
              { "id": "t.ridge.boss", "kind": "objective", "text": "Defeat the Ridge Boss" } ] },
            { "zone": "t.town2" }
          ]
        }
        """;

    public static RoutePack Synthetic() => Load(SyntheticJson);

    public static RoutePack Act1() => Load(Fixtures.DataText("routes/act1-opening.json"));

    public static RoutePack Load(string json)
    {
        var result = RoutePackLoader.Load(json);
        Assert.True(result.Pack is not null, result.Error);
        Assert.Null(result.Error);
        return result.Pack!;
    }

    /// <summary>The synthetic pack with one change applied, as JSON.</summary>
    public static string SyntheticWith(Action<JsonObject> change)
    {
        var node = JsonNode.Parse(SyntheticJson)!.AsObject();
        change(node);
        return node.ToJsonString();
    }
}
