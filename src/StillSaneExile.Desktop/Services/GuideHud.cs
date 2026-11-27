using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Desktop.Views;

namespace StillSaneExile.Desktop.Services;

/// <summary>Decides when the pinned guide lines show: only while the game is in front with the lines
/// switched on, or for a short preview the player asked for. They step aside while the item peek card
/// is up, held or pinned (by watching the card's visibility, so the item HUD needs no changes: a pinned
/// card may sit where the lines go, and the player closes it when done) and hide on minimize and lock
/// like the card. A zone change only updates the text: nothing opens, nothing takes focus.
/// Like the item HUD, it reads window titles only and never the game's memory or files.</summary>
public sealed class GuideHud : IDisposable
{
    private static readonly TimeSpan PreviewLength = TimeSpan.FromSeconds(6);

    private readonly GuideViewModel _guide;
    private readonly Window _peek;
    private readonly Window _main;
    private readonly ForegroundWatcher _foreground = new();
    private readonly GuideHudWindow _window = new();
    private readonly DispatcherTimer _preview;
    private ForegroundKind _front;
    private bool _locked;

    public GuideHud(GuideViewModel guide, Window peek, Window main)
    {
        _guide = guide;
        _peek = peek;
        _main = main;
        _window.DataContext = guide;
        _preview = new DispatcherTimer(DispatcherPriority.Normal, main.Dispatcher) { Interval = PreviewLength };
        _preview.Tick += (_, _) =>
        {
            _preview.Stop();
            Refresh();
        };
    }

    public void Start()
    {
        _foreground.Changed += OnForegroundChanged;
        _peek.IsVisibleChanged += OnPeekVisibleChanged;
        _guide.PropertyChanged += OnGuideChanged;
        _guide.PreviewRequested += OnPreviewRequested;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        _foreground.Start();
        _front = _foreground.Current;
        Refresh();
    }

    public void Dispose()
    {
        _foreground.Changed -= OnForegroundChanged;
        _peek.IsVisibleChanged -= OnPeekVisibleChanged;
        _guide.PropertyChanged -= OnGuideChanged;
        _guide.PreviewRequested -= OnPreviewRequested;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _preview.Stop();
        _foreground.Dispose();
        _window.Close();
    }

    private void Refresh()
    {
        var previewing = _preview.IsEnabled;
        var show = _guide.HasHudLines && !_peek.IsVisible && !_locked
            && ((_guide.HudEnabled && _front == ForegroundKind.Game) || previewing);
        if (!show)
        {
            if (_window.IsVisible)
            {
                _window.Hide();
            }

            return;
        }

        var over = _front == ForegroundKind.Game ? _foreground.ForegroundWindow : new WindowInteropHelper(_main).Handle;
        _window.ShowOver(over);
    }

    private void OnForegroundChanged(ForegroundKind kind, string reason)
    {
        _front = kind;
        if (kind != ForegroundKind.ThisApp)
        {
            _preview.Stop(); // a preview is for this window only
        }

        Refresh();
    }

    private void OnPeekVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => Refresh();

    private void OnGuideChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void OnPreviewRequested(object? sender, EventArgs e)
    {
        _preview.Stop();
        _preview.Start();
        Refresh();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => _main.Dispatcher.BeginInvoke(() =>
    {
        _locked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect;
        _front = _foreground.Current;
        Refresh();
    });

    private void OnDisplayChanged(object? sender, EventArgs e) => _main.Dispatcher.BeginInvoke(() =>
    {
        _window.Hide();
        Refresh();
    });
}
