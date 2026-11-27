using System.Diagnostics;
using System.Runtime.InteropServices;
using StillSaneExile.CaptureSpike.Interop;
using StillSaneExile.Domain.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace StillSaneExile.CaptureSpike.Capture;

/// <summary>A D3D11 device on the default adapter, plus the GPU-to-memory copy every backend ends with.
/// Creating the device captures nothing, so one device lives as long as its frame source.</summary>
internal sealed unsafe class D3DDevice : IDisposable
{
    private nint _device;
    private nint _context;
    private IDirect3DDevice? _winrt;

    private D3DDevice(nint device, nint context)
    {
        _device = device;
        _context = context;
        var dxgi = Dx.QueryInterface(device, Dx.IID_IDXGIDevice);
        try
        {
            Dx.Check(Dx.GetAdapter(dxgi, out var adapter), "IDXGIDevice.GetAdapter");
            try
            {
                var adapter1 = Dx.QueryInterface(adapter, Dx.IID_IDXGIAdapter1);
                var desc = Dx.GetAdapterDesc1(adapter1);
                Dx.Release(ref adapter1);
                AdapterName = desc.Description.TrimEnd('\0');
                VendorId = desc.VendorId;
                DeviceId = desc.DeviceId;
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

    public nint Device => _device;

    public string AdapterName { get; }

    public uint VendorId { get; }

    public uint DeviceId { get; }

    /// <summary>Microsoft Basic Render Driver: no GPU, as on CI runners.</summary>
    public bool IsSoftware => VendorId == 0x1414 && DeviceId == 0x8C;

    public static D3DDevice Create()
    {
        var levels = stackalloc int[] { 0xb100, 0xb000 };
        Dx.Check(Dx.D3D11CreateDevice(0, Dx.D3D_DRIVER_TYPE_HARDWARE, 0, Dx.D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, 2, Dx.D3D11_SDK_VERSION, out var device, out _, out var context), "D3D11CreateDevice");
        return new D3DDevice(device, context);
    }

    /// <summary>The same device as the WinRT IDirect3DDevice that Windows.Graphics.Capture takes.</summary>
    public IDirect3DDevice WinRt
    {
        get
        {
            if (_winrt is null)
            {
                var dxgi = Dx.QueryInterface(_device, Dx.IID_IDXGIDevice);
                try
                {
                    Dx.Check(Dx.CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable), "CreateDirect3D11DeviceFromDXGIDevice");
                    try
                    {
                        _winrt = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                    }
                    finally
                    {
                        Marshal.Release(inspectable);
                    }
                }
                finally
                {
                    Dx.Release(ref dxgi);
                }
            }

            return _winrt;
        }
    }

    /// <summary>Queues a GPU copy of <paramref name="crop"/> from <paramref name="texture"/> into a new
    /// CPU-readable texture and returns it. The source can be released (and its capture closed) right
    /// after: the copy keeps what it needs alive.</summary>
    public Staged BeginCopy(nint texture, Dx.D3D11_BOX crop)
    {
        var src = Dx.GetDesc(texture);
        var format = src.Format switch
        {
            Dx.DXGI_FORMAT_B8G8R8A8_UNORM => FramePixelFormat.Bgra8,
            Dx.DXGI_FORMAT_R16G16B16A16_FLOAT => FramePixelFormat.RgbaHalf,
            _ => throw new NotSupportedException($"DXGI format {src.Format} is not handled by the spike."),
        };
        var desc = new Dx.D3D11_TEXTURE2D_DESC
        {
            Width = (uint)crop.Width,
            Height = (uint)crop.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = src.Format,
            SampleDesc = new Dx.DXGI_SAMPLE_DESC { Count = 1 },
            Usage = Dx.D3D11_USAGE_STAGING,
            CPUAccessFlags = Dx.D3D11_CPU_ACCESS_READ,
        };
        Dx.Check(Dx.CreateTexture2D(_device, &desc, 0, out var staging), "CreateTexture2D (staging)");
        Dx.CopySubresourceRegion(_context, staging, texture, &crop);
        return new Staged(staging, crop.Width, crop.Height, format);
    }

    /// <summary>Waits for the copy and moves the pixels into a <see cref="Frame"/>.</summary>
    public Frame EndCopy(Staged staged, double sdrWhite)
    {
        var tex = staged.Texture;
        try
        {
            Dx.D3D11_MAPPED_SUBRESOURCE mapped;
            Dx.Check(Dx.Map(_context, tex, &mapped), "Map");
            try
            {
                var bpp = Frame.BytesPer(staged.Format);
                var rowBytes = staged.Width * bpp;
                var pixels = GC.AllocateUninitializedArray<byte>(rowBytes * staged.Height);
                fixed (byte* dst = pixels)
                {
                    for (var y = 0; y < staged.Height; y++)
                    {
                        Buffer.MemoryCopy((byte*)mapped.pData + (long)y * mapped.RowPitch, dst + (long)y * rowBytes, rowBytes, rowBytes);
                    }
                }

                return new Frame(staged.Width, staged.Height, rowBytes, staged.Format, pixels, Stopwatch.GetTimestamp(), sdrWhite);
            }
            finally
            {
                Dx.Unmap(_context, tex);
            }
        }
        finally
        {
            Dx.Release(ref tex);
        }
    }

    public void Dispose()
    {
        _winrt?.Dispose();
        _winrt = null;
        Dx.Release(ref _context);
        Dx.Release(ref _device);
    }

    public readonly record struct Staged(nint Texture, int Width, int Height, FramePixelFormat Format);
}
