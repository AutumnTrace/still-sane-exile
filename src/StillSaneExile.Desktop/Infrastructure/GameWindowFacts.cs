using System.Runtime.InteropServices;

namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>What a stash picture's notes say about the game window and its monitor, read from the OS at
/// the moment of the picture. Sizes are physical pixels.</summary>
/// <param name="WindowMode">"windowed" (a title bar), "borderless fullscreen" (no title bar, covers the
/// monitor) or "borderless".</param>
/// <param name="MonitorName">The monitor's model as Windows names it, if it says.</param>
/// <param name="ScalingPercent">Windows display scaling of that monitor: 100, 125, 150...</param>
/// <param name="SdrWhite">With HDR on, the scRGB value of SDR white (Windows' "SDR content brightness"
/// over 80 nits); 1.0 with HDR off.</param>
public sealed record GameWindowFacts(
    int ClientWidth, int ClientHeight, string WindowMode,
    string? MonitorName, int MonitorWidth, int MonitorHeight, int ScalingPercent,
    bool Hdr, double SdrWhite)
{
    /// <summary>Reads the facts for <paramref name="window"/>. Call it with per-monitor DPI awareness on the
    /// thread (<see cref="CaptureInterop.PhysicalPixels"/>).</summary>
    public static GameWindowFacts Read(nint window)
    {
        CaptureInterop.GetClientRect(window, out var client);
        var monitor = CaptureInterop.MonitorOf(window);
        var info = new CaptureInterop.MonitorInfoEx { Size = Marshal.SizeOf<CaptureInterop.MonitorInfoEx>() };
        var haveMonitor = CaptureInterop.GetMonitorInfo(monitor, ref info);
        var scaling = CaptureInterop.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? (int)Math.Round(dpi * 100.0 / 96) : 100;

        var style = (long)CaptureInterop.GetWindowLongPtr(window, CaptureInterop.GWL_STYLE);
        CaptureInterop.GetWindowRect(window, out var bounds);
        var mode = (style & CaptureInterop.WS_CAPTION) == CaptureInterop.WS_CAPTION ? "windowed"
            : haveMonitor && bounds.Left <= info.Monitor.Left && bounds.Top <= info.Monitor.Top && bounds.Right >= info.Monitor.Right && bounds.Bottom >= info.Monitor.Bottom ? "borderless fullscreen"
            : "borderless";

        var color = haveMonitor ? DisplayColors.ForGdiName(info.Device) : null;
        return new GameWindowFacts(client.Width, client.Height, mode, color?.FriendlyName is { Length: > 0 } n ? n : null,
            haveMonitor ? info.Monitor.Width : 0, haveMonitor ? info.Monitor.Height : 0, scaling, color?.HdrOn == true, color is { HdrOn: true } c ? c.SdrWhite : 1.0);
    }

    /// <summary>Where the client area sits in a window capture, which covers the window's visible frame
    /// (DWM's extended frame bounds, without the invisible resize borders). For a borderless game window
    /// the two are the same; a title bar and borders are cut off.</summary>
    internal static CaptureInterop.Box ClientInCapture(nint window, int contentWidth, int contentHeight)
    {
        CaptureInterop.GetClientRect(window, out var client);
        var origin = new CaptureInterop.Point();
        CaptureInterop.ClientToScreen(window, ref origin);
        if (CaptureInterop.DwmGetWindowAttribute(window, CaptureInterop.DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, Marshal.SizeOf<CaptureInterop.Rect>()) != 0)
        {
            CaptureInterop.GetWindowRect(window, out frame);
        }

        var x = origin.X - frame.Left;
        var y = origin.Y - frame.Top;
        var left = Math.Clamp(x, 0, contentWidth);
        var top = Math.Clamp(y, 0, contentHeight);
        var right = Math.Clamp(x + client.Width, left, contentWidth);
        var bottom = Math.Clamp(y + client.Height, top, contentHeight);
        return CaptureInterop.Box.Of(left, top, right - left, bottom - top);
    }
}

/// <summary>A monitor's HDR state, from the display configuration. Read only; nothing is changed.</summary>
internal sealed record DisplayColor(string GdiName, string FriendlyName, bool HdrOn, double SdrWhite);

/// <summary>HDR state per monitor, from QueryDisplayConfig and DisplayConfigGetDeviceInfo (as in the
/// capture spike, which found the older "advanced colour" bit reports SDR monitors with colour management
/// as HDR, so Windows 11 24H2's active colour mode is asked first).</summary>
internal static unsafe class DisplayColors
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int GET_SOURCE_NAME = 1;
    private const int GET_TARGET_NAME = 2;
    private const int GET_ADVANCED_COLOR_INFO = 9;
    private const int GET_SDR_WHITE_LEVEL = 11;
    private const int GET_ADVANCED_COLOR_INFO_2 = 15;
    private const int PathInfoSize = 72;
    private const int ModeInfoSize = 64;

    [StructLayout(LayoutKind.Sequential)]
    private struct Header
    {
        public int Type;
        public int Size;
        public uint AdapterLow;
        public int AdapterHigh;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetName
    {
        public Header Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo
    {
        public Header Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo2
    {
        public Header Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
        public uint ActiveColorMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdrWhiteLevel
    {
        public Header Header;
        public uint Level;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint paths, byte* pathArray, ref uint modes, byte* modeArray, nint topology);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetSourceName(ref SourceName info);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetTargetName(ref TargetName info);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetAdvancedColor(ref AdvancedColorInfo info);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetAdvancedColor2(ref AdvancedColorInfo2 info);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetSdrWhite(ref SdrWhiteLevel info);

    /// <summary>The monitor whose GDI name (\\.\DISPLAY1) is <paramref name="gdiName"/>, or null.</summary>
    public static DisplayColor? ForGdiName(string gdiName)
    {
        try
        {
            return All().FirstOrDefault(d => d.GdiName.Equals(gdiName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static List<DisplayColor> All()
    {
        var list = new List<DisplayColor>();
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
        {
            return list;
        }

        var paths = new byte[pathCount * PathInfoSize];
        var modes = new byte[modeCount * ModeInfoSize];
        fixed (byte* p = paths)
        fixed (byte* m = modes)
        {
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, p, ref modeCount, m, 0) != 0)
            {
                return list;
            }

            for (var i = 0; i < pathCount; i++)
            {
                // DISPLAYCONFIG_PATH_SOURCE_INFO: adapter LUID, id, mode index, status (20 bytes);
                // then DISPLAYCONFIG_PATH_TARGET_INFO starts with adapter LUID and id.
                var path = p + i * PathInfoSize;
                var srcLow = *(uint*)path;
                var srcHigh = *(int*)(path + 4);
                var srcId = *(uint*)(path + 8);
                var tgtLow = *(uint*)(path + 20);
                var tgtHigh = *(int*)(path + 24);
                var tgtId = *(uint*)(path + 28);
                Header Target(int type, int size) => new() { Type = type, Size = size, AdapterLow = tgtLow, AdapterHigh = tgtHigh, Id = tgtId };

                var source = new SourceName { Header = new Header { Type = GET_SOURCE_NAME, Size = Marshal.SizeOf<SourceName>(), AdapterLow = srcLow, AdapterHigh = srcHigh, Id = srcId } };
                if (GetSourceName(ref source) != 0)
                {
                    continue;
                }

                var target = new TargetName { Header = Target(GET_TARGET_NAME, Marshal.SizeOf<TargetName>()) };
                var friendly = GetTargetName(ref target) == 0 ? target.MonitorFriendlyDeviceName : "";

                var white = new SdrWhiteLevel { Header = Target(GET_SDR_WHITE_LEVEL, Marshal.SizeOf<SdrWhiteLevel>()) };
                var sdrWhite = GetSdrWhite(ref white) == 0 && white.Level > 0 ? white.Level / 1000.0 : 1.0;

                bool hdr;
                var color2 = new AdvancedColorInfo2 { Header = Target(GET_ADVANCED_COLOR_INFO_2, Marshal.SizeOf<AdvancedColorInfo2>()) };
                if (GetAdvancedColor2(ref color2) == 0)
                {
                    hdr = color2.ActiveColorMode == 2; // DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR
                }
                else
                {
                    // Older Windows. Bits: supported, enabled, wide colour enforced (not HDR), force disabled.
                    var color = new AdvancedColorInfo { Header = Target(GET_ADVANCED_COLOR_INFO, Marshal.SizeOf<AdvancedColorInfo>()) };
                    hdr = GetAdvancedColor(ref color) == 0 && (color.Value & 0x2) != 0 && (color.Value & 0x4) == 0 && (color.Value & 0x8) == 0;
                }

                list.Add(new DisplayColor(source.ViewGdiDeviceName, friendly, hdr, hdr ? sdrWhite : 1.0));
            }
        }

        return list;
    }
}
