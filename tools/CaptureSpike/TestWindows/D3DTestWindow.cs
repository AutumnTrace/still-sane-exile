using System.Diagnostics;
using System.Runtime.InteropServices;
using StillSaneExile.CaptureSpike.Interop;

namespace StillSaneExile.CaptureSpike.TestWindows;

public enum D3DWindowMode
{
    /// <summary>A normal window with a title bar, flip-model swap chain (how a windowed DX11/DX12 game presents).</summary>
    Windowed,
    /// <summary>A borderless popup covering the primary monitor, flip-model: PoE2's "Windowed Fullscreen".
    /// With nothing on top, DWM can hand it the display directly (independent flip).</summary>
    Borderless,
    /// <summary>A normal window with an old blt-model (DISCARD) swap chain.</summary>
    WindowedBlt,
}

/// <summary>A Win32 window that renders with its own D3D11 device and DXGI swap chain at the monitor's
/// refresh rate, like a game. Each frame draws <see cref="Marker"/> with the frame number. Its thread
/// also counts WM_PRINT / WM_PRINTCLIENT, the messages PrintWindow sends.</summary>
public sealed unsafe class D3DTestWindow : ITestWindow
{
    private static readonly long FrameTicks = Stopwatch.Frequency / 240;
    private static int _classes;

    private readonly long[] _renderedAt = new long[4096];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, int> _messages = new();

    private readonly D3DWindowMode _mode;
    private readonly int _width;
    private readonly int _height;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Win32.WndProc _proc;
    private volatile bool _quit;
    private long _presented;
    private int _printMessages;
    private long _stallUntil;
    private Exception? _failure;

    private D3DTestWindow(D3DWindowMode mode, int width, int height)
    {
        _mode = mode;
        _width = width;
        _height = height;
        _proc = WndProc;
        _thread = new Thread(Run) { IsBackground = true, Name = "D3D test window" };
    }

    public nint Handle { get; private set; }

    /// <summary>Frames presented so far; the frame being drawn now carries this number.</summary>
    public long Presented => Interlocked.Read(ref _presented);

    public int PrintMessages => Volatile.Read(ref _printMessages);

    /// <summary>The last HRESULT of Present: 0, or 0x087A0001 (DXGI_STATUS_OCCLUDED) when nothing of the
    /// window can be seen, as on a locked session.</summary>
    public int LastPresentResult { get; private set; }

    /// <summary>When the frame whose marker reads <paramref name="marker"/> was drawn (Stopwatch ticks).</summary>
    public long RenderedAt(int marker) => Volatile.Read(ref _renderedAt[marker & 4095]);

    public int ClientWidth { get; private set; }

    public int ClientHeight { get; private set; }

    public string Description => _mode switch
    {
        D3DWindowMode.Windowed => $"D3D11 flip-model window {ClientWidth}×{ClientHeight}",
        D3DWindowMode.Borderless => $"D3D11 flip-model borderless fullscreen {ClientWidth}×{ClientHeight}",
        _ => $"D3D11 blt-model window {ClientWidth}×{ClientHeight}",
    };

    public static D3DTestWindow Open(D3DWindowMode mode, int width = 960, int height = 540)
    {
        var w = new D3DTestWindow(mode, width, height);
        w._thread.Start();
        if (!w._ready.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("The D3D test window did not start.");
        }

        if (w._failure is not null)
        {
            throw new InvalidOperationException("The D3D test window could not start: " + w._failure.Message, w._failure);
        }

        var wait = Stopwatch.StartNew();
        while (w.Presented < 30 && wait.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(10);
        }

        return w;
    }

    /// <summary>Blocks the window's thread for <paramref name="duration"/>: no messages handled and
    /// nothing presented, like a game busy loading an area.</summary>
    public void Stall(TimeSpan duration) =>
        Interlocked.Exchange(ref _stallUntil, Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency));

    public void Minimize() => Win32.ShowWindow(Handle, Win32.SW_MINIMIZE);

    public void Restore() => Win32.ShowWindow(Handle, Win32.SW_RESTORE);

    public void Dispose()
    {
        _quit = true;
        _thread.Join(TimeSpan.FromSeconds(5));
        _ready.Dispose();
    }

    private void Run()
    {
        using var dpi = Win32.PerMonitorDpi();
        nint device = 0, context = 0, dxgi = 0, adapter = 0, factory = 0, swap = 0, back = 0, rtv = 0;
        try
        {
            var module = Win32.GetModuleHandle(null);
            var cls = "SseCaptureSpikeD3D" + Interlocked.Increment(ref _classes);
            var wc = new Win32.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<Win32.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                hInstance = module,
                lpszClassName = cls,
            };
            if (Win32.RegisterClassEx(ref wc) == 0)
            {
                throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastWin32Error()}).");
            }

            int x = 80, y = 80, w = _width + 16, h = _height + 39;
            var style = (uint)(Win32.WS_OVERLAPPEDWINDOW | Win32.WS_VISIBLE);
            if (_mode == D3DWindowMode.Borderless)
            {
                var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
                Win32.GetMonitorInfo(Win32.MonitorFromPoint(default, Win32.MONITOR_DEFAULTTONEAREST), ref info);
                (x, y, w, h) = (info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Width, info.rcMonitor.Height);
                style = Win32.WS_POPUP | Win32.WS_VISIBLE;
            }

            Handle = Win32.CreateWindowEx(Win32.WS_EX_TOPMOST, cls, "SSE capture spike: D3D test window", style, x, y, w, h, 0, 0, module, 0);
            if (Handle == 0)
            {
                throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()}).");
            }

            Win32.GetClientRect(Handle, out var client);
            ClientWidth = client.Width;
            ClientHeight = client.Height;

            var levels = stackalloc int[] { 0xb000 };
            Dx.Check(Dx.D3D11CreateDevice(0, Dx.D3D_DRIVER_TYPE_HARDWARE, 0, Dx.D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, 1, Dx.D3D11_SDK_VERSION, out device, out _, out context), "D3D11CreateDevice");
            dxgi = Dx.QueryInterface(device, Dx.IID_IDXGIDevice);
            Dx.Check(Dx.GetAdapter(dxgi, out adapter), "GetAdapter");
            Dx.Check(Dx.GetParent(adapter, Dx.IID_IDXGIFactory2, out factory), "GetParent(IDXGIFactory2)");
            var flip = _mode != D3DWindowMode.WindowedBlt;
            var desc = new Dx.DXGI_SWAP_CHAIN_DESC1
            {
                Width = (uint)ClientWidth,
                Height = (uint)ClientHeight,
                Format = Dx.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new Dx.DXGI_SAMPLE_DESC { Count = 1 },
                BufferUsage = Dx.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                BufferCount = flip ? 2u : 1u,
                Scaling = Dx.DXGI_SCALING_STRETCH,
                SwapEffect = flip ? Dx.DXGI_SWAP_EFFECT_FLIP_DISCARD : Dx.DXGI_SWAP_EFFECT_DISCARD,
            };
            Dx.Check(Dx.CreateSwapChainForHwnd(factory, device, Handle, &desc, out swap), "CreateSwapChainForHwnd");
            Dx.Check(Dx.GetBuffer(swap, Dx.IID_ID3D11Texture2D, out back), "GetBuffer");
            Dx.Check(Dx.CreateRenderTargetView(device, back, out rtv), "CreateRenderTargetView");
        }
        catch (Exception ex)
        {
            _failure = ex;
            _ready.Set();
            Cleanup();
            return;
        }

        _ready.Set();
        var clock = Stopwatch.StartNew();
        var marker = new byte[Marker.Size * Marker.Size * 4];
        var box = Dx.D3D11_BOX.Of(Marker.X, Marker.Y, Marker.Size, Marker.Size);
        var bg = stackalloc float[4];
        var nextFrame = Stopwatch.GetTimestamp() + FrameTicks;
        while (!_quit)
        {
            var stall = Interlocked.Read(ref _stallUntil);
            var now = Stopwatch.GetTimestamp();
            if (now < stall)
            {
                Thread.Sleep(TimeSpan.FromSeconds((double)(stall - now) / Stopwatch.Frequency));
                continue;
            }

            while (Win32.PeekMessage(out var msg, 0, 0, 0, Win32.PM_REMOVE))
            {
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessage(ref msg);
            }

            var n = Interlocked.Read(ref _presented);
            var (r, g, b) = Marker.Background(clock.Elapsed.TotalSeconds);
            bg[0] = r;
            bg[1] = g;
            bg[2] = b;
            bg[3] = 1;
            Dx.ClearRenderTargetView(context, rtv, bg);
            var (mr, mg, mb) = Marker.Encode(n);
            for (var i = 0; i < marker.Length; i += 4)
            {
                marker[i] = mb;
                marker[i + 1] = mg;
                marker[i + 2] = mr;
                marker[i + 3] = 255;
            }

            fixed (byte* m = marker)
            {
                Dx.UpdateSubresource(context, back, &box, m, Marker.Size * 4);
            }

            Volatile.Write(ref _renderedAt[n & 4095], Stopwatch.GetTimestamp());
            var hr = Dx.Present(swap, 1);
            LastPresentResult = hr;

            // Present(1) does not always wait for the display (it returned at once here, about 7000
            // frames a second), so hold the loop to about 240 Hz like a fast game.
            var spare = nextFrame - Stopwatch.GetTimestamp();
            if (spare > 0)
            {
                Win32.PreciseSleep(TimeSpan.FromSeconds((double)spare / Stopwatch.Frequency));
            }

            nextFrame = Math.Max(nextFrame, Stopwatch.GetTimestamp() - FrameTicks) + FrameTicks;

            Interlocked.Increment(ref _presented);
        }

        Cleanup();

        void Cleanup()
        {
            Dx.Release(ref rtv);
            Dx.Release(ref back);
            Dx.Release(ref swap);
            Dx.Release(ref factory);
            Dx.Release(ref adapter);
            Dx.Release(ref dxgi);
            Dx.Release(ref context);
            Dx.Release(ref device);
            if (Handle != 0)
            {
                Win32.DestroyWindow(Handle);
            }
        }
    }

    public IReadOnlyDictionary<uint, int> MessageCounts() => new Dictionary<uint, int>(_messages);

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        _messages.AddOrUpdate(msg, 1, (_, n) => n + 1);
        switch (msg)
        {
            case Win32.WM_PRINT:
            case Win32.WM_PRINTCLIENT:
                Interlocked.Increment(ref _printMessages);
                break;
            case Win32.WM_ERASEBKGND:
                return 1;
            case Win32.WM_CLOSE:
                return 0; // closed by Dispose only
        }

        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }
}
