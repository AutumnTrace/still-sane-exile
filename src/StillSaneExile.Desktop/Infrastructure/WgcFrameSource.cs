using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>How one picture was taken, for its notes.</summary>
/// <param name="PixelFormat">What Windows was asked for: "B8G8R8A8UIntNormalized" (8-bit) or, with HDR on,
/// "R16G16B16A16Float".</param>
/// <param name="CreateToFrameMs">From asking Windows for the capture to holding the frame.</param>
public sealed record CaptureFacts(string Backend, GameWindowFacts Window, string PixelFormat, double CreateToFrameMs);

/// <summary>The frame source of the stash picture collector: an <see cref="IFrameSource"/> that can be
/// readied ahead of the first press, released when collecting is switched off, and says how it took each
/// frame.</summary>
public interface IStashFrameSource : IFrameSource, IDisposable
{
    /// <summary>Null when pictures can be taken on this computer; otherwise why not, in words.</summary>
    string? Unsupported { get; }

    /// <summary>Why the last capture gave no frame, in words, or null.</summary>
    string? LastProblem { get; }

    /// <summary>Creates the graphics device and the capture factories, so the first press is not slow.
    /// Captures nothing. Blocks for a moment: call it off the UI thread.</summary>
    void Prepare();

    /// <summary>Drops the device (collecting was switched off). The next capture prepares again.</summary>
    void Release();

    /// <summary>How <paramref name="frame"/> was taken, if this source took it.</summary>
    CaptureFacts? FactsFor(Frame frame);
}

/// <summary>Windows.Graphics.Capture, one frame per call, through raw COM. Per call: a capture item for
/// the window, a free-threaded frame pool with one buffer, a session with the cursor left out and the
/// yellow capture border left on, then this thread alone asks the pool for its next frame until one is
/// there or 500 ms have passed. The first frame ends the asking, its client area is copied on the GPU,
/// and frame, session and pool are closed before the copy is read back. There is no FrameArrived
/// handler, so no frame can be taken by another thread or after the call gave up, which the spike's
/// event handler could (#4).</summary>
public sealed class WgcFrameSource : IStashFrameSource
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(0.5);
    private static readonly Lazy<string?> WhyNotHere = new(CheckSupport);

    private readonly object _one = new();
    private readonly ConditionalWeakTable<Frame, CaptureFacts> _facts = [];
    private CaptureInterop.ShortWait? _wait;
    private nint _device;
    private nint _context;
    private nint _winrtDevice;
    private nint _itemInterop;
    private nint _poolStatics;
    private int _captures, _acquisitions, _opened, _closed, _timeouts;

    public string Name => "Windows.Graphics.Capture";

    public string? Unsupported => WhyNotHere.Value;

    public string? LastProblem { get; private set; }

    public bool IsPrepared
    {
        get
        {
            lock (_one)
            {
                return _device != 0;
            }
        }
    }

    /// <summary><see cref="CaptureOnce"/> calls.</summary>
    public int Captures => Volatile.Read(ref _captures);

    /// <summary>Frames Windows handed over (non-empty TryGetNextFrame results): at most one per call.</summary>
    public int Acquisitions => Volatile.Read(ref _acquisitions);

    public int SessionsOpened => Volatile.Read(ref _opened);

    public int SessionsClosed => Volatile.Read(ref _closed);

    public int Timeouts => Volatile.Read(ref _timeouts);

    public void Prepare()
    {
        lock (_one)
        {
            if (Unsupported is { } why)
            {
                LastProblem = why;
                return;
            }

            try
            {
                EnsurePrepared();
            }
            catch (Exception e) when (e is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
            {
                // The next press tries again, and says so if it fails too.
                LastProblem = "Windows could not set up for pictures: " + e.Message;
            }
        }
    }

    public void Release()
    {
        lock (_one)
        {
            Teardown();
        }
    }

    public void Dispose() => Release();

    public CaptureFacts? FactsFor(Frame frame) => _facts.TryGetValue(frame, out var facts) ? facts : null;

    public Frame? CaptureOnce(nint window)
    {
        lock (_one)
        {
            Interlocked.Increment(ref _captures);
            LastProblem = null;
            using var dpi = CaptureInterop.PhysicalPixels();
            if (Unsupported is { } why)
            {
                return Fail(why);
            }

            if (!CaptureInterop.IsWindow(window) || CaptureInterop.IsIconic(window))
            {
                return Fail("the game window is closed or minimized");
            }

            GameWindowFacts facts;
            uint format;
            nint item = 0, pool = 0, session = 0, frame = 0, staging = 0;
            CaptureInterop.Box crop = default;
            var opened = false;
            long tCreate, tFrame;
            try
            {
                EnsurePrepared();
                facts = GameWindowFacts.Read(window);
                format = facts.Hdr ? CaptureInterop.DXGI_FORMAT_R16G16B16A16_FLOAT : CaptureInterop.DXGI_FORMAT_B8G8R8A8_UNORM;

                tCreate = Stopwatch.GetTimestamp();
                item = CaptureInterop.CreateItemForWindow(_itemInterop, window);
                pool = CaptureInterop.CreateFreeThreadedPool(_poolStatics, _winrtDevice, format, 1, CaptureInterop.ItemSize(item));
                session = CaptureInterop.CreateSession(pool, item);
                CaptureInterop.HideCursor(session);
                CaptureInterop.StartCapture(session);
                opened = true;
                Interlocked.Increment(ref _opened);

                var deadline = tCreate + (long)(Timeout.TotalSeconds * Stopwatch.Frequency);
                while ((frame = CaptureInterop.TryGetNextFrame(pool)) == 0 && Stopwatch.GetTimestamp() < deadline)
                {
                    _wait!.Wait(PollInterval);
                }

                tFrame = Stopwatch.GetTimestamp();
                if (frame == 0)
                {
                    Interlocked.Increment(ref _timeouts);
                    return Fail($"Windows gave no picture of the game within {Timeout.TotalMilliseconds:0} ms");
                }

                Interlocked.Increment(ref _acquisitions);
                var content = CaptureInterop.FrameContentSize(frame);
                crop = GameWindowFacts.ClientInCapture(window, content.Width, content.Height);
                if (crop.Width <= 0 || crop.Height <= 0)
                {
                    return Fail("the game window has no visible client area");
                }

                var surface = CaptureInterop.FrameSurface(frame);
                try
                {
                    var texture = CaptureInterop.SurfaceTexture(surface);
                    try
                    {
                        format = CaptureInterop.TextureFormat(texture);
                        if (format is not (CaptureInterop.DXGI_FORMAT_B8G8R8A8_UNORM or CaptureInterop.DXGI_FORMAT_R16G16B16A16_FLOAT))
                        {
                            return Fail($"Windows gave a picture in an unexpected format ({format})");
                        }

                        staging = CaptureInterop.CopyToStaging(_device, _context, texture, crop, format);
                    }
                    finally
                    {
                        CaptureInterop.Release(ref texture);
                    }
                }
                finally
                {
                    CaptureInterop.Release(ref surface);
                }
            }
            catch (Exception e) when (e is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
            {
                // A lost or reset GPU device among them: start from a fresh device on the next press.
                CaptureInterop.Release(ref staging);
                Teardown();
                return Fail("Windows could not take the picture: " + e.Message);
            }
            finally
            {
                // The frame first (its buffer goes back to the pool), then the session (capture stops,
                // the yellow border goes), then the pool. The GPU copy keeps what it needs alive.
                CaptureInterop.CloseAndRelease(ref frame);
                CaptureInterop.CloseAndRelease(ref session);
                CaptureInterop.CloseAndRelease(ref pool);
                CaptureInterop.Release(ref item);
                if (opened)
                {
                    Interlocked.Increment(ref _closed);
                }
            }

            try
            {
                var half = format == CaptureInterop.DXGI_FORMAT_R16G16B16A16_FLOAT;
                var result = ReadBack(staging, crop.Width, crop.Height, half ? FramePixelFormat.RgbaHalf : FramePixelFormat.Bgra8, half ? facts.SdrWhite : 1.0);
                _facts.AddOrUpdate(result, new CaptureFacts(Name, facts, half ? "R16G16B16A16Float" : "B8G8R8A8UIntNormalized", Stopwatch.GetElapsedTime(tCreate, tFrame).TotalMilliseconds));
                return result;
            }
            catch (COMException e)
            {
                Teardown();
                return Fail("Windows could not hand over the picture: " + e.Message);
            }
            finally
            {
                CaptureInterop.Release(ref staging);
            }
        }
    }

    private Frame? Fail(string problem)
    {
        LastProblem = problem;
        return null;
    }

    private unsafe Frame ReadBack(nint staging, int width, int height, FramePixelFormat format, double sdrWhite)
    {
        var mapped = CaptureInterop.Map(_context, staging);
        try
        {
            var rowBytes = width * Frame.BytesPer(format);
            var pixels = GC.AllocateUninitializedArray<byte>(rowBytes * height);
            fixed (byte* dst = pixels)
            {
                for (var y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy((byte*)mapped.Data + (long)y * mapped.RowPitch, dst + (long)y * rowBytes, rowBytes, rowBytes);
                }
            }

            return new Frame(width, height, rowBytes, format, pixels, Stopwatch.GetTimestamp(), sdrWhite);
        }
        finally
        {
            CaptureInterop.Unmap(_context, staging);
        }
    }

    /// <summary>Creates the device and factories once. Nothing here captures anything.</summary>
    private void EnsurePrepared()
    {
        if (_device != 0)
        {
            return;
        }

        if (Unsupported is { } why)
        {
            throw new InvalidOperationException(why);
        }

        try
        {
            CaptureInterop.EnsureMta();
            (_device, _context) = CaptureInterop.CreateDevice();
            _winrtDevice = CaptureInterop.WinRtDevice(_device);
            _itemInterop = CaptureInterop.ItemInterop();
            _poolStatics = CaptureInterop.FramePoolStatics();
            _wait ??= new CaptureInterop.ShortWait();
        }
        catch
        {
            Teardown();
            throw;
        }
    }

    private void Teardown()
    {
        CaptureInterop.Release(ref _poolStatics);
        CaptureInterop.Release(ref _itemInterop);
        CaptureInterop.Release(ref _winrtDevice);
        CaptureInterop.Release(ref _context);
        CaptureInterop.Release(ref _device);
        _wait?.Dispose();
        _wait = null;
    }

    /// <summary>Null when a Direct3D 11 device runs on a graphics card here; otherwise what there is
    /// instead. CI runners have only Microsoft's software renderer, so real-capture tests skip there.</summary>
    public static string? GraphicsProblem()
    {
        try
        {
            var (device, context) = CaptureInterop.CreateDevice();
            try
            {
                var (name, vendor) = CaptureInterop.Adapter(device);
                return vendor == 0x1414 ? $"no graphics card ({name})" : null;
            }
            finally
            {
                CaptureInterop.Release(ref context);
                CaptureInterop.Release(ref device);
            }
        }
        catch (Exception e) when (e is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            return "no Direct3D 11 hardware device: " + e.Message;
        }
    }

    /// <summary>Null when Windows.Graphics.Capture can take pictures of windows here.</summary>
    private static string? CheckSupport()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return "Taking pictures of the game window needs Windows 10 version 2004 or later.";
        }

        try
        {
            CaptureInterop.EnsureMta();
            return CaptureInterop.IsCaptureSupported() ? null : "Windows says it can't take pictures of windows on this computer.";
        }
        catch (Exception e) when (e is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            return "Windows can't take pictures of windows on this computer: " + e.Message;
        }
    }
}
