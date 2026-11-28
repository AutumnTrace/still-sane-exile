using System.Windows;
using System.Windows.Media;

namespace StillSaneExile.Desktop.Views;

/// <summary>A roll drawn like a slider: a rail from the lowest to the highest number the tier rolls, with
/// end stops, and a marker where this roll sits. When which end is better is known, the rail fills up to
/// the marker from the worse end. The rail is in the empty tick box's colour, which keeps 3:1 against the
/// panels; the marker stands taller than the rail so it reads on the filled part too.</summary>
public sealed class RollBar : FrameworkElement
{
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(RollBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScoredProperty = DependencyProperty.Register(
        nameof(Scored), typeof(bool), typeof(RollBar), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double Rail = 3;
    private const double Marker = 3;
    private const double Stop = 1;

    public RollBar()
    {
        Height = 10;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    /// <summary>0 to 1, from the left end.</summary>
    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    /// <summary>Fill the rail up to the marker: the left end is the worse one.</summary>
    public bool Scored
    {
        get => (bool)GetValue(ScoredProperty);
        set => SetValue(ScoredProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < Marker * 2 || h <= 0)
        {
            return;
        }

        var rail = Find("Unchecked");
        var y = Math.Round((h - Rail) / 2);
        dc.DrawRectangle(rail, null, new Rect(0, y, w, Rail));
        dc.DrawRectangle(rail, null, new Rect(0, y - 2, Stop, Rail + 4));
        dc.DrawRectangle(rail, null, new Rect(w - Stop, y - 2, Stop, Rail + 4));

        var x = Math.Round(Math.Clamp(Position, 0, 1) * (w - Marker));
        if (Scored)
        {
            dc.DrawRectangle(Find("BarFill"), null, new Rect(0, y, x + Marker / 2, Rail));
        }

        dc.DrawRectangle(Find("Text"), null, new Rect(x, 0, Marker, h));
    }

    private Brush Find(string key) => TryFindResource(key) as Brush ?? Brushes.White;
}
