using System.Globalization;

namespace StillSaneExile.Domain.Placement;

/// <summary>Settings → Price card → Place the card.</summary>
public enum CardPosition
{
    /// <summary>Right beside the game's tooltip, towards the middle of the screen, so the eyes barely move.</summary>
    Tooltip,

    /// <summary>The half of the screen away from the cursor, clear of the skill bar and the top-right map
    /// (the only placement before the tooltip one).</summary>
    Edge,

    /// <summary>Where the player last dragged a card they kept open.</summary>
    Fixed,
}

/// <summary>Which rule put the card where it is.</summary>
public enum CardSpot
{
    BesideTooltip,
    OtherSideOfTooltip,
    BelowTooltip,
    AboveTooltip,
    ScreenEdge,
    Fixed,
}

/// <param name="Card">The visible card, in physical pixels.</param>
/// <param name="Tooltip">The game's tooltip as guessed, when the rule needed one.</param>
/// <param name="Item">The hovered item as guessed, when the rule needed one.</param>
public sealed record PlacedCard(Box Card, CardSpot Spot, Box? Tooltip = null, Box? Item = null);

/// <summary>A spot the player dragged the card to: its top-left corner as shares of the game window, so it
/// stays in place through a resolution change.</summary>
public readonly record struct FixedSpot(double Left, double Top)
{
    public string Format() => string.Create(CultureInfo.InvariantCulture, $"{Left:0.####},{Top:0.####}");

    /// <summary>"0.4,0.25" → that spot; anything else (empty, out of the window, not numbers) → none.</summary>
    public static FixedSpot? Parse(string? text)
    {
        if (text?.Split(',') is not [var l, var t]
            || !double.TryParse(l, NumberStyles.Float, CultureInfo.InvariantCulture, out var left)
            || !double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var top)
            || left is < 0 or > 1 || top is < 0 or > 1)
        {
            return null;
        }

        return new FixedSpot(left, top);
    }

    public static FixedSpot Of(Box card, Box screen) => new(
        Math.Clamp((card.X - screen.X) / screen.Width, 0, 1),
        Math.Clamp((card.Y - screen.Y) / screen.Height, 0, 1));
}

/// <summary>Where the quick price card goes on the game window. Next to the game's tooltip (the default):
/// beside it towards the middle of the screen, lined up with its top; if that does not fit, the other side,
/// then below, then above; never over the tooltip, the hovered item or the skill bar, and when nothing fits,
/// the screen edge. Everything is in physical pixels of the game window, so it works at any resolution.</summary>
public static class CardPlacement
{
    /// <summary>The bottom of the screen kept clear for the skill bar and the life and mana globes.</summary>
    public const double SkillBarShare = 0.17;

    /// <summary>Between the card and the tooltip, and around the screen, at 1080 pixels high. Wider than it
    /// looks it needs: the tooltip's box is a guess (<see cref="TooltipGuess"/>).</summary>
    public const double Gap = 16;

    public static PlacedCard Place(CardPosition position, Box screen, Pos cursor, double width, double height, string? itemText, FixedSpot? spot)
    {
        if (position == CardPosition.Fixed && spot is { } s)
        {
            return Fixed(screen, s, width, height);
        }

        if (position == CardPosition.Tooltip && TooltipGuess.For(itemText, screen, cursor) is { } tooltip)
        {
            return NextToTooltip(screen, cursor, width, height, tooltip);
        }

        return Edge(screen, cursor, width, height);
    }

    /// <summary>The screen less a margin, above the skill bar.</summary>
    public static Box SafeArea(Box screen)
    {
        var m = Margin(screen);
        return new Box(screen.X + m, screen.Y + m, screen.Width - (2 * m), (screen.Height * (1 - SkillBarShare)) - m);
    }

    public static PlacedCard NextToTooltip(Box screen, Pos cursor, double width, double height, Box tooltip)
    {
        var safe = SafeArea(screen);
        var m = Margin(screen);
        var item = TooltipGuess.Item(tooltip, screen, cursor);
        var towardLeft = tooltip.Centre.X < cursor.X;
        var leftOf = tooltip.X - m - width;
        var rightOf = tooltip.Right + m;
        var centred = Math.Max(safe.X, Math.Min(tooltip.X + ((tooltip.Width - width) / 2), safe.Right - width));
        var level = Math.Max(safe.Y, Math.Min(tooltip.Y, safe.Bottom - height));
        (CardSpot Spot, double X, double Y)[] tries =
        [
            (CardSpot.BesideTooltip, towardLeft ? leftOf : rightOf, level),
            (CardSpot.OtherSideOfTooltip, towardLeft ? rightOf : leftOf, level),
            (CardSpot.BelowTooltip, centred, tooltip.Bottom + m),
            (CardSpot.AboveTooltip, centred, tooltip.Y - m - height),
        ];

        foreach (var (spot, x, y) in tries)
        {
            var card = new Box(x, y, width, height);
            if (card.Within(safe) && !card.Overlaps(tooltip) && !card.Overlaps(item))
            {
                return new PlacedCard(card.Rounded(), spot, tooltip, item);
            }
        }

        return Edge(screen, cursor, width, height) with { Tooltip = tooltip, Item = item };
    }

    /// <summary>The half of the screen away from the cursor (where the game draws its tooltip), clear of the
    /// screen centre, the skill bar and the top-right map: the right side starts lower, below the map.</summary>
    public static PlacedCard Edge(Box screen, Pos cursor, double width, double height)
    {
        var left = TooltipGuess.TowardLeft(screen, cursor);
        var margin = Math.Floor(screen.Width * 0.03);
        var x = left ? screen.X + margin : screen.Right - margin - width;
        var top = screen.Y + Math.Floor(screen.Height * (left ? 0.16 : 0.27));
        var bottomLimit = screen.Bottom - Math.Floor(screen.Height * SkillBarShare);
        var y = Math.Max(screen.Y + margin, Math.Min(top, bottomLimit - height));
        return new PlacedCard(new Box(x, y, width, height).Rounded(), CardSpot.ScreenEdge);
    }

    /// <summary>Where the player dragged it, kept on the screen. The skill bar is theirs to cover.</summary>
    public static PlacedCard Fixed(Box screen, FixedSpot spot, double width, double height) =>
        new(new Box(screen.X + (spot.Left * screen.Width), screen.Y + (spot.Top * screen.Height), width, height).KeptWithin(screen).Rounded(), CardSpot.Fixed);

    /// <summary>The card changed size where it is (Search added its listings): it keeps its corner unless
    /// it would then cover the skill bar (not for a fixed spot) or leave the screen, and then moves up just
    /// enough. It never jumps to another rule's spot under the player's cursor.</summary>
    public static Box Resized(Box placed, double width, double height, Box screen, bool keepClearOfSkillBar)
    {
        var area = keepClearOfSkillBar ? SafeArea(screen) : screen;
        var resized = placed with { Width = width, Height = height };
        var x = Math.Max(area.X, Math.Min(resized.X, area.Right - width));
        var y = Math.Max(area.Y, Math.Min(resized.Y, area.Bottom - height));
        return resized.MovedTo(Math.Min(x, resized.X), Math.Min(y, resized.Y)).Rounded();
    }

    private static double Margin(Box screen) => Math.Round(Gap * screen.Height / TooltipGuess.ReferenceHeight);
}
