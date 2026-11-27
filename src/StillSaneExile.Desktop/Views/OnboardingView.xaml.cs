using System.Windows.Controls;

namespace StillSaneExile.Desktop.Views;

/// <summary>First start: profile, display currency and keys, then how to read a price check.
/// DataContext: the SettingsViewModel, which also records key changes for it.</summary>
public partial class OnboardingView : UserControl
{
    public OnboardingView()
    {
        InitializeComponent();
    }
}
