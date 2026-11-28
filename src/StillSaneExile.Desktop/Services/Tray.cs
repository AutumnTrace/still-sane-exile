using System.ComponentModel;
using System.Windows;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Views;

namespace StillSaneExile.Desktop.Services;

/// <summary>SSE living in the notification area, kept out of <c>App</c> so it can be tested with a fake icon
/// and window. With "Close to tray" and "Minimize to tray" on (the defaults), the main window's × and
/// minimize only hide it, and SSE keeps running with its keys: what ends it is Quit in the icon's menu,
/// Windows ending the session, or "Restart now" for an update. Opening brings the window back (shown,
/// restored, in front) and only ever follows the player's double-click, <c>Enter</c> or menu choice on the
/// icon, so taking the foreground is theirs to ask for.</summary>
public sealed class Tray : IDisposable
{
    public const string NoticeTitle = "Still Sane, Exile? is still running";

    public const string NoticeText = "It's here by the clock, and its keys still work. Double-click to open it; right-click for Quit.";

    private readonly ITrayIcon _icon;
    private readonly ITrayWindow _main;
    private readonly Settings _settings;
    private readonly Func<bool> _gameInFront;
    private bool _quitting;
    private bool _disposed;

    /// <param name="gameInFront">Whether the game is the window in front: the one-time notice waits
    /// rather than appear over it.</param>
    public Tray(ITrayIcon icon, ITrayWindow main, Settings settings, Func<bool> gameInFront)
    {
        _icon = icon;
        _main = main;
        _settings = settings;
        _gameInFront = gameInFront;
        _icon.OpenRequested += OnOpen;
        _icon.QuitRequested += Quit;
        _main.Closing += OnClosing;
        _main.StateChanged += OnStateChanged;
    }

    /// <summary>Whether the app's exit hands a downloaded update to Velopack's updater. Not once Windows is
    /// ending the session: the updater could be stopped halfway by the logoff, and Velopack installs a
    /// downloaded update on the next start anyway.</summary>
    public bool InstallUpdateOnExit { get; private set; } = true;

    public void Start() => _icon.Show();

    /// <summary>Quit in the icon's menu: the main window really closes, and the app exits the way it does
    /// when that window closes (the HUD's hooks, the icon and a downloaded update included).</summary>
    public void Quit()
    {
        if (_disposed)
        {
            return;
        }

        _quitting = true;
        _main.Close();
    }

    /// <summary>Windows is logging off, restarting or shutting down: nothing goes to the notification area
    /// any more, and the window closes with the app.</summary>
    public void SessionEnding()
    {
        _quitting = true;
        InstallUpdateOnExit = false;
    }

    /// <summary>On the app's exit: the icon leaves the notification area.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.OpenRequested -= OnOpen;
        _icon.QuitRequested -= Quit;
        _main.Closing -= OnClosing;
        _main.StateChanged -= OnStateChanged;
        _icon.Dispose();
    }

    private void OnOpen() => _main.BringToFront();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_quitting || !_settings.CloseToTray)
        {
            return;
        }

        e.Cancel = true;
        ToTray();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!_quitting && _main.WindowState == WindowState.Minimized && _settings.MinimizeToTray)
        {
            ToTray();
        }
    }

    /// <summary>Hides the window, which takes its taskbar button with it, and the first time says where it
    /// went. The notice never takes the focus, but it still waits while the game is in front.</summary>
    private void ToTray()
    {
        _main.Hide();
        if (!_settings.TrayNoticeShown && !_gameInFront() && _icon.Notify(NoticeTitle, NoticeText))
        {
            _settings.TrayNoticeShown = true;
        }
    }
}
