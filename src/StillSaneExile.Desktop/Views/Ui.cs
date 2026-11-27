using System.Windows;
using System.Windows.Media;

namespace StillSaneExile.Desktop.Views;

/// <summary>Attached settings the shared control templates in App.xaml read, so one template serves
/// buttons with different corner radii and text boxes with different focus borders.</summary>
public static class Ui
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(6)));

    /// <summary>A text box's border while it has keyboard focus.</summary>
    public static readonly DependencyProperty FocusBrushProperty = DependencyProperty.RegisterAttached(
        "FocusBrush", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static CornerRadius GetRadius(DependencyObject element) => (CornerRadius)element.GetValue(RadiusProperty);

    public static void SetRadius(DependencyObject element, CornerRadius value) => element.SetValue(RadiusProperty, value);

    public static Brush? GetFocusBrush(DependencyObject element) => (Brush?)element.GetValue(FocusBrushProperty);

    public static void SetFocusBrush(DependencyObject element, Brush? value) => element.SetValue(FocusBrushProperty, value);
}
