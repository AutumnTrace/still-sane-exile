using System.Diagnostics;
using System.Windows.Threading;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Desktop.Views;

namespace StillSaneExile.Desktop.Tests;

/// <summary>Stands in for the keyboard hook. <see cref="Press"/> and <see cref="Release"/> raise the
/// event synchronously on the calling thread, as the hook callback does on the UI thread.</summary>
internal sealed class FakeKeys : IKeySource
{
    public event Action<KeyEvent>? Key;

    public bool AltGrDetected { get; set; }

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

    public event Action<ForegroundKind, string>? Changed;

    public ForegroundKind Current { get; set; } = ForegroundKind.Game;

    public nint ForegroundWindow => Current == ForegroundKind.Game ? GameWindow : OtherWindow;

    public void Start()
    {
    }

    public void Dispose()
    {
    }

    /// <summary>Another window comes to the front, as the WinEvent hook reports it.</summary>
    public void Switch(ForegroundKind kind, string reason = "foreground changed")
    {
        Current = kind;
        Changed?.Invoke(kind, reason);
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

internal sealed class FakePeek : IPeekSurface
{
    public List<PeekViewModel> Shown { get; } = [];

    public nint ShownOver { get; private set; }

    public bool IsVisible { get; private set; }

    public void ShowCard(PeekViewModel card, PeekAnchor anchor, double scale, nint referenceWindow)
    {
        Shown.Add(card);
        ShownOver = referenceWindow;
        IsVisible = true;
    }

    public void HideCard() => IsVisible = false;
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
            Hud = new HudController(App.Shell, Keys, Foreground, App.Clipboard, Peek, PriceCheck, Window);
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
    public HudController Hud { get; }
    public ShellViewModel Shell => App.Shell;
    public FakeClipboard Clipboard => App.Clipboard;

    public void Dispose()
    {
        Hud.Dispose();
        App.Dispose();
    }
}
