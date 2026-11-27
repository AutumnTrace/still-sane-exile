using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.ViewModels;

namespace StillSaneExile.Desktop.Views;

/// <summary>What the HUD needs from the price check window, so its decisions can be tested with a fake.</summary>
public interface IPriceCheckSurface
{
    bool IsVisible { get; }

    void ShowFor(ItemViewModel item, nint gameWindow);

    void HideWindow();
}

/// <summary>The price check over the game, opened with F3 after a peek. Unlike the peek card it takes
/// focus, because it has boxes to tick and numbers to type. Esc, the close button, F3 again or clicking
/// back into the game closes it, and focus goes back to the game.</summary>
public partial class PriceCheckWindow : Window, IPriceCheckSurface
{
    /// <summary>The least the window needs, unscaled: the 600-wide price check with its scroll bar and
    /// shadow margins, and the shortest scroll area <see cref="Place"/> allows.</summary>
    private static readonly Size Minimum = new(642, 276);

    private nint _gameWindow;
    private bool _hiding;
    private TextScale? _textScale;

    public PriceCheckWindow()
    {
        InitializeComponent();
        View.CloseCommand = new RelayCommand(HideWindow);
    }

    /// <summary>The factor the content is drawn at (Windows' text size, as far as it fits the game).</summary>
    public double ContentScale { get; private set; } = 1;

    /// <summary>Follows Windows' text size, applied each time the window opens.</summary>
    public void UseTextScale(TextScale textScale) => _textScale = textScale;

    /// <summary>Scales the whole price check, as the main window does for the text size.</summary>
    public void SetContentScale(double scale)
    {
        ContentScale = scale;
        Root.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
        if (scale == 1)
        {
            Root.ClearValue(TextOptions.TextFormattingModeProperty);
        }
        else
        {
            TextOptions.SetTextFormattingMode(Root, TextFormattingMode.Ideal);
        }
    }

    public void ShowFor(ItemViewModel item, nint gameWindow)
    {
        _gameWindow = gameWindow;
        DataContext = item;
        Opacity = 0;
        Show();
        Place();
        Opacity = 1;
        Activate();
        View.Focus();
    }

    public void HideWindow()
    {
        if (_hiding || !IsVisible)
        {
            return;
        }

        _hiding = true;
        var active = IsActive;
        Hide();
        DataContext = null;
        if (active && _gameWindow != 0 && NativeMethods.IsWindow(_gameWindow))
        {
            NativeMethods.SetForegroundWindow(_gameWindow);
        }

        _hiding = false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideWindow();
        }
    }

    /// <summary>Clicking back into the game (or anywhere else) closes it, so it never covers the game unseen.</summary>
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_hiding && IsVisible)
        {
            _hiding = true;
            Hide();
            DataContext = null;
            _hiding = false;
        }
    }

    /// <summary>Left side of the game window (the inventory is on the right), as tall as the game allows.
    /// Positions are physical pixels.</summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (_gameWindow == 0 || !NativeMethods.GetWindowRect(_gameWindow, out var area) || area.Width <= 0 || NativeMethods.IsIconic(_gameWindow))
        {
            var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            NativeMethods.GetMonitorInfo(monitor, ref info);
            area = info.rcWork;
        }

        var dpi = _gameWindow != 0 ? NativeMethods.GetDpiForWindow(_gameWindow) : NativeMethods.GetDpiForWindow(hwnd);
        var factor = dpi > 0 ? dpi / 96.0 : 1.0;
        var margin = (int)(area.Height * 0.02);
        var room = new Size(area.Width * 0.94 / factor, (area.Height - 2 * margin) / factor);
        var scale = TextScale.Fit(_textScale?.Factor ?? 1, room, Minimum);
        SetContentScale(scale);
        Scroller.MaxHeight = Math.Max(240, (room.Height - 32) / scale);
        UpdateLayout();
        var x = area.Left + (int)(area.Width * 0.03);
        var y = area.Top + margin;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0, NativeMethods.SWP_NOSIZE);
    }
}
