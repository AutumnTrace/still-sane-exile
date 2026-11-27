using System.Runtime.InteropServices;
using StillSaneExile.CaptureSpike.Interop;

namespace StillSaneExile.CaptureSpike.Capture;

/// <summary>What one monitor reports about HDR ("advanced colour" in the display configuration API).</summary>
/// <param name="SdrWhite">The scRGB value of SDR white on this monitor: the "SDR content brightness"
/// slider divided by 80 nits. 1.0 with HDR off.</param>
public sealed record DisplayColor(string GdiName, string FriendlyName, bool HdrSupported, bool HdrOn, int BitsPerChannel, double SdrWhite)
{
    public double SdrWhiteNits => SdrWhite * 80;
}

/// <summary>HDR state per monitor, from QueryDisplayConfig and DisplayConfigGetDeviceInfo. Reads the
/// configuration only; it never changes it.</summary>
internal static unsafe class Displays
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

    /// <summary>DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 (Windows 11 24H2): tells HDR apart from wide
    /// colour on an SDR panel, which the older query reports as "advanced colour" too.</summary>
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

    public static IReadOnlyList<DisplayColor> All()
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
                var path = p + i * PathInfoSize;
                // DISPLAYCONFIG_PATH_SOURCE_INFO: adapter LUID, id, mode index, status (20 bytes);
                // then DISPLAYCONFIG_PATH_TARGET_INFO starts with adapter LUID and id.
                var srcLow = *(uint*)path;
                var srcHigh = *(int*)(path + 4);
                var srcId = *(uint*)(path + 8);
                var tgtLow = *(uint*)(path + 20);
                var tgtHigh = *(int*)(path + 24);
                var tgtId = *(uint*)(path + 28);

                var source = new SourceName { Header = new Header { Type = GET_SOURCE_NAME, Size = Marshal.SizeOf<SourceName>(), AdapterLow = srcLow, AdapterHigh = srcHigh, Id = srcId } };
                if (GetSourceName(ref source) != 0)
                {
                    continue;
                }

                var target = new TargetName { Header = new Header { Type = GET_TARGET_NAME, Size = Marshal.SizeOf<TargetName>(), AdapterLow = tgtLow, AdapterHigh = tgtHigh, Id = tgtId } };
                var friendly = GetTargetName(ref target) == 0 ? target.MonitorFriendlyDeviceName : "";

                var color = new AdvancedColorInfo { Header = new Header { Type = GET_ADVANCED_COLOR_INFO, Size = Marshal.SizeOf<AdvancedColorInfo>(), AdapterLow = tgtLow, AdapterHigh = tgtHigh, Id = tgtId } };
                var haveColor = GetAdvancedColor(ref color) == 0;

                var white = new SdrWhiteLevel { Header = new Header { Type = GET_SDR_WHITE_LEVEL, Size = Marshal.SizeOf<SdrWhiteLevel>(), AdapterLow = tgtLow, AdapterHigh = tgtHigh, Id = tgtId } };
                var sdrWhite = GetSdrWhite(ref white) == 0 && white.Level > 0 ? white.Level / 1000.0 : 1.0;

                bool supported, enabled;
                var color2 = new AdvancedColorInfo2 { Header = new Header { Type = GET_ADVANCED_COLOR_INFO_2, Size = Marshal.SizeOf<AdvancedColorInfo2>(), AdapterLow = tgtLow, AdapterHigh = tgtHigh, Id = tgtId } };
                if (GetAdvancedColor2(ref color2) == 0)
                {
                    // Bit 4: high dynamic range supported. Active colour mode 2: HDR.
                    supported = (color2.Value & 0x10) != 0;
                    enabled = color2.ActiveColorMode == 2;
                }
                else
                {
                    // Older Windows. Bits: supported, enabled, wide colour enforced (not HDR), force disabled.
                    supported = haveColor && (color.Value & 0x1) != 0;
                    enabled = haveColor && (color.Value & 0x2) != 0 && (color.Value & 0x4) == 0 && (color.Value & 0x8) == 0;
                }

                list.Add(new DisplayColor(source.ViewGdiDeviceName, friendly, supported, enabled, haveColor ? (int)color.BitsPerColorChannel : 8, enabled ? sdrWhite : 1.0));
            }
        }

        return list;
    }

    /// <summary>The HDR state of the monitor <paramref name="window"/> is mostly on.</summary>
    public static DisplayColor? ForWindow(nint window)
    {
        var monitor = Win32.MonitorFromWindow(window, Win32.MONITOR_DEFAULTTONEAREST);
        var name = GdiName(monitor);
        return name is null ? null : All().FirstOrDefault(d => d.GdiName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static string? GdiName(nint monitor)
    {
        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        return GetMonitorInfo(monitor, ref info) ? info.szDevice : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Win32.RECT rcMonitor;
        public Win32.RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);
}
