using Windows.Graphics.Capture;

namespace StillSaneExile.CaptureSpike.Capture;

/// <summary>Whether this machine can run a real capture: Windows 10 2004 or later, Windows.Graphics.Capture
/// supported, an interactive desktop, and a real GPU. CI runners have the Microsoft Basic Render
/// Driver and no guarantees, so capture tests skip there instead of failing.</summary>
public static class CaptureSupport
{
    private static readonly Lazy<string?> Reason = new(Check);

    /// <summary>Null when real captures can run here; otherwise why not.</summary>
    public static string? WhyNot => Reason.Value;

    private static string? Check()
    {
        if (Environment.GetEnvironmentVariable("SSE_CAPTURE_TESTS") == "0")
        {
            return "SSE_CAPTURE_TESTS=0";
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return "needs Windows 10 version 2004 or later";
        }

        if (!Environment.UserInteractive)
        {
            return "no interactive desktop";
        }

        try
        {
            if (!GraphicsCaptureSession.IsSupported())
            {
                return "Windows.Graphics.Capture is not supported here";
            }

            using var device = D3DDevice.Create();
            return device.VendorId == 0x1414 ? $"no GPU ({device.AdapterName})" : null;
        }
        catch (Exception ex)
        {
            return "no D3D11 hardware device: " + ex.Message;
        }
    }
}
