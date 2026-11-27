using System.Diagnostics;
using System.Runtime.InteropServices;
using StillSaneExile.CaptureSpike.Interop;
using StillSaneExile.Domain.Capture;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace StillSaneExile.CaptureSpike.Capture;

public sealed record WgcOptions(CaptureFormat Format = CaptureFormat.Auto, TimeSpan? Timeout = null, bool HideBorder = false);

/// <summary>Windows.Graphics.Capture, one frame per call: create the item for the window, a frame pool
/// with one buffer, start the session, take the first frame, and close the session and pool as soon
/// as the frame's GPU copy is queued. The FrameArrived handler takes at most one frame per session;
/// with one buffer held, the pool cannot produce another before it is closed.</summary>
public sealed class WgcFrameSource : ISpikeSource
{
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");

    private readonly object _one = new();
    private readonly WgcOptions _options;
    private D3DDevice? _device;

    public WgcFrameSource(WgcOptions? options = null)
    {
        _options = options ?? new WgcOptions();
        if (_options.HideBorder && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            BorderAccess = GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless).AsTask().GetAwaiter().GetResult().ToString();
        }
    }

    public string Name => "Windows.Graphics.Capture";

    public SourceCounters Counters { get; } = new();

    public CaptureTiming? LastTiming { get; private set; }

    public string LastFormat { get; private set; } = "";

    /// <summary>The answer to the borderless capture request, when one was made.</summary>
    public string? BorderAccess { get; }

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

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
            var half = _options.Format == CaptureFormat.RgbaHalf || (_options.Format == CaptureFormat.Auto && hdr);
            var pixelFormat = half ? DirectXPixelFormat.R16G16B16A16Float : DirectXPixelFormat.B8G8R8A8UIntNormalized;
            LastFormat = half ? "R16G16B16A16Float" : "B8G8R8A8UIntNormalized";
            var sdrWhite = half ? color?.SdrWhite ?? 1.0 : 1.0;

            var tCreate = Stopwatch.GetTimestamp();
            var item = CreateItem(window);
            var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device.WinRt, pixelFormat, 1, item.Size);
            var session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            if (_options.HideBorder && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            {
                session.IsBorderRequired = false;
            }

            Direct3D11CaptureFrame? frame = null;
            var taken = 0;
            var closed = 0;
            long tFrame = 0;
            using var arrived = new ManualResetEventSlim();
            TypedEventHandler<Direct3D11CaptureFramePool, object> onFrame = (p, _) =>
            {
                Counters.Arrived();
                if (Volatile.Read(ref closed) != 0)
                {
                    Counters.AfterClose();
                    return;
                }

                if (Interlocked.Exchange(ref taken, 1) != 0)
                {
                    Counters.Extra();
                    return;
                }

                frame = p.TryGetNextFrame();
                tFrame = Stopwatch.GetTimestamp();
                if (frame is not null)
                {
                    Counters.Acquired();
                }

                arrived.Set();
            };
            pool.FrameArrived += onFrame;

            var tStart = Stopwatch.GetTimestamp();
            session.StartCapture();
            Counters.Opened();
            var got = arrived.Wait(_options.Timeout ?? TimeSpan.FromMilliseconds(500));

            TimeSpan? composed = null;
            D3DDevice.Staged? staged = null;
            string? note = null;
            try
            {
                if (got && frame is not null)
                {
                    composed = frame.SystemRelativeTime;
                    var size = frame.ContentSize;
                    var crop = ClientArea.InWindowCapture(window, size.Width, size.Height);
                    if (crop.Width > 0 && crop.Height > 0)
                    {
                        var texture = TextureOf(frame.Surface);
                        try
                        {
                            staged = _device.BeginCopy(texture, crop);
                        }
                        finally
                        {
                            Dx.Release(ref texture);
                        }
                    }
                    else
                    {
                        note = "empty client area";
                    }
                }
                else
                {
                    Counters.Timeout();
                    note = "no frame before the timeout";
                }
            }
            finally
            {
                Volatile.Write(ref closed, 1);
                frame?.Dispose();
                session.Dispose();
                pool.Dispose();
                Counters.Closed();
            }

            var tClosed = Stopwatch.GetTimestamp();
            if (staged is not { } s)
            {
                LastTiming = new CaptureTiming(setup, got ? Ms.Between(tCreate, tFrame) : 0, Ms.Between(tStart, tClosed), 0, Ms.Since(t0), Note: note);
                return null;
            }

            var result = _device.EndCopy(s, sdrWhite);
            var tDone = Stopwatch.GetTimestamp();
            Counters.Frame();
            double? age = composed is { } c ? (QpcTime(tStart) - c).TotalMilliseconds : null;
            LastTiming = new CaptureTiming(setup, Ms.Between(tCreate, tFrame), Ms.Between(tStart, tClosed), Ms.Between(tClosed, tDone), Ms.Between(t0, tDone), age, hdr ? $"HDR on, SDR white {sdrWhite:0.##}" : null);
            return result;
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

    /// <summary>Frame.SystemRelativeTime is QPC time since boot; Stopwatch ticks are QPC ticks.</summary>
    private static TimeSpan QpcTime(long stopwatchTicks) => TimeSpan.FromSeconds((double)stopwatchTicks / Stopwatch.Frequency);

    private static GraphicsCaptureItem CreateItem(nint window)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = Dx.QueryInterface(factory.ThisPtr, IID_IGraphicsCaptureItemInterop);
        try
        {
            Dx.Check(Dx.CallSlot3(interop, window, IID_IGraphicsCaptureItem, out var itemPointer), "IGraphicsCaptureItemInterop.CreateForWindow");
            try
            {
                return GraphicsCaptureItem.FromAbi(itemPointer);
            }
            finally
            {
                Marshal.Release(itemPointer);
            }
        }
        finally
        {
            Dx.Release(ref interop);
        }
    }

    private static nint TextureOf(IDirect3DSurface surface)
    {
        var unknown = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            var access = Dx.QueryInterface(unknown, IID_IDirect3DDxgiInterfaceAccess);
            try
            {
                Dx.Check(Dx.GetInterface(access, Dx.IID_ID3D11Texture2D, out var texture), "IDirect3DDxgiInterfaceAccess.GetInterface");
                return texture;
            }
            finally
            {
                Dx.Release(ref access);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }
}
