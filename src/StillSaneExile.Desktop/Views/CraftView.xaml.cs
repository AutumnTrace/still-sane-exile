using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StillSaneExile.Desktop.ViewModels;

namespace StillSaneExile.Desktop.Views;

/// <summary>The craft simulator. An orb picked up (Settings, Crafting: like in game) is drawn next to the
/// cursor until it is used on the item or put back, as the game draws currency in hand; right-click or Esc
/// puts it back.</summary>
public partial class CraftView : UserControl
{
    /// <summary>Where the picture sits from the cursor's tip: below and right, clear of the pointer.</summary>
    private static readonly Vector HeldOffset = new(10, 12);

    public CraftView()
    {
        InitializeComponent();
    }

    private CraftViewModel? Bench => DataContext as CraftViewModel;

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Bench is not { IsHolding: true })
        {
            HeldIcon.Visibility = Visibility.Collapsed;
            return;
        }

        var at = e.GetPosition(Root) + HeldOffset;
        Canvas.SetLeft(HeldIcon, at.X);
        Canvas.SetTop(HeldIcon, at.Y);
        HeldIcon.Visibility = Visibility.Visible;
    }

    private void OnMouseLeave(object sender, MouseEventArgs e) => HeldIcon.Visibility = Visibility.Collapsed;

    private void OnRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Bench is { IsHolding: true } bench)
        {
            bench.DropCommand.Execute(null);
            HeldIcon.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Bench is { IsHolding: true } bench)
        {
            bench.DropCommand.Execute(null);
            HeldIcon.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }
}
