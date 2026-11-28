using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>SSE's icon in the notification area as the app sees it, so the wiring can be tested with a
/// fake: the player asks for the main window, or to quit.</summary>
public interface ITrayIcon : IDisposable
{
    /// <summary>A double-click on the icon, <c>Enter</c> on it, "Open Still Sane, Exile?" in its menu, or a
    /// click on its notification.</summary>
    event Action? OpenRequested;

    /// <summary>"Quit" in the icon's menu.</summary>
    event Action? QuitRequested;

    /// <summary>Puts the icon in the notification area, and back after Explorer restarts.</summary>
    void Show();

    /// <summary>A quiet notification from the icon (no sound; Windows shows it without taking the focus).
    /// False when the notification area does not hold the icon, so nothing was shown.</summary>
    bool Notify(string title, string text);
}

/// <summary>
/// The icon over <c>Shell_NotifyIcon</c> rather than WinForms' <c>NotifyIcon</c>: that one needs
/// <c>UseWindowsForms</c>, a second UI framework in a WPF app (its own DPI setup, implicit usings that
/// clash with WPF's) for the few calls it makes the same way underneath. A hidden window of SSE's own hears
/// the icon's clicks, and the menu is Windows' own popup menu, which the keyboard and screen readers know.
/// <para>The hidden window is top-level (never shown, no taskbar button), not message-only, because only
/// top-level windows get "TaskbarCreated", which Explorer broadcasts when it restarts with an empty
/// notification area.</para>
/// <para>The icon goes when this is disposed (the app's exit), when its window is destroyed some other way,
/// and when the process ends without either: restarting for an update exits at once, and an exception on
/// another thread ends the process. Otherwise a dead icon stays until the mouse passes over it.</para>
/// </summary>
public sealed class TrayIcon : ITrayIcon
{
    public const string Tip = "Still Sane, Exile?";

    /// <summary>The message the notification area sends the tray window about the icon.</summary>
    public const int CallbackMessage = NativeMethods.WM_APP + 1;

    /// <summary>The menu's choices, as they come back in WM_COMMAND.</summary>
    public const int OpenCommand = 1;
    public const int QuitCommand = 2;

    private const uint IconId = 1;

    private readonly HwndSource _window;
    private readonly HwndSourceHook _hook;
    private readonly nint _hwnd;
    private readonly uint _taskbarCreated;
    private readonly EventHandler _onProcessExit;
    private readonly UnhandledExceptionEventHandler _onCrash;
    private nint _icon;
    private bool _wanted;
    private int _shown;
    private bool _menuOpen;
    private bool _disposed;

    public TrayIcon()
    {
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _window = new HwndSource(new HwndSourceParameters(Tip)
        {
            WindowStyle = 0,
            ExtendedWindowStyle = (int)NativeMethods.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        });
        _hwnd = _window.Handle;
        _hook = WndProc;
        _window.AddHook(_hook);
        if (_taskbarCreated != 0)
        {
            _ = NativeMethods.ChangeWindowMessageFilterEx(_hwnd, _taskbarCreated, NativeMethods.MSGFLT_ALLOW, 0);
        }

        _onProcessExit = (_, _) => Remove();
        _onCrash = (_, _) => Remove();
        AppDomain.CurrentDomain.ProcessExit += _onProcessExit;
        AppDomain.CurrentDomain.UnhandledException += _onCrash;
    }

    public event Action? OpenRequested;

    public event Action? QuitRequested;

    /// <summary>The hidden window the notification area talks to.</summary>
    public nint Handle => _hwnd;

    /// <summary>True while the notification area holds the icon: Windows took it and it has not been
    /// removed since.</summary>
    public bool IsShown => Volatile.Read(ref _shown) == 1;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _wanted = true;
        Add();
    }

    /// <summary>Windows 10 and 11 show it as a notification in the corner, which never takes the focus, and
    /// keep it in the notification centre; with "Do not disturb" on (Windows turns it on for full-screen
    /// games by default) it only goes there.</summary>
    public bool Notify(string title, string text)
    {
        if (_disposed || !IsShown)
        {
            return false;
        }

        var data = Data(NativeMethods.NIF_INFO, title, text);
        data.dwInfoFlags = NativeMethods.NIIF_INFO | NativeMethods.NIIF_NOSOUND;
        return NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wanted = false;
        AppDomain.CurrentDomain.ProcessExit -= _onProcessExit;
        AppDomain.CurrentDomain.UnhandledException -= _onCrash;
        Remove();
        _window.RemoveHook(_hook);
        _window.Dispose();
        if (_icon != 0)
        {
            _ = NativeMethods.DestroyIcon(_icon);
            _icon = 0;
        }
    }

    /// <summary>sse.ico, the app's icon, as built into the app.</summary>
    public static byte[] IconFile()
    {
        var resource = Application.GetResourceStream(new Uri("/StillSaneExile;component/Assets/sse.ico", UriKind.Relative))
            ?? throw new InvalidOperationException("sse.ico is missing from the app.");
        using var stream = resource.Stream;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Where in an .ico file the image for <paramref name="size"/> pixels is: the smallest one at
    /// least that size, so Windows only ever scales it down, or else the largest.</summary>
    public static (int Offset, int Length) PickImage(ReadOnlySpan<byte> ico, int size)
    {
        if (ico.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(ico[2..]) != 1)
        {
            throw new InvalidDataException("Not an icon file.");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico[4..]);
        if (ico.Length < 6 + 16 * count)
        {
            throw new InvalidDataException("The icon file is cut short.");
        }

        var best = (Offset: -1, Length: 0, Size: 0);
        for (var i = 0; i < count; i++)
        {
            var entry = ico.Slice(6 + 16 * i, 16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            if (offset < 0 || length <= 0 || offset > ico.Length - length)
            {
                continue;
            }

            var better = best.Offset < 0
                || (width >= size ? best.Size < size || width < best.Size : best.Size < size && width > best.Size);
            if (better)
            {
                best = (offset, length, width);
            }
        }

        return best.Offset >= 0 ? (best.Offset, best.Length) : throw new InvalidDataException("The icon file has no image.");
    }

    private void Add()
    {
        if (_icon == 0)
        {
            // The small icon size: 16 px at 100 % display scaling, 24 px at 150 %.
            _icon = LoadIcon(NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON));
        }

        // Fails with no notification area (Explorer starting or not running: TaskbarCreated adds it later), and
        // with the icon already there, which then stays as it is.
        var data = Data(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
        {
            return;
        }

        // Version 4: a click comes with the icon's position, and Enter or the menu key on the icon come at all.
        data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        _ = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
        Volatile.Write(ref _shown, 1);
    }

    /// <summary>Takes the icon out of the notification area, once. Process exit calls this from another
    /// thread, which <c>Shell_NotifyIcon</c> allows.</summary>
    private void Remove()
    {
        if (Interlocked.Exchange(ref _shown, 0) == 1)
        {
            var data = Data(0);
            _ = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
        }
    }

    private NativeMethods.NOTIFYICONDATA Data(uint flags, string infoTitle = "", string info = "") => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = Tip,
        szInfo = info.Length > 255 ? info[..255] : info,
        szInfoTitle = infoTitle.Length > 63 ? infoTitle[..63] : infoTitle,
    };

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            handled = true;
            switch (LowWord(lParam))
            {
                case NativeMethods.WM_LBUTTONDBLCLK:
                case NativeMethods.NIN_KEYSELECT:
                case NativeMethods.NIN_BALLOONUSERCLICK:
                    Raise(open: true);
                    break;
                case NativeMethods.WM_CONTEXTMENU:
                    // The icon's position on the screen (the mouse's, or the icon's when opened from the keyboard).
                    ShowMenu((short)LowWord(wParam), (short)LowWord(wParam >> 16));
                    break;
            }
        }
        else if (msg == NativeMethods.WM_COMMAND && lParam == 0 && LowWord(wParam) is OpenCommand or QuitCommand)
        {
            handled = true;
            Raise(open: LowWord(wParam) == OpenCommand);
        }
        else if (_taskbarCreated != 0 && msg == (int)_taskbarCreated && _wanted)
        {
            // Explorer restarted with an empty notification area.
            Add();
        }
        else if (msg == NativeMethods.WM_DESTROY)
        {
            Remove();
        }

        return 0;
    }

    /// <summary>The app answers after the shell's message is handled, not inside it: quitting destroys this
    /// window.</summary>
    private void Raise(bool open) => _window.Dispatcher.InvokeAsync(() =>
    {
        if (!_disposed)
        {
            (open ? OpenRequested : QuitRequested)?.Invoke();
        }
    });

    private void ShowMenu(int x, int y)
    {
        if (_menuOpen)
        {
            return;
        }

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        _menuOpen = true;
        try
        {
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, OpenCommand, "&Open Still Sane, Exile?");
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, QuitCommand, "&Quit");
            _ = NativeMethods.SetMenuDefaultItem(menu, OpenCommand, 0); // in bold: what a double-click does

            // Windows' rule for a notification-area menu: its window must be in front, or the menu stays
            // open after a click elsewhere. Only ever after the player's own right-click on the icon.
            _ = NativeMethods.SetForegroundWindow(_hwnd);
            var align = NativeMethods.GetSystemMetrics(NativeMethods.SM_MENUDROPALIGNMENT) != 0 ? NativeMethods.TPM_RIGHTALIGN : NativeMethods.TPM_LEFTALIGN;
            _ = NativeMethods.TrackPopupMenuEx(menu, align | NativeMethods.TPM_RIGHTBUTTON, x, y, _hwnd, 0);

            // And an empty message after it, Windows' documented fix for a tray menu that would otherwise
            // close at once the next time it opens.
            _ = NativeMethods.PostMessage(_hwnd, NativeMethods.WM_NULL, 0, 0);
        }
        finally
        {
            _ = NativeMethods.DestroyMenu(menu);
            _menuOpen = false;
        }
    }

    private static nint LoadIcon(int size)
    {
        var ico = IconFile();
        var (offset, length) = PickImage(ico, size);
        var bits = ico.AsSpan(offset, length).ToArray();
        return NativeMethods.CreateIconFromResourceEx(bits, (uint)bits.Length, icon: true, version: 0x00030000, size, size, flags: 0);
    }

    private static int LowWord(nint value) => (int)(value & 0xFFFF);
}
