using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Win32;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Desktop.Views;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Desktop.Services;

/// <summary>Connects physical key events to the capture gate, the one-shot clipboard read and the HUD
/// card. Holds no network client: the HUD path cannot make market requests.</summary>
/// <remarks>Hold mode: the card shows while the key is held and hides on release, unless the cursor
/// moved onto it meanwhile. Then it is pinned: it takes clicks, its stats can be ticked, and it stays
/// until the player closes it, presses again over another item, or the game loses the foreground to
/// another program. Toggle mode is unchanged: one press shows, the next hides.</remarks>
public sealed class HudController : IDisposable
{
    public const string PeekCommand = "item-peek";

    private readonly ShellViewModel _shell;
    private readonly IKeySource _keys;
    private readonly IForegroundSource _foreground;
    private readonly IClipboardSource _clipboard;
    private readonly CaptureGate _gate = new();
    private readonly IPeekSurface _peek;
    private readonly IPriceCheckSurface _priceCheck;
    private readonly IShellWindow _main;
    private readonly ICursorSource _cursor;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<int, long> _downSince = [];
    private Hotkey _peekKey;
    private Hotkey _openLastKey;
    private bool _openLastHeld;

    /// <summary>The card showing, the window it was placed against (the game, or SSE for practice), and
    /// whether the cursor was on it at the last sample, so only an approach pins it.</summary>
    private PeekViewModel? _card;
    private nint _cardOver;
    private bool _cardOverGame;
    private bool _cursorWasNear;

    public HudController(ShellViewModel shell, IKeySource keys, IForegroundSource foreground, IClipboardSource clipboard, IPeekSurface peek, IPriceCheckSurface priceCheck, IShellWindow main, ICursorSource cursor)
    {
        _shell = shell;
        _keys = keys;
        _foreground = foreground;
        _clipboard = clipboard;
        _peek = peek;
        _priceCheck = priceCheck;
        _main = main;
        _cursor = cursor;
        _dispatcher = main.Dispatcher;
        _peekKey = shell.Settings.PeekKey;
        _openLastKey = shell.Settings.OpenLastKey;
    }

    public bool AltGrDetected => _keys.AltGrDetected;

    public CaptureCounters Counters => _gate.Counters;

    public void Start()
    {
        _keys.Key += OnKey;
        _foreground.Changed += OnForegroundChanged;
        _cursor.Tick += OnCursorTick;
        _peek.CloseRequested += OnCloseRequested;
        _peek.MoreRequested += OnMoreRequested;
        _shell.PropertyChanged += OnShellChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        _keys.Start();
        _foreground.Start();
        ApplySettings();
    }

    public void ApplySettings()
    {
        _peekKey = _shell.Settings.PeekKey;
        _openLastKey = _shell.Settings.OpenLastKey;
        _gate.Configure(PeekCommand, _shell.Settings.PeekMode);
        UpdateMouseButtons();
        Invalidate("settings changed");
    }

    public void Dispose()
    {
        _keys.Key -= OnKey;
        _foreground.Changed -= OnForegroundChanged;
        _cursor.Tick -= OnCursorTick;
        _cursor.Stop();
        _peek.CloseRequested -= OnCloseRequested;
        _peek.MoreRequested -= OnMoreRequested;
        _shell.PropertyChanged -= OnShellChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _keys.Dispose();
        _foreground.Dispose();
    }

    /// <summary>Lock, unlock or a user switch (on the UI thread): everything over the game hides.</summary>
    public void SessionChanged(SessionSwitchReason reason)
    {
        _priceCheck.HideWindow();
        Invalidate("session " + reason);
    }

    /// <summary>Resolution or monitor change (on the UI thread): everything over the game hides.</summary>
    public void DisplayChanged()
    {
        _priceCheck.HideWindow();
        Invalidate("display settings changed");
    }

    /// <summary>Side buttons are observed only while one is bound to something active, or while the
    /// Settings page is open so the key tester can show them. Otherwise no mouse hook runs at all.</summary>
    private void UpdateMouseButtons() =>
        _keys.ObserveMouseButtons = _peekKey.IsMouseButton || _openLastKey.IsMouseButton || _shell.CurrentPage is SettingsViewModel;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentPage))
        {
            UpdateMouseButtons();
        }
    }

    /// <summary>Runs inside the hook callback (the keyboard's, or the mouse's for a side button):
    /// decide quickly, defer any work.</summary>
    private void OnKey(KeyEvent e)
    {
        TraceForSettings(e);

        if (e.Down && _peekKey.Matches(e.Vk, e.Mods))
        {
            var fg = _foreground.Current;
            // Practice: the demo's Price check page with SSE in front, but not while the player types in
            // one of its boxes. There Ctrl+C copies their own text, and that press is only a copy. A
            // pinned card the player clicked is SSE too, but the window under it may be the game.
            var practice = fg == ForegroundKind.ThisApp && _shell.Session.IsDemo && _shell.CurrentPage is InspectViewModel && !_main.IsTyping
                && _foreground.ForegroundWindow != _peek.Handle;
            var decision = _gate.KeyDown(PeekCommand, e.Injected, fg == ForegroundKind.Game || practice);
            if (decision.Action == GateAction.Read)
            {
                var gameWindow = fg == ForegroundKind.Game ? _foreground.ForegroundWindow : 0;
                _dispatcher.BeginInvoke(() =>
                {
                    // A card still up (pinned) is replaced: click-through again until the new one lands.
                    StopWatching();
                    _peek.SetPinned(false);
                    _ = ReadAndShowAsync(decision.Generation, practice, e.ClipboardSequence, gameWindow);
                });
            }
            else if (decision.Action == GateAction.Hide)
            {
                _dispatcher.BeginInvoke(HideCard);
            }
        }
        else if (!e.Down && e.Vk == _peekKey.Vk)
        {
            var decision = _gate.KeyUp(PeekCommand, e.Injected);
            if (!e.Injected)
            {
                _dispatcher.BeginInvoke(StopWatching);
            }

            if (decision.Action == GateAction.Hide)
            {
                _dispatcher.BeginInvoke(HideCard);
            }
        }

        if (e.Vk == _openLastKey.Vk && !e.Injected)
        {
            var fg = _foreground.Current;
            var window = _foreground.ForegroundWindow;
            if (fg == ForegroundKind.ThisApp && window == _peek.Handle && _peek.IsVisible)
            {
                // The player clicked the pinned card, so it has the keys: F3 is about the game below it.
                fg = _cardOverGame ? ForegroundKind.Game : ForegroundKind.ThisApp;
                window = _cardOver;
            }

            if (e.Down && !_openLastHeld && _openLastKey.Matches(e.Vk, e.Mods) && fg is ForegroundKind.Game or ForegroundKind.ThisApp)
            {
                _dispatcher.BeginInvoke(() => ChangeSearch(fg, window));
            }

            _openLastHeld = e.Down;
        }

        _dispatcher.BeginInvoke(PublishDiagnostics);
    }

    private async Task ReadAndShowAsync(long generation, bool practice, uint clipboardSequence, nint gameWindow)
    {
        var mode = _shell.Settings.PeekMode;
        var release = PeekViewModel.ReleaseHintFor(mode);
        PeekViewModel card;

        if (practice)
        {
            var selected = _shell.Inspect.Selected;
            card = selected is null
                ? PeekViewModel.Message("Load a practice item first, then hold the key again.", true, release)
                : PeekViewModel.From(selected, true, release, _openLastKey.Label);
        }
        else
        {
            // Only the game's own copy key makes the game copy the hovered item. Any other binding (a
            // side button, say) reads what is already on the clipboard, and the card says so.
            var read = _peekKey.IsNativeCopy
                ? await _clipboard.ReadAfterChangeAsync(clipboardSequence, TimeSpan.FromMilliseconds(250))
                : _clipboard.ReadNow();
            if (read.Text is null)
            {
                card = PeekViewModel.Message(read.Error ?? "Nothing to read.", _shell.Session.IsDemo, release);
            }
            else
            {
                var vm = _shell.Inspect.AddFromText(read.Text, "hud");
                card = vm is null
                    ? PeekViewModel.Message(_shell.Inspect.Message ?? "Not an item.", _shell.Session.IsDemo, release)
                    : PeekViewModel.From(vm, _shell.Session.IsDemo, release, _openLastKey.Label, note: _peekKey.IsNativeCopy ? null : PeekViewModel.LastCopiedNote);
            }
        }

        // Released, focus lost or a newer press while we were reading: never paint late.
        if (_gate.TryAccept(PeekCommand, generation))
        {
            _card = card;
            _cardOverGame = gameWindow != 0;
            _cardOver = _cardOverGame ? gameWindow : _main.Handle;
            _peek.ShowCard(card, _shell.Settings.Anchor, _shell.Settings.PeekScale, _cardOver);
            if (mode == HoldMode.Hold && _gate.IsHeld(PeekCommand))
            {
                StartWatching();
            }
        }

        PublishDiagnostics();
    }

    /// <summary>While the key is held and the card shows: sample the cursor, to notice the player moving
    /// onto the card. A card that opens under the cursor waits for it to leave and come back.</summary>
    private void StartWatching()
    {
        _cursorWasNear = _cursor.Position is { } p && _peek.IsNear(p);
        _cursor.Start();
    }

    private void StopWatching() => _cursor.Stop();

    private void OnCursorTick()
    {
        if (_card is null || !_peek.IsVisible || !_gate.IsHeld(PeekCommand) || !_gate.IsShowing(PeekCommand))
        {
            StopWatching();
            return;
        }

        var near = _cursor.Position is { } p && _peek.IsNear(p);
        if (near && !_cursorWasNear && _gate.Pin(PeekCommand))
        {
            StopWatching();
            _card.IsPinned = true;
            _peek.SetPinned(true);
        }

        _cursorWasNear = near;
    }

    /// <summary>The pinned card's close button, or Esc on it.</summary>
    private void OnCloseRequested()
    {
        StopWatching();
        _gate.Dismiss(PeekCommand, "closed");
        _peek.HideCard(returnFocus: true);
        _card = null;
        PublishDiagnostics();
    }

    /// <summary>"All filters" on the pinned card: the full price check for this item, over the game
    /// (SSE's Price check page for a practice card).</summary>
    private void OnMoreRequested()
    {
        var item = _card?.Item;
        if (item is null)
        {
            return;
        }

        if (_cardOverGame)
        {
            // The price check first: it takes the focus, so hiding the card hands it to nothing else.
            _priceCheck.ShowFor(item, _cardOver);
            CloseCardQuietly();
            return;
        }

        CloseCardQuietly();
        _shell.Inspect.Selected = item;
        OpenLastItem();
    }

    /// <summary>F3. Over the game: the price check window for the last item. Pressed again: closes it.
    /// In this app, or with nothing checked yet: this window, on the Price check page.</summary>
    private void ChangeSearch(ForegroundKind fg, nint window)
    {
        if (_priceCheck.IsVisible)
        {
            _priceCheck.HideWindow();
            return;
        }

        var item = _shell.Inspect.Selected ?? _shell.Inspect.Recent.FirstOrDefault();
        if (fg == ForegroundKind.Game && item is not null)
        {
            _priceCheck.ShowFor(item, window);
            CloseCardQuietly();
            return;
        }

        OpenLastItem();
    }

    private void CloseCardQuietly()
    {
        StopWatching();
        _gate.Dismiss(PeekCommand, "price check opened");
        _peek.HideCard();
        _card = null;
    }

    private void OpenLastItem()
    {
        _shell.Navigate(_shell.Inspect);
        if (_shell.Inspect.Selected is null && _shell.Inspect.Recent.Count > 0)
        {
            _shell.Inspect.Selected = _shell.Inspect.Recent[0];
        }

        _main.BringToFront();
    }

    private void HideCard()
    {
        StopWatching();
        _peek.HideCard();
        _card = null;
    }

    private void OnForegroundChanged(ForegroundKind kind, string reason)
    {
        // A pinned card stays while the player clicks it or goes back to the game under it. Anything
        // else in front (another program, SSE's own window, a minimize) hides it, as any card.
        if (kind != ForegroundKind.Other && _gate.IsPinned(PeekCommand) && _peek.IsVisible)
        {
            var front = _foreground.ForegroundWindow;
            if (front == _peek.Handle || front == _cardOver)
            {
                PublishDiagnostics();
                return;
            }
        }

        Invalidate(reason);
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => _dispatcher.BeginInvoke(() => SessionChanged(e.Reason));

    private void OnDisplayChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(DisplayChanged);

    private void Invalidate(string reason)
    {
        StopWatching();
        if (_gate.Invalidate(reason).Action == GateAction.Hide || _peek.IsVisible)
        {
            _peek.HideCard();
            _card = null;
        }

        PublishDiagnostics();
    }

    private void PublishDiagnostics()
    {
        var c = _gate.Counters;
        _shell.KeysSent = c.GameActionsSent;
        _shell.ItemsRead = c.Reads;
        _shell.Diagnostics = $"Reads {c.Reads} · auto-repeat ignored {c.AutoRepeatIgnored} · synthetic ignored {c.InjectedIgnored} · late results dropped {c.StaleResultsDropped} · game inputs sent {c.GameActionsSent} · market requests from keys 0";
    }

    /// <summary>The settings page's press/hold/release tester. Only while this app is in front, so keys
    /// typed into other programs are never shown.</summary>
    private void TraceForSettings(KeyEvent e)
    {
        TimeSpan? held = null;
        if (e.Down)
        {
            if (_downSince.TryGetValue(e.Vk, out var since))
            {
                held = Stopwatch.GetElapsedTime(since, e.Timestamp);
            }
            else
            {
                _downSince[e.Vk] = e.Timestamp;
            }
        }
        else if (_downSince.Remove(e.Vk, out var since))
        {
            held = Stopwatch.GetElapsedTime(since, e.Timestamp);
        }

        if (_shell.CurrentPage is not SettingsViewModel settings || _foreground.Current != ForegroundKind.ThisApp)
        {
            return;
        }

        var name = new Hotkey(e.Vk, e.Mods.Ctrl && e.Vk is not (0xA2 or 0xA3), e.Mods.Shift && e.Vk is not (0xA0 or 0xA1), e.Mods.Alt && e.Vk is not (0xA4 or 0xA5)).Label;
        _dispatcher.BeginInvoke(() => settings.OnTestKey(e, name, held));
    }
}
