using StillSaneExile.Domain.Placement;

namespace StillSaneExile.Domain.Tests;

/// <summary>The game's tooltip, guessed from the copied text: lines give the height, the longest line the
/// width, the window's height the scale and the cursor the side. Not measured in the game yet.</summary>
public class TooltipGuessTests
{
    private static readonly Box Screen = new(0, 0, 1920, 1080);

    private static string Advanced => Fixtures.Text("items/rare-crossbow-advanced.txt");

    /// <summary>The same item as a normal copy would print it: no affix lines in braces.</summary>
    private static string Normal => string.Join("\n", Advanced.ReplaceLineEndings("\n").Split('\n').Where(l => !l.StartsWith('{')));

    [Fact]
    public void It_counts_what_the_tooltip_prints()
    {
        var advanced = TooltipGuess.Measure(Advanced)!.Value;
        Assert.Equal(2, advanced.Header); // name and base; the rarity is their colour
        Assert.Equal(7, advanced.Separators);
        Assert.Equal(23, advanced.Body);

        var normal = TooltipGuess.Measure(Normal)!.Value;
        Assert.Equal(advanced.Body - 6, normal.Body);
        Assert.Equal("Grenade Skills Fire an additional Projectile".Length, normal.Longest); // "(implicit)" is the copy's, not the tooltip's
    }

    [Fact]
    public void An_advanced_copy_is_a_taller_tooltip()
    {
        var cursor = new Pos(1560, 540);
        var advanced = TooltipGuess.For(Advanced, Screen, cursor)!.Value;
        var normal = TooltipGuess.For(Normal, Screen, cursor)!.Value;

        Assert.Equal(6 * TooltipGuess.BodyLine, advanced.Height - normal.Height, 3);
        Assert.InRange(normal.Height, 300, 700);
        Assert.InRange(normal.Width, TooltipGuess.MinWidth, TooltipGuess.MaxWidth);
    }

    [Fact]
    public void It_opens_towards_the_middle_of_the_screen_level_with_the_cursor()
    {
        var right = TooltipGuess.For(Advanced, Screen, new Pos(1560, 540))!.Value;
        Assert.Equal(1560 - TooltipGuess.Cell, right.Right, 3);
        Assert.Equal(540, right.Centre.Y, 3);

        var left = TooltipGuess.For(Advanced, Screen, new Pos(380, 540))!.Value;
        Assert.Equal(380 + TooltipGuess.Cell, left.X, 3);
    }

    [Fact]
    public void It_stays_on_the_screen()
    {
        var top = TooltipGuess.For(Advanced, Screen, new Pos(1560, 20))!.Value;
        Assert.Equal(0, top.Y);
        Assert.True(top.Within(Screen));

        var bottom = TooltipGuess.For(Advanced, Screen, new Pos(380, 1070))!.Value;
        Assert.Equal(1080, bottom.Bottom, 3);
    }

    [Fact]
    public void It_scales_with_the_game_window_height()
    {
        var hd = TooltipGuess.For(Normal, Screen, new Pos(1560, 540))!.Value;
        var uhd = TooltipGuess.For(Normal, new Box(0, 0, 3840, 2160), new Pos(3120, 1080))!.Value;

        Assert.Equal(hd.Width * 2, uhd.Width, 3);
        Assert.Equal(hd.Height * 2, uhd.Height, 3);
    }

    [Fact]
    public void The_hovered_item_lies_between_the_tooltip_and_a_cell_past_the_cursor()
    {
        var cursor = new Pos(1560, 540);
        var tooltip = TooltipGuess.For(Advanced, Screen, cursor)!.Value;
        var item = TooltipGuess.Item(tooltip, Screen, cursor);

        Assert.Equal(tooltip.Right, item.X, 3);
        Assert.Equal(cursor.X + TooltipGuess.Cell, item.Right, 3);
        Assert.Equal(4 * TooltipGuess.Cell, item.Height, 3);
        Assert.Equal(cursor.Y, item.Centre.Y, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("--------\n--------")]
    public void Nothing_to_measure_is_no_tooltip(string? text) => Assert.Null(TooltipGuess.For(text, Screen, new Pos(900, 500)));
}
