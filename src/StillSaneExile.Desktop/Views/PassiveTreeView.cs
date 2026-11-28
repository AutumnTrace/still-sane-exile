using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Desktop.Views;

/// <summary>Draws a build's passive tree the way the game does, with the art of GGG's own export: the
/// stone ground, the centre circle, mastery patterns, connectors and orbit arcs, and every node's icon in
/// its frame, allocated, next to the build or not. The main tree and an ascendancy are drawn by two
/// instances, because the export places ascendancies far outside the main tree. Without the art files it
/// falls back to plain shapes.
///
/// The art is built once per build into frozen drawings, cut into tiles, and only the tiles in view are
/// drawn; zoomed far out, one picture of the whole tree is drawn instead. Hover, selection and focus are
/// the only things drawn anew each frame.
///
/// Mouse: the wheel zooms around the pointer, dragging pans, a click selects a node (a click that turns
/// into a drag does not), a click on empty ground clears it. Keyboard: the drawing is a tab stop; the
/// arrow keys move through the build's points in path order, Home and End jump to the ends, Esc clears,
/// + and − zoom. The buttons beside it run the same commands. Screen readers hear the selected node as
/// the drawing's item status.</summary>
public sealed class PassiveTreeView : FrameworkElement
{
    public static readonly RoutedUICommand ZoomInCommand = new("Zoom in", nameof(ZoomInCommand), typeof(PassiveTreeView));
    public static readonly RoutedUICommand ZoomOutCommand = new("Zoom out", nameof(ZoomOutCommand), typeof(PassiveTreeView));
    public static readonly RoutedUICommand FitBuildCommand = new("Fit to the build", nameof(FitBuildCommand), typeof(PassiveTreeView));
    public static readonly RoutedUICommand ShowAllCommand = new("Whole tree", nameof(ShowAllCommand), typeof(PassiveTreeView));
    public static readonly RoutedUICommand NextPointCommand = new("Next point", nameof(NextPointCommand), typeof(PassiveTreeView));
    public static readonly RoutedUICommand PreviousPointCommand = new("Previous point", nameof(PreviousPointCommand), typeof(PassiveTreeView));

    public static readonly DependencyProperty AllocationProperty = DependencyProperty.Register(
        nameof(Allocation), typeof(TreeAllocation), typeof(PassiveTreeView), new FrameworkPropertyMetadata(null, (d, _) => ((PassiveTreeView)d).Rebuild()));

    public static readonly DependencyProperty AscendancyOnlyProperty = DependencyProperty.Register(
        nameof(AscendancyOnly), typeof(bool), typeof(PassiveTreeView), new FrameworkPropertyMetadata(false, (d, _) => ((PassiveTreeView)d).Rebuild()));

    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art), typeof(PassiveTreeArt), typeof(PassiveTreeView), new FrameworkPropertyMetadata(null, (d, _) => ((PassiveTreeView)d).Rebuild()));

    public static readonly DependencyProperty HoveredProperty = DependencyProperty.Register(
        nameof(Hovered), typeof(TreeNode), typeof(PassiveTreeView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register(
        nameof(Selected), typeof(TreeNode), typeof(PassiveTreeView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((PassiveTreeView)d).OnSelectedChanged((TreeNode?)e.OldValue, (TreeNode?)e.NewValue)));

    /// <summary>The art's own resolution: one sheet pixel per screen pixel. Closer only blurs it.</summary>
    private const double MaxScale = 0.5;

    private const double Padding = 300;
    private const double TileSize = 2048;

    /// <summary>A press that moves further than this is a drag, not a click.</summary>
    private const double DragDistance = 4;

    /// <summary>The whole-tree picture is this many pixels on its long side; it stands in for the tiles
    /// while one tree unit is smaller than a pixel by this much again.</summary>
    private const int OverviewPixels = 1600;

    private static readonly Brush Ground = Frozen(0x0B, 0x0A, 0x08);
    private static readonly Brush Line = Frozen(0x4A, 0x41, 0x30);
    private static readonly Brush Taken = Frozen(0xD6, 0xB2, 0x60);
    private static readonly Brush NodeFill = Frozen(0x1F, 0x1B, 0x15);
    private static readonly Brush NodeEdge = Frozen(0x76, 0x6A, 0x54);
    private static readonly Brush JewelEdge = Frozen(0x6C, 0x8F, 0xA8);
    private static readonly Brush TakenFill = Frozen(0xE3, 0xC4, 0x7A);
    private static readonly Brush HoverEdge = Frozen(0xF2, 0xEB, 0xDA);
    private static readonly Brush SelectedEdge = Frozen(0xFF, 0xF4, 0xD0);
    private static readonly Brush SelectedGlow = Frozen(0xD6, 0xB2, 0x60, 0x70);

    private readonly MatrixTransform _view = new();
    private Scene? _scene;
    private double _scale = 1;
    private Vector _offset;
    private bool _moved;
    private Point? _down;
    private Point _last;
    private bool _dragging;
    private PassiveTreeViewPeer? _peer;

    public PassiveTreeView()
    {
        Focusable = true;
        FocusVisualStyle = null; // the ring is drawn in OnRender, inside the drawing's edge
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        AutomationProperties.SetHelpText(this, "Arrow keys move through the build's points, Escape clears the choice, plus and minus zoom.");
        CommandBindings.Add(new CommandBinding(ZoomInCommand, (_, _) => ZoomAt(Centre, 1.5), CanRun));
        CommandBindings.Add(new CommandBinding(ZoomOutCommand, (_, _) => ZoomAt(Centre, 1 / 1.5), CanRun));
        CommandBindings.Add(new CommandBinding(FitBuildCommand, (_, _) => Fit(_scene?.Build ?? Rect.Empty, byPlayer: false), CanRun));
        CommandBindings.Add(new CommandBinding(ShowAllCommand, (_, _) => Fit(_scene?.All ?? Rect.Empty, byPlayer: true), CanRun));
        CommandBindings.Add(new CommandBinding(NextPointCommand, (_, _) => Step(1), CanStep));
        CommandBindings.Add(new CommandBinding(PreviousPointCommand, (_, _) => Step(-1), CanStep));
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

    /// <summary>The export's pictures. Null draws plain shapes.</summary>
    public PassiveTreeArt? Art
    {
        get => (PassiveTreeArt?)GetValue(ArtProperty);
        set => SetValue(ArtProperty, value);
    }

    /// <summary>The node under the mouse.</summary>
    public TreeNode? Hovered
    {
        get => (TreeNode?)GetValue(HoveredProperty);
        set => SetValue(HoveredProperty, value);
    }

    /// <summary>The node the player clicked or stepped to; it stays marked until cleared.</summary>
    public TreeNode? Selected
    {
        get => (TreeNode?)GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    /// <summary>The build's points in this drawing, in path order from the start: what the arrow keys
    /// step through.</summary>
    public IReadOnlyList<TreeNode> Points => _scene?.Order ?? [];

    /// <summary>Screen pixels per tree unit, for tests and the snapshots.</summary>
    public double Scale => _scale;

    /// <summary>Whether the art is drawn tile by tile (false: one whole-tree picture, zoomed far out).</summary>
    public bool DrawsTiles => _scene is null || !UseOverview(_scene);

    private Point Centre => new(ActualWidth / 2, ActualHeight / 2);

    private double MinScale => _scene is null ? 0.001 : FitScale(Pad(_scene.All)) * 0.8;

    // ---- Pointer and keys: the handlers pass positions on, so tests can drive the same logic ----

    internal void PointerDown(Point at)
    {
        _down = at;
        _last = at;
        _dragging = false;
    }

    internal void PointerMove(Point at, bool pressed)
    {
        if (_down is { } from && pressed)
        {
            if (!_dragging && (at - from).Length > DragDistance)
            {
                _dragging = true;
            }

            if (_dragging)
            {
                _offset += at - _last;
                _last = at;
                _moved = true;
                Apply();
            }

            return;
        }

        var hit = NodeAt(at);
        if (!ReferenceEquals(hit, Hovered))
        {
            SetCurrentValue(HoveredProperty, hit);
        }
    }

    internal void PointerUp(Point at)
    {
        if (_down is not null && !_dragging)
        {
            SetCurrentValue(SelectedProperty, NodeAt(at));
        }

        _down = null;
        _dragging = false;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        PointerDown(e.GetPosition(this));
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        PointerMove(e.GetPosition(this), e.LeftButton == MouseButtonState.Pressed);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        PointerUp(e.GetPosition(this));
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        (_down, _dragging) = (null, false);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_down is null)
        {
            SetCurrentValue(HoveredProperty, null);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_scene is not null)
        {
            ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.25 : 0.8);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        e.Handled = Press(e.Key);
    }

    /// <summary>What a key does while the drawing has the focus; true when it did something.</summary>
    internal bool Press(Key key) =>
        key switch
        {
            Key.Right or Key.Down => Step(1),
            Key.Left or Key.Up => Step(-1),
            Key.Home => Jump(first: true),
            Key.End => Jump(first: false),
            Key.Escape when Selected is not null => Clear(),
            Key.Add or Key.OemPlus => Zoomed(1.5),
            Key.Subtract or Key.OemMinus => Zoomed(1 / 1.5),
            _ => false,
        };

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => _peer = new PassiveTreeViewPeer(this);

    /// <summary>Moves the selection along the build's points; from nothing, to the first (or last).</summary>
    internal bool Step(int by)
    {
        var order = Points;
        if (order.Count == 0)
        {
            return false;
        }

        var at = Selected is { } s ? IndexOf(order, s) : -1;
        var next = at < 0 ? (by > 0 ? 0 : order.Count - 1) : Math.Clamp(at + by, 0, order.Count - 1);
        Choose(order[next]);
        return true;
    }

    private bool Jump(bool first)
    {
        if (Points.Count == 0)
        {
            return false;
        }

        Choose(first ? Points[0] : Points[^1]);
        return true;
    }

    private bool Clear()
    {
        SetCurrentValue(SelectedProperty, null);
        return true;
    }

    private bool Zoomed(double factor)
    {
        ZoomAt(Centre, factor);
        return true;
    }

    private void Choose(TreeNode node)
    {
        SetCurrentValue(SelectedProperty, node);
        ShowNode(node);
    }

    private static int IndexOf(IReadOnlyList<TreeNode> order, TreeNode node)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i].Id == node.Id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Brings a node into view when it is off screen or near the edge, keeping the zoom.</summary>
    private void ShowNode(TreeNode node)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var p = ToScreen(new Point(node.X, node.Y));
        var inner = new Rect(ActualWidth * 0.15, ActualHeight * 0.15, ActualWidth * 0.7, ActualHeight * 0.7);
        if (!inner.Contains(p))
        {
            _offset += Centre - p;
            _moved = true;
            Apply();
        }
    }

    private void OnSelectedChanged(TreeNode? old, TreeNode? now)
    {
        var said = now is null ? "" : Describe(now);
        AutomationProperties.SetItemStatus(this, said);
        _peer?.RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty, old is null ? "" : Describe(old), said);
    }

    /// <summary>The selected node as a screen reader says it.</summary>
    internal string Describe(TreeNode n)
    {
        var taken = Allocation?.Nodes.Contains(n.Id) == true ? "taken" : "not taken";
        var name = n.Name.Length > 0 ? n.Name : n.Kind.ToString();
        var stats = n.Stats.Count > 0 ? ": " + string.Join("; ", n.Stats).Replace("\n", " ", StringComparison.Ordinal) : "";
        return string.Create(CultureInfo.InvariantCulture, $"{name}, {Kind(n.Kind)}, {taken}{stats}");
    }

    private static string Kind(TreeNodeKind kind) => kind switch
    {
        TreeNodeKind.Keystone => "keystone",
        TreeNodeKind.Notable => "notable",
        TreeNodeKind.Jewel => "jewel socket",
        TreeNodeKind.Attribute => "attribute",
        TreeNodeKind.AscendancyStart => "ascendancy start",
        TreeNodeKind.Start => "class start",
        TreeNodeKind.Mastery => "mastery",
        _ => "passive",
    };

    // ---- Drawing ----

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Rect(RenderSize);
        dc.DrawRectangle(Ground, null, size);
        if (_scene is { } scene)
        {
            dc.PushTransform(_view);
            if (scene.GroundBrush is { } ground)
            {
                dc.DrawRectangle(ground, null, scene.GroundArea);
            }

            dc.Pop();
            if (UseOverview(scene) && Overview(scene) is { } picture)
            {
                dc.DrawImage(picture, new Rect(ToScreen(scene.OverviewArea.TopLeft), ToScreen(scene.OverviewArea.BottomRight)));
            }
            else
            {
                var view = new Rect(FromScreen(size.TopLeft), FromScreen(size.BottomRight));
                dc.PushTransform(_view);
                foreach (var layer in scene.Layers)
                {
                    foreach (var tile in layer)
                    {
                        if (tile.Bounds.IntersectsWith(view))
                        {
                            dc.DrawDrawing(tile.Drawing);
                        }
                    }
                }

                dc.Pop();
            }

            if (Hovered is { } h && scene.Shows(h) && !ReferenceEquals(h, Selected))
            {
                Ring(dc, h, HoverEdge, 1.5, 3);
            }

            if (Selected is { } s && scene.Shows(s))
            {
                var p = ToScreen(new Point(s.X, s.Y));
                var r = Math.Max(Radius(s) * _scale, 6) + 5;
                dc.DrawEllipse(null, new Pen(SelectedGlow, 7), p, r, r);
                dc.DrawEllipse(null, new Pen(SelectedEdge, 2.5), p, r, r);
            }
        }

        // Keyboard focus: a 2 px ring in the text colour inside the drawing's edge, as other controls have.
        if (IsKeyboardFocused)
        {
            var ring = TryFindResource("Text") as Brush ?? HoverEdge;
            dc.DrawRectangle(null, new Pen(ring, 2), new Rect(1, 1, Math.Max(ActualWidth - 2, 0), Math.Max(ActualHeight - 2, 0)));
        }
    }

    private void Ring(DrawingContext dc, TreeNode n, Brush brush, double thickness, double gap)
    {
        var p = ToScreen(new Point(n.X, n.Y));
        var r = Math.Max(Radius(n) * _scale, 4) + gap;
        dc.DrawEllipse(null, new Pen(brush, thickness), p, r, r);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (!_moved && _scene is not null)
        {
            Fit(_scene.Build, byPlayer: false);
        }
    }

    private bool UseOverview(Scene scene) => scene.Large && _scale < scene.OverviewScale * 1.25;

    /// <summary>The whole tree as one picture (without the ground), made the first time the player zooms far out.</summary>
    private BitmapSource? Overview(Scene scene)
    {
        if (scene.OverviewPicture is not null || scene.Layers.Length == 0)
        {
            return scene.OverviewPicture;
        }

        var area = scene.OverviewArea;
        var k = scene.OverviewScale;
        var width = Math.Max(1, (int)Math.Ceiling(area.Width * k));
        var height = Math.Max(1, (int)Math.Ceiling(area.Height * k));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Transparent where there is no tree: the live ground shows through, so no edge is seen.
            dc.PushTransform(new MatrixTransform(k, 0, 0, k, -area.X * k, -area.Y * k));
            foreach (var tile in scene.Layers.SelectMany(l => l))
            {
                dc.DrawDrawing(tile.Drawing);
            }

            dc.Pop();
        }

        var picture = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        picture.Render(visual);
        picture.Freeze();
        scene.OverviewPicture = picture;
        return picture;
    }

    private void Rebuild()
    {
        _scene = null;
        _moved = false;
        if (Allocation is { } a && !a.Tree.IsEmpty && (!AscendancyOnly || a.Ascendancy is not null))
        {
            _scene = Scene.Create(a, AscendancyOnly ? a.Ascendancy!.Id : null, Art);
        }

        Fit(_scene?.Build ?? Rect.Empty, byPlayer: false);
        CommandManager.InvalidateRequerySuggested();

        // Make the whole-tree picture while nothing else is going on, so the first zoom out is smooth.
        if (_scene is { Large: true } scene)
        {
            Dispatcher.BeginInvoke(() => Overview(scene), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    private void CanRun(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _scene is not null;

    private void CanStep(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Points.Count > 0;

    /// <summary>The node within reach of a point on screen, if any.</summary>
    internal TreeNode? NodeAt(Point screen)
    {
        if (_scene is null)
        {
            return null;
        }

        var p = FromScreen(screen);
        TreeNode? best = null;
        var bestDistance = double.MaxValue;
        foreach (var n in _scene.Pickable)
        {
            var d = Math.Sqrt(((n.X - p.X) * (n.X - p.X)) + ((n.Y - p.Y) * (n.Y - p.Y)));
            var reach = Math.Max(Radius(n), 8 / _scale);
            if (d < reach && d < bestDistance)
            {
                (best, bestDistance) = (n, d);
            }
        }

        return best;
    }

    /// <summary>A node's reach in tree units: half its frame in the export's art.</summary>
    internal static double Radius(TreeNode n) => n.Kind switch
    {
        TreeNodeKind.Keystone => 109,
        TreeNodeKind.Notable when n.Ascendancy is not null => 103,
        TreeNodeKind.Notable or TreeNodeKind.Jewel => 76,
        TreeNodeKind.Start => 150,
        TreeNodeKind.AscendancyStart => 45,
        TreeNodeKind.Mastery => 40,
        _ when n.Ascendancy is not null => 80,
        _ => 51,
    };

    private void ZoomAt(Point screen, double factor)
    {
        var scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        var tree = FromScreen(screen);
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

    /// <summary>Where a node is drawn, in this control's pixels.</summary>
    internal Point ScreenOf(TreeNode n) => ToScreen(new Point(n.X, n.Y));

    private Point ToScreen(Point tree) => new((tree.X * _scale) + _offset.X, (tree.Y * _scale) + _offset.Y);

    private Point FromScreen(Point screen) => new((screen.X - _offset.X) / _scale, (screen.Y - _offset.Y) / _scale);

    private static SolidColorBrush Frozen(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private sealed record Tile(Rect Bounds, Drawing Drawing);

    /// <summary>Everything static about one build's drawing: frozen tiles per layer, built once.</summary>
    private sealed class Scene
    {
        private readonly HashSet<int> _shown;

        private Scene(List<TreeNode> shown, Rect all, Rect build, List<Tile>[] layers, IReadOnlyList<TreeNode> order, Brush? ground)
        {
            _shown = shown.Select(n => n.Id).ToHashSet();
            Pickable = shown.Where(n => n.Kind is not (TreeNodeKind.Mastery or TreeNodeKind.Start) && !n.IsWaypoint).ToList();
            All = all;
            Build = build;
            Layers = layers;
            Order = order;
            GroundBrush = ground;
            GroundArea = Rect.Inflate(all, 40000, 40000);
            Large = shown.Count > 400;
            var area = Rect.Inflate(all, Padding, Padding);
            OverviewArea = area;
            OverviewScale = OverviewPixels / Math.Max(area.Width, area.Height);
        }

        public IReadOnlyList<TreeNode> Pickable { get; }

        public Rect All { get; }

        public Rect Build { get; }

        public List<Tile>[] Layers { get; }

        public IReadOnlyList<TreeNode> Order { get; }

        public Brush? GroundBrush { get; }

        public Rect GroundArea { get; }

        /// <summary>Big enough that drawing every tile far out would be slow.</summary>
        public bool Large { get; }

        public Rect OverviewArea { get; }

        public double OverviewScale { get; }

        public BitmapSource? OverviewPicture { get; set; }

        public bool Shows(TreeNode n) => _shown.Contains(n.Id);

        public static Scene Create(TreeAllocation a, string? ascendancy, PassiveTreeArt? art)
        {
            var tree = a.Tree;
            var shown = tree.Nodes.Values.Where(n => n.Ascendancy == ascendancy).ToList();
            var ids = shown.Select(n => n.Id).ToHashSet();
            var edges = tree.Edges.Where(e => ids.Contains(e.From) && ids.Contains(e.To)).ToList();
            var all = Rect.Empty;
            var build = Rect.Empty;
            foreach (var n in shown)
            {
                var r = Radius(n);
                var area = new Rect(n.X - r, n.Y - r, 2 * r, 2 * r);
                all.Union(area);
                if (a.Nodes.Contains(n.Id))
                {
                    build.Union(area);
                }
            }

            // An ascendancy is small: show all of it, so the choices left are in view too.
            if (build.IsEmpty || ascendancy is not null)
            {
                build = all;
            }

            var near = Neighbours(edges, a);
            var layers = art is null ? Shapes(tree, shown, edges, a) : Pictures(art, tree, shown, edges, a, near, ascendancy is null);
            var ground = art?.Get("background:Background2") is { } tile ? Tiled(tile) : null;
            return new Scene(shown, all, build, layers, PathOrder(shown, edges, a), ground);
        }

        /// <summary>Unallocated nodes next to the build: the ones the game shows as "can allocate".</summary>
        private static HashSet<int> Neighbours(List<TreeEdge> edges, TreeAllocation a)
        {
            var near = new HashSet<int>();
            foreach (var e in edges)
            {
                if (a.Nodes.Contains(e.From) && !a.Nodes.Contains(e.To))
                {
                    near.Add(e.To);
                }
                else if (a.Nodes.Contains(e.To) && !a.Nodes.Contains(e.From))
                {
                    near.Add(e.From);
                }
            }

            return near;
        }

        /// <summary>The build's points from its start outwards along its own connections, the rest by id.</summary>
        private static IReadOnlyList<TreeNode> PathOrder(List<TreeNode> shown, List<TreeEdge> edges, TreeAllocation a)
        {
            var taken = shown.Where(n => a.Nodes.Contains(n.Id)).ToDictionary(n => n.Id);
            var links = new Dictionary<int, List<int>>();
            foreach (var e in edges.Where(a.Takes))
            {
                (links.TryGetValue(e.From, out var f) ? f : links[e.From] = []).Add(e.To);
                (links.TryGetValue(e.To, out var t) ? t : links[e.To] = []).Add(e.From);
            }

            var order = new List<TreeNode>();
            var seen = new HashSet<int>();
            var queue = new Queue<int>(taken.Values.Where(n => !n.IsPoint).OrderBy(n => n.Id).Select(n => n.Id));
            foreach (var id in queue)
            {
                seen.Add(id);
            }

            void Drain()
            {
                while (queue.Count > 0)
                {
                    var id = queue.Dequeue();
                    if (taken[id].IsPoint && !taken[id].IsWaypoint)
                    {
                        order.Add(taken[id]);
                    }

                    foreach (var next in (links.TryGetValue(id, out var l) ? l : []).Order())
                    {
                        if (taken.ContainsKey(next) && seen.Add(next))
                        {
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            Drain();
            foreach (var n in taken.Values.OrderBy(n => n.Id).Where(n => n.IsPoint && seen.Add(n.Id)))
            {
                queue.Enqueue(n.Id);
                Drain();
            }

            return order;
        }

        private static ImageBrush Tiled(TreeSprite tile)
        {
            var brush = new ImageBrush(tile.Image)
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, tile.Width, tile.Height),
                Stretch = Stretch.Fill,
            };
            brush.Freeze();
            return brush;
        }

        /// <summary>The game's look: centre circle, mastery patterns, connectors, then nodes.</summary>
        private static List<Tile>[] Pictures(PassiveTreeArt art, PassiveTree tree, List<TreeNode> shown, List<TreeEdge> edges, TreeAllocation a, HashSet<int> near, bool main)
        {
            var back = new TileSet();
            var lines = new TileSet();
            var nodes = new TileSet();

            if (main && art.Get("startNode:MainCircle") is { } circle)
            {
                back.Add(new Point(0, 0), Centred(new Point(0, 0), circle), dc => dc.DrawImage(circle.Image, Centred(new Point(0, 0), circle)));
            }

            foreach (var m in shown.Where(n => n.Kind == TreeNodeKind.Mastery && n.Pattern is not null))
            {
                var lit = edges.Any(e => (e.From == m.Id && a.Nodes.Contains(e.To)) || (e.To == m.Id && a.Nodes.Contains(e.From)));
                if (art.Get((lit ? "masteryEffectActive:" : "masteryEffectInactive:") + m.Pattern) is { } pattern)
                {
                    var at = new Point(m.X, m.Y);
                    back.Add(at, Centred(at, pattern), dc => dc.DrawImage(pattern.Image, Centred(at, pattern)));
                }
            }

            foreach (var e in edges)
            {
                var from = tree.Nodes[e.From];
                var to = tree.Nodes[e.To];
                var state = a.Takes(e) ? "Active" : a.Nodes.Contains(e.From) || a.Nodes.Contains(e.To) ? "Intermediate" : "Normal";
                if (e.IsArc && e.Orbit is { } orbit && art.Get($"line:Orbit{orbit + 1}{state}") is { } arc)
                {
                    Arc(lines, arc, InnerEdge(art, state, arc.Width), from, to, new Point(e.CentreX!.Value, e.CentreY!.Value));
                }
                else if (art.Get($"line:LineConnector{state}") is { } line)
                {
                    Straight(lines, line, from, to);
                }
            }

            foreach (var n in shown.Where(n => n.Kind is not (TreeNodeKind.Mastery or TreeNodeKind.Start) && !n.IsWaypoint))
            {
                var taken = a.Nodes.Contains(n.Id);
                var at = new Point(n.X, n.Y);
                var parts = Parts(n, taken, near.Contains(n.Id)).Select(art.Get).OfType<TreeSprite>().ToList();
                if (parts.Count > 0)
                {
                    var bounds = parts.Select(p => Centred(at, p)).Aggregate(Rect.Union);
                    nodes.Add(at, bounds, dc =>
                    {
                        foreach (var p in parts)
                        {
                            dc.DrawImage(p.Image, Centred(at, p));
                        }
                    });
                }
            }

            return [back.Close(), lines.Close(), nodes.Close()];
        }

        /// <summary>A node's pictures, bottom to top: ascendancy backing, icon, frame.</summary>
        private static IEnumerable<string> Parts(TreeNode n, bool taken, bool canTake)
        {
            var state = taken ? "Allocated" : canTake ? "CanAllocate" : "Unallocated";
            var icon = taken ? "Active" : "Inactive";
            switch (n.Kind)
            {
                case TreeNodeKind.AscendancyStart:
                    yield return "frame:AscendancyStartNode";
                    yield break;
                case TreeNodeKind.Jewel:
                    yield return $"frame:JewelFrame{state}";
                    yield break;
                case TreeNodeKind.Keystone:
                    yield return $"keystone{icon}:{n.Icon}";
                    yield return $"frame:KeystoneFrame{state}";
                    yield break;
                case TreeNodeKind.Notable when n.Ascendancy is not null:
                    yield return "frame:AscendancyFrameNotableBacking";
                    yield return $"notable{icon}:{n.Icon}";
                    yield return $"frame:AscendancyFrameNotable{state}";
                    yield break;
                case TreeNodeKind.Notable:
                    yield return $"notable{icon}:{n.Icon}";
                    yield return n.Blighted ? $"frame:BlightedNotableFrame{state}" : $"frame:NotableFrame{state}";
                    yield break;
                default:
                    if (n.Ascendancy is not null)
                    {
                        yield return "frame:AscendancyFrameNormalBacking";
                        yield return $"normal{icon}:{n.Icon}";
                        yield return $"frame:AscendancyFrameNormal{state}";
                    }
                    else
                    {
                        yield return $"normal{icon}:{n.Icon}";
                        yield return taken ? "frame:PSSkillFrameActive" : canTake ? "frame:PSSkillFrameHighlighted" : "frame:PSSkillFrame";
                    }

                    yield break;
            }
        }

        /// <summary>A straight connector: the export's strip, stretched from one node to the other.</summary>
        private static void Straight(TileSet set, TreeSprite strip, TreeNode from, TreeNode to)
        {
            var a = new Point(from.X, from.Y);
            var b = new Point(to.X, to.Y);
            var length = (b - a).Length;
            if (length < 1)
            {
                return;
            }

            var m = Matrix.Identity;
            m.Rotate(Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI);
            m.Translate(a.X, a.Y);
            var place = new MatrixTransform(m);
            place.Freeze();
            var rect = new Rect(0, -strip.Height / 2, length, strip.Height);
            set.Add(new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), place.TransformBounds(rect), dc =>
            {
                dc.PushTransform(place);
                dc.DrawImage(strip.Image, rect);
                dc.Pop();
            });
        }

        /// <summary>The export keeps all orbit sprites of a state in one corner of its sheet, nested: a big
        /// orbit's picture holds the smaller orbits' arcs too. Everything inside the next smaller sprite is
        /// theirs, so an arc is cut to the ring between that and its own size.</summary>
        private static double InnerEdge(PassiveTreeArt art, string state, double size)
        {
            var inner = 0.0;
            for (var k = 1; k <= 12; k++)
            {
                if (art.Get($"line:Orbit{k}{state}") is { } other && other.Width < size - 1e-6)
                {
                    inner = Math.Max(inner, other.Width);
                }
            }

            return inner;
        }

        /// <summary>An orbit arc. The export's orbit sprite is a quarter circle whose bottom-right corner is
        /// the orbit's centre; it is turned to where the arc starts and cut to the arc, a quarter at a time.</summary>
        private static void Arc(TileSet set, TreeSprite quarter, double inner, TreeNode from, TreeNode to, Point c)
        {
            var start = Math.Atan2(from.Y - c.Y, from.X - c.X);
            var span = Math.Atan2(to.Y - c.Y, to.X - c.X) - start;
            span = Math.IEEERemainder(span, 2 * Math.PI);
            if (span < 0)
            {
                start += span;
                span = -span;
            }

            var size = quarter.Width;
            var image = new Rect(c.X - size, c.Y - size, size, size);
            var reach = size * 1.01;
            var bounds = new Rect(c.X - size, c.Y - size, 2 * size, 2 * size);
            while (span > 1e-6)
            {
                var piece = Math.Min(span, Math.PI / 2);
                var turn = new RotateTransform(((start - Math.PI) * 180 / Math.PI), c.X, c.Y);
                turn.Freeze();
                var cut = Ring(c, inner, reach, start, start + piece);
                set.Add(new Point(c.X + (Math.Cos(start + (piece / 2)) * size * 0.7), c.Y + (Math.Sin(start + (piece / 2)) * size * 0.7)), bounds, dc =>
                {
                    dc.PushClip(cut);
                    dc.PushTransform(turn);
                    dc.DrawImage(quarter.Image, image);
                    dc.Pop();
                    dc.Pop();
                });
                start += piece;
                span -= piece;
            }
        }

        /// <summary>The part of a ring between two angles (angles grow clockwise on screen).</summary>
        private static Geometry Ring(Point c, double inner, double outer, double from, double to)
        {
            Point At(double r, double angle) => new(c.X + (r * Math.Cos(angle)), c.Y + (r * Math.Sin(angle)));
            var large = to - from > Math.PI;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(inner > 0 ? At(inner, from) : c, true, true);
                ctx.LineTo(At(outer, from), false, false);
                ctx.ArcTo(At(outer, to), new Size(outer, outer), 0, large, SweepDirection.Clockwise, false, false);
                if (inner > 0)
                {
                    ctx.LineTo(At(inner, to), false, false);
                    ctx.ArcTo(At(inner, from), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, false, false);
                }
            }

            g.Freeze();
            return g;
        }

        private static Rect Centred(Point at, TreeSprite s) => new(at.X - (s.Width / 2), at.Y - (s.Height / 2), s.Width, s.Height);

        /// <summary>Without the art: lines and dots in the game's colours.</summary>
        private static List<Tile>[] Shapes(PassiveTree tree, List<TreeNode> shown, List<TreeEdge> edges, TreeAllocation a)
        {
            var lines = new TileSet();
            var nodes = new TileSet();
            var plain = new Pen(Line, 12);
            var taken = new Pen(Taken, 22);
            plain.Freeze();
            taken.Freeze();
            foreach (var e in edges)
            {
                var p = new Point(tree.Nodes[e.From].X, tree.Nodes[e.From].Y);
                var q = new Point(tree.Nodes[e.To].X, tree.Nodes[e.To].Y);
                var pen = a.Takes(e) ? taken : plain;
                lines.Add(new Point((p.X + q.X) / 2, (p.Y + q.Y) / 2), new Rect(p, q), dc => dc.DrawLine(pen, p, q));
            }

            var edge = new Pen(NodeEdge, 8);
            var jewel = new Pen(JewelEdge, 8);
            edge.Freeze();
            jewel.Freeze();
            foreach (var n in shown.Where(n => n.Kind is not (TreeNodeKind.Mastery or TreeNodeKind.Start) && !n.IsWaypoint))
            {
                var at = new Point(n.X, n.Y);
                var r = Radius(n) * 0.6;
                var fill = a.Nodes.Contains(n.Id) ? TakenFill : NodeFill;
                var pen = n.Kind == TreeNodeKind.Jewel ? jewel : edge;
                nodes.Add(at, new Rect(at.X - r, at.Y - r, 2 * r, 2 * r), dc => dc.DrawEllipse(fill, pen, at, r, r));
            }

            return [lines.Close(), nodes.Close()];
        }
    }

    /// <summary>Collects one layer's drawings into tiles of <see cref="TileSize"/> tree units, by where each
    /// drawing's anchor falls; a tile's bounds grow to cover everything in it.</summary>
    private sealed class TileSet
    {
        private readonly Dictionary<(int, int), (DrawingGroup Group, DrawingContext Context, Rect Bounds)> _tiles = [];

        public void Add(Point anchor, Rect bounds, Action<DrawingContext> draw)
        {
            var key = ((int)Math.Floor(anchor.X / TileSize), (int)Math.Floor(anchor.Y / TileSize));
            if (!_tiles.TryGetValue(key, out var tile))
            {
                var group = new DrawingGroup();
                tile = (group, group.Open(), Rect.Empty);
            }

            draw(tile.Context);
            tile.Bounds.Union(bounds);
            _tiles[key] = tile;
        }

        public List<Tile> Close()
        {
            var list = new List<Tile>();
            foreach (var (group, context, bounds) in _tiles.Values)
            {
                context.Close();
                group.Freeze();
                list.Add(new Tile(bounds, group));
            }

            return list;
        }
    }
}

/// <summary>Says the drawing is the passive tree and, as its item status, which node is selected.</summary>
public sealed class PassiveTreeViewPeer(PassiveTreeView owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

    protected override string GetLocalizedControlTypeCore() => "passive tree";

    protected override string GetClassNameCore() => "PassiveTree";

    protected override bool IsKeyboardFocusableCore() => true;
}
