using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Domain.Placement;

namespace StillSaneExile.Desktop.Views;

/// <summary>What the HUD knows when the key is heard, for the cues that answer the press.</summary>
/// <param name="Cursor">Where the cursor was, in physical pixels, if Windows could say.</param>
/// <param name="Over">The window the cues go over: the game, or SSE's own window for practice.</param>
/// <param name="LessMotion">Settings → Price card → Less motion.</param>
/// <param name="Click">Settings → Price card → a soft click when the key is heard.</param>
public sealed record PressCue(ScreenPoint? Cursor, nint Over, bool LessMotion, bool Click);

/// <summary>How to show a card: where (the player's choice, and what it needs), how big and how it moves.</summary>
/// <param name="Over">The window it goes over: the game, or SSE's own window for practice.</param>
/// <param name="Cursor">Where the cursor was when the key was heard, in physical pixels.</param>
/// <param name="ItemText">The copied text, to guess the game's tooltip from; null on a message card.</param>
/// <param name="Spot">Where the player dragged the card to with Fixed spot, if they have.</param>
/// <param name="SameItem">The card showing is for this same item: it stays where it is and only flashes.</param>
public sealed record CardRequest(CardPosition Position, double Scale, nint Over, ScreenPoint? Cursor, string? ItemText, FixedSpot? Spot, bool LessMotion, bool SameItem = false);

/// <summary>What the HUD needs from the peek card's window and the cues around it, so its decisions can be
/// tested with a fake.</summary>
public interface IPeekSurface
{
    /// <summary>A card shows, or is coming in. One fading out after the key was let go no longer counts.</summary>
    bool IsVisible { get; }

    /// <summary>The card's own window, to tell it apart when it comes to the front (the player clicked it).</summary>
    nint Handle { get; }

    /// <summary>The pinned card's close button, or Esc while it has the focus.</summary>
    event Action? CloseRequested;

    /// <summary>The pinned card's "All filters".</summary>
    event Action? MoreRequested;

    /// <summary>With Fixed spot, the player dragged a card they kept open to where it should go from now on.</summary>
    event Action<FixedSpot>? SpotChosen;

    /// <summary>The key was heard, before anything is read: a ring at the cursor, and the soft click if it is on.</summary>
    void KeyHeard(PressCue cue);

    /// <summary>The game copied nothing (no item under the cursor): a red ring and a one-line note, no card.</summary>
    void Missed(PressCue cue, string note);

    /// <summary>Shows the card click-through and never activated, as while the key is held.</summary>
    void ShowCard(PeekViewModel card, CardRequest request);

    /// <summary>True when <paramref name="point"/> (physical pixels) is on the visible card or within a
    /// short reach of it, so moving towards the card catches it.</summary>
    bool IsNear(ScreenPoint point);

    /// <summary>Pinned: the card takes the mouse, and the focus once clicked. Unpinned: click-through and
    /// never activated again.</summary>
    void SetPinned(bool pinned);

    /// <summary>Hides the card. With <paramref name="returnFocus"/>, and only if the card has the focus,
    /// the window it was shown over (the game) gets the focus back. With <paramref name="fade"/> (the key was
    /// let go) it fades out quickly; it is click-through and counts as hidden from the start.</summary>
    void HideCard(bool returnFocus = false, bool fade = false);

    /// <summary>Takes the ring, the note and the line away now: something else came to the front.</summary>
    void ClearCues();
}

/// <summary>The HUD card: topmost, never in the taskbar and never activated. While the key is held it is
/// click-through; pinned, it takes clicks but still never becomes the active window, the way an on-screen
/// keyboard does. Activating it would take the foreground from the game, and a game in fullscreen drops out
/// as if alt-tabbed (which then hides the card). So the game keeps the keyboard the whole time.
/// It also owns the cues around a press (<see cref="CueWindow"/>): the ring, the miss note and the line.</summary>
public partial class PeekWindow : Window, IPeekSurface
{
    /// <summary>How far around the card, in device-independent pixels, the cursor counts as on it.</summary>
    public const double Reach = 20;

    /// <summary>Only click-through comes off when pinned; WS_EX_NOACTIVATE stays on at all times.</summary>
    private const long Passive = NativeMethods.WS_EX_TRANSPARENT;

    /// <summary>Close to the design's cubic-bezier(0.2, 0.8, 0.2, 1): quick to start, soft to land.</summary>
    private static readonly IEasingFunction SlideEase = Frozen(new QuarticEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction FadeOutEase = Frozen(new QuadraticEase { EasingMode = EasingMode.EaseIn });
    private static readonly IEasingFunction FlashEase = Frozen(new QuadraticEase { EasingMode = EasingMode.EaseOut });

    private CueWindow? _ring;
    private CueWindow? _line;
    private nint _over;
    private bool _pinned;

    /// <summary>Let go: the card is fading out, and already hidden as far as the HUD is concerned.</summary>
    private bool _fading;

    private CardRequest? _request;

    /// <summary>Where the visible card is, in physical pixels, so a card that grows (Search adds its
    /// listings) stays where it is and keeps clear of the skill bar.</summary>
    private Box? _placed;

    private Drag? _drag;

    public PeekWindow()
    {
        InitializeComponent();
        CardView.CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());
        CardView.MoreCommand = new RelayCommand(() => MoreRequested?.Invoke());
        SizeChanged += (_, e) =>
        {
            if (e.HeightChanged && IsVisible && !_fading && _placed is { } placed && _request is { } request)
            {
                var f = Overlay.Factor(_over, Handle);
                var (width, height) = CardSize(f);
                var resized = CardPlacement.Resized(placed, width, height, Overlay.AreaOf(_over, Handle), keepClearOfSkillBar: request.Position != CardPosition.Fixed);
                MoveCardTo(resized, f);
            }
        };
        Closed += (_, _) =>
        {
            _ring?.Close();
            _line?.Close();
        };
    }

    public event Action? CloseRequested;

    public event Action? MoreRequested;

    public event Action<FixedSpot>? SpotChosen;

    public nint Handle => new WindowInteropHelper(this).Handle;

    bool IPeekSurface.IsVisible => IsVisible && !_fading;

    private CueWindow RingCue => _ring ??= new CueWindow();

    private CueWindow LineCue => _line ??= new CueWindow();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Overlay.MakePassive(new WindowInteropHelper(this).Handle);
    }

    public void KeyHeard(PressCue cue)
    {
        if (cue.Click)
        {
            SoftClick.Play();
        }

        _line?.Clear(); // the last card's line, if it is still fading
        if (MotionFor(cue.LessMotion).Ring && cue.Cursor is { } cursor)
        {
            RingCue.ShowRing(cursor, cue.Over);
        }
        else
        {
            _ring?.Clear();
        }
    }

    public void Missed(PressCue cue, string note) => RingCue.ShowMiss(cue.Cursor, cue.Over, note, MotionFor(cue.LessMotion).Ring);

    public void ClearCues()
    {
        _ring?.Clear();
        _line?.Clear();
    }

    public void ShowCard(PeekViewModel card, CardRequest request)
    {
        SetPinned(false);
        EndDrag(save: false);
        var showing = IsVisible && !_fading;
        _fading = false;
        StopMotion();
        _over = request.Over;
        _request = request;
        DataContext = card;
        Scale.ScaleX = Scale.ScaleY = request.Scale;

        if (request.SameItem && showing && _placed is not null)
        {
            // Pressed again over the item it shows: no new card, it stays put and flashes.
            UpdateLayout();
            FlashCard(TimeSpan.Zero);
            return;
        }

        _placed = null;
        Opacity = 0;
        Show();
        UpdateLayout();
        var f = Overlay.Factor(request.Over, Handle);
        var area = Overlay.AreaOf(request.Over, Handle);
        var cursor = request.Cursor is { } c ? new Pos(c.X, c.Y) : area.Centre;
        var (width, height) = CardSize(f);
        var placed = CardPlacement.Place(request.Position, area, cursor, width, height, request.ItemText, request.Spot).Card;
        MoveCardTo(placed, f);
        Opacity = 1;

        var motion = MotionFor(request.LessMotion);
        if (motion.Slide)
        {
            var (dx, dy) = PressMotion.SlideFrom(placed, cursor, PressMotion.SlideDistance);
            Slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, PressMotion.SlideFor) { EasingFunction = SlideEase });
            Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, PressMotion.SlideFor) { EasingFunction = SlideEase });
            Motion.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, PressMotion.SlideFor) { EasingFunction = SlideEase });
        }
        else
        {
            Motion.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, PressMotion.FadeInFor));
        }

        if (motion.FlashOnShow)
        {
            FlashCard(motion.CardInFor);
        }

        if (motion.Line && request.Cursor is { } from && PressMotion.LineEnd(placed, cursor, PressMotion.MinLineLength * f) is { } end)
        {
            LineCue.ShowLine(from, end, request.Over);
        }
    }

    public bool IsNear(ScreenPoint point) =>
        IsVisible && !_fading && CardView.ScreenBounds() is { } card && Within(card, point, Reach * VisualTreeHelper.GetDpi(this).DpiScaleX);

    /// <summary>Whether <paramref name="point"/> lies on <paramref name="card"/> grown by <paramref name="reach"/> on every side.</summary>
    public static bool Within(Rect card, ScreenPoint point, double reach)
    {
        card.Inflate(reach, reach);
        return card.Contains(point.X, point.Y);
    }

    public void SetPinned(bool pinned)
    {
        _pinned = pinned;
        var hwnd = Handle;
        if (hwnd == 0)
        {
            return;
        }

        var ex = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var next = pinned ? ex & ~Passive : ex | Passive;
        if (next != ex)
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)next);
            NativeMethods.SetWindowPos(hwnd, 0, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
        }
    }

    public void HideCard(bool returnFocus = false, bool fade = false)
    {
        // The game first: hiding the active card would otherwise hand the focus to some other window.
        if (returnFocus && IsVisible && IsActive && CanFocus(_over))
        {
            NativeMethods.SetForegroundWindow(_over);
        }

        EndDrag(save: false);
        SetPinned(false);
        _line?.Clear();
        if (fade && IsVisible && !_fading)
        {
            _fading = true;
            var fadeOut = new DoubleAnimation(0, PressMotion.FadeOutFor) { EasingFunction = FadeOutEase };
            fadeOut.Completed += (_, _) =>
            {
                if (_fading)
                {
                    HideNow();
                }
            };
            Motion.BeginAnimation(OpacityProperty, fadeOut);
            return;
        }

        HideNow();
    }

    private void HideNow()
    {
        _fading = false;
        Hide();
        StopMotion();
        DataContext = null;
        _placed = null;
        _request = null;
    }

    /// <summary>Activated by a click with nothing focusable under it: the card itself takes the keys, so
    /// Esc still closes it.</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (!IsKeyboardFocusWithin)
        {
            Focus();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _pinned)
        {
            e.Handled = true;
            CloseRequested?.Invoke();
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>Fixed spot: a card the player kept open can be dragged by any part that is not a control.
    /// The card's window moves (never activated); the game sees none of it.</summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || !_pinned || _fading || _request?.Position != CardPosition.Fixed || _placed is null
            || !NativeMethods.GetCursorPos(out var start) || !NativeMethods.GetWindowRect(Handle, out var at))
        {
            return;
        }

        _drag = new Drag(start.X, start.Y, at.Left, at.Top, Moved: false);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag is not { } drag || !NativeMethods.GetCursorPos(out var now))
        {
            return;
        }

        var dx = now.X - drag.StartX;
        var dy = now.Y - drag.StartY;
        if (!drag.Moved && Math.Abs(dx) + Math.Abs(dy) < 4)
        {
            return;
        }

        _drag = drag with { Moved = true };
        Overlay.MoveTo(Handle, drag.Left + dx, drag.Top + dy);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndDrag(save: true);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndDrag(save: true);
    }

    /// <summary>Where the card was dropped, kept on the game window, becomes its fixed spot.</summary>
    private void EndDrag(bool save)
    {
        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (!save || !drag.Moved || !NativeMethods.GetWindowRect(Handle, out var at))
        {
            return;
        }

        var f = Overlay.Factor(_over, Handle);
        var area = Overlay.AreaOf(_over, Handle);
        var (width, height) = CardSize(f);
        var card = new Box(at.Left + (Room.Padding.Left * f), at.Top + (Room.Padding.Top * f), width, height).KeptWithin(area).Rounded();
        MoveCardTo(card, f);
        SpotChosen?.Invoke(FixedSpot.Of(card, area));
    }

    /// <summary>The visible card's size in physical pixels: the card at its scale, without the room around it.</summary>
    private (double Width, double Height) CardSize(double factor) =>
        (CardView.ActualWidth * Scale.ScaleX * factor, CardView.ActualHeight * Scale.ScaleY * factor);

    /// <summary>Puts the visible card (not the room around it) at <paramref name="card"/>.</summary>
    private void MoveCardTo(Box card, double factor)
    {
        _placed = card;
        Overlay.MoveTo(Handle, card.X - (Room.Padding.Left * factor), card.Y - (Room.Padding.Top * factor));
    }

    private void FlashCard(TimeSpan after) =>
        Flash.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, PressMotion.FlashFor) { BeginTime = after, EasingFunction = FlashEase });

    private void StopMotion()
    {
        Slide.BeginAnimation(TranslateTransform.XProperty, null);
        Slide.BeginAnimation(TranslateTransform.YProperty, null);
        Motion.BeginAnimation(OpacityProperty, null);
        Flash.BeginAnimation(OpacityProperty, null);
    }

    /// <summary>The player's Less motion, or Windows' animation effects switched off.</summary>
    private static PressMotion MotionFor(bool lessMotion) => PressMotion.For(lessMotion, SystemParameters.ClientAreaAnimation);

    private static bool CanFocus(nint window) => window != 0 && NativeMethods.IsWindow(window) && !NativeMethods.IsIconic(window);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>A drag in progress: where the cursor and the window started, in physical pixels.</summary>
    private sealed record Drag(int StartX, int StartY, int Left, int Top, bool Moved);
}
