namespace StillSaneExile.Domain.Placement;

/// <summary>How the quick price key answers a press, from the owner's design ("Ctrl+C: answer in under
/// 100 ms, look where you already are"). The ring at the cursor answers as soon as the key is heard, before
/// anything is read; the card slides in from the cursor's side with a line from the cursor to it, and a
/// bronze flash says it has landed; letting go fades it out faster than it came in. With less motion (the
/// player's setting, or Windows' animation effects switched off) there is no ring, slide or line: the card
/// only fades in and out.</summary>
/// <param name="Ring">The ring at the cursor when the key is heard, and the red ring with its shake on a miss.</param>
/// <param name="Slide">The card slides <see cref="SlideDistance"/> in from the cursor's side; otherwise it fades in.</param>
/// <param name="Line">A line from the cursor to where the card went, when it is far enough to need one.</param>
/// <param name="FlashOnShow">The bronze flash once a new card has landed. Pressing again over the item a card
/// already shows flashes it with less motion too: then it is the only sign the press was heard.</param>
public sealed record PressMotion(bool Ring, bool Slide, bool Line, bool FlashOnShow)
{
    public static readonly TimeSpan RingFor = TimeSpan.FromMilliseconds(240);

    /// <summary>The dot in the ring's middle fades a little after the ring.</summary>
    public static readonly TimeSpan DotFor = TimeSpan.FromMilliseconds(260);

    public static readonly TimeSpan SlideFor = TimeSpan.FromMilliseconds(140);

    /// <summary>In device-independent pixels.</summary>
    public const double SlideDistance = 18;

    /// <summary>With less motion the card fades in instead, a little slower than it would slide.</summary>
    public static readonly TimeSpan FadeInFor = TimeSpan.FromMilliseconds(120);

    public static readonly TimeSpan LineFor = TimeSpan.FromMilliseconds(520);

    /// <summary>The line is drawn out from the cursor over this share of <see cref="LineFor"/>, then fades.</summary>
    public const double LineDrawnShare = 0.55;

    /// <summary>In device-independent pixels: a card closer to the cursor than this is plainly in view.</summary>
    public const double MinLineLength = 30;

    public static readonly TimeSpan FlashFor = TimeSpan.FromMilliseconds(450);

    /// <summary>Letting go: hiding should never feel slow.</summary>
    public static readonly TimeSpan FadeOutFor = TimeSpan.FromMilliseconds(90);

    /// <summary>A miss: the red ring and its one-line note.</summary>
    public static readonly TimeSpan MissFor = TimeSpan.FromMilliseconds(1600);

    public static readonly TimeSpan ShakeFor = TimeSpan.FromMilliseconds(280);

    public static readonly PressMotion Full = new(Ring: true, Slide: true, Line: true, FlashOnShow: true);

    public static readonly PressMotion Less = new(Ring: false, Slide: false, Line: false, FlashOnShow: false);

    /// <param name="lessMotion">Settings → Price card → Less motion.</param>
    /// <param name="windowsAnimates">Windows' "Animation effects" (Accessibility → Visual effects).</param>
    public static PressMotion For(bool lessMotion, bool windowsAnimates) => lessMotion || !windowsAnimates ? Less : Full;

    /// <summary>How long a new card takes to come in; the flash starts when it has.</summary>
    public TimeSpan CardInFor => Slide ? SlideFor : FadeInFor;

    /// <summary>Where the card starts its slide, relative to where it lands: <paramref name="distance"/>
    /// towards the cursor, so it comes in from the cursor's side. Nothing when the cursor is on the card.</summary>
    public static (double X, double Y) SlideFrom(Box card, Pos cursor, double distance)
    {
        var near = card.Nearest(cursor);
        var length = near.DistanceTo(cursor);
        return length < 0.5 ? (0, 0) : ((cursor.X - near.X) / length * distance, (cursor.Y - near.Y) / length * distance);
    }

    /// <summary>The line's end on the card: the point of the card nearest the cursor, or none when the card
    /// is within <paramref name="minLength"/> of the cursor (same units as the boxes).</summary>
    public static Pos? LineEnd(Box card, Pos cursor, double minLength)
    {
        var near = card.Nearest(cursor);
        return near.DistanceTo(cursor) > minLength ? near : null;
    }
}
