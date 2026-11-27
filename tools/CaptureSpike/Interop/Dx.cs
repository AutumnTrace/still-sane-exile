using System.Runtime.InteropServices;

namespace StillSaneExile.CaptureSpike.Interop;

/// <summary>The handful of D3D11 and DXGI calls the spike needs, as raw COM vtable calls (no package).
/// Slot numbers follow the order of methods in d3d11.h, dxgi.h, dxgi1_2.h, dxgi1_5.h and dxgi1_6.h,
/// counting IUnknown's three methods first.</summary>
internal static unsafe class Dx
{
    public const uint DXGI_FORMAT_R16G16B16A16_FLOAT = 10;
    public const uint DXGI_FORMAT_R10G10B10A2_UNORM = 24;
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;

    public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    public const uint D3D11_SDK_VERSION = 7;
    public const int D3D_DRIVER_TYPE_UNKNOWN = 0;
    public const int D3D_DRIVER_TYPE_HARDWARE = 1;
    public const uint D3D11_USAGE_STAGING = 3;
    public const uint D3D11_CPU_ACCESS_READ = 0x20000;
    public const uint D3D11_MAP_READ = 1;

    public const uint DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x20;
    public const uint DXGI_SWAP_EFFECT_DISCARD = 0;
    public const uint DXGI_SWAP_EFFECT_FLIP_DISCARD = 4;
    public const uint DXGI_SCALING_STRETCH = 0;
    public const uint DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 = 12;

    public const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    public const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
    public const int DXGI_ERROR_UNSUPPORTED = unchecked((int)0x887A0004);
    public const int E_ACCESSDENIED = unchecked((int)0x80070005);

    public static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IID_IDXGIAdapter1 = new("29038f61-3839-4626-91fd-086879011a05");
    public static readonly Guid IID_IDXGIFactory2 = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    public static readonly Guid IID_IDXGIOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
    public static readonly Guid IID_IDXGIOutput5 = new("80a07424-ab52-42eb-833c-0c42fd282d98");
    public static readonly Guid IID_IDXGIOutput6 = new("068346e8-aaec-4b84-add7-137f513f77a1");
    public static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public void* pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_BOX
    {
        public uint left;
        public uint top;
        public uint front;
        public uint right;
        public uint bottom;
        public uint back;

        public static D3D11_BOX Of(int x, int y, int width, int height) =>
            new() { left = (uint)x, top = (uint)y, front = 0, right = (uint)(x + width), bottom = (uint)(y + height), back = 1 };

        public readonly int Width => (int)(right - left);

        public readonly int Height => (int)(bottom - top);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SWAP_CHAIN_DESC1
    {
        public uint Width;
        public uint Height;
        public uint Format;
        public int Stereo;
        public DXGI_SAMPLE_DESC SampleDesc;
        public uint BufferUsage;
        public uint BufferCount;
        public uint Scaling;
        public uint SwapEffect;
        public uint AlphaMode;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_ADAPTER_DESC1
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public Win32.RECT DesktopCoordinates;
        public int AttachedToDesktop;
        public uint Rotation;
        public nint Monitor;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_OUTPUT_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public Win32.RECT DesktopCoordinates;
        public int AttachedToDesktop;
        public uint Rotation;
        public nint Monitor;
        public uint BitsPerColor;
        public uint ColorSpace;
        public float RedX, RedY, GreenX, GreenY, BlueX, BlueY, WhiteX, WhiteY;
        public float MinLuminance;
        public float MaxLuminance;
        public float MaxFullFrameLuminance;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_FRAME_INFO
    {
        public long LastPresentTime;
        public long LastMouseUpdateTime;
        public uint AccumulatedFrames;
        public int RectsCoalesced;
        public int ProtectedContentMaskedOut;
        public Win32.POINT PointerPosition;
        public int PointerVisible;
        public uint TotalMetadataBufferSize;
        public uint PointerShapeBufferSize;
    }

    [DllImport("d3d11.dll")]
    public static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, int* featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    /// <summary>Wraps a DXGI device as the WinRT IDirect3DDevice that Windows.Graphics.Capture takes.</summary>
    [DllImport("d3d11.dll")]
    public static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    public static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new COMException($"{what} failed (0x{hr:X8}).", hr);
        }
    }

    public static nint QueryInterface(nint obj, Guid iid)
    {
        Check(Marshal.QueryInterface(obj, in iid, out var result), "QueryInterface " + iid);
        return result;
    }

    public static nint TryQueryInterface(nint obj, Guid iid) => Marshal.QueryInterface(obj, in iid, out var result) >= 0 ? result : 0;

    public static void Release(ref nint obj)
    {
        if (obj != 0)
        {
            Marshal.Release(obj);
            obj = 0;
        }
    }

    private static void* Slot(nint obj, int index) => (*(void***)obj)[index];

    // ID3D11Device
    public static int CreateTexture2D(nint device, D3D11_TEXTURE2D_DESC* desc, nint initialData, out nint texture)
    {
        nint t;
        var hr = ((delegate* unmanaged<nint, D3D11_TEXTURE2D_DESC*, nint, nint*, int>)Slot(device, 5))(device, desc, initialData, &t);
        texture = t;
        return hr;
    }

    public static int CreateRenderTargetView(nint device, nint resource, out nint view)
    {
        nint v;
        var hr = ((delegate* unmanaged<nint, nint, nint, nint*, int>)Slot(device, 9))(device, resource, 0, &v);
        view = v;
        return hr;
    }

    // ID3D11DeviceContext
    public static int Map(nint context, nint resource, D3D11_MAPPED_SUBRESOURCE* mapped) =>
        ((delegate* unmanaged<nint, nint, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)Slot(context, 14))(context, resource, 0, D3D11_MAP_READ, 0, mapped);

    public static void Unmap(nint context, nint resource) =>
        ((delegate* unmanaged<nint, nint, uint, void>)Slot(context, 15))(context, resource, 0);

    public static void CopySubresourceRegion(nint context, nint dst, nint src, D3D11_BOX* box) =>
        ((delegate* unmanaged<nint, nint, uint, uint, uint, uint, nint, uint, D3D11_BOX*, void>)Slot(context, 46))(context, dst, 0, 0, 0, 0, src, 0, box);

    public static void UpdateSubresource(nint context, nint dst, D3D11_BOX* box, void* data, uint rowPitch) =>
        ((delegate* unmanaged<nint, nint, uint, D3D11_BOX*, void*, uint, uint, void>)Slot(context, 48))(context, dst, 0, box, data, rowPitch, 0);

    public static void ClearRenderTargetView(nint context, nint view, float* rgba) =>
        ((delegate* unmanaged<nint, nint, float*, void>)Slot(context, 50))(context, view, rgba);

    // ID3D11Texture2D
    public static D3D11_TEXTURE2D_DESC GetDesc(nint texture)
    {
        D3D11_TEXTURE2D_DESC d;
        ((delegate* unmanaged<nint, D3D11_TEXTURE2D_DESC*, void>)Slot(texture, 10))(texture, &d);
        return d;
    }

    // IDXGIObject
    public static int GetParent(nint obj, Guid iid, out nint parent)
    {
        nint p;
        var hr = ((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(obj, 6))(obj, &iid, &p);
        parent = p;
        return hr;
    }

    // IDXGIDevice
    public static int GetAdapter(nint dxgiDevice, out nint adapter)
    {
        nint a;
        var hr = ((delegate* unmanaged<nint, nint*, int>)Slot(dxgiDevice, 7))(dxgiDevice, &a);
        adapter = a;
        return hr;
    }

    // IDXGIAdapter / IDXGIAdapter1
    public static int EnumOutputs(nint adapter, uint index, out nint output)
    {
        nint o;
        var hr = ((delegate* unmanaged<nint, uint, nint*, int>)Slot(adapter, 7))(adapter, index, &o);
        output = o;
        return hr;
    }

    public static DXGI_ADAPTER_DESC1 GetAdapterDesc1(nint adapter1)
    {
        var size = Marshal.SizeOf<DXGI_ADAPTER_DESC1>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Check(((delegate* unmanaged<nint, nint, int>)Slot(adapter1, 10))(adapter1, buffer), "IDXGIAdapter1.GetDesc1");
            return Marshal.PtrToStructure<DXGI_ADAPTER_DESC1>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // IDXGIOutput
    public static DXGI_OUTPUT_DESC GetOutputDesc(nint output)
    {
        var size = Marshal.SizeOf<DXGI_OUTPUT_DESC>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Check(((delegate* unmanaged<nint, nint, int>)Slot(output, 7))(output, buffer), "IDXGIOutput.GetDesc");
            return Marshal.PtrToStructure<DXGI_OUTPUT_DESC>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // IDXGIOutput1.DuplicateOutput
    public static int DuplicateOutput(nint output1, nint device, out nint duplication)
    {
        nint d;
        var hr = ((delegate* unmanaged<nint, nint, nint*, int>)Slot(output1, 22))(output1, device, &d);
        duplication = d;
        return hr;
    }

    // IDXGIOutput5.DuplicateOutput1
    public static int DuplicateOutput1(nint output5, nint device, uint[] formats, out nint duplication)
    {
        nint d;
        fixed (uint* f = formats)
        {
            var hr = ((delegate* unmanaged<nint, nint, uint, uint, uint*, nint*, int>)Slot(output5, 26))(output5, device, 0, (uint)formats.Length, f, &d);
            duplication = d;
            return hr;
        }
    }

    // IDXGIOutput6.GetDesc1
    public static DXGI_OUTPUT_DESC1 GetOutputDesc1(nint output6)
    {
        var size = Marshal.SizeOf<DXGI_OUTPUT_DESC1>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Check(((delegate* unmanaged<nint, nint, int>)Slot(output6, 27))(output6, buffer), "IDXGIOutput6.GetDesc1");
            return Marshal.PtrToStructure<DXGI_OUTPUT_DESC1>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // IDXGIOutputDuplication
    public static int AcquireNextFrame(nint duplication, uint timeoutMs, out DXGI_OUTDUPL_FRAME_INFO info, out nint resource)
    {
        DXGI_OUTDUPL_FRAME_INFO i;
        nint r;
        var hr = ((delegate* unmanaged<nint, uint, DXGI_OUTDUPL_FRAME_INFO*, nint*, int>)Slot(duplication, 8))(duplication, timeoutMs, &i, &r);
        info = i;
        resource = r;
        return hr;
    }

    public static int ReleaseFrame(nint duplication) =>
        ((delegate* unmanaged<nint, int>)Slot(duplication, 14))(duplication);

    // IDXGIFactory2.CreateSwapChainForHwnd
    public static int CreateSwapChainForHwnd(nint factory2, nint device, nint hwnd, DXGI_SWAP_CHAIN_DESC1* desc, out nint swapChain)
    {
        nint s;
        var hr = ((delegate* unmanaged<nint, nint, nint, DXGI_SWAP_CHAIN_DESC1*, nint, nint, nint*, int>)Slot(factory2, 15))(factory2, device, hwnd, desc, 0, 0, &s);
        swapChain = s;
        return hr;
    }

    // IDXGISwapChain
    public static int Present(nint swapChain, uint syncInterval) =>
        ((delegate* unmanaged<nint, uint, uint, int>)Slot(swapChain, 8))(swapChain, syncInterval, 0);

    public static int GetBuffer(nint swapChain, Guid iid, out nint buffer)
    {
        nint b;
        var hr = ((delegate* unmanaged<nint, uint, Guid*, nint*, int>)Slot(swapChain, 9))(swapChain, 0, &iid, &b);
        buffer = b;
        return hr;
    }

    // IDirect3DDxgiInterfaceAccess.GetInterface, IGraphicsCaptureItemInterop.CreateForWindow
    public static int CallSlot3(nint obj, nint arg, Guid iid, out nint result)
    {
        nint r;
        var hr = ((delegate* unmanaged<nint, nint, Guid*, nint*, int>)Slot(obj, 3))(obj, arg, &iid, &r);
        result = r;
        return hr;
    }

    public static int GetInterface(nint access, Guid iid, out nint result)
    {
        nint r;
        var hr = ((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(access, 3))(access, &iid, &r);
        result = r;
        return hr;
    }
}
