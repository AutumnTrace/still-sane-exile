using System.Text.RegularExpressions;

namespace StillSaneExile.Domain.Placement;

/// <summary>Where the game's own tooltip for the copied item probably is, so the card can sit beside it
/// instead of on it: its size from the copied text (lines give the height, the longest line the width), its
/// side from the cursor (the game opens it towards the middle of the screen), level with the cursor.
/// A guess, not a measurement: SSE never looks at the screen for it, and the sizes below are estimates for
/// the game's default UI scale that have not been measured in the game yet (#5). The card keeps a margin
/// from the guess for that reason.</summary>
public static partial class TooltipGuess
{
    /// <summary>The game window height the sizes below are written for; the game's UI scales with the
    /// window's height.</summary>
    public const double ReferenceHeight = 1080;

    // Unverified estimates at 1080 pixels high: a header line (name, base) and a body line, the gap a
    // separator leaves, the panel's padding, an average character, the narrowest and widest tooltip, and an
    // inventory cell (the tooltip opens about a cell from the cursor, past the edge of the hovered item).
    public const double HeaderLine = 30;
    public const double BodyLine = 20;
    public const double SeparatorGap = 12;
    public const double PaddingY = 16;
    public const double CharWidth = 8.5;
    public const double PaddingX = 32;
    public const double MinWidth = 280;
    public const double MaxWidth = 560;
    public const double Cell = 53;

    /// <summary>The name and base are drawn bigger than the stats; an advanced copy's affix lines smaller.</summary>
    private const double HeaderCharScale = 1.35;
    private const double AffixCharScale = 0.85;

    private const string Separator = "--------";

    /// <summary>What the copy adds to a line that the tooltip itself does not print.</summary>
    [GeneratedRegex(@"\s\((implicit|rune|augmented|crafted|enchant|desecrated|fractured|unmet)\)$")]
    private static partial Regex CopyNote();

    /// <summary>The tooltip's box on <paramref name="screen"/> (the game window) for an item copied with the
    /// cursor at <paramref name="cursor"/>; null when <paramref name="itemText"/> has nothing to measure.</summary>
    public static Box? For(string? itemText, Box screen, Pos cursor)
    {
        var size = Measure(itemText);
        if (size is not { } s)
        {
            return null;
        }

        var k = screen.Height / ReferenceHeight;
        var width = Math.Min(Math.Clamp((s.Longest * CharWidth) + PaddingX, MinWidth, MaxWidth) * k, screen.Width);
        var height = Math.Min(((s.Header * HeaderLine) + (s.Body * BodyLine) + (s.Separators * SeparatorGap) + (2 * PaddingY)) * k, screen.Height);
        var gap = Cell * k;
        var x = TowardLeft(screen, cursor) ? cursor.X - gap - width : cursor.X + gap;
        return new Box(x, cursor.Y - (height / 2), width, height).KeptWithin(screen);
    }

    /// <summary>The hovered item, about: from the tooltip's near edge to a cell past the cursor, and two cells
    /// up and down (items are up to two cells wide and four tall, and the cursor can be anywhere on one).</summary>
    public static Box Item(Box tooltip, Box screen, Pos cursor)
    {
        var cell = Cell * screen.Height / ReferenceHeight;
        var tooltipLeft = tooltip.Centre.X < cursor.X;
        var left = tooltipLeft ? tooltip.Right : cursor.X - cell;
        var right = tooltipLeft ? cursor.X + cell : tooltip.X;
        return new Box(left, cursor.Y - (2 * cell), Math.Max(0, right - left), 4 * cell);
    }

    /// <summary>The game opens the tooltip towards the middle of the screen: to the left of an item on the
    /// right half.</summary>
    public static bool TowardLeft(Box screen, Pos cursor) => cursor.X > screen.X + (screen.Width / 2);

    /// <summary>Header lines, body lines, separators and the longest line in average characters.</summary>
    public static (int Header, int Body, int Separators, double Longest)? Measure(string? itemText)
    {
        if (string.IsNullOrWhiteSpace(itemText))
        {
            return null;
        }

        int header = 0, body = 0, separators = 0;
        double longest = 0;
        var inHeader = false;
        foreach (var raw in itemText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line == Separator)
            {
                if (header > 0 || body > 0)
                {
                    separators++;
                }

                inHeader = false;
                continue;
            }

            // The rarity is the name's colour, not a line; the name and base follow it.
            if (line.StartsWith("Rarity:", StringComparison.Ordinal))
            {
                inHeader = true;
                continue;
            }

            line = CopyNote().Replace(line, "");
            if (line.StartsWith("Item Class:", StringComparison.Ordinal))
            {
                line = line["Item Class:".Length..].Trim();
            }

            double width = line.Length;
            if (inHeader)
            {
                header++;
                width *= HeaderCharScale;
            }
            else
            {
                body++;
                if (line.StartsWith('{'))
                {
                    width *= AffixCharScale;
                }
            }

            longest = Math.Max(longest, width);
        }

        return header + body == 0 ? null : (header, body, separators, longest);
    }
}
