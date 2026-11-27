using StillSaneExile.CaptureSpike.Capture;
using StillSaneExile.CaptureSpike.Interop;
using Windows.Foundation.Metadata;

namespace StillSaneExile.CaptureSpike;

/// <summary>What this machine offers: OS build, GPU, monitors and their HDR state, keyboard repeat.</summary>
internal static class Info
{
    public static int Run()
    {
        using var dpi = Win32.PerMonitorDpi();
        Console.WriteLine($"OS: {Environment.OSVersion.Version} ({(Environment.Is64BitProcess ? "x64" : "x86")})");
        Console.WriteLine($"Windows.Graphics.Capture supported: {WgcFrameSource.IsSupported}");
        const string session = "Windows.Graphics.Capture.GraphicsCaptureSession";
        Console.WriteLine($"  IsCursorCaptureEnabled: {ApiInformation.IsPropertyPresent(session, "IsCursorCaptureEnabled")}, IsBorderRequired: {ApiInformation.IsPropertyPresent(session, "IsBorderRequired")}, MinUpdateInterval: {ApiInformation.IsPropertyPresent(session, "MinUpdateInterval")}");

        try
        {
            using var device = D3DDevice.Create();
            Console.WriteLine($"Default adapter: {device.AdapterName} (vendor 0x{device.VendorId:X4}, device 0x{device.DeviceId:X4}){(device.IsSoftware ? " - software, no GPU" : "")}");
            Outputs(device.Device);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"No D3D11 hardware device: {ex.Message}");
        }

        foreach (var d in Displays.All())
        {
            Console.WriteLine($"Monitor {d.GdiName} \"{d.FriendlyName}\": HDR supported {d.HdrSupported}, HDR on {d.HdrOn}, {d.BitsPerChannel} bits per channel, SDR white {d.SdrWhiteNits:0} nits (scRGB {d.SdrWhite:0.##})");
        }

        var (delay, rate) = KeyRepeat();
        Console.WriteLine($"Keyboard repeat: first repeat after {delay.TotalMilliseconds:0} ms, then {rate:0.#} per second");
        return 0;
    }

    /// <summary>The user's keyboard repeat settings: delay before the first repeat and repeats per second.</summary>
    public static (TimeSpan Delay, double PerSecond) KeyRepeat()
    {
        Win32.SystemParametersInfo(Win32.SPI_GETKEYBOARDDELAY, 0, out var delay, 0);
        Win32.SystemParametersInfo(Win32.SPI_GETKEYBOARDSPEED, 0, out var speed, 0);
        // Delay 0..3 is 250..1000 ms; speed 0..31 is about 2.5..30 repeats per second.
        return (TimeSpan.FromMilliseconds(250 * (delay + 1)), 2.5 + Math.Min(speed, 31u) * 27.5 / 31);
    }

    private static void Outputs(nint device)
    {
        var dxgi = Dx.QueryInterface(device, Dx.IID_IDXGIDevice);
        try
        {
            Dx.Check(Dx.GetAdapter(dxgi, out var adapter), "GetAdapter");
            for (uint i = 0; Dx.EnumOutputs(adapter, i, out var output) >= 0; i++)
            {
                var six = Dx.TryQueryInterface(output, Dx.IID_IDXGIOutput6);
                if (six != 0)
                {
                    var d = Dx.GetOutputDesc1(six);
                    var space = d.ColorSpace == Dx.DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 ? "HDR10 (ST.2084, BT.2020)" : d.ColorSpace == 0 ? "sRGB (G22, BT.709)" : $"colour space {d.ColorSpace}";
                    Console.WriteLine($"  Output {d.DeviceName}: {d.DesktopCoordinates}, {space}, {d.BitsPerColor} bits, luminance {d.MinLuminance:0.###}-{d.MaxLuminance:0} nits (full frame {d.MaxFullFrameLuminance:0})");
                    Dx.Release(ref six);
                }

                Dx.Release(ref output);
            }

            Dx.Release(ref adapter);
        }
        finally
        {
            Dx.Release(ref dxgi);
        }
    }
}
