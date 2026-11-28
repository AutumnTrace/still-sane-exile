using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Desktop.Views;

/// <summary>Draws a build's passive tree on Path of Exile 2's tree from GGG's export: every node and
/// connection where the game puts it, the build's own in gold, notables and keystones larger, as in the
/// game. Shapes in the game's colours, not the game's art: whether to ship the export's art is the
/// owner's call (docs/SOURCES.md, row 22). The main tree and an ascendancy are drawn by two instances,
/// because the export places ascendancies far outside the main tree.
///
/// The wheel zooms around the pointer and dragging pans; the buttons beside it run the same commands
/// from the keyboard. It is never a tab stop: the list of picks under it is what a screen reader reads.</summary>
public sealed class PassiveTreeView : FrameworkElement
{
    public static readonly RoutedUICommand ZoomInCommand = new("Zoom in", nameof(ZoomInCommand), typeof(PassiveTreeView));
    public static readonly RoutedUICommand ZoomOutCommand = new("Zoom out", nameof(ZoomOutCommand), typeof(PassiveTreeView));
    public static readonly RoutedUICommand FitBuildCommand = new("Fit to the build", nameof(FitBuildCommand), typeof(PassiveTreeView));
    public static readonly RoutedUICommand ShowAllCommand = new("Whole tree", nameof(ShowAllCommand), typeof(PassiveTreeView));

    public static readonly DependencyProperty AllocationProperty = DependencyProperty.Register(
        nameof(Allocation), typeof(TreeAllocation), typeof(PassiveTreeView), new FrameworkPropertyMetadata(null, (d, _) => ((PassiveTreeView)d).Rebuild()));

    public static readonly DependencyProperty AscendancyOnlyProperty = DependencyProperty.Register(
        nameof(AscendancyOnly), typeof(bool), typeof(PassiveTreeView), new FrameworkPropertyMetadata(false, (d, _) => ((PassiveTreeView)d).Rebuild()));

    public static readonly DependencyProperty HoveredProperty = DependencyProperty.Register(
        nameof(Hovered), typeof(TreeNode), typeof(PassiveTreeView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    private const double MaxScale = 0.6;
    private const double Padding = 260;

    // The drawing's own palette, close to the game's tree: dark ground, bronze lines, gold for the build.
    private static readonly Brush Ground = Frozen(0x0E, 0x0D, 0x0B);
    private static readonly Brush Line = Frozen(0x4A, 0x41, 0x30);
    private static readonly Brush Taken = Frozen(0xD6, 0xB2, 0x60);
    private static readonly Brush NodeFill = Frozen(0x1F, 0x1B, 0x15);
    private static readonly Brush NodeEdge = Frozen(0x76, 0x6A, 0x54);
    private static readonly Brush BigEdge = Frozen(0xA8, 0x91, 0x5E);
    private static readonly Brush JewelFill = Frozen(0x16, 0x20, 0x2A);
    private static readonly Brush JewelEdge = Frozen(0x6C, 0x8F, 0xA8);
    private static readonly Brush MasteryFill = Frozen(0x26, 0x21, 0x19);
    private static readonly Brush TakenFill = Frozen(0xE3, 0xC4, 0x7A);
    private static readonly Brush TakenEdge = Frozen(0xFF, 0xF0, 0xC8);
    private static readonly Brush HoverEdge = Frozen(0xF2, 0xEB, 0xDA);

    private readonly MatrixTransform _view = new();
    private readonly List<Layer> _layers = [];
    private List<TreeNode> _shown = [];
    private Rect _all = Rect.Empty;
    private Rect _build = Rect.Empty;
    private double _scale = 1;
    private Vector _offset;
    private bool _moved;
    private Point? _drag;

    public PassiveTreeView()
    {
        Focusable = false;
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        CommandBindings.Add(new CommandBinding(ZoomInCommand, (_, _) => ZoomAt(Centre, 1.5), CanRun));
        CommandBindings.Add(new CommandBinding(ZoomOutCommand, (_, _) => ZoomAt(Centre, 1 / 1.5), CanRun));
        CommandBindings.Add(new CommandBinding(FitBuildCommand, (_, _) => Fit(_build, byPlayer: false), CanRun));
        CommandBindings.Add(new CommandBinding(ShowAllCommand, (_, _) => Fit(_all, byPlayer: true), CanRun));
    }

    public TreeAllocation? Allocation
    {
        get => (TreeAllocation?)GetValue(AllocationProperty);
        set => SetValue(AllocationProperty, value);
    }

    /// <summary>Draws the build's ascendancy instead of the main tree.</summary>
    public bool AscendancyOnly
    {
        get => (bool)GetValue(AscendancyOnlyProperty);
        set => SetValue(AscendancyOnlyProperty, value);
    }

    /// <summary>The node under the mouse, for the line under the drawing.</summary>
    public TreeNode? Hovered
    {
        get => (TreeNode?)GetValue(HoveredProperty);
        set => SetValue(HoveredProperty, value);
    }

    private Point Centre => new(ActualWidth / 2, ActualHeight / 2);

    private double MinScale => _all.IsEmpty ? 0.001 : FitScale(Pad(_all)) * 0.8;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Ground, null, new Rect(RenderSize));
        foreach (var layer in _layers)
        {
            var pen = layer.Stroke is null ? null : new Pen(layer.Stroke, Math.Clamp(layer.Units * _scale, layer.MinPx, layer.MaxPx));
            dc.DrawGeometry(layer.Fill, pen, layer.Geometry);
        }

        if (Hovered is { } h && _shown.Contains(h))
        {
            var p = ToScreen(new Point(h.X, h.Y));
            var r = Math.Max(Radius(h) * _scale, 4) + 4;
            dc.DrawEllipse(null, new Pen(HoverEdge, 2), p, r, r);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (!_moved)
        {
            Fit(_build, byPlayer: false);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_layers.Count > 0)
        {
            ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.25 : 0.8);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _drag = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _drag = null;
        ReleaseMouseCapture();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var at = e.GetPosition(this);
        if (_drag is { } from && e.LeftButton == MouseButtonState.Pressed)
        {
            _offset += at - from;
            _drag = at;
            _moved = true;
            Apply();
            return;
        }

        var hit = NodeAt(at);
        if (!ReferenceEquals(hit, Hovered))
        {
            SetCurrentValue(HoveredProperty, hit);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_drag is null)
        {
            SetCurrentValue(HoveredProperty, null);
        }
    }

    private void CanRun(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _layers.Count > 0;

    /// <summary>The node within reach of a point on screen, if any.</summary>
    private TreeNode? NodeAt(Point screen)
    {
        if (_layers.Count == 0)
        {
            return null;
        }

        var p = new Point((screen.X - _offset.X) / _scale, (screen.Y - _offset.Y) / _scale);
        TreeNode? best = null;
        var bestDistance = double.MaxValue;
        foreach (var n in _shown)
        {
            var d = Math.Sqrt(((n.X - p.X) * (n.X - p.X)) + ((n.Y - p.Y) * (n.Y - p.Y)));
            var reach = Math.Max(Radius(n) * 1.3, 10 / _scale);
            if (d < reach && d < bestDistance)
            {
                (best, bestDistance) = (n, d);
            }
        }

        return best;
    }

    private void Rebuild()
    {
        _layers.Clear();
        _shown = [];
        (_all, _build) = (Rect.Empty, Rect.Empty);
        _moved = false;
        if (Allocation is { } a && !a.Tree.IsEmpty)
        {
            var ascendancy = a.Ascendancy?.Id;
            if (!AscendancyOnly || ascendancy is not null)
            {
                Build(a, AscendancyOnly ? ascendancy : null);
            }
        }

        Fit(_build, byPlayer: false);
        CommandManager.InvalidateRequerySuggested();
    }

    private void Build(TreeAllocation a, string? ascendancy)
    {
        var tree = a.Tree;
        _shown = tree.Nodes.Values.Where(n => n.Ascendancy == ascendancy).ToList();
        var shown = _shown.Select(n => n.Id).ToHashSet();
        foreach (var n in _shown)
        {
            var r = Radius(n);
            var area = new Rect(n.X - r, n.Y - r, 2 * r, 2 * r);
            _all.Union(area);
            if (a.Nodes.Contains(n.Id))
            {
                _build.Union(area);
            }
        }

        // An ascendancy is small: show all of it, so the choices left are in view too.
        if (_build.IsEmpty || ascendancy is not null)
        {
            _build = _all;
        }

        var plain = new StreamGeometry { Transform = _view };
        var taken = new StreamGeometry { Transform = _view };
        using (var plainLines = plain.Open())
        using (var takenLines = taken.Open())
        {
            foreach (var e in tree.Edges.Where(e => shown.Contains(e.From) && shown.Contains(e.To)))
            {
                Connect(a.Takes(e) ? takenLines : plainLines, tree.Nodes[e.From], tree.Nodes[e.To], e);
            }
        }

        _layers.Add(new Layer(plain, null, Line, 12, 0.8, 5));
        _layers.Add(new Layer(taken, null, Taken, 22, 2, 9));

        // Unallocated nodes by look, then the build's own on top.
        foreach (var group in _shown.Where(n => !a.Nodes.Contains(n.Id)).GroupBy(Look))
        {
            var (fill, stroke, units) = group.Key;
            _layers.Add(new Layer(Circles(group), fill, stroke, units, 0.8, 4));
        }

        var mine = _shown.Where(n => a.Nodes.Contains(n.Id) && n.Kind != TreeNodeKind.Mastery).ToList();
        if (mine.Count > 0)
        {
            _layers.Add(new Layer(Circles(mine), TakenFill, TakenEdge, 8, 1.5, 4));
        }
    }

    private static void Connect(StreamGeometryContext g, TreeNode from, TreeNode to, TreeEdge e)
    {
        var a = new Point(from.X, from.Y);
        var b = new Point(to.X, to.Y);
        g.BeginFigure(a, false, false);
        if (e.IsArc)
        {
            var c = new Point(e.CentreX!.Value, e.CentreY!.Value);
            var r = (a - c).Length;
            var cross = ((a.X - c.X) * (b.Y - c.Y)) - ((a.Y - c.Y) * (b.X - c.X));
            g.ArcTo(b, new Size(r, r), 0, false, cross > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
        }
        else
        {
            g.LineTo(b, true, false);
        }
    }

    private GeometryGroup Circles(IEnumerable<TreeNode> nodes)
    {
        var group = new GeometryGroup { Transform = _view, FillRule = FillRule.Nonzero };
        foreach (var n in nodes)
        {
            var r = Radius(n);
            group.Children.Add(new EllipseGeometry(new Point(n.X, n.Y), r, r));
        }

        return group;
    }

    /// <summary>Sizes in tree units, from the export's spacing: neighbours sit 130 units apart or more.</summary>
    private static double Radius(TreeNode n) => n.Kind switch
    {
        TreeNodeKind.Keystone => 80,
        TreeNodeKind.Notable => 54,
        TreeNodeKind.Jewel => 48,
        TreeNodeKind.Start => 150,
        TreeNodeKind.AscendancyStart => 64,
        TreeNodeKind.Mastery => 40,
        TreeNodeKind.Attribute => 30,
        _ => 36,
    };

    private static (Brush? Fill, Brush? Stroke, double Units) Look(TreeNode n) => n.Kind switch
    {
        TreeNodeKind.Keystone or TreeNodeKind.Notable => (NodeFill, BigEdge, 12),
        TreeNodeKind.Jewel => (JewelFill, JewelEdge, 9),
        TreeNodeKind.Mastery => (MasteryFill, null, 0),
        TreeNodeKind.Start => (null, BigEdge, 16),
        TreeNodeKind.AscendancyStart => (NodeFill, BigEdge, 12),
        _ => (NodeFill, NodeEdge, 7),
    };

    private void ZoomAt(Point screen, double factor)
    {
        var scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        var tree = new Point((screen.X - _offset.X) / _scale, (screen.Y - _offset.Y) / _scale);
        _scale = scale;
        _offset = new Vector(screen.X - (tree.X * scale), screen.Y - (tree.Y * scale));
        _moved = true;
        Apply();
    }

    private void Fit(Rect area, bool byPlayer)
    {
        if (area.IsEmpty || ActualWidth <= 0 || ActualHeight <= 0)
        {
            InvalidateVisual();
            return;
        }

        var padded = Pad(area);
        _scale = Math.Min(FitScale(padded), MaxScale);
        var middle = new Point(padded.X + (padded.Width / 2), padded.Y + (padded.Height / 2));
        _offset = new Vector((ActualWidth / 2) - (middle.X * _scale), (ActualHeight / 2) - (middle.Y * _scale));
        _moved = byPlayer;
        Apply();
    }

    private double FitScale(Rect r) => Math.Min(ActualWidth / r.Width, ActualHeight / r.Height);

    private static Rect Pad(Rect r)
    {
        var w = Math.Max(r.Width + (2 * Padding), 1600);
        var h = Math.Max(r.Height + (2 * Padding), 1600);
        return new Rect(r.X + (r.Width / 2) - (w / 2), r.Y + (r.Height / 2) - (h / 2), w, h);
    }

    private void Apply()
    {
        _view.Matrix = new Matrix(_scale, 0, 0, _scale, _offset.X, _offset.Y);
        InvalidateVisual();
    }

    private Point ToScreen(Point tree) => new((tree.X * _scale) + _offset.X, (tree.Y * _scale) + _offset.Y);

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private sealed record Layer(Geometry Geometry, Brush? Fill, Brush? Stroke, double Units, double MinPx, double MaxPx);
}
