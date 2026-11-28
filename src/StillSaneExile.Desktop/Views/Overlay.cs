using System.Runtime.InteropServices;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Placement;

namespace StillSaneExile.Desktop.Views;

/// <summary>What the windows SSE draws over the game share: the area they go on (the game window's
/// rectangle, from the OS), its scale, and the styles that keep a window click-through and never activated.
/// All in physical pixels, as Windows places windows.</summary>
internal static class Overlay
{
    /// <summary>The game window, or the work area of the monitor <paramref name="self"/> is on when there
    /// is none (practice over SSE's own window falls back here only if that window is gone).</summary>
    public static Box AreaOf(nint over, nint self)
    {
        if (over == 0 || !NativeMethods.GetWindowRect(over, out var area) || area.Width <= 0 || NativeMethods.IsIconic(over))
        {
            var monitor = NativeMethods.MonitorFromWindow(self, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            NativeMethods.GetMonitorInfo(monitor, ref info);
            area = info.rcWork;
        }

        return new Box(area.Left, area.Top, area.Width, area.Height);
    }

    /// <summary>Physical pixels per device-independent pixel on the game's monitor.</summary>
    public static double Factor(nint over, nint self)
    {
        var dpi = over != 0 ? NativeMethods.GetDpiForWindow(over) : NativeMethods.GetDpiForWindow(self);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    /// <summary>Click-through, layered, never activated and out of Alt+Tab.</summary>
    public static void MakePassive(nint hwnd)
    {
        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var style = ex | (nint)(NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, style);
    }

    /// <summary>Moves the window's top-left corner there, on top, without activating it.</summary>
    public static void MoveTo(nint hwnd, double x, double y) =>
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, (int)Math.Round(x), (int)Math.Round(y), 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
}
