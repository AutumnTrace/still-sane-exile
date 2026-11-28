using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Domain.Tests;

/// <summary>The passive tree's pictures: a synthetic atlas, then the shipped one against the shipped tree
/// (data/tree/art/atlas.json and data/tree/poe2-tree.json, both from GGG's export).</summary>
public class TreeAtlasTests
{
    private const string Tiny = """
        {
          "sheets": { "frame": { "file": "frame.png", "units": 2 }, "skills": { "file": "skills.jpg", "units": 2 } },
          "frames": {
            "frame:PSSkillFrame": ["frame", 0, 0, 51, 51],
            "normalActive:Art/Practice/Int.png": ["skills", 34, 0, 34, 34],
            "frame:Nowhere": ["missing-sheet", 0, 0, 10, 10],
            "frame:Empty": ["frame", 0, 0, 0, 10]
          }
        }
        """;

    [Fact]
    public void Reads_sheets_and_frames_and_drops_frames_it_cannot_draw()
    {
        var atlas = TreeAtlas.Parse(Tiny);

        Assert.Equal(new AtlasSheet("skills.jpg", 2), atlas.Sheets["skills"]);
        Assert.Equal(new AtlasFrame("frame", 0, 0, 51, 51), atlas.Find("frame:PSSkillFrame")?.Frame);
        Assert.Equal("skills.jpg", atlas.Find("normalActive:Art/Practice/Int.png")?.Sheet.File);
        Assert.Null(atlas.Find("frame:Nowhere"));
        Assert.Null(atlas.Find("frame:Empty"));
        Assert.Null(atlas.Find("not there"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{ "sheets": { "a": { "file": "a.png", "units": 2 } }, "frames": {} }""")]
    [InlineData("""{ "sheets": { "a": { "file": "..\\..\\secret.png", "units": 2 } }, "frames": { "x": ["a", 0, 0, 1, 1] } }""")]
    [InlineData("""{ "sheets": { "a": { "file": "C:/Windows/a.png", "units": 2 } }, "frames": { "x": ["a", 0, 0, 1, 1] } }""")]
    [InlineData("""{ "sheets": { "a": { "file": "a.png", "units": 0 } }, "frames": { "x": ["a", 0, 0, 1, 1] } }""")]
    public void An_atlas_that_is_broken_or_points_outside_its_folder_is_a_format_error(string json) =>
        Assert.Throws<FormatException>(() => TreeAtlas.Parse(json));

    [Fact]
    public void The_shipped_art_has_a_picture_for_every_node_and_connection_of_the_shipped_tree()
    {
        var atlas = TreeAtlas.Parse(Fixtures.DataText("tree/art/atlas.json"));
        var tree = PassiveTree.Parse(Fixtures.DataText("tree/poe2-tree.json"));

        static string? Sheet(TreeNode n) => n.Kind switch
        {
            TreeNodeKind.Keystone => "keystone",
            TreeNodeKind.Notable => "notable",
            TreeNodeKind.Normal or TreeNodeKind.Attribute => "normal",
            _ => null,
        };

        // Nameless nodes without icons are path points (and placeholders of ascendancies the export does not
        // name): they are not drawn as nodes, so they need no picture.
        Assert.All(tree.Nodes.Values.Where(n => Sheet(n) is not null && n.Icon is null), n => Assert.True(n.IsWaypoint));
        foreach (var n in tree.Nodes.Values.Where(n => Sheet(n) is not null && n.Icon is not null))
        {
            Assert.True(atlas.Find($"{Sheet(n)}Active:{n.Icon}") is not null, $"{n.Name} ({n.Id}) has no active icon");
            Assert.True(atlas.Find($"{Sheet(n)}Inactive:{n.Icon}") is not null, $"{n.Name} ({n.Id}) has no inactive icon");
        }

        foreach (var m in tree.Nodes.Values.Where(n => n.Kind == TreeNodeKind.Mastery))
        {
            Assert.NotNull(atlas.Find($"masteryEffectActive:{m.Pattern}"));
            Assert.NotNull(atlas.Find($"masteryEffectInactive:{m.Pattern}"));
        }

        var arcs = tree.Edges.Where(e => e.IsArc).ToList();
        Assert.True(arcs.Count > 1000);
        foreach (var state in new[] { "Normal", "Intermediate", "Active" })
        {
            Assert.NotNull(atlas.Find($"line:LineConnector{state}"));
            Assert.All(arcs, e => Assert.NotNull(atlas.Find($"line:Orbit{e.Orbit + 1}{state}")));
        }

        foreach (var frame in new[] { "PSSkillFrame", "PSSkillFrameActive", "PSSkillFrameHighlighted", "NotableFrameAllocated", "KeystoneFrameUnallocated", "JewelFrameCanAllocate", "AscendancyFrameNormalBacking", "AscendancyFrameNotableAllocated", "AscendancyStartNode" })
        {
            Assert.NotNull(atlas.Find($"frame:{frame}"));
        }

        Assert.NotNull(atlas.Find("background:Background2"));
        Assert.NotNull(atlas.Find("startNode:MainCircle"));
        Assert.All(atlas.Sheets.Values, s => Assert.True(s.File.EndsWith(".png", StringComparison.Ordinal) || s.File.EndsWith(".jpg", StringComparison.Ordinal), s.File));
    }
}
