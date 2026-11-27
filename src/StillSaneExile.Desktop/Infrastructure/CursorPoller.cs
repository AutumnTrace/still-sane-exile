using System.Windows.Threading;

namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>A point on the screen in physical pixels, as Windows reports the cursor and places windows.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>What the HUD needs to know about the mouse cursor, so its decisions can be tested with a
/// fake: where it is, sampled on a short timer that runs only between <see cref="Start"/> and
/// <see cref="Stop"/>.</summary>
public interface ICursorSource
{
    /// <summary>Where the cursor is now, or null if Windows can't say (a secure desktop, say).</summary>
    ScreenPoint? Position { get; }

    bool IsRunning { get; }

    /// <summary>Raised on the UI thread on every sample while running.</summary>
    event Action? Tick;

    void Start();

    void Stop();
}

/// <summary>Reads the cursor position (<c>GetCursorPos</c>: OS metadata, not the screen) every
/// <see cref="Interval"/> while started. The HUD starts it only while the quick price key is held and
/// the card is showing, to notice the player moving onto the card; no mouse hook is involved.</summary>
public sealed class CursorPoller : ICursorSource
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(40);

    private readonly DispatcherTimer _timer;

    public CursorPoller(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Input, dispatcher) { Interval = Interval };
        _timer.Tick += (_, _) => Tick?.Invoke();
    }

    public event Action? Tick;

    public ScreenPoint? Position => NativeMethods.GetCursorPos(out var p) ? new ScreenPoint(p.X, p.Y) : null;

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
    {
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    public void Stop() => _timer.Stop();
}
