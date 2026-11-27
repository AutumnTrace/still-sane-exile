using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace StillSaneExile.Desktop.Views;

/// <summary>What the HUD needs from the main window, so its decisions can be tested with a fake: the
/// UI thread's dispatcher, a window to place the card against when the game's is unknown, and a way
/// to bring the window to the front.</summary>
public interface IShellWindow
{
    Dispatcher Dispatcher { get; }

    nint Handle { get; }

    void BringToFront();
}

public partial class MainWindow : Window, IShellWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public nint Handle => new WindowInteropHelper(this).Handle;

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
    }
}
