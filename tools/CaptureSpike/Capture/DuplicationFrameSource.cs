using System.Diagnostics;
using StillSaneExile.CaptureSpike.Interop;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.CaptureSpike.Capture;

/// <summary>Fallback 2: DXGI Desktop Duplication of the monitor the window is on, cropped to the
/// window's client area on the GPU. One duplication per call: DuplicateOutput1, one AcquireNextFrame,
/// ReleaseFrame, release. It sees the screen, not the window, so anything on top of the game (SSE's
/// own HUD, a notification, another window) is in the frame, and the GPU briefly holds the whole
/// monitor's image.</summary>
public sealed class DuplicationFrameSource : ISpikeSource
{
    private readonly object _one = new();
    private readonly CaptureFormat _format;
    private readonly bool _untilImage;
    private D3DDevice? _device;

    /// <param name="untilImage">Measurement only: keep calling AcquireNextFrame (up to 5 times) until a
    /// frame carries a desktop image. That is more than one acquisition per press, which the rule
    /// forbids; it exists to show what Desktop Duplication would need.</param>
    public DuplicationFrameSource(CaptureFormat format = CaptureFormat.Auto, bool untilImage = false)
    {
        _format = format;
        _untilImage = untilImage;
    }

    public string Name => _untilImage ? "DXGI Desktop Duplication, until an image" : "DXGI Desktop Duplication";

    public SourceCounters Counters { get; } = new();

    public CaptureTiming? LastTiming { get; private set; }

    public string LastFormat { get; private set; } = "";

    /// <summary>AccumulatedFrames and LastPresentTime of the last frame, to see whether the first
    /// frame of a fresh duplication is a real update or the standing desktop image.</summary>
    public string LastFrameInfo { get; private set; } = "";

    public Frame? CaptureOnce(nint window)
    {
        lock (_one)
        {
            Counters.Capture();
            using var dpi = Win32.PerMonitorDpi();
            var t0 = Stopwatch.GetTimestamp();
            if (!Win32.IsWindow(window) || Win32.IsIconic(window))
            {
                LastTiming = new CaptureTiming(0, 0, 0, 0, Ms.Since(t0), Note: "window closed or minimized");
                return null;
            }

            var setup = 0.0;
            if (_device is null)
            {
                _device = D3DDevice.Create();
                setup = Ms.Since(t0);
            }

            var color = Displays.ForWindow(window);
            var hdr = color?.HdrOn == true;
            var half = _format == CaptureFormat.RgbaHalf || (_format == CaptureFormat.Auto && hdr);
            uint[] formats = half
                ? [Dx.DXGI_FORMAT_R16G16B16A16_FLOAT, Dx.DXGI_FORMAT_B8G8R8A8_UNORM]
                : [Dx.DXGI_FORMAT_B8G8R8A8_UNORM];

            var output = FindOutput(_device.Device, Win32.MonitorFromWindow(window, Win32.MONITOR_DEFAULTTONEAREST), out var desktop);
            if (output == 0)
            {
                LastTiming = new CaptureTiming(setup, 0, 0, 0, Ms.Since(t0), Note: "the window's monitor is not on this adapter");
                return null;
            }

            nint duplication = 0, resource = 0, texture = 0;
            D3DDevice.Staged? staged = null;
            long tOpen = 0, tFrame = 0;
            string? note = null;
            try
            {
                tOpen = Stopwatch.GetTimestamp();
                var output5 = Dx.TryQueryInterface(output, Dx.IID_IDXGIOutput5);
                int hr;
                if (output5 != 0)
                {
                    hr = Dx.DuplicateOutput1(output5, _device.Device, formats, out duplication);
                    Dx.Release(ref output5);
                }
                else
                {
                    var output1 = Dx.QueryInterface(output, Dx.IID_IDXGIOutput1);
                    hr = Dx.DuplicateOutput(output1, _device.Device, out duplication);
                    Dx.Release(ref output1);
                }

                if (hr < 0)
                {
                    LastTiming = new CaptureTiming(setup, 0, 0, 0, Ms.Since(t0), Note: $"DuplicateOutput failed (0x{hr:X8})");
                    return null;
                }

                Counters.Opened();
                hr = Dx.AcquireNextFrame(duplication, 500, out var info, out resource);
                var attempts = 1;
                while (_untilImage && hr >= 0 && info.LastPresentTime == 0 && attempts < 5)
                {
                    Counters.Acquired();
                    Dx.Release(ref resource);
                    Dx.ReleaseFrame(duplication);
                    hr = Dx.AcquireNextFrame(duplication, 500, out info, out resource);
                    attempts++;
                }

                tFrame = Stopwatch.GetTimestamp();
                if (hr == Dx.DXGI_ERROR_WAIT_TIMEOUT)
                {
                    Counters.Timeout();
                    note = "no desktop update before the timeout";
                }
                else
                {
                    Dx.Check(hr, "AcquireNextFrame");
                    Counters.Acquired();
                    LastFrameInfo = $"{attempts} AcquireNextFrame call(s); the frame used: AccumulatedFrames {info.AccumulatedFrames}, LastPresentTime {(info.LastPresentTime == 0 ? "0 (no image)" : "set")}";
                    texture = Dx.QueryInterface(resource, Dx.IID_ID3D11Texture2D);
                    var desc = Dx.GetDesc(texture);
                    LastFormat = desc.Format switch
                    {
                        Dx.DXGI_FORMAT_R16G16B16A16_FLOAT => "R16G16B16A16_FLOAT",
                        Dx.DXGI_FORMAT_B8G8R8A8_UNORM => "B8G8R8A8_UNORM",
                        _ => $"DXGI format {desc.Format}",
                    };
                    var crop = ClientArea.InMonitorCapture(window, desktop, (int)desc.Width, (int)desc.Height);
                    if (crop.Width > 0 && crop.Height > 0)
                    {
                        staged = _device.BeginCopy(texture, crop);
                    }
                    else
                    {
                        note = "window is off this monitor";
                    }

                    Dx.Release(ref texture);
                    Dx.Release(ref resource);
                    Dx.ReleaseFrame(duplication);
                }
            }
            finally
            {
                Dx.Release(ref texture);
                Dx.Release(ref resource);
                if (duplication != 0)
                {
                    Dx.Release(ref duplication);
                    Counters.Closed();
                }

                Dx.Release(ref output);
            }

            var tClosed = Stopwatch.GetTimestamp();
            if (staged is not { } s)
            {
                LastTiming = new CaptureTiming(setup, Ms.Between(tOpen, tFrame), Ms.Between(tOpen, tClosed), 0, Ms.Since(t0), Note: note);
                return null;
            }

            var frame = _device.EndCopy(s, s.Format == FramePixelFormat.RgbaHalf ? color?.SdrWhite ?? 1.0 : 1.0);
            var tDone = Stopwatch.GetTimestamp();
            Counters.Frame();
            LastTiming = new CaptureTiming(setup, Ms.Between(tOpen, tFrame), Ms.Between(tOpen, tClosed), Ms.Between(tClosed, tDone), Ms.Between(t0, tDone), Note: hdr ? $"HDR on, SDR white {color!.SdrWhite:0.##}" : null);
            return frame;
        }
    }

    public void Dispose()
    {
        lock (_one)
        {
            _device?.Dispose();
            _device = null;
        }
    }

    private static nint FindOutput(nint device, nint monitor, out Win32.RECT desktop)
    {
        desktop = default;
        var dxgi = Dx.QueryInterface(device, Dx.IID_IDXGIDevice);
        try
        {
            Dx.Check(Dx.GetAdapter(dxgi, out var adapter), "IDXGIDevice.GetAdapter");
            try
            {
                for (uint i = 0; Dx.EnumOutputs(adapter, i, out var output) >= 0; i++)
                {
                    var desc = Dx.GetOutputDesc(output);
                    if (desc.Monitor == monitor)
                    {
                        desktop = desc.DesktopCoordinates;
                        return output;
                    }

                    Dx.Release(ref output);
                }

                return 0;
            }
            finally
            {
                Dx.Release(ref adapter);
            }
        }
        finally
        {
            Dx.Release(ref dxgi);
        }
    }
}
