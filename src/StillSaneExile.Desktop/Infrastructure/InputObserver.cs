namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>What the HUD needs from the input observers, so its decisions can be tested with a fake.</summary>
public interface IKeySource : IDisposable
{
    /// <summary>Raised on the thread that started the source, inside the hook callback.</summary>
    event Action<KeyEvent>? Key;

    bool AltGrDetected { get; }

    /// <summary>Whether side mouse buttons are reported too. A low-level mouse hook is called for
    /// every mouse move in the system, so it is on only while something needs it. Set it on the UI
    /// thread: the hook calls back on the thread that installed it.</summary>
    bool ObserveMouseButtons { get; set; }

    void Start();
}

/// <summary>The keyboard observer, plus the side mouse buttons while they are wanted, as one stream
/// of key events. A side button arrives as VK_XBUTTON1 or VK_XBUTTON2 with the modifiers the keyboard
/// observer is tracking, so the HUD and the capture gate treat it exactly like a key.</summary>
public sealed class InputObserver : IKeySource
{
    private readonly KeyboardObserver _keyboard = new();
    private readonly MouseButtonObserver _mouse;

    public InputObserver()
    {
        _mouse = new MouseButtonObserver(() => _keyboard.Modifiers);
        _keyboard.Key += e => Key?.Invoke(e);
        _mouse.Button += e => Key?.Invoke(e);
    }

    public event Action<KeyEvent>? Key;

    public bool AltGrDetected => _keyboard.AltGrDetected;

    public bool ObserveMouseButtons
    {
        get => _mouse.IsRunning;
        set
        {
            if (value)
            {
                _mouse.Start();
            }
            else
            {
                _mouse.Stop();
            }
        }
    }

    public void Start() => _keyboard.Start();

    public void Dispose()
    {
        _mouse.Dispose();
        _keyboard.Dispose();
    }
}
