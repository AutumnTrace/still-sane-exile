using System.Windows;
using System.Windows.Interop;
using StillSaneExile.Desktop.Infrastructure;

namespace StillSaneExile.Desktop.Views;

/// <summary>The pinned guide lines: topmost, click-through, never activated, never focusable, like the
/// item peek card. <see cref="Services.GuideHud"/> decides when it shows.</summary>
public partial class GuideHudWindow : Window
{
    public GuideHudWindow()
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

    /// <summary>Shows the lines over <paramref name="referenceWindow"/> (the game, or this app for a
    /// preview) without activating anything. Already showing: only moves it.</summary>
    public void ShowOver(nint referenceWindow)
    {
        if (IsVisible)
        {
            UpdateLayout();
            Place(referenceWindow);
            return;
        }

        Opacity = 0;
        Show();
        UpdateLayout();
        Place(referenceWindow);
        Opacity = 1;
    }

    /// <summary>Top left, below where the game lists buffs and clear of the item card's usual spot
    /// (which the card takes over anyway: these lines step aside while it shows). Not yet measured on
    /// real screens (#5). Positions are physical pixels.</summary>
    private void Place(nint referenceWindow)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (referenceWindow == 0 || !NativeMethods.GetWindowRect(referenceWindow, out var area) || area.Width <= 0 || NativeMethods.IsIconic(referenceWindow))
        {
            var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            NativeMethods.GetMonitorInfo(monitor, ref info);
            area = info.rcWork;
        }

        var x = area.Left + (int)(area.Width * 0.02);
        var y = area.Top + (int)(area.Height * 0.10);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
