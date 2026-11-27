using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StillSaneExile.Desktop.Views;

/// <summary>The price check for one item (DataContext: an ItemViewModel). Shown in the middle of the
/// Price check page and, over the game, in the price check window.</summary>
public partial class PriceCheckView : UserControl
{
    /// <summary>When set, a close button appears in the header (the in-game window sets it).</summary>
    public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(
        nameof(CloseCommand), typeof(ICommand), typeof(PriceCheckView));

    public PriceCheckView()
    {
        InitializeComponent();
    }

    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }
}
