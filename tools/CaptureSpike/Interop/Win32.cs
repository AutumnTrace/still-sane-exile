using System.Runtime.InteropServices;
using System.Text;

namespace StillSaneExile.CaptureSpike.Interop;

/// <summary>User32, GDI and DWM calls for the spike. Window and monitor metadata, the test windows,
/// PrintWindow and the keyboard hook of the live mode.</summary>
internal static class Win32
{
    public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const uint WS_POPUP = 0x80000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_PRINT = 0x0317;
    public const uint WM_PRINTCLIENT = 0x0318;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;
    public const uint PM_REMOVE = 0x0001;

    public const uint PW_CLIENTONLY = 0x1;
    public const uint PW_RENDERFULLCONTENT = 0x2;

    public const int WH_KEYBOARD_LL = 13;
    public const uint LLKHF_LOWER_IL_INJECTED = 0x02;
    public const uint LLKHF_INJECTED = 0x10;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public const uint SPI_GETKEYBOARDSPEED = 0x000A;
    public const uint SPI_GETKEYBOARDDELAY = 0x0016;

    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
    public static readonly nint HWND_TOPMOST = -1;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;

        public override readonly string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}×{Height}";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    public delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    public delegate nint LowLevelHookProc(int nCode, nint wParam, nint lParam);

    public delegate void WinEventDelegate(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(int exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessage(out MSG msg, nint hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG msg, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessage(ref MSG msg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint hwnd, int cmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(nint hwnd, ref POINT point);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(uint action, uint param, out uint value, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int idHook, LowLevelHookProc proc, nint module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hook, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventDelegate proc, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern nint CreateDIBSection(nint hdc, ref BITMAPINFOHEADER bmi, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GdiFlush();

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out RECT value, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint completion, nint arg, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    /// <summary>Sleeps with sub-millisecond precision (Thread.Sleep rounds up to the 15.6 ms timer tick).</summary>
    public static void PreciseSleep(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var timer = CreateWaitableTimerEx(0, null, 0x2 /* CREATE_WAITABLE_TIMER_HIGH_RESOLUTION */, 0x1F0003);
        if (timer == 0)
        {
            Thread.Sleep(duration);
            return;
        }

        try
        {
            var due = -duration.Ticks;
            if (SetWaitableTimer(timer, ref due, 0, 0, 0, false))
            {
                WaitForSingleObject(timer, uint.MaxValue);
            }
        }
        finally
        {
            CloseHandle(timer);
        }
    }

    /// <summary>INPUT with KEYBDINPUT, x64 layout. Used only by the live mode's self-test.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct KeyInput
    {
        [FieldOffset(0)]
        public uint Type;
        [FieldOffset(8)]
        public ushort Vk;
        [FieldOffset(10)]
        public ushort Scan;
        [FieldOffset(12)]
        public uint Flags;
        [FieldOffset(16)]
        public uint Time;
        [FieldOffset(24)]
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, KeyInput[] inputs, int size);

    public static string WindowTitle(nint hwnd)
    {
        var sb = new StringBuilder(256);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool OwnedByThisProcess(nint hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>Per-monitor DPI awareness for the calling thread until disposed, so every rectangle is
    /// in physical pixels like the capture APIs' frames (the test host is not DPI aware by manifest).</summary>
    public static DpiScope PerMonitorDpi() => new(SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));

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
}
