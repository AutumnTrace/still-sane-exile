using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Desktop.Views;

namespace StillSaneExile.Desktop.Tests;

/// <summary>Stands in for the keyboard and mouse hooks. <see cref="Press"/> and <see cref="Release"/>
/// raise the event synchronously on the calling thread, as the hook callback does on the UI thread; a
/// side button is a <see cref="Hotkey"/> like any key.</summary>
internal sealed class FakeKeys : IKeySource
{
    public event Action<KeyEvent>? Key;

    public bool AltGrDetected { get; set; }

    public bool ObserveMouseButtons { get; set; }

    public bool Started { get; private set; }

    /// <summary>The clipboard change counter the next event carries.</summary>
    public uint ClipboardSequence { get; set; } = 1;

    public void Start() => Started = true;

    public void Dispose()
    {
    }

    public void Press(Hotkey key, bool injected = false) => Raise(key, down: true, injected);

    public void Release(Hotkey key, bool injected = false) => Raise(key, down: false, injected);

    private void Raise(Hotkey key, bool down, bool injected) =>
        Key?.Invoke(new KeyEvent(key.Vk, down, injected, new Modifiers(key.Ctrl, key.Shift, key.Alt), ClipboardSequence, Stopwatch.GetTimestamp()));
}

internal sealed class FakeForeground : IForegroundSource
{
    public const nint GameWindow = 0x1234;
    public const nint OtherWindow = 0x5678;

    private ForegroundKind _current = ForegroundKind.Game;
    private nint? _window;

    public event Action<ForegroundKind, string>? Changed;

    public ForegroundKind Current
    {
        get => _current;
        set
        {
            _current = value;
            _window = null;
        }
    }

    public nint ForegroundWindow => _window ?? (Current == ForegroundKind.Game ? GameWindow : OtherWindow);

    public void Start()
    {
    }

    public void Dispose()
    {
    }

    /// <summary>Another window comes to the front, as the WinEvent hook reports it. <paramref name="window"/>
    /// names it when it matters which one (the card, SSE's main window).</summary>
    public void Switch(ForegroundKind kind, string reason = "foreground changed", nint? window = null)
    {
        Current = kind;
        _window = window;
        Changed?.Invoke(kind, reason);
    }
}

/// <summary>The cursor, sampled only when the test says: <see cref="Poll"/> is one tick of the timer,
/// and does nothing while the HUD has not started it.</summary>
internal sealed class FakeCursor : ICursorSource
{
    /// <summary>Over the inventory, where the item was: far from the card.</summary>
    public static readonly ScreenPoint Away = new(1500, 600);

    /// <summary>Inside <see cref="FakePeek.Area"/>.</summary>
    public static readonly ScreenPoint OnCard = new(200, 350);

    public event Action? Tick;

    public ScreenPoint? Position { get; set; } = Away;

    public bool IsRunning { get; private set; }

    public int Starts { get; private set; }

    public void Start()
    {
        if (!IsRunning)
        {
            Starts++;
        }

        IsRunning = true;
    }

    public void Stop() => IsRunning = false;

    public void Poll()
    {
        if (IsRunning)
        {
            Tick?.Invoke();
        }
    }

    /// <summary>The player moves the mouse to <paramref name="point"/>; the timer samples it once.</summary>
    public void MoveTo(ScreenPoint point)
    {
        Position = point;
        Poll();
    }
}

/// <summary>Hands out <see cref="Text"/> and counts every read. With <see cref="HoldResults"/> on, a
/// wait-for-the-game's-copy read only happens when the test calls <see cref="CompleteRead"/>, like
/// a copy that lands late.</summary>
internal sealed class FakeClipboard : IClipboardSource
{
    private TaskCompletionSource<ClipboardRead>? _pending;

    public string? Text { get; set; }

    public int Reads { get; private set; }

    public bool HoldResults { get; set; }

    public Task<ClipboardRead> ReadAfterChangeAsync(uint sequenceAtPress, TimeSpan timeout)
    {
        if (!HoldResults)
        {
            return Task.FromResult(ReadNow());
        }

        _pending = new TaskCompletionSource<ClipboardRead>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _pending.Task;
    }

    public void CompleteRead()
    {
        var pending = _pending ?? throw new InvalidOperationException("No read is waiting.");
        _pending = null;
        pending.SetResult(ReadNow());
    }

    public ClipboardRead ReadNow()
    {
        Reads++;
        return Text is null ? new ClipboardRead(null, "The clipboard holds no text.") : new ClipboardRead(Text, null);
    }
}

/// <summary>The card's window. A click lands on it only while it takes clicks (pinned), and gives it the
/// focus, as on Windows; Esc reaches it only while it has the focus.</summary>
internal sealed class FakePeek : IPeekSurface
{
    public const nint CardWindow = 0x7777;

    public event Action? CloseRequested;

    public event Action? MoreRequested;

    public List<PeekViewModel> Shown { get; } = [];

    public nint ShownOver { get; private set; }

    public bool IsVisible { get; private set; }

    public nint Handle => CardWindow;

    /// <summary>Not click-through: the window was pinned.</summary>
    public bool TakesClicks { get; private set; }

    public bool HasFocus { get; set; }

    /// <summary>The window that got the focus back when the card closed; 0 if none did.</summary>
    public nint FocusReturnedTo { get; private set; }

    /// <summary>Where the card and its reach are on the screen.</summary>
    public Rect Area { get; set; } = new(60, 200, 400, 520);

    public void ShowCard(PeekViewModel card, PeekAnchor anchor, double scale, nint referenceWindow)
    {
        Shown.Add(card);
        ShownOver = referenceWindow;
        IsVisible = true;
        TakesClicks = false;
    }

    public bool IsNear(ScreenPoint point) => IsVisible && Area.Contains(point.X, point.Y);

    public void SetPinned(bool pinned) => TakesClicks = pinned;

    public void HideCard(bool returnFocus = false)
    {
        if (returnFocus && HasFocus && IsVisible)
        {
            FocusReturnedTo = ShownOver;
        }

        HasFocus = false;
        IsVisible = false;
        TakesClicks = false;
    }

    /// <summary>A click on the card: true if it landed (and gave the card the focus).</summary>
    public bool Click()
    {
        if (!IsVisible || !TakesClicks)
        {
            return false;
        }

        HasFocus = true;
        return true;
    }

    public void ClickClose()
    {
        if (Click())
        {
            CloseRequested?.Invoke();
        }
    }

    public void ClickMore()
    {
        if (Click())
        {
            MoreRequested?.Invoke();
        }
    }

    public void PressEscape()
    {
        if (IsVisible && HasFocus)
        {
            CloseRequested?.Invoke();
        }
    }
}

internal sealed class FakePriceCheck : IPriceCheckSurface
{
    public ItemViewModel? Item { get; private set; }

    public nint ShownOver { get; private set; }

    public bool IsVisible { get; private set; }

    public void ShowFor(ItemViewModel item, nint gameWindow)
    {
        Item = item;
        ShownOver = gameWindow;
        IsVisible = true;
    }

    public void HideWindow() => IsVisible = false;
}

internal sealed class FakeShellWindow : IShellWindow
{
    public const nint MainWindow = 0x9999;

    public Dispatcher Dispatcher { get; } = Dispatcher.CurrentDispatcher;

    public nint Handle => MainWindow;

    public bool IsTyping { get; set; }

    public int BroughtToFront { get; private set; }

    public void BringToFront() => BroughtToFront++;
}

/// <summary>A started <see cref="HudController"/> wired to fakes, on the current STA thread.</summary>
internal sealed class HudRig : IDisposable
{
    public HudRig(Action<Settings>? configure = null)
    {
        App = new TestApp(configure);
        try
        {
            Hud = new HudController(App.Shell, Keys, Foreground, App.Clipboard, Peek, PriceCheck, Window, Cursor);
            App.Shell.Hud = Hud;
            Hud.Start();
            Sta.Drain();
        }
        catch
        {
            App.Dispose();
            throw;
        }
    }

    public TestApp App { get; }
    public FakeKeys Keys { get; } = new();
    public FakeForeground Foreground { get; } = new();
    public FakePeek Peek { get; } = new();
    public FakePriceCheck PriceCheck { get; } = new();
    public FakeShellWindow Window { get; } = new();
    public FakeCursor Cursor { get; } = new();
    public HudController Hud { get; }
    public ShellViewModel Shell => App.Shell;
    public FakeClipboard Clipboard => App.Clipboard;

    /// <summary>The player clicks the card: if it takes clicks, it comes to the front.</summary>
    public void ClickCard()
    {
        if (Peek.Click())
        {
            Foreground.Switch(ForegroundKind.ThisApp, window: FakePeek.CardWindow);
        }
    }

    /// <summary>The game is in front again (the player moved off the card they clicked).</summary>
    public void BackToGame()
    {
        Peek.HasFocus = false;
        Foreground.Switch(ForegroundKind.Game);
    }

    public void Dispose()
    {
        Hud.Dispose();
        App.Dispose();
    }
}
