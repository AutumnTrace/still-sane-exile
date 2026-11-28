using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Desktop.Views;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Desktop.Tests;

/// <summary>The passive tree drawing: the export's art, clicking and dragging, the keyboard, and what a
/// screen reader hears. The build is the synthetic practice build; the tree and its art are the shipped
/// trim of GGG's export.</summary>
public class PassiveTreeViewTests
{
    private const int PureEnergy = 2254;

    private static (PassiveTreeViewModel Tree, PassiveTreeView View) Drawn(TestApp app, bool art = true)
    {
        var guide = app.Shell.Guide;
        guide.NewCharacterName = "Practice exile";
        guide.AddCharacterCommand.Execute(null);
        guide.Plan.CodeDraft = GuideBuildTests.PracticeCode(app);
        guide.Plan.ReadCodeCommand.Execute(null);
        var tree = guide.Plan.Tree;
        var view = new PassiveTreeView { Art = art ? tree.Art : null, Allocation = tree.Allocation };
        view.Measure(new Size(630, 460));
        view.Arrange(new Rect(0, 0, 630, 460));
        view.UpdateLayout();
        return (tree, view);
    }

    private static void Click(PassiveTreeView view, Point at)
    {
        view.PointerDown(at);
        view.PointerUp(at);
    }

    /// <summary>A point on the drawing with no node within reach.</summary>
    private static Point Empty(PassiveTreeView view)
    {
        for (var y = 10.0; y < 450; y += 20)
        {
            for (var x = 10.0; x < 620; x += 20)
            {
                if (view.NodeAt(new Point(x, y)) is null)
                {
                    return new Point(x, y);
                }
            }
        }

        throw new InvalidOperationException("No empty ground on the drawing.");
    }

    [Fact]
    public void The_shipped_art_loads_and_gives_pictures_in_tree_units() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var art = PassiveTreeArt.Load(app.Paths);

        Assert.NotNull(art);
        var frame = art.Get("frame:PSSkillFrame");
        Assert.NotNull(frame);
        Assert.Equal(102, frame.Width); // 51 pixels at the export's scale of 0.5
        Assert.Equal(51, frame.Image.PixelWidth);
        Assert.Same(frame, art.Get("frame:PSSkillFrame")); // cut out once
        Assert.NotNull(art.Get("line:Orbit3Active"));
        Assert.Null(art.Get("frame:NoSuchFrame"));
    });

    [Fact]
    public void Missing_or_damaged_art_means_no_art_and_the_tree_is_drawn_with_shapes() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        Assert.Null(PassiveTreeArt.Read(dir.Root));
        File.WriteAllText(dir.Sub("atlas.json"), """{ "sheets": { "frame": { "file": "frame.png", "units": 2 } }, "frames": { "frame:X": ["frame", 0, 0, 4, 4] } }""");
        File.WriteAllText(dir.Sub("frame.png"), "not a picture");
        Assert.Null(PassiveTreeArt.Read(dir.Root));

        using var app = new TestApp();
        var (_, view) = Drawn(app, art: false);
        Assert.True(view.DrawsTiles);
        Assert.Equal(10, view.Points.Count);
        Assert.NotNull(view.NodeAt(view.ScreenOf(view.Points[0])));
    });

    [Fact]
    public void A_click_selects_a_node_and_a_click_on_empty_ground_clears_it() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var (tree, view) = Drawn(app);
        var node = tree.Allocation!.Tree.Nodes[PureEnergy];

        Click(view, view.ScreenOf(node));

        Assert.Equal(PureEnergy, view.Selected?.Id);
        Assert.Equal("Pure Energy, notable, taken: 30% increased maximum Energy Shield; +10 to Intelligence", AutomationProperties.GetItemStatus(view));

        Click(view, Empty(view));

        Assert.Null(view.Selected);
        Assert.Equal("", AutomationProperties.GetItemStatus(view));
    });

    [Fact]
    public void A_press_that_turns_into_a_drag_moves_the_tree_and_selects_nothing() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var (tree, view) = Drawn(app);
        var node = tree.Allocation!.Tree.Nodes[PureEnergy];
        var at = view.ScreenOf(node);

        view.PointerDown(at);
        view.PointerMove(at + new Vector(3, 0), pressed: true); // a little wobble is still a click
        view.PointerMove(at + new Vector(40, 10), pressed: true);
        view.PointerUp(at + new Vector(40, 10));

        Assert.Null(view.Selected);
        Assert.Equal(at + new Vector(40, 10), view.ScreenOf(node));

        view.PointerDown(view.ScreenOf(node));
        view.PointerMove(view.ScreenOf(node) + new Vector(3, 0), pressed: true);
        view.PointerUp(view.ScreenOf(node) + new Vector(3, 0));
        Assert.Equal(PureEnergy, view.Selected?.Id);
    });

    [Fact]
    public void The_arrow_keys_walk_the_build_from_its_start_and_escape_clears() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var (tree, view) = Drawn(app);
        var a = tree.Allocation!;
        var points = view.Points;

        Assert.Equal(a.Points, points.Count);
        Assert.Contains(a.Tree.Edges, e => (e.From == 54447 && e.To == points[0].Id) || (e.To == 54447 && e.From == points[0].Id)); // next to the Witch's start

        Assert.True(view.Press(Key.Right));
        Assert.Same(points[0], view.Selected);
        Assert.True(view.Press(Key.Down));
        Assert.Same(points[1], view.Selected);
        Assert.True(view.Press(Key.Left));
        Assert.Same(points[0], view.Selected);
        Assert.True(view.Press(Key.Up)); // stays on the first
        Assert.Same(points[0], view.Selected);
        Assert.True(view.Press(Key.End));
        Assert.Same(points[^1], view.Selected);
        Assert.True(view.Press(Key.Home));
        Assert.Same(points[0], view.Selected);

        Assert.True(view.Press(Key.Escape));
        Assert.Null(view.Selected);
        Assert.False(view.Press(Key.Escape)); // nothing to clear: left for the page
        Assert.False(view.Press(Key.A));
    });

    [Fact]
    public void Stepping_to_a_point_off_screen_brings_it_into_view_and_zoom_keys_stop_at_the_arts_resolution() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var (_, view) = Drawn(app);
        for (var i = 0; i < 20; i++)
        {
            view.Press(Key.Add);
        }

        Assert.Equal(0.5, view.Scale, 3);
        view.Press(Key.End);
        var p = view.ScreenOf(view.Selected!);
        Assert.InRange(p.X, 0, 630);
        Assert.InRange(p.Y, 0, 460);

        view.Press(Key.Subtract);
        Assert.True(view.Scale < 0.5);
    });

    [Fact]
    public void The_drawing_says_it_is_the_passive_tree_and_is_a_tab_stop() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var (tree, view) = Drawn(app);
        AutomationProperties.SetName(view, "Passive tree");
        var peer = UIElementAutomationPeer.CreatePeerForElement(view);

        Assert.True(view.Focusable);
        Assert.Equal("Passive tree", peer.GetName());
        Assert.Equal("passive tree", peer.GetLocalizedControlType());
        Assert.True(peer.IsKeyboardFocusable());

        view.Selected = tree.Allocation!.Tree.Nodes[PureEnergy];
        Assert.StartsWith("Pure Energy, notable, taken", peer.GetItemStatus(), StringComparison.Ordinal);
    });

    [Fact]
    public void The_page_shows_the_selected_nodes_details_and_a_new_tree_clears_them() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var (tree, _) = Drawn(app);

        tree.ShowSpecCommand.Execute(tree.Specs.Single(s => s.Title == "Act 2"));
        tree.Selected = tree.Allocation!.Tree.Nodes[49363];

        Assert.True(tree.HasSelected);
        Assert.Equal("Wildsurge Incantation", tree.SelectedName);
        Assert.Equal("Keystone · the build takes it", tree.SelectedFacts);
        Assert.StartsWith("Storm and Plant Spells:", tree.SelectedStats, StringComparison.Ordinal);
        Assert.Equal("Practice note: take it last.", tree.SelectedNote);

        tree.Selected = tree.Allocation.Tree.Nodes.Values.First(n => n.Kind == TreeNodeKind.Notable && n.Ascendancy is null && !tree.Allocation.Nodes.Contains(n.Id));
        Assert.EndsWith("not taken in this tree", tree.SelectedFacts, StringComparison.Ordinal);
        Assert.False(tree.HasSelectedNote);

        tree.Selected = tree.Allocation.Tree.Nodes[8415]; // Sanguimancy, in the Blood Mage tree
        Assert.Equal("Notable · Blood Mage · the build takes it", tree.SelectedFacts);

        tree.ShowSpecCommand.Execute(tree.Specs.Single(s => s.Title == "Act 1"));
        Assert.False(tree.HasSelected);
    });
}
