using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Domain.Tests;

/// <summary>The passive tree SSE draws a build on: a synthetic tree in the shipped file's shape, then the
/// shipped trim of GGG's export itself (data/tree/poe2-tree.json).</summary>
public class PassiveTreeTests
{
    private const string Tiny = """
        {
          "version": "0.5.5", "source": "synthetic", "fetched": "2026-11-28",
          "classes": [ { "name": "Witch", "ascendancies": [ { "id": "Witch2", "name": "Blood Mage" } ] } ],
          "nodes": [
            { "id": 1, "name": "WITCH", "kind": "start", "x": 0, "y": 0 },
            { "id": 2, "name": "Intelligence", "kind": "attribute", "x": 100, "y": 0, "stats": ["+5 to Intelligence"] },
            { "id": 3, "name": "Practice Notable", "kind": "notable", "x": 200, "y": 0, "stats": ["10% increased Spell Damage"] },
            { "id": 4, "name": "Practice Keystone", "kind": "keystone", "x": 300, "y": 100 },
            { "id": 5, "name": "Something new", "kind": "brand new kind", "x": 0, "y": 300 },
            { "id": 10, "name": "Blood Mage", "kind": "ascendancyStart", "x": 0, "y": -5000, "ascendancy": "Witch2" },
            { "id": 11, "name": "Practice Ascendancy Notable", "kind": "notable", "x": 0, "y": -5200, "ascendancy": "Witch2" }
          ],
          "edges": [ [1, 2], [2, 3], [3, 4, 200, 100], [10, 11], [4, 99] ]
        }
        """;

    private static readonly PassiveTree Tree = PassiveTree.Parse(Tiny);

    private static PobTreeSpec Spec(string? ascendancy, string? version, params int[] nodes) =>
        new(null, version, ascendancy, nodes, new Dictionary<int, int> { [3] = 1 }, new Dictionary<int, string> { [4] = "Take it last." });

    [Fact]
    public void Reads_nodes_their_kinds_and_stats_the_edges_and_the_ascendancies()
    {
        Assert.Equal("0.5.5", Tree.Version);
        Assert.Equal(7, Tree.Nodes.Count);
        Assert.Equal(TreeNodeKind.Attribute, Tree.Nodes[2].Kind);
        Assert.Equal(TreeNodeKind.Normal, Tree.Nodes[5].Kind); // an unknown kind is drawn as a plain node
        Assert.Equal(["10% increased Spell Damage"], Tree.Nodes[3].Stats);
        Assert.Equal("Witch2", Tree.Nodes[11].Ascendancy);

        Assert.Equal(4, Tree.Edges.Count); // the edge to a node the tree does not have is dropped
        Assert.True(Tree.Edges.Single(e => e.From == 3).IsArc);
        Assert.False(Tree.Edges.Single(e => e.From == 1).IsArc);
        Assert.Equal(new TreeAscendancy("Witch2", "Blood Mage", "Witch"), Assert.Single(Tree.Ascendancies));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "nodes": [], "edges": [], "classes": [] }""")]
    [InlineData("""{ "nodes": [ { "id": 1 } ], "edges": [], "classes": [] }""")]
    public void A_file_that_is_not_a_tree_is_a_format_error(string json) => Assert.Throws<FormatException>(() => PassiveTree.Parse(json));

    [Fact]
    public void A_build_counts_points_without_its_starts_and_lists_keystones_then_notables()
    {
        var a = Tree.Allocate(Spec("Witch2", "0_5", 1, 2, 3, 4, 10, 11, 777));

        Assert.Equal(3, a.Points); // 2, 3 and 4; the class start is not a point
        Assert.Equal(1, a.AscendancyPoints); // 11; the ascendancy start is not one either
        Assert.Equal(["Practice Keystone", "Practice Notable"], a.Picks.Select(n => n.Name));
        Assert.Equal(["Practice Ascendancy Notable"], a.AscendancyPicks.Select(n => n.Name));
        Assert.Equal([777], a.Missing);
        Assert.Equal("Blood Mage", a.Ascendancy?.Name);
        Assert.Equal(1, a.WeaponSetPoints(1));
        Assert.Equal("Take it last.", a.Notes[4]);
        Assert.True(a.Takes(Tree.Edges.Single(e => e.From == 3)));
        Assert.False(a.Takes(new TreeEdge(4, 5, null, null)));
        Assert.True(a.SameVersion);
    }

    [Fact]
    public void The_ascendancy_comes_from_its_points_when_the_id_does_not_match()
    {
        Assert.Equal("Blood Mage", Tree.Allocate(Spec("SomethingElse", null, 1, 10, 11)).Ascendancy?.Name);
        Assert.Null(Tree.Allocate(Spec(null, null, 1, 2)).Ascendancy);
    }

    [Theory]
    [InlineData("0_5", true)]
    [InlineData("0_5_ruthless", true)]
    [InlineData(null, true)]
    [InlineData("0_4", false)]
    [InlineData("1_0", false)]
    public void Path_of_building_tree_versions_compare_by_major_and_minor(string? pob, bool same) =>
        Assert.Equal(same, Tree.Allocate(Spec(null, pob, 1)).SameVersion);

    [Fact]
    public void The_shipped_tree_is_ggg_export_and_holds_the_practice_build_as_one_connected_tree()
    {
        var tree = PassiveTree.Parse(Fixtures.DataText("tree/poe2-tree.json"));

        Assert.StartsWith("0.5", tree.Version, StringComparison.Ordinal);
        Assert.StartsWith("https://raw.githubusercontent.com/grindinggear/poe2-skilltree-export/", tree.Source, StringComparison.Ordinal);
        Assert.True(tree.Nodes.Count > 4000);
        Assert.True(tree.Edges.Count > 4000);
        Assert.Contains(tree.Ascendancies, a => a.Name == "Blood Mage" && a.ClassName == "Witch");
        Assert.DoesNotContain(tree.Nodes.Values.SelectMany(n => n.Stats), s => s.Contains('[', StringComparison.Ordinal) || s.Contains('}', StringComparison.Ordinal)); // markup removed

        var build = PobCodeTests.Practice();
        var a = tree.Allocate(build.Tree!);
        Assert.Empty(a.Missing);
        Assert.True(a.SameVersion);
        Assert.Equal("Blood Mage", a.Ascendancy?.Name);
        Assert.Equal(24, a.Points);
        Assert.Equal(1, a.AscendancyPoints);
        Assert.Equal("Wildsurge Incantation", a.Picks[0].Name);
        Assert.Equal(TreeNodeKind.Keystone, a.Picks[0].Kind);
        Assert.Equal(5, a.Picks.Count);

        // Every allocated node but the starts links to another allocated node: the drawing is one tree.
        var linked = tree.Edges.Where(a.Takes).SelectMany(e => new[] { e.From, e.To }).ToHashSet();
        Assert.All(a.Allocated.Where(n => n.IsPoint), n => Assert.Contains(n.Id, linked));
    }
}
