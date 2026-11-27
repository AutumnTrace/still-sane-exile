using System.Windows;
using System.Windows.Interop;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.ViewModels;

namespace StillSaneExile.Desktop.Views;

/// <summary>What the HUD needs from the peek card's window, so its decisions can be tested with a fake.</summary>
public interface IPeekSurface
{
    bool IsVisible { get; }

    void ShowCard(PeekViewModel card, PeekAnchor anchor, double scale, nint referenceWindow);

    void HideCard();
}

/// <summary>The passive HUD card: topmost, click-through, never activated, never focusable.</summary>
public partial class PeekWindow : Window, IPeekSurface
{
    public PeekWindow()
    {
        InitializeComponent();
    }

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
        DataContext = card;
        Scale.ScaleX = Scale.ScaleY = scale;
        Opacity = 0;
        Show();
        UpdateLayout();
        Place(anchor, referenceWindow);
        Opacity = 1;
    }

    public void HideCard()
    {
        Hide();
        DataContext = null;
    }

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
