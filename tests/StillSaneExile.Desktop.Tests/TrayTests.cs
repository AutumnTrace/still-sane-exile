using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.Views;

namespace StillSaneExile.Desktop.Tests;

/// <summary>A test that puts a real icon in the notification area (and takes it out again): skipped, not
/// failed, where there is no desktop or no taskbar, or with SSE_TRAY_TESTS=0.</summary>
public sealed class RealTrayFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> WhyNot = new(() =>
        Environment.GetEnvironmentVariable("SSE_TRAY_TESTS") == "0" ? "SSE_TRAY_TESTS=0"
        : !Environment.UserInteractive ? "no interactive desktop"
        : TrayTests.FindWindow("Shell_TrayWnd", null) == 0 ? "no taskbar (Explorer is not running)"
        : null);

    public RealTrayFactAttribute()
    {
        if (WhyNot.Value is { } reason)
        {
            Skip = "Needs a real notification area: " + reason;
        }
    }
}

public class TrayTests
{
    private const int WM_NCDESTROY = 0x0082;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_COMMAND = 0x0111;
    private const int NIN_SELECT = 0x0400;
    private const int NIN_KEYSELECT = 0x0401;
    private const int NIN_BALLOONUSERCLICK = 0x0405;
    private const uint NIM_MODIFY = 0x1;
    private const uint NIM_DELETE = 0x2;

    [Fact]
    public void Starting_puts_the_icon_in_the_notification_area() => Sta.Run(() =>
    {
        using var rig = new TrayRig();

        Assert.True(rig.Icon.IsShown);
    });

    [Fact]
    public void A_double_click_brings_the_main_window_back() => Sta.Run(() =>
    {
        using var rig = new TrayRig();

        rig.Icon.DoubleClick();

        Assert.Equal(1, rig.Window.BroughtToFront);
        Assert.False(rig.Window.IsClosed);
    });

    [Fact]
    public void Closing_the_window_hides_it_and_SSE_keeps_running() => Sta.Run(() =>
    {
        using var rig = new TrayRig();

        rig.Window.ClickClose();

        Assert.False(rig.Window.IsVisible);
        Assert.False(rig.Window.IsClosed);
        Assert.True(rig.Icon.IsShown);
        Assert.Equal(0, rig.Icon.Disposals);

        rig.Icon.DoubleClick();

        Assert.True(rig.Window.IsVisible);
    });

    [Fact]
    public void Minimizing_hides_the_window_to_the_notification_area() => Sta.Run(() =>
    {
        using var rig = new TrayRig();

        rig.Window.Minimize();

        Assert.False(rig.Window.IsVisible); // hidden: no taskbar button
        Assert.False(rig.Window.IsClosed);

        rig.Icon.DoubleClick();

        Assert.True(rig.Window.IsVisible);
        Assert.Equal(WindowState.Normal, rig.Window.WindowState);
    });

    [Fact]
    public void With_both_switches_off_the_window_closes_and_minimizes_as_it_used_to() => Sta.Run(() =>
    {
        using var rig = new TrayRig(s =>
        {
            s.CloseToTray = false;
            s.MinimizeToTray = false;
        });

        rig.Window.Minimize();

        Assert.True(rig.Window.IsVisible);
        Assert.Equal(WindowState.Minimized, rig.Window.WindowState);

        rig.Window.ClickClose();

        Assert.True(rig.Window.IsClosed); // and in the app, closing it ends SSE
        Assert.Empty(rig.Icon.Notices);
        Assert.True(rig.Tray.InstallUpdateOnExit);
    });

    [Fact]
    public void Quit_really_closes_the_window_and_the_update_installs_on_exit() => Sta.Run(() =>
    {
        using var rig = new TrayRig();
        rig.Window.ClickClose();

        rig.Icon.ChooseQuit();

        Assert.True(rig.Window.IsClosed);
        Assert.True(rig.Tray.InstallUpdateOnExit);
        Assert.Equal(0, rig.Window.BroughtToFront);
    });

    [Fact]
    public void When_Windows_ends_the_session_the_window_closes_and_the_update_waits_for_the_next_start() => Sta.Run(() =>
    {
        using var rig = new TrayRig();

        rig.Tray.SessionEnding();
        rig.Window.ClickClose(); // the app's shutdown closes its windows

        Assert.True(rig.Window.IsClosed);
        Assert.False(rig.Tray.InstallUpdateOnExit);
        Assert.Empty(rig.Icon.Notices);
    });

    [Fact]
    public void The_player_is_told_once_where_the_window_went_and_never_over_the_game() => Sta.Run(() =>
    {
        using var rig = new TrayRig();
        rig.GameInFront = true;

        rig.Window.Minimize();

        Assert.Empty(rig.Icon.Notices);
        Assert.False(rig.Settings.TrayNoticeShown);

        rig.Icon.DoubleClick();
        rig.GameInFront = false;
        rig.Window.ClickClose();

        var notice = Assert.Single(rig.Icon.Notices);
        Assert.Equal(("Still Sane, Exile? is still running", "It's here by the clock, and its keys still work. Double-click to open it; right-click for Quit."), notice);
        Assert.True(rig.Settings.TrayNoticeShown);

        rig.Icon.DoubleClick();
        rig.Window.Minimize();
        rig.Icon.DoubleClick();
        rig.Window.ClickClose();

        Assert.Single(rig.Icon.Notices);
    });

    [Fact]
    public void Without_the_icon_in_the_notification_area_the_notice_waits() => Sta.Run(() =>
    {
        using var rig = new TrayRig(start: false);

        rig.Window.ClickClose();

        Assert.Empty(rig.Icon.Notices);
        Assert.False(rig.Settings.TrayNoticeShown);
    });

    [Fact]
    public void Exit_removes_the_icon_and_nothing_answers_it_after() => Sta.Run(() =>
    {
        using var rig = new TrayRig();

        rig.Tray.Dispose();
        rig.Tray.Dispose();
        rig.Icon.DoubleClick();
        rig.Icon.ChooseQuit();

        Assert.False(rig.Icon.IsShown);
        Assert.Equal(1, rig.Icon.Disposals);
        Assert.Equal(0, rig.Window.BroughtToFront);
        Assert.False(rig.Window.IsClosed);
    });

    [Fact]
    public void A_real_main_window_closes_and_minimizes_to_the_tray_and_Quit_closes_it() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var window = new MainWindow
        {
            DataContext = app.Shell,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = 0,
            Width = 1440,
            Height = 1000,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        var closed = false;
        window.Closed += (_, _) => closed = true;
        var icon = new FakeTrayIcon();
        using var tray = new Tray(icon, window, app.Settings, () => false);
        tray.Start();
        try
        {
            window.Show();

            window.Close();
            Sta.Drain();

            Assert.False(closed);
            Assert.False(window.IsVisible);
            Assert.Single(icon.Notices);

            icon.DoubleClick();

            Assert.True(window.IsVisible);

            window.WindowState = WindowState.Minimized;
            Sta.Drain();

            Assert.False(window.IsVisible);

            icon.DoubleClick();

            Sta.Until(() => window.WindowState == WindowState.Normal, "the window to be restored");
            Assert.True(window.IsVisible);

            icon.ChooseQuit();

            Assert.True(closed);
        }
        finally
        {
            if (!closed)
            {
                tray.Quit();
            }
        }
    });

    [Fact]
    public void Opening_restores_a_minimized_main_window_and_shows_a_hidden_one() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var window = new MainWindow
        {
            DataContext = app.Shell,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = 0,
            Width = 1440,
            Height = 1000,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        try
        {
            window.Show();
            window.WindowState = WindowState.Minimized;
            Sta.Drain();

            window.BringToFront();

            Sta.Until(() => window.WindowState == WindowState.Normal, "the window to be restored");
            Assert.True(window.IsVisible);

            window.Hide();
            window.BringToFront();

            Assert.True(window.IsVisible);
            Assert.Equal(WindowState.Normal, window.WindowState);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void The_icon_window_opens_on_a_double_click_Enter_or_its_notification_but_not_a_single_click() => Sta.Run(() =>
    {
        using var icon = new TrayIcon(); // never shown: its window only, nothing in the notification area
        var opened = 0;
        var quits = 0;
        icon.OpenRequested += () => opened++;
        icon.QuitRequested += () => quits++;

        Shell(icon, WM_LBUTTONDOWN);
        Shell(icon, WM_LBUTTONUP);
        Shell(icon, NIN_SELECT);
        Sta.Drain();

        Assert.Equal(0, opened);

        Shell(icon, WM_LBUTTONDBLCLK);
        Sta.Drain();

        Assert.Equal(1, opened);

        Shell(icon, NIN_KEYSELECT);
        Sta.Drain();

        Assert.Equal(2, opened);

        Shell(icon, NIN_BALLOONUSERCLICK); // a click on its notification
        Sta.Drain();

        Assert.Equal(3, opened);
        Assert.Equal(0, quits);
        Assert.False(icon.IsShown);
        Assert.False(icon.Notify(Tray.NoticeTitle, Tray.NoticeText)); // nothing to show it from
    });

    [Fact]
    public void The_icon_menu_opens_the_window_or_quits() => Sta.Run(() =>
    {
        using var icon = new TrayIcon();
        var opened = 0;
        var quits = 0;
        icon.OpenRequested += () => opened++;
        icon.QuitRequested += () => quits++;

        _ = SendMessage(icon.Handle, WM_COMMAND, TrayIcon.OpenCommand, 0);
        Sta.Drain();

        Assert.Equal((1, 0), (opened, quits));

        _ = SendMessage(icon.Handle, WM_COMMAND, TrayIcon.QuitCommand, 0);
        Sta.Drain();

        Assert.Equal((1, 1), (opened, quits));
    });

    [Fact]
    public void A_click_the_app_has_not_answered_yet_is_dropped_on_exit() => Sta.Run(() =>
    {
        var icon = new TrayIcon();
        var handle = icon.Handle;
        var opened = 0;
        icon.OpenRequested += () => opened++;

        Shell(icon, WM_LBUTTONDBLCLK);
        icon.Dispose();
        Sta.Drain();

        Assert.Equal(0, opened);
        Assert.False(IsWindow(handle));
    });

    [Fact]
    public void The_tray_picture_is_the_app_icon_at_the_size_asked_or_the_next_larger()
    {
        var ico = TrayIcon.IconFile();

        // sse.ico holds 16, 20, 24, 32, 40, 48, 64, 128 and 256 px images (scripts/make-icon.ps1).
        Assert.Equal(16, Width(ico, TrayIcon.PickImage(ico, 16)));
        Assert.Equal(20, Width(ico, TrayIcon.PickImage(ico, 20)));
        Assert.Equal(24, Width(ico, TrayIcon.PickImage(ico, 24)));
        Assert.Equal(32, Width(ico, TrayIcon.PickImage(ico, 30)));
        Assert.Equal(256, Width(ico, TrayIcon.PickImage(ico, 300)));
        Assert.Throws<System.IO.InvalidDataException>(() => TrayIcon.PickImage(new byte[] { 0, 0, 2, 0, 0, 0 }, 16));
    }

    [RealTrayFact]
    public void The_icon_sits_in_the_notification_area_until_SSE_exits() => Sta.Run(() =>
    {
        var icon = new TrayIcon();
        var handle = icon.Handle;
        var lastLook = AtTheEnd(handle);
        try
        {
            icon.Show();

            Assert.True(icon.IsShown);
            Assert.True(InTray(handle));

            // TaskbarCreated with the icon still there: it stays.
            _ = SendMessage(handle, (int)RegisterWindowMessage("TaskbarCreated"), 0, 0);

            Assert.True(InTray(handle));

            // Explorer restarted: its notification area is empty, and it says so with TaskbarCreated.
            Assert.True(TakeOut(handle));
            _ = SendMessage(handle, (int)RegisterWindowMessage("TaskbarCreated"), 0, 0);

            Assert.True(icon.IsShown);
            Assert.True(InTray(handle));
        }
        finally
        {
            icon.Dispose();
        }

        Assert.False(lastLook() ?? true);
        Assert.False(icon.IsShown);
        Assert.False(IsWindow(handle));
    });

    [RealTrayFact]
    public void The_icon_goes_when_its_window_goes_without_an_orderly_exit()
    {
        TrayIcon? icon = null;
        Func<bool?>? lastLook = null;

        // The thread's dispatcher shuts down at the end, which destroys the icon's window.
        Sta.Run(() =>
        {
            icon = new TrayIcon();
            lastLook = AtTheEnd(icon.Handle);
            icon.Show();
            Assert.True(InTray(icon.Handle));
        });

        Assert.False(lastLook!() ?? true);
        Assert.False(icon!.IsShown);
        Assert.False(IsWindow(icon.Handle));
    }

    /// <summary>A started <see cref="Tray"/> on a fake icon and main window, with real settings in a temp folder.</summary>
    private sealed class TrayRig : IDisposable
    {
        private readonly TempDir _dir = new();

        public TrayRig(Action<Settings>? configure = null, bool start = true)
        {
            Settings = new Settings(new Store(_dir.Sub("sse.db")));
            configure?.Invoke(Settings);
            Tray = new Tray(Icon, Window, Settings, () => GameInFront);
            if (start)
            {
                Tray.Start();
            }
        }

        public FakeTrayIcon Icon { get; } = new();

        public FakeMainWindow Window { get; } = new();

        public Settings Settings { get; }

        public Tray Tray { get; }

        public bool GameInFront { get; set; }

        public void Dispose()
        {
            Tray.Dispose();
            _dir.Dispose();
        }
    }

    /// <summary>What the notification area sends about the icon (version 4: the event in the low word,
    /// the icon's id in the high word).</summary>
    private static void Shell(TrayIcon icon, int what) =>
        _ = SendMessage(icon.Handle, TrayIcon.CallbackMessage, 0, what | (1 << 16));

    /// <summary>Asks the notification area itself whether it holds the icon: changing nothing about an icon
    /// succeeds only while it is there.</summary>
    private static bool InTray(nint hwnd)
    {
        var data = Identity(hwnd);
        return Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>Whether the notification area still held the icon at its window's very last message
    /// (WM_NCDESTROY, after SSE's own WM_DESTROY, with the window still there); null if that never came.
    /// Asked later, the notification area says no for any icon of a destroyed window, removed or not.</summary>
    private static Func<bool?> AtTheEnd(nint hwnd)
    {
        bool? inTray = null;
        HwndSource.FromHwnd(hwnd).AddHook((nint h, int msg, nint w, nint l, ref bool handled) =>
        {
            if (msg == WM_NCDESTROY)
            {
                inTray = InTray(h);
            }

            return 0;
        });
        return () => inTray;
    }

    /// <summary>Takes the icon out behind SSE's back, as an Explorer restart does.</summary>
    private static bool TakeOut(nint hwnd)
    {
        var data = Identity(hwnd);
        return Shell_NotifyIcon(NIM_DELETE, ref data);
    }

    /// <summary>The icon as the notification area knows it: SSE's tray window and icon id 1.</summary>
    private static NotifyIconData Identity(nint hwnd) =>
        new() { cbSize = Marshal.SizeOf<NotifyIconData>(), hWnd = hwnd, uID = 1, szTip = "", szInfo = "", szInfoTitle = "" };

    private static int Width(byte[] ico, (int Offset, int Length) image)
    {
        for (var i = 0; i < BitConverter.ToUInt16(ico, 4); i++)
        {
            var entry = 6 + 16 * i;
            if (BitConverter.ToInt32(ico, entry + 12) == image.Offset)
            {
                Assert.Equal(BitConverter.ToInt32(ico, entry + 8), image.Length);
                return ico[entry] == 0 ? 256 : ico[entry];
            }
        }

        throw new InvalidOperationException("No image at that offset.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindow(string className, string? windowName);
}
