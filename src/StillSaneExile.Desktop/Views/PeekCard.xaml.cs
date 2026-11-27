using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StillSaneExile.Desktop.ViewModels;

namespace StillSaneExile.Desktop.Views;

/// <summary>The quick peek card. Shared by the HUD window and onboarding's "How to read a price check",
/// where numbered pins mark each part of the card.</summary>
public partial class PeekCard : UserControl
{
    private const double PinSize = 22;
    private const double PinGutter = 12;

    public PeekCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Dispatcher.BeginInvoke(PlacePins, System.Windows.Threading.DispatcherPriority.Loaded);
        Loaded += (_, _) => PlacePins();
        SizeChanged += (_, _) => PlacePins();
    }

    private bool _placing;

    private void PlacePins()
    {
        if (_placing)
        {
            return;
        }

        _placing = true;
        try
        {
            PlacePinsCore();
        }
        finally
        {
            _placing = false;
        }
    }

    private void PlacePinsCore()
    {
        var show = DataContext is PeekViewModel { ShowPins: true, IsMessage: false };
        Pins.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Card.Margin = show ? new Thickness(PinGutter, 0, PinGutter, 0) : new Thickness(0);
        Pins.Children.Clear();
        if (!show || !IsLoaded)
        {
            return;
        }

        UpdateLayout();
        var firstRow = RowsList.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
        // Pins straddle the card's left and right edges, clear of the text inside.
        var left = PinGutter - PinSize / 2;
        var right = PinGutter + Card.ActualWidth - PinSize / 2;
        Pin(1, NameText, left);
        Pin(2, BigText, left);
        Pin(3, CountBlock, right);
        Pin(4, SearchedWith, left);
        Pin(5, firstRow, left);
        Pin(6, ChangeHint, right);
    }

    /// <summary>Puts pin <paramref name="n"/> level with the middle of <paramref name="target"/>.</summary>
    private void Pin(int n, FrameworkElement? target, double x)
    {
        if (target is null || !target.IsVisible || target.ActualHeight <= 0)
        {
            return;
        }

        var top = target.TransformToAncestor(Root).Transform(new Point(0, target.ActualHeight / 2)).Y - PinSize / 2;
        var pin = new Border
        {
            Style = (Style)FindResource("GuidePin"),
            Child = new TextBlock { Text = n.ToString(System.Globalization.CultureInfo.InvariantCulture), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Effect = null,
        };
        pin.BorderBrush = (Brush)FindResource("Surface2");
        pin.BorderThickness = new Thickness(3);
        pin.Width = pin.Height = PinSize + 6;
        pin.CornerRadius = new CornerRadius((PinSize + 6) / 2);
        Canvas.SetLeft(pin, x - 3);
        Canvas.SetTop(pin, top - 3);
        Pins.Children.Add(pin);
    }
}
