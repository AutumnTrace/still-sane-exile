using System.Windows.Controls;

namespace StillSaneExile.Desktop.Views;

/// <summary>A stat's tier and roll bar, under the stat on the card and in the price check. Its data
/// context is a <see cref="ViewModels.RollView"/>.</summary>
public partial class RollLine : UserControl
{
    public RollLine()
    {
        InitializeComponent();
    }
}
