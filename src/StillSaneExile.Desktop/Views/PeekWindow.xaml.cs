using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.ViewModels;

namespace StillSaneExile.Desktop.Views;

/// <summary>What the HUD needs from the peek card's window, so its decisions can be tested with a fake.</summary>
public interface IPeekSurface
{
    bool IsVisible { get; }

    /// <summary>The card's own window, to tell it apart when it comes to the front (the player clicked it).</summary>
    nint Handle { get; }

    /// <summary>The pinned card's close button, or Esc while it has the focus.</summary>
    event Action? CloseRequested;

    /// <summary>The pinned card's "All filters".</summary>
    event Action? MoreRequested;

    /// <summary>Shows the card click-through and never activated, as while the key is held.</summary>
    void ShowCard(PeekViewModel card, PeekAnchor anchor, double scale, nint referenceWindow);

    /// <summary>True when <paramref name="point"/> (physical pixels) is on the visible card or within a
    /// short reach of it, so moving towards the card catches it.</summary>
    bool IsNear(ScreenPoint point);

    /// <summary>Pinned: the card takes the mouse, and the focus once clicked. Unpinned: click-through and
    /// never activated again.</summary>
    void SetPinned(bool pinned);

    /// <summary>Hides the card. With <paramref name="returnFocus"/>, and only if the card has the focus,
    /// the window it was shown over (the game) gets the focus back.</summary>
    void HideCard(bool returnFocus = false);
}

/// <summary>The HUD card: topmost and never in the taskbar. While the key is held it is click-through
/// and never activated; pinned, it takes clicks, and the focus when the player clicks it.</summary>
public partial class PeekWindow : Window, IPeekSurface
{
    /// <summary>How far around the card, in device-independent pixels, the cursor counts as on it.</summary>
    public const double Reach = 20;

    private const long Passive = NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE;

    private nint _over;
    private bool _pinned;

    public PeekWindow()
    {
        InitializeComponent();
        CardView.CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());
        CardView.MoreCommand = new RelayCommand(() => MoreRequested?.Invoke());
    }

    public event Action? CloseRequested;

    public event Action? MoreRequested;

    public nint Handle => new WindowInteropHelper(this).Handle;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var style = ex | (nint)(NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, style);
    }

    public void ShowCard(PeekViewModel card, PeekAnchor anchor, double scale, nint referenceWindow)
    {
        SetPinned(false);
        _over = referenceWindow;
        DataContext = card;
        Scale.ScaleX = Scale.ScaleY = scale;
        Opacity = 0;
        Show();
        UpdateLayout();
        Place(anchor, referenceWindow);
        Opacity = 1;
    }

    public bool IsNear(ScreenPoint point) =>
        IsVisible && CardView.ScreenBounds() is { } card && Within(card, point, Reach * VisualTreeHelper.GetDpi(this).DpiScaleX);

    /// <summary>Whether <paramref name="point"/> lies on <paramref name="card"/> grown by <paramref name="reach"/> on every side.</summary>
    public static bool Within(Rect card, ScreenPoint point, double reach)
    {
        card.Inflate(reach, reach);
        return card.Contains(point.X, point.Y);
    }

    public void SetPinned(bool pinned)
    {
        _pinned = pinned;
        var hwnd = Handle;
        if (hwnd == 0)
        {
            return;
        }

        var ex = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var next = pinned ? ex & ~Passive : ex | Passive;
        if (next != ex)
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)next);
            NativeMethods.SetWindowPos(hwnd, 0, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
        }
    }

    public void HideCard(bool returnFocus = false)
    {
        // The game first: hiding the active card would otherwise hand the focus to some other window.
        if (returnFocus && IsVisible && IsActive && CanFocus(_over))
        {
            NativeMethods.SetForegroundWindow(_over);
        }

        Hide();
        DataContext = null;
        SetPinned(false);
    }

    /// <summary>Activated by a click with nothing focusable under it: the card itself takes the keys, so
    /// Esc still closes it.</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (!IsKeyboardFocusWithin)
        {
            Focus();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _pinned)
        {
            e.Handled = true;
            CloseRequested?.Invoke();
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>The player clicked the pinned card, then moved off it: the game gets the keyboard back, so
    /// the next press over an item reaches the game (and copies it). The card stays.</summary>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_pinned && IsVisible && IsActive && Mouse.Captured is null && CanFocus(_over)
            && NativeMethods.GetCursorPos(out var p) && CardView.ScreenBounds() is { } card && !card.Contains(p.X, p.Y))
        {
            NativeMethods.SetForegroundWindow(_over);
        }
    }

    private static bool CanFocus(nint window) => window != 0 && NativeMethods.IsWindow(window) && !NativeMethods.IsIconic(window);

    /// <summary>Opposite half from the cursor (where the game draws its tooltip), clear of the screen
    /// centre, the bottom skill bar and the top-right map. Positions are physical pixels.</summary>
    private void Place(PeekAnchor anchor, nint referenceWindow)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (referenceWindow == 0 || !NativeMethods.GetWindowRect(referenceWindow, out var area) || area.Width <= 0 || NativeMethods.IsIconic(referenceWindow))
        {
            var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            NativeMethods.GetMonitorInfo(monitor, ref info);
            area = info.rcWork;
        }

        var dpi = referenceWindow != 0 ? NativeMethods.GetDpiForWindow(referenceWindow) : NativeMethods.GetDpiForWindow(hwnd);
        var factor = dpi > 0 ? dpi / 96.0 : 1.0;
        var w = (int)Math.Ceiling(ActualWidth * factor);
        var h = (int)Math.Ceiling(ActualHeight * factor);

        NativeMethods.GetCursorPos(out var cursor);
        var left = anchor switch
        {
            PeekAnchor.Left => true,
            PeekAnchor.Right => false,
            _ => cursor.X > area.Left + area.Width / 2,
        };

        var margin = (int)(area.Width * 0.03);
        var x = left ? area.Left + margin : area.Right - margin - w;
        var top = area.Top + (int)(area.Height * (left ? 0.16 : 0.27)); // right side starts below the map
        var bottomLimit = area.Bottom - (int)(area.Height * 0.17);       // keep the skill bar clear
        var y = Math.Max(area.Top + margin, Math.Min(top, bottomLimit - h));

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
