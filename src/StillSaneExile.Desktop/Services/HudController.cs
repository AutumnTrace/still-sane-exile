using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Desktop.Views;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Desktop.Services;

/// <summary>Connects physical key events to the capture gate, the one-shot clipboard read and the HUD
/// card. Holds no network client: the HUD path cannot make market requests.</summary>
public sealed class HudController : IDisposable
{
    public const string PeekCommand = "item-peek";

    private readonly ShellViewModel _shell;
    private readonly KeyboardObserver _keys;
    private readonly ForegroundWatcher _foreground;
    private readonly ClipboardReader _clipboard;
    private readonly CaptureGate _gate = new();
    private readonly PeekWindow _peek;
    private readonly PriceCheckWindow _priceCheck;
    private readonly Window _main;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<int, long> _downSince = [];
    private Hotkey _peekKey;
    private Hotkey _openLastKey;
    private bool _openLastHeld;

    public HudController(ShellViewModel shell, KeyboardObserver keys, ForegroundWatcher foreground, ClipboardReader clipboard, PeekWindow peek, PriceCheckWindow priceCheck, Window main)
    {
        _shell = shell;
        _keys = keys;
        _foreground = foreground;
        _clipboard = clipboard;
        _peek = peek;
        _priceCheck = priceCheck;
        _main = main;
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
        Invalidate("settings changed");
    }

    public void Dispose()
    {
        _keys.Key -= OnKey;
        _foreground.Changed -= OnForegroundChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _keys.Dispose();
        _foreground.Dispose();
    }

    /// <summary>Runs inside the keyboard hook callback: decide quickly, defer any work.</summary>
    private void OnKey(KeyEvent e)
    {
        TraceForSettings(e);

        if (e.Down && _peekKey.Matches(e.Vk, e.Mods))
        {
            var fg = _foreground.Current;
            var practice = fg == ForegroundKind.ThisApp && _shell.Session.IsDemo && _shell.CurrentPage is InspectViewModel;
            var decision = _gate.KeyDown(PeekCommand, e.Injected, fg == ForegroundKind.Game || practice);
            if (decision.Action == GateAction.Read)
            {
                var gameWindow = fg == ForegroundKind.Game ? _foreground.ForegroundWindow : 0;
                _dispatcher.BeginInvoke(() => _ = ReadAndShowAsync(decision.Generation, practice, e.ClipboardSequence, gameWindow));
            }
            else if (decision.Action == GateAction.Hide)
            {
                _dispatcher.BeginInvoke(_peek.HideCard);
            }
        }
        else if (!e.Down && e.Vk == _peekKey.Vk)
        {
            if (_gate.KeyUp(PeekCommand, e.Injected).Action == GateAction.Hide)
            {
                _dispatcher.BeginInvoke(_peek.HideCard);
            }
        }

        if (e.Vk == _openLastKey.Vk && !e.Injected)
        {
            var fg = _foreground.Current;
            if (e.Down && !_openLastHeld && _openLastKey.Matches(e.Vk, e.Mods) && fg is ForegroundKind.Game or ForegroundKind.ThisApp)
            {
                var window = _foreground.ForegroundWindow;
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
                : PeekViewModel.From(selected, true, release, _openLastKey.ToString());
        }
        else
        {
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
                    : PeekViewModel.From(vm, _shell.Session.IsDemo, release, _openLastKey.ToString());
            }
        }

        // Released, focus lost or a newer press while we were reading: never paint late.
        if (_gate.TryAccept(PeekCommand, generation))
        {
            _peek.ShowCard(card, _shell.Settings.Anchor, _shell.Settings.PeekScale, gameWindow != 0 ? gameWindow : new System.Windows.Interop.WindowInteropHelper(_main).Handle);
        }

        PublishDiagnostics();
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
            _peek.HideCard();
            _priceCheck.ShowFor(item, window);
            return;
        }

        OpenLastItem();
    }

    private void OpenLastItem()
    {
        _shell.Navigate(_shell.Inspect);
        if (_shell.Inspect.Selected is null && _shell.Inspect.Recent.Count > 0)
        {
            _shell.Inspect.Selected = _shell.Inspect.Recent[0];
        }

        if (_main.WindowState == WindowState.Minimized)
        {
            _main.WindowState = WindowState.Normal;
        }

        _main.Show();
        _main.Activate();
    }

    private void OnForegroundChanged(ForegroundKind kind, string reason) => Invalidate(reason);

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => _dispatcher.BeginInvoke(() =>
    {
        _priceCheck.HideWindow();
        Invalidate("session " + e.Reason);
    });

    private void OnDisplayChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(() =>
    {
        _priceCheck.HideWindow();
        Invalidate("display settings changed");
    });

    private void Invalidate(string reason)
    {
        if (_gate.Invalidate(reason).Action == GateAction.Hide || _peek.IsVisible)
        {
            _peek.HideCard();
        }

        PublishDiagnostics();
    }

    private void PublishDiagnostics()
    {
        var c = _gate.Counters;
        _shell.KeysSent = c.GameActionsSent;
        _shell.ItemsRead = c.Reads;
        _shell.Diagnostics = $"Reads {c.Reads} · auto-repeat ignored {c.AutoRepeatIgnored} · synthetic ignored {c.InjectedIgnored} · late results dropped {c.StaleResultsDropped} · game inputs sent {c.GameActionsSent} · market requests 0";
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

        var name = new Hotkey(e.Vk, e.Mods.Ctrl && e.Vk is not (0xA2 or 0xA3), e.Mods.Shift && e.Vk is not (0xA0 or 0xA1), e.Mods.Alt && e.Vk is not (0xA4 or 0xA5)).ToString();
        _dispatcher.BeginInvoke(() => settings.OnTestKey(e, name, held));
    }
}
