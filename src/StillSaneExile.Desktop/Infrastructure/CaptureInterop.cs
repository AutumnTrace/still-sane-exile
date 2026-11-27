using System.Runtime.InteropServices;

namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>The Windows calls of the stash picture collector, and nothing else uses them: Direct3D 11 and
/// DXGI for SSE's own device and the copy from the GPU into memory, Windows.Graphics.Capture through raw
/// COM, and window, monitor and display configuration metadata. Raw COM instead of the WinRT projection
/// keeps the app on its plain <c>net10.0-windows</c> target and out of the 25 MB projection assembly.
/// Every call is about SSE's own device or OS window and display metadata. None touches the game process,
/// its memory, its files or its input. Slot numbers count IUnknown's three methods first, and for WinRT
/// interfaces IInspectable's three more; they follow the Windows SDK's headers.</summary>
internal static unsafe class CaptureInterop
{
    public const uint DXGI_FORMAT_R16G16B16A16_FLOAT = 10;
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);

    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint D3D11_SDK_VERSION = 7;
    private const int D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D11_USAGE_STAGING = 3;
    private const uint D3D11_CPU_ACCESS_READ = 0x20000;
    private const uint D3D11_MAP_READ = 1;

    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IID_IDXGIAdapter1 = new("29038f61-3839-4626-91fd-086879011a05");
    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    private static readonly Guid IID_IGraphicsCaptureSessionStatics = new("2224a540-5974-49aa-b232-0882536f4cb5");
    private static readonly Guid IID_IGraphicsCaptureSession2 = new("2c39ae40-7d2e-5044-804e-8b6799d4cf9e");
    private static readonly Guid IID_IDirect3D11CaptureFramePoolStatics2 = new("589b103f-6bbc-5df5-a991-02e28b3b66d5");
    private static readonly Guid IID_IDirect3DDevice = new("a37624ab-8d5f-4650-9d3e-9eae3d9bc670");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
    private static readonly Guid IID_IClosable = new("30d5a829-7fa4-4026-83bb-d75bae4ea99e");

    private static int _mta;

    [StructLayout(LayoutKind.Sequential)]
    public struct SizeInt32
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Box
    {
        public uint Left;
        public uint Top;
        public uint Front;
        public uint Right;
        public uint Bottom;
        public uint Back;

        public static Box Of(int x, int y, int width, int height) =>
            new() { Left = (uint)x, Top = (uint)y, Front = 0, Right = (uint)(x + width), Bottom = (uint)(y + height), Back = 1 };

        public readonly int Width => (int)(Right - Left);

        public readonly int Height => (int)(Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SampleDesc
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Texture2DDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public SampleDesc SampleDesc;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MappedSubresource
    {
        public void* Data;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, int* featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    /// <summary>Wraps a DXGI device as the WinRT IDirect3DDevice that Windows.Graphics.Capture takes.</summary>
    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, uint length, out nint hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint activatableClassId, ref Guid iid, out nint factory);

    /// <summary>Keeps the process's multithreaded apartment alive, so the thread pool threads that take
    /// the pictures can activate WinRT classes without initialising COM themselves.</summary>
    [DllImport("combase.dll")]
    private static extern int CoIncrementMTAUsage(out nint cookie);

    public static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new COMException($"{what} failed (0x{hr:X8}).", hr);
        }
    }

    public static void Release(ref nint obj)
    {
        if (obj != 0)
        {
            Marshal.Release(obj);
            obj = 0;
        }
    }

    private static nint QueryInterface(nint obj, Guid iid)
    {
        Check(Marshal.QueryInterface(obj, in iid, out var result), "QueryInterface " + iid);
        return result;
    }

    private static void* Slot(nint obj, int index) => (*(void***)obj)[index];

    public static void EnsureMta()
    {
        if (Interlocked.Exchange(ref _mta, 1) == 0)
        {
            Check(CoIncrementMTAUsage(out _), "CoIncrementMTAUsage");
        }
    }

    /// <summary>A WinRT class's activation factory, as the interface <paramref name="iid"/>.</summary>
    private static nint Factory(string className, Guid iid)
    {
        Check(WindowsCreateString(className, (uint)className.Length, out var name), "WindowsCreateString");
        try
        {
            Check(RoGetActivationFactory(name, ref iid, out var factory), "RoGetActivationFactory " + className);
            return factory;
        }
        finally
        {
            WindowsDeleteString(name);
        }
    }

    // ---- Direct3D 11 and DXGI: SSE's own device, and the copy of one frame into memory ----

    public static (nint Device, nint Context) CreateDevice()
    {
        var levels = stackalloc int[] { 0xb100, 0xb000 };
        Check(D3D11CreateDevice(0, D3D_DRIVER_TYPE_HARDWARE, 0, D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, 2, D3D11_SDK_VERSION, out var device, out _, out var context), "D3D11CreateDevice");
        return (device, context);
    }

    /// <summary>The adapter the device runs on. Vendor 0x1414 is Microsoft's software renderer: no GPU.</summary>
    public static (string Name, uint VendorId) Adapter(nint device)
    {
        var dxgi = QueryInterface(device, IID_IDXGIDevice);
        try
        {
            nint adapter;
            Check(((delegate* unmanaged<nint, nint*, int>)Slot(dxgi, 7))(dxgi, &adapter), "IDXGIDevice.GetAdapter");
            try
            {
                var adapter1 = QueryInterface(adapter, IID_IDXGIAdapter1);
                try
                {
                    var size = Marshal.SizeOf<AdapterDesc1>();
                    var buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        Check(((delegate* unmanaged<nint, nint, int>)Slot(adapter1, 10))(adapter1, buffer), "IDXGIAdapter1.GetDesc1");
                        var desc = Marshal.PtrToStructure<AdapterDesc1>(buffer);
                        return (desc.Description.TrimEnd('\0'), desc.VendorId);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    Release(ref adapter1);
                }
            }
            finally
            {
                Release(ref adapter);
            }
        }
        finally
        {
            Release(ref dxgi);
        }
    }

    /// <summary>The same device as the WinRT IDirect3DDevice that the frame pool takes.</summary>
    public static nint WinRtDevice(nint device)
    {
        var dxgi = QueryInterface(device, IID_IDXGIDevice);
        try
        {
            Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable), "CreateDirect3D11DeviceFromDXGIDevice");
            try
            {
                return QueryInterface(inspectable, IID_IDirect3DDevice);
            }
            finally
            {
                Release(ref inspectable);
            }
        }
        finally
        {
            Release(ref dxgi);
        }
    }

    /// <summary>The texture's DXGI format.</summary>
    public static uint TextureFormat(nint texture)
    {
        Texture2DDesc d;
        ((delegate* unmanaged<nint, Texture2DDesc*, void>)Slot(texture, 10))(texture, &d);
        return d.Format;
    }

    /// <summary>Queues a GPU copy of <paramref name="crop"/> into a new CPU-readable texture and returns it.</summary>
    public static nint CopyToStaging(nint device, nint context, nint texture, Box crop, uint format)
    {
        var desc = new Texture2DDesc
        {
            Width = (uint)crop.Width,
            Height = (uint)crop.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDesc = new SampleDesc { Count = 1 },
            Usage = D3D11_USAGE_STAGING,
            CPUAccessFlags = D3D11_CPU_ACCESS_READ,
        };
        nint staging;
        Check(((delegate* unmanaged<nint, Texture2DDesc*, nint, nint*, int>)Slot(device, 5))(device, &desc, 0, &staging), "CreateTexture2D (staging)");
        ((delegate* unmanaged<nint, nint, uint, uint, uint, uint, nint, uint, Box*, void>)Slot(context, 46))(context, staging, 0, 0, 0, 0, texture, 0, &crop);
        return staging;
    }

    /// <summary>Waits for the copy and maps the staging texture for reading.</summary>
    public static MappedSubresource Map(nint context, nint staging)
    {
        MappedSubresource mapped;
        Check(((delegate* unmanaged<nint, nint, uint, uint, uint, MappedSubresource*, int>)Slot(context, 14))(context, staging, 0, D3D11_MAP_READ, 0, &mapped), "Map");
        return mapped;
    }

    public static void Unmap(nint context, nint staging) =>
        ((delegate* unmanaged<nint, nint, uint, void>)Slot(context, 15))(context, staging, 0);

    // ---- Windows.Graphics.Capture, one session per picture ----

    /// <summary>GraphicsCaptureSession.IsSupported().</summary>
    public static bool IsCaptureSupported()
    {
        var statics = Factory("Windows.Graphics.Capture.GraphicsCaptureSession", IID_IGraphicsCaptureSessionStatics);
        try
        {
            byte supported;
            Check(((delegate* unmanaged<nint, byte*, int>)Slot(statics, 6))(statics, &supported), "GraphicsCaptureSession.IsSupported");
            return supported != 0;
        }
        finally
        {
            Release(ref statics);
        }
    }

    /// <summary>IGraphicsCaptureItemInterop, to make a capture item for a window.</summary>
    public static nint ItemInterop()
    {
        var factory = Factory("Windows.Graphics.Capture.GraphicsCaptureItem", new Guid("00000035-0000-0000-c000-000000000046") /* IActivationFactory */);
        try
        {
            return QueryInterface(factory, IID_IGraphicsCaptureItemInterop);
        }
        finally
        {
            Release(ref factory);
        }
    }

    /// <summary>Direct3D11CaptureFramePool's statics, for CreateFreeThreaded.</summary>
    public static nint FramePoolStatics() =>
        Factory("Windows.Graphics.Capture.Direct3D11CaptureFramePool", IID_IDirect3D11CaptureFramePoolStatics2);

    /// <summary>IGraphicsCaptureItemInterop.CreateForWindow.</summary>
    public static nint CreateItemForWindow(nint interop, nint window)
    {
        var iid = IID_IGraphicsCaptureItem;
        nint item;
        Check(((delegate* unmanaged<nint, nint, Guid*, nint*, int>)Slot(interop, 3))(interop, window, &iid, &item), "CreateForWindow");
        return item;
    }

    /// <summary>GraphicsCaptureItem.Size.</summary>
    public static SizeInt32 ItemSize(nint item)
    {
        SizeInt32 size;
        Check(((delegate* unmanaged<nint, SizeInt32*, int>)Slot(item, 7))(item, &size), "GraphicsCaptureItem.Size");
        return size;
    }

    /// <summary>Direct3D11CaptureFramePool.CreateFreeThreaded: frames arrive on a system thread, and
    /// TryGetNextFrame can be called from any thread.</summary>
    public static nint CreateFreeThreadedPool(nint statics, nint winrtDevice, uint format, int buffers, SizeInt32 size)
    {
        nint pool;
        Check(((delegate* unmanaged<nint, nint, uint, int, SizeInt32, nint*, int>)Slot(statics, 6))(statics, winrtDevice, format, buffers, size, &pool), "CreateFreeThreaded");
        return pool;
    }

    /// <summary>Direct3D11CaptureFramePool.CreateCaptureSession.</summary>
    public static nint CreateSession(nint pool, nint item)
    {
        nint session;
        Check(((delegate* unmanaged<nint, nint, nint*, int>)Slot(pool, 10))(pool, item, &session), "CreateCaptureSession");
        return session;
    }

    /// <summary>GraphicsCaptureSession.IsCursorCaptureEnabled = false (Windows 10 2004 and later).</summary>
    public static void HideCursor(nint session)
    {
        var session2 = QueryInterface(session, IID_IGraphicsCaptureSession2);
        try
        {
            Check(((delegate* unmanaged<nint, byte, int>)Slot(session2, 7))(session2, 0), "IsCursorCaptureEnabled");
        }
        finally
        {
            Release(ref session2);
        }
    }

    /// <summary>GraphicsCaptureSession.StartCapture.</summary>
    public static void StartCapture(nint session) =>
        Check(((delegate* unmanaged<nint, int>)Slot(session, 6))(session), "StartCapture");

    /// <summary>Direct3D11CaptureFramePool.TryGetNextFrame: the frame waiting in the pool, or 0 if none
    /// has arrived yet. Each non-zero result is one frame handed over by the OS.</summary>
    public static nint TryGetNextFrame(nint pool)
    {
        nint frame;
        Check(((delegate* unmanaged<nint, nint*, int>)Slot(pool, 7))(pool, &frame), "TryGetNextFrame");
        return frame;
    }

    /// <summary>Direct3D11CaptureFrame.Surface.</summary>
    public static nint FrameSurface(nint frame)
    {
        nint surface;
        Check(((delegate* unmanaged<nint, nint*, int>)Slot(frame, 6))(frame, &surface), "Direct3D11CaptureFrame.Surface");
        return surface;
    }

    /// <summary>Direct3D11CaptureFrame.ContentSize.</summary>
    public static SizeInt32 FrameContentSize(nint frame)
    {
        SizeInt32 size;
        Check(((delegate* unmanaged<nint, SizeInt32*, int>)Slot(frame, 8))(frame, &size), "Direct3D11CaptureFrame.ContentSize");
        return size;
    }

    /// <summary>The ID3D11Texture2D behind a WinRT Direct3D surface.</summary>
    public static nint SurfaceTexture(nint surface)
    {
        var access = QueryInterface(surface, IID_IDirect3DDxgiInterfaceAccess);
        try
        {
            var iid = IID_ID3D11Texture2D;
            nint texture;
            Check(((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(access, 3))(access, &iid, &texture), "IDirect3DDxgiInterfaceAccess.GetInterface");
            return texture;
        }
        finally
        {
            Release(ref access);
        }
    }

    /// <summary>IClosable.Close, then Release. For frames, sessions and pools: closing a session stops
    /// the capture, closing a frame hands its buffer back. Never throws: this runs during cleanup.</summary>
    public static void CloseAndRelease(ref nint obj)
    {
        if (obj == 0)
        {
            return;
        }

        if (Marshal.QueryInterface(obj, in IID_IClosable, out var closable) >= 0)
        {
            ((delegate* unmanaged<nint, int>)Slot(closable, 6))(closable);
            Marshal.Release(closable);
        }

        Release(ref obj);
    }

    // ---- Window and monitor metadata, in physical pixels ----

    public const int GWL_STYLE = -16;
    public const long WS_CAPTION = 0x00C00000;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(nint hwnd, ref Point point);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect value, int size);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    /// <summary>MDT_EFFECTIVE_DPI (0): the Windows display scaling of the monitor, as DPI.</summary>
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    public static nint MonitorOf(nint window) => MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);

    /// <summary>Per-monitor DPI awareness for the calling thread until disposed, so every rectangle is in
    /// physical pixels like the captured frame, whatever the process's own setting (a test host is not
    /// DPI aware).</summary>
    public static DpiScope PhysicalPixels() => new(SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));

    public readonly struct DpiScope(nint previous) : IDisposable
    {
        public void Dispose()
        {
            if (previous != 0)
            {
                SetThreadDpiAwarenessContext(previous);
            }
        }
    }

    // ---- A sub-millisecond wait, while the one frame is on its way ----

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint completion, nint arg, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    /// <summary>A high-resolution waitable timer (Windows 10 1803 and later); Thread.Sleep rounds up to
    /// the 15.6 ms timer tick, longer than a whole capture.</summary>
    public sealed class ShortWait : IDisposable
    {
        private nint _timer = CreateWaitableTimerExW(0, null, 0x2 /* CREATE_WAITABLE_TIMER_HIGH_RESOLUTION */, 0x1F0003 /* TIMER_ALL_ACCESS */);

        public void Wait(TimeSpan duration)
        {
            var due = -duration.Ticks;
            if (_timer == 0 || !SetWaitableTimer(_timer, ref due, 0, 0, 0, false))
            {
                Thread.Sleep(1);
                return;
            }

            WaitForSingleObject(_timer, 1000);
        }

        public void Dispose()
        {
            if (_timer != 0)
            {
                CloseHandle(_timer);
                _timer = 0;
            }
        }
    }
}
