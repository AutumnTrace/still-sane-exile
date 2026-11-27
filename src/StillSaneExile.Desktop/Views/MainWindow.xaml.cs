using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace StillSaneExile.Desktop.Views;

/// <summary>What the HUD needs from the main window, so its decisions can be tested with a fake: the
/// UI thread's dispatcher, a window to place the card against when the game's is unknown, whether the
/// player is typing in SSE, and a way to bring the window to the front.</summary>
public interface IShellWindow
{
    Dispatcher Dispatcher { get; }

    nint Handle { get; }

    /// <summary>True while keyboard focus is in one of SSE's text inputs, in any of its windows.</summary>
    bool IsTyping { get; }

    void BringToFront();
}

public partial class MainWindow : Window, IShellWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Keyboard focus belongs to the UI thread, not to one window, so this also covers the
    /// price check window's number boxes. Read on the UI thread (the hook callback runs there).</summary>
    public bool IsTyping => IsTextInput(Keyboard.FocusedElement);

    /// <summary>Text boxes, rich text boxes, password boxes and editable combo boxes: the places where
    /// Ctrl+C copies the player's own text.</summary>
    public static bool IsTextInput(IInputElement? element) =>
        element is TextBoxBase or PasswordBox or ComboBox { IsEditable: true };

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
