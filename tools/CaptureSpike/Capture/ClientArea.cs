using StillSaneExile.CaptureSpike.Interop;

namespace StillSaneExile.CaptureSpike.Capture;

/// <summary>Where a window's client area sits inside what each backend captures, in physical pixels.</summary>
internal static class ClientArea
{
    /// <summary>The client rectangle on screen.</summary>
    public static Win32.RECT OnScreen(nint window)
    {
        Win32.GetClientRect(window, out var client);
        var origin = new Win32.POINT();
        Win32.ClientToScreen(window, ref origin);
        return new Win32.RECT { Left = origin.X, Top = origin.Y, Right = origin.X + client.Width, Bottom = origin.Y + client.Height };
    }

    /// <summary>A window capture covers the window's visible frame (DWM's extended frame bounds,
    /// without the invisible resize borders). The client area is offset inside it by the title bar
    /// and borders; for a borderless game window the two are the same.</summary>
    public static Dx.D3D11_BOX InWindowCapture(nint window, int contentWidth, int contentHeight)
    {
        var client = OnScreen(window);
        if (Win32.DwmGetWindowAttribute(window, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, System.Runtime.InteropServices.Marshal.SizeOf<Win32.RECT>()) != 0)
        {
            Win32.GetWindowRect(window, out frame);
        }

        return Clamp(client.Left - frame.Left, client.Top - frame.Top, client.Width, client.Height, contentWidth, contentHeight);
    }

    /// <summary>A monitor capture covers the whole output; the client area is where it is on screen.</summary>
    public static Dx.D3D11_BOX InMonitorCapture(nint window, Win32.RECT output, int textureWidth, int textureHeight)
    {
        var client = OnScreen(window);
        return Clamp(client.Left - output.Left, client.Top - output.Top, client.Width, client.Height, textureWidth, textureHeight);
    }

    private static Dx.D3D11_BOX Clamp(int x, int y, int w, int h, int maxW, int maxH)
    {
        var left = Math.Clamp(x, 0, maxW);
        var top = Math.Clamp(y, 0, maxH);
        var right = Math.Clamp(x + w, left, maxW);
        var bottom = Math.Clamp(y + h, top, maxH);
        return Dx.D3D11_BOX.Of(left, top, right - left, bottom - top);
    }
}
