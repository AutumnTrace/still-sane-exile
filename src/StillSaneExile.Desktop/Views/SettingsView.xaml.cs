using System.Windows.Controls;
using System.Windows.Input;
using StillSaneExile.Desktop.ViewModels;

namespace StillSaneExile.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.Recording is not null)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            e.Handled = vm.Record(key, Keyboard.Modifiers);
        }
    }
}
