using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;

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
    private readonly Size _designMinimum;
    private TextScale? _textScale;
    private Size? _screen;
    private bool _menuFromKeyboard;

    public MainWindow()
    {
        InitializeComponent();
        _designMinimum = new Size(MinWidth, MinHeight);

        // Until the window has a screen, the primary screen's work area stands in for it.
        Loaded += (_, _) => ApplyTextScale(keepOnScreen: true);
        DpiChanged += (_, _) => ApplyTextScale(keepOnScreen: true);
    }

    public nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>The factor the content is drawn at: Windows' text size, as far as the window still fits
    /// its screen. 1 at the default text size.</summary>
    public double ContentScale { get; private set; } = 1;

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

    /// <summary>Follows Windows' text size from now on. <paramref name="screen"/> replaces the screen's
    /// work area when fitting (snapshots, which render off screen).</summary>
    public void UseTextScale(TextScale textScale, Size? screen = null)
    {
        _textScale = textScale;
        _screen = screen;
        textScale.Changed += (_, _) => ApplyTextScale(keepOnScreen: true);
        ApplyTextScale(keepOnScreen: false);
    }

    /// <summary>Scales everything in the window, like a browser's zoom: the pages were laid out for
    /// explicit font sizes and reflow the same way at any factor. The minimum size grows with it, which
    /// is why the factor stops where that minimum would no longer fit the screen.</summary>
    private void ApplyTextScale(bool keepOnScreen)
    {
        if (_textScale is null || Content is not FrameworkElement root)
        {
            return;
        }

        var scale = TextScale.Fit(_textScale.Factor, _screen ?? WorkArea(), _designMinimum);
        ContentScale = scale;
        root.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);

        // Display formatting snaps glyphs to whole pixels at the unscaled size; scaled, it spaces them
        // unevenly, so scaled text uses ideal formatting.
        if (scale == 1)
        {
            root.ClearValue(TextOptions.TextFormattingModeProperty);
        }
        else
        {
            TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
        }

        MinWidth = _designMinimum.Width * scale;
        MinHeight = _designMinimum.Height * scale;
        if (keepOnScreen && _screen is null && WindowState == WindowState.Normal && IsLoaded)
        {
            KeepOnScreen();
        }
    }

    /// <summary>The work area of the screen the window is on, in device-independent pixels.</summary>
    private Size WorkArea()
    {
        var hwnd = Handle;
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (hwnd == 0 || !NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST), ref info))
        {
            return SystemParameters.WorkArea.Size;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        return new Size(info.rcWork.Width / dpi.DpiScaleX, info.rcWork.Height / dpi.DpiScaleY);
    }

    /// <summary>After growing for a larger text size, moves the window back inside its screen.</summary>
    private void KeepOnScreen()
    {
        UpdateLayout();
        var hwnd = Handle;
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (hwnd == 0 || !NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST), ref info))
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var left = info.rcWork.Left / dpi.DpiScaleX;
        var top = info.rcWork.Top / dpi.DpiScaleY;
        var right = info.rcWork.Right / dpi.DpiScaleX;
        var bottom = info.rcWork.Bottom / dpi.DpiScaleY;
        Left = Math.Max(left, Math.Min(Left, right - ActualWidth));
        Top = Math.Max(top, Math.Min(Top, bottom - ActualHeight));
    }

    private void OnCurrencyButtonKeyDown(object sender, KeyEventArgs e) => _menuFromKeyboard = e.Key is Key.Enter or Key.Space;

    /// <summary>Opened from the keyboard (Enter or Space on "Show prices in"), the currency menu takes
    /// focus on the current choice, so the arrow keys, Tab, Enter and Esc work in it. Opened with the
    /// mouse, focus stays where it was.</summary>
    private void OnCurrencyMenuOpened(object? sender, EventArgs e)
    {
        if (!_menuFromKeyboard)
        {
            return;
        }

        _menuFromKeyboard = false;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            var choices = Descendants<Button>(CurrencyMenuItems).ToList();
            (choices.FirstOrDefault(b => Equals(b.Tag, "active")) ?? choices.FirstOrDefault())?.Focus();
        });
    }

    /// <summary>Focus left in a closed menu would be lost: it goes back to the button that opened it.</summary>
    private void OnCurrencyMenuClosed(object? sender, EventArgs e)
    {
        _menuFromKeyboard = false;
        if (CurrencyMenuItems.IsKeyboardFocusWithin)
        {
            CurrencyButton.Focus();
        }
    }

    private void OnCurrencyMenuKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CurrencyMenu.SetCurrentValue(Popup.IsOpenProperty, false);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var inner in Descendants<T>(child))
            {
                yield return inner;
            }
        }
    }
}
