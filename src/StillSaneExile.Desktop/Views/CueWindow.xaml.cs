using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Placement;

namespace StillSaneExile.Desktop.Views;

/// <summary>A cue over the game, drawn by SSE and gone by itself: the ring at the cursor when the quick
/// price key is heard, the red ring and its one-line note when nothing was under the cursor, or the line from
/// the cursor to where the card went. Topmost, click-through, never activated and never focusable, like the
/// guide lines. The window covers only the cue, never the whole game, so the game under it stays untouched
/// and drawing it costs next to nothing.</summary>
public partial class CueWindow : Window
{
    /// <summary>Device-independent pixels, as in the design: the ring before it grows, how far it grows, the
    /// dot in its middle, and where the note sits from the cursor.</summary>
    private const double RingSize = 56;
    private const double RingGrowsTo = 1.7;
    private const double DotSize = 14;
    private const double LineDot = 7;
    private static readonly Vector NoteOffset = new(26, 30);

    /// <summary>Half the window around the ring at its largest, with its shade.</summary>
    private static readonly double RingRoom = Math.Ceiling((RingSize * RingGrowsTo / 2) + 4);

    private static readonly IEasingFunction EaseOut = Frozen(new QuadraticEase { EasingMode = EasingMode.EaseOut });

    /// <summary>Counts cues, so one that ends after a newer one began leaves the newer one alone.</summary>
    private int _shown;

    public CueWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Overlay.MakePassive(new WindowInteropHelper(this).Handle);
    }

    /// <summary>The key was heard: a gold ring grows out of the cursor and fades, around a dot.</summary>
    public void ShowRing(ScreenPoint cursor, nint over)
    {
        var id = Begin();
        var (ring, ringScale) = Ring(Brush("Signal"));
        var (dot, dotScale) = Dot(Brush("Signal"), DotSize);
        LayOutRing(ring, dot, RingRoom, RingRoom);
        ShowAt(cursor.X, cursor.Y, over, new Vector(-RingRoom, -RingRoom), Stage.Width, Stage.Height);

        Grow(ringScale, 0.3, RingGrowsTo, PressMotion.RingFor);
        ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.95, 0, PressMotion.RingFor) { EasingFunction = EaseOut });
        Grow(dotScale, 0.6, 1, PressMotion.DotFor);
        var fade = new DoubleAnimation(1, 0, PressMotion.DotFor) { EasingFunction = EaseOut };
        fade.Completed += (_, _) => End(id);
        dot.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Nothing under the cursor: a red ring that shakes once, and a one-line note beside it, both
    /// gone after <see cref="PressMotion.MissFor"/>. With less motion, the note alone fades in and out.</summary>
    public void ShowMiss(ScreenPoint? cursor, nint over, string note, bool motion)
    {
        var id = Begin();
        var self = new WindowInteropHelper(this).EnsureHandle();
        var area = Overlay.AreaOf(over, self);
        var f = Overlay.Factor(over, self);
        var at = cursor is { } c ? new Pos(c.X, c.Y) : area.Centre;

        var box = Note(note);
        var size = box.DesiredSize;
        var left = at.X + ((NoteOffset.X + size.Width) * f) > area.Right ? -NoteOffset.X - size.Width : NoteOffset.X;
        var top = at.Y + ((NoteOffset.Y + size.Height) * f) > area.Bottom ? -NoteOffset.Y - size.Height : NoteOffset.Y;
        var origin = new Vector(Math.Min(left, motion ? -RingRoom : 0), Math.Min(top, motion ? -RingRoom : 0));
        var right = Math.Max(left + size.Width, motion ? RingRoom : 0);
        var bottom = Math.Max(top + size.Height + 6, motion ? RingRoom : 0);

        var keep = Keep(PressMotion.MissFor);
        if (motion)
        {
            var (ring, _) = Ring(Brush("Miss"));
            var (dot, dotScale) = Dot(Brush("Miss"), DotSize);
            LayOutRing(ring, dot, -origin.X, -origin.Y);
            var shake = new TranslateTransform();
            ring.RenderTransform = new TransformGroup { Children = { ring.RenderTransform, shake } };
            shake.BeginAnimation(TranslateTransform.XProperty, Shake());
            ring.BeginAnimation(OpacityProperty, keep);
            Grow(dotScale, 0.6, 1, TimeSpan.FromMilliseconds(600));
            dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600)) { EasingFunction = EaseOut });
        }

        Canvas.SetLeft(box, left - origin.X);
        Canvas.SetTop(box, top - origin.Y);
        Stage.Width = right - origin.X;
        Stage.Height = bottom - origin.Y;
        ShowAt(at.X, at.Y, over, origin, Stage.Width, Stage.Height);

        var appear = NoteFade();
        appear.Completed += (_, _) => End(id);
        box.BeginAnimation(OpacityProperty, appear);

        // A status, as in the design: a screen reader that follows live regions can say it, while the
        // window itself never takes the focus.
        if (box.Child is TextBlock text && UIElementAutomationPeer.CreatePeerForElement(text) is { } peer && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        if (motion)
        {
            var rise = new TranslateTransform();
            box.RenderTransform = rise;
            rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(PressMotion.MissFor.TotalMilliseconds * 0.12)) { EasingFunction = EaseOut });
        }
    }

    /// <summary>A line drawn out from the cursor to where the card went, then faded: it shows where to
    /// look when the card is not right next to the cursor.</summary>
    public void ShowLine(ScreenPoint from, Pos to, nint over)
    {
        var id = Begin();
        var self = new WindowInteropHelper(this).EnsureHandle();
        var f = Overlay.Factor(over, self);
        var room = (LineDot / 2) + 3;
        var origin = new Vector(Math.Min(0, (to.X - from.X) / f) - room, Math.Min(0, (to.Y - from.Y) / f) - room);
        var end = new Point(((to.X - from.X) / f) - origin.X, ((to.Y - from.Y) / f) - origin.Y);
        var start = new Point(-origin.X, -origin.Y);
        var (shade, line) = LayOutLine(start, end);
        Stage.Width = Math.Max(start.X, end.X) + room;
        Stage.Height = Math.Max(start.Y, end.Y) + room;
        ShowAt(from.X, from.Y, over, origin, Stage.Width, Stage.Height);

        var drawn = TimeSpan.FromMilliseconds(PressMotion.LineFor.TotalMilliseconds * PressMotion.LineDrawnShare);
        Draw(shade, drawn);
        Draw(line, drawn);
        var fade = new DoubleAnimationUsingKeyFrames { Duration = PressMotion.LineFor };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(drawn)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(PressMotion.LineFor)));
        fade.Completed += (_, _) => End(id);
        Stage.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Takes the cue away now.</summary>
    public void Clear()
    {
        _shown++;
        Stage.BeginAnimation(OpacityProperty, null);
        Stage.Children.Clear();
        if (IsVisible)
        {
            Hide();
        }
    }

    /// <summary>For the snapshots: the ring as it grows, still, without moving or showing the window.</summary>
    internal void PoseRing()
    {
        Begin();
        var (ring, ringScale) = Ring(Brush("Signal"));
        var (dot, _) = Dot(Brush("Signal"), DotSize);
        LayOutRing(ring, dot, RingRoom, RingRoom);
        ringScale.ScaleX = ringScale.ScaleY = 1.1;
        ring.Opacity = 0.7;
    }

    /// <summary>For the snapshots: the miss at its fullest, ring and note.</summary>
    internal void PoseMiss(string note)
    {
        Begin();
        var box = Note(note);
        var (ring, _) = Ring(Brush("Miss"));
        var (dot, _) = Dot(Brush("Miss"), DotSize);
        LayOutRing(ring, dot, RingRoom, RingRoom);
        dot.Opacity = 0.6;
        Canvas.SetLeft(box, RingRoom + NoteOffset.X);
        Canvas.SetTop(box, RingRoom + NoteOffset.Y);
        Stage.Width = RingRoom + NoteOffset.X + box.DesiredSize.Width + 4;
        Stage.Height = Math.Max(2 * RingRoom, RingRoom + NoteOffset.Y + box.DesiredSize.Height + 6);
    }

    /// <summary>For the snapshots: the line fully drawn, from the top-left to <paramref name="to"/>.</summary>
    internal void PoseLine(Vector to)
    {
        Begin();
        var room = (LineDot / 2) + 3;
        var start = new Point(room, room);
        var end = start + to;
        LayOutLine(start, end);
        Stage.Width = end.X + room;
        Stage.Height = end.Y + room;
    }

    private int Begin()
    {
        _shown++;
        Stage.BeginAnimation(OpacityProperty, null);
        Stage.Opacity = 1;
        Stage.Children.Clear();
        return _shown;
    }

    private void End(int id)
    {
        if (id == _shown)
        {
            Clear();
        }
    }

    /// <summary>Puts the window's <paramref name="origin"/> (device-independent pixels from the anchor) on
    /// the screen at the anchor, a physical pixel position, before it is seen there.</summary>
    private void ShowAt(double x, double y, nint over, Vector origin, double width, double height)
    {
        var self = new WindowInteropHelper(this).EnsureHandle();
        var f = Overlay.Factor(over, self);
        Stage.Width = width;
        Stage.Height = height;
        Opacity = 0;
        Show();
        UpdateLayout();
        Overlay.MoveTo(new WindowInteropHelper(this).Handle, x + (origin.X * f), y + (origin.Y * f));
        Opacity = 1;
    }

    /// <summary>The ring with a dark shade under its stroke, so it shows on snow as on a dark cave floor.</summary>
    private (Grid Ring, ScaleTransform Scale) Ring(Brush stroke)
    {
        var scale = new ScaleTransform(1, 1);
        var ring = new Grid { Width = RingSize, Height = RingSize, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale };
        ring.Children.Add(new Ellipse { Stroke = Brush("CueShade"), StrokeThickness = 6 });
        ring.Children.Add(new Ellipse { Stroke = stroke, StrokeThickness = 3, Margin = new Thickness(1.5) });
        return (ring, scale);
    }

    private (Ellipse Dot, ScaleTransform Scale) Dot(Brush fill, double size)
    {
        var scale = new ScaleTransform(1, 1);
        return (new Ellipse { Width = size, Height = size, Fill = fill, Stroke = Brush("CueShade"), StrokeThickness = 1, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale }, scale);
    }

    private void LayOutRing(FrameworkElement ring, FrameworkElement dot, double x, double y)
    {
        Canvas.SetLeft(ring, x - (RingSize / 2));
        Canvas.SetTop(ring, y - (RingSize / 2));
        Canvas.SetLeft(dot, x - (dot.Width / 2));
        Canvas.SetTop(dot, y - (dot.Height / 2));
        Stage.Children.Add(ring);
        Stage.Children.Add(dot);
        Stage.Width = 2 * RingRoom;
        Stage.Height = 2 * RingRoom;
    }

    private (Line Shade, Line Line) LayOutLine(Point start, Point end)
    {
        Line Make(Brush brush, double thickness) => new()
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeDashCap = PenLineCap.Round,
        };

        var shade = Make(Brush("CueShade"), 5);
        var line = Make(Brush("Signal"), 2);
        var dot = new Ellipse { Width = LineDot, Height = LineDot, Fill = Brush("Signal"), Stroke = Brush("CueShade"), StrokeThickness = 1 };
        Canvas.SetLeft(dot, end.X - (LineDot / 2));
        Canvas.SetTop(dot, end.Y - (LineDot / 2));
        Stage.Children.Add(shade);
        Stage.Children.Add(line);
        Stage.Children.Add(dot);
        return (shade, line);
    }

    /// <summary>The line appears from the cursor outwards: one dash as long as the line, slid in.</summary>
    private static void Draw(Line line, TimeSpan over)
    {
        var length = (new Point(line.X2, line.Y2) - new Point(line.X1, line.Y1)).Length / line.StrokeThickness;
        line.StrokeDashArray = new DoubleCollection { length, length + 2 };
        line.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(length, 0, over) { EasingFunction = EaseOut });
    }

    /// <summary>The note, on the stage already so it measures in the window's own font.</summary>
    private Border Note(string note)
    {
        var box = new Border
        {
            Background = Brush("Panel"),
            BorderBrush = Brush("MissEdge"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 7),
            Child = new TextBlock { Text = note, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brush("Warn") },
        };
        AutomationProperties.SetLiveSetting(box.Child, AutomationLiveSetting.Polite);
        Stage.Children.Add(box);
        box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return box;
    }

    /// <summary>The red ring's small shake, left and right, settling in the middle.</summary>
    private static DoubleAnimationUsingKeyFrames Shake()
    {
        var shake = new DoubleAnimationUsingKeyFrames { Duration = PressMotion.ShakeFor };
        double[] steps = [0, -5, 5, -3, 3, 0];
        for (var i = 0; i < steps.Length; i++)
        {
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(steps[i], KeyTime.FromPercent(i / (double)(steps.Length - 1))));
        }

        return shake;
    }

    /// <summary>The miss stays fully there for most of its time, then fades.</summary>
    private static DoubleAnimationUsingKeyFrames Keep(TimeSpan total)
    {
        var keep = new DoubleAnimationUsingKeyFrames { Duration = total };
        keep.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        keep.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.85)));
        keep.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        return keep;
    }

    /// <summary>The note comes in quickly, stays, and fades with the ring.</summary>
    private static DoubleAnimationUsingKeyFrames NoteFade()
    {
        var fade = new DoubleAnimationUsingKeyFrames { Duration = PressMotion.MissFor };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.12)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.85)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        return fade;
    }

    private static void Grow(ScaleTransform scale, double from, double to, TimeSpan over)
    {
        var grow = new DoubleAnimation(from, to, over) { EasingFunction = EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
