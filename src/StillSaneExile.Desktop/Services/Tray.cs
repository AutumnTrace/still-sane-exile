using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Views;

namespace StillSaneExile.Desktop.Services;

/// <summary>What SSE's notification-area icon does, kept out of <c>App</c> so it can be tested with a fake
/// icon and window. Opening brings the main window back (shown, restored, in front): it only ever follows
/// the player's double-click, <c>Enter</c> or menu choice on the icon, never anything on its own, so taking
/// the foreground is theirs to ask for. Quit leaves the way closing the main window does.</summary>
public sealed class Tray : IDisposable
{
    private readonly ITrayIcon _icon;
    private readonly IShellWindow _main;
    private readonly Action _quit;
    private bool _disposed;

    /// <param name="quit">The app's own way out; the app passes closing the main window.</param>
    public Tray(ITrayIcon icon, IShellWindow main, Action quit)
    {
        _icon = icon;
        _main = main;
        _quit = quit;
        _icon.OpenRequested += OnOpen;
        _icon.QuitRequested += OnQuit;
    }

    public void Start() => _icon.Show();

    /// <summary>On the app's exit: the icon leaves the notification area.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.OpenRequested -= OnOpen;
        _icon.QuitRequested -= OnQuit;
        _icon.Dispose();
    }

    private void OnOpen() => _main.BringToFront();

    private void OnQuit() => _quit();
}
