using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Desktop.Views;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Desktop.Services;

/// <summary>The stash key while "Collect stash pictures" is on: each fresh physical press with the game in
/// front takes exactly one frame of the game window and saves it, with its notes, in the data folder. It
/// listens to the same key and foreground events as the HUD but has a gate of its own, so the quick price
/// path is untouched.</summary>
/// <remarks>The gate is in <see cref="HoldMode.Shot"/>: auto-repeat, synthetic input and a press with
/// another window in front take nothing, and a frame that comes back after focus loss, lock, a display
/// change or a newer press is dropped unsaved. Letting go of the key does not drop it, so a quick tap
/// keeps its picture. The frame itself is discarded as soon as its 8-bit copy is made; the copy is
/// cleared once the PNG is written.</remarks>
public sealed class StashCollector : IDisposable
{
    public const string Command = "stash-pictures";

    private const int VkRightAlt = 0xA5;

    private readonly StashPicturesViewModel _pictures;
    private readonly string _version;
    private readonly IKeySource _keys;
    private readonly IForegroundSource _foreground;
    private readonly IStashFrameSource _source;
    private readonly IStashToast _toast;
    private readonly Dispatcher _dispatcher;
    private readonly CaptureGate _gate = new();
    private int _busy;

    public StashCollector(StashPicturesViewModel pictures, string version, IKeySource keys, IForegroundSource foreground, IStashFrameSource source, IStashToast toast, Dispatcher dispatcher)
    {
        _pictures = pictures;
        _version = version;
        _keys = keys;
        _foreground = foreground;
        _source = source;
        _toast = toast;
        _dispatcher = dispatcher;
        _gate.Configure(Command, HoldMode.Shot);
        Reader = new FrameReader(_gate, Command, source);
    }

    /// <summary>The gate and capture counters: one press, one capture.</summary>
    public FrameReader Reader { get; }

    /// <summary>Pictures being taken or saved right now.</summary>
    public bool IsBusy => Volatile.Read(ref _busy) > 0;

    public void Start()
    {
        _keys.Key += OnKey;
        _foreground.Changed += OnForegroundChanged;
        _pictures.PropertyChanged += OnPicturesChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        if (_pictures.IsCollecting)
        {
            _ = Task.Run(_source.Prepare);
        }
    }

    public void Dispose()
    {
        _keys.Key -= OnKey;
        _foreground.Changed -= OnForegroundChanged;
        _pictures.PropertyChanged -= OnPicturesChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
    }

    /// <summary>Runs inside the hook callback: decide quickly, defer the capture.</summary>
    private void OnKey(KeyEvent e)
    {
        var key = _pictures.Key;
        if (e.Down && key.Matches(e.Vk, e.Mods))
        {
            // Off: the key is still tracked as held, so switching on mid-hold can't turn auto-repeat into a press.
            var collecting = _pictures.IsCollecting;
            var fresh = !e.Injected && !_gate.IsHeld(Command);
            var game = collecting && _foreground.Current == ForegroundKind.Game;

            // Right Alt is AltGr on this keyboard: typing @ or € in the game's chat would take pictures of it.
            var altGr = key.Vk == VkRightAlt && _keys.AltGrDetected;
            var decision = Reader.KeyDown(e.Injected, game && !altGr);
            if (decision.Action == GateAction.Read)
            {
                var window = _foreground.ForegroundWindow;
                var pressed = e.Timestamp;
                _dispatcher.BeginInvoke(() => _ = TakeAsync(decision, window, pressed));
            }
            else if (fresh && game && altGr)
            {
                var window = _foreground.ForegroundWindow;
                _dispatcher.BeginInvoke(() => _toast.Show("No stash picture: Right Alt is AltGr on your keyboard. Pick another stash key in Settings, F8 for example.", window));
            }
        }
        else if (!e.Down && e.Vk == key.Vk)
        {
            Reader.KeyUp(e.Injected);
        }
    }

    private async Task TakeAsync(GateDecision decision, nint window, long pressed)
    {
        Interlocked.Increment(ref _busy);
        try
        {
            var read = await Reader.ReadAsync(decision, window, frame => new Shot(PictureCopy.Of(frame), _source.FactsFor(frame)));
            switch (read.Outcome)
            {
                case FrameReadOutcome.Accepted when read.Value is { } shot:
                    using (shot)
                    {
                        await SaveAsync(shot, window, pressed);
                    }

                    break;
                case FrameReadOutcome.NoFrame:
                    _toast.Show($"No stash picture: {_source.LastProblem ?? read.Error ?? "Windows gave no picture of the game"}.", window);
                    break;
                case FrameReadOutcome.AnalysisFailed:
                    _toast.Show($"No stash picture: {read.Error}", window);
                    break;
                default:
                    // Late: the game lost the front, or a newer press came, while the frame was taken.
                    // Nothing is saved, and the newer press speaks for itself.
                    break;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
        }
    }

    private async Task SaveAsync(Shot shot, nint window, long pressed)
    {
        if (shot.Copy.IsBlank)
        {
            _toast.Show("That picture came back black, so it wasn't saved. If the game is in exclusive Fullscreen, try Windowed Fullscreen.", window);
            return;
        }

        var pressToFrame = Stopwatch.GetElapsedTime(pressed, shot.Copy.Timestamp).TotalMilliseconds;
        var notes = StashPictureNotes.For(shot.Copy, shot.Facts, DateTimeOffset.Now, _version, Math.Max(0, pressToFrame));
        StashPicture saved;
        try
        {
            saved = await Task.Run(() => _pictures.Store.Save(shot.Copy, notes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _toast.Show("Couldn't save the stash picture: " + e.Message, window);
            return;
        }

        _pictures.Add(saved);
        _toast.Show($"Saved stash picture {_pictures.Count}", window);
    }

    private void OnPicturesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StashPicturesViewModel.IsCollecting))
        {
            return;
        }

        if (_pictures.IsCollecting)
        {
            _ = Task.Run(_source.Prepare); // the device ahead of the first press: it captures nothing
        }
        else
        {
            Reader.Invalidate("collecting switched off");
            _toast.Hide();
            _ = Task.Run(_source.Release);
        }
    }

    private void OnForegroundChanged(ForegroundKind kind, string reason)
    {
        Reader.Invalidate(reason);
        if (kind != ForegroundKind.Game)
        {
            _toast.Hide();
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => _dispatcher.BeginInvoke(() =>
    {
        Reader.Invalidate("session " + e.Reason);
        _toast.Hide();
    });

    private void OnDisplayChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(() =>
    {
        Reader.Invalidate("display settings changed");
        _toast.Hide();
    });

    /// <summary>What the analysis keeps of the frame: its 8-bit copy and how it was taken. A late one is
    /// disposed by the reader, which clears the copy.</summary>
    private sealed record Shot(PictureCopy Copy, CaptureFacts? Facts) : IDisposable
    {
        public void Dispose() => Copy.Dispose();
    }
}
