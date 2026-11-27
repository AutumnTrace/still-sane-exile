using System.Diagnostics;
using System.Runtime.InteropServices;
using StillSaneExile.CaptureSpike.Interop;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.CaptureSpike.Capture;

/// <summary>Fallback 1: PrintWindow(PW_CLIENTONLY | PW_RENDERFULLCONTENT) into a 32-bit DIB. One call,
/// one image, no session. Its documentation says the window's owner "processes the PrintWindow call"
/// and receives WM_PRINT, so it depends on the target's message loop; the bench counts the messages.</summary>
public sealed class PrintWindowFrameSource : ISpikeSource
{
    private readonly object _one = new();

    public string Name => "PrintWindow (PW_RENDERFULLCONTENT)";

    public SourceCounters Counters { get; } = new();

    public CaptureTiming? LastTiming { get; private set; }

    public string LastFormat => "BGRA8 (GDI)";

    public Frame? CaptureOnce(nint window)
    {
        lock (_one)
        {
            Counters.Capture();
            using var dpi = Win32.PerMonitorDpi();
            var t0 = Stopwatch.GetTimestamp();
            if (!Win32.IsWindow(window) || Win32.IsIconic(window) || !Win32.GetClientRect(window, out var client) || client.Width <= 0 || client.Height <= 0)
            {
                LastTiming = new CaptureTiming(0, 0, 0, 0, Ms.Since(t0), Note: "window closed, minimized or empty");
                return null;
            }

            int w = client.Width, h = client.Height;
            var screen = Win32.GetDC(0);
            var dc = Win32.CreateCompatibleDC(screen);
            var header = new Win32.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // top-down rows
                biPlanes = 1,
                biBitCount = 32,
            };
            var dib = Win32.CreateDIBSection(dc, ref header, 0, out var bits, 0, 0);
            var old = Win32.SelectObject(dc, dib);
            try
            {
                var tCall = Stopwatch.GetTimestamp();
                Counters.Opened();
                var ok = Win32.PrintWindow(window, dc, Win32.PW_CLIENTONLY | Win32.PW_RENDERFULLCONTENT);
                Win32.GdiFlush();
                var tDone = Stopwatch.GetTimestamp();
                Counters.Closed();
                if (!ok)
                {
                    LastTiming = new CaptureTiming(0, Ms.Between(tCall, tDone), Ms.Between(tCall, tDone), 0, Ms.Since(t0), Note: "PrintWindow returned FALSE");
                    return null;
                }

                Counters.Acquired();
                var pixels = new byte[w * h * 4];
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                for (var i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255; // GDI leaves alpha undefined
                }

                var frame = new Frame(w, h, w * 4, FramePixelFormat.Bgra8, pixels, Stopwatch.GetTimestamp());
                var tEnd = Stopwatch.GetTimestamp();
                Counters.Frame();
                LastTiming = new CaptureTiming(0, Ms.Between(tCall, tDone), Ms.Between(tCall, tDone), Ms.Between(tDone, tEnd), Ms.Between(t0, tEnd));
                return frame;
            }
            finally
            {
                Win32.SelectObject(dc, old);
                Win32.DeleteObject(dib);
                Win32.DeleteDC(dc);
                Win32.ReleaseDC(0, screen);
            }
        }
    }

    public void Dispose()
    {
    }
}
