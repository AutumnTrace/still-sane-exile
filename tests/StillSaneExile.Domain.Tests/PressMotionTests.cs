using StillSaneExile.Domain.Placement;

namespace StillSaneExile.Domain.Tests;

/// <summary>How a press is answered, in the design's timings: under 100 ms feels instant, hiding is faster
/// than showing, and less motion keeps only fades.</summary>
public class PressMotionTests
{
    [Fact]
    public void The_timings_are_the_designs()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(240), PressMotion.RingFor);
        Assert.Equal(TimeSpan.FromMilliseconds(140), PressMotion.SlideFor);
        Assert.Equal(18, PressMotion.SlideDistance);
        Assert.Equal(TimeSpan.FromMilliseconds(520), PressMotion.LineFor);
        Assert.Equal(TimeSpan.FromMilliseconds(450), PressMotion.FlashFor);
        Assert.Equal(TimeSpan.FromMilliseconds(90), PressMotion.FadeOutFor);
        Assert.Equal(TimeSpan.FromMilliseconds(120), PressMotion.FadeInFor);
        Assert.Equal(TimeSpan.FromSeconds(1.6), PressMotion.MissFor);
        Assert.Equal(TimeSpan.FromMilliseconds(280), PressMotion.ShakeFor);
    }

    [Fact]
    public void Hiding_is_faster_than_showing()
    {
        Assert.True(PressMotion.FadeOutFor < PressMotion.Full.CardInFor);
        Assert.True(PressMotion.FadeOutFor < PressMotion.Less.CardInFor);
    }

    [Fact]
    public void The_flash_comes_once_the_card_has_landed()
    {
        Assert.Equal(PressMotion.SlideFor, PressMotion.Full.CardInFor);
        Assert.Equal(PressMotion.FadeInFor, PressMotion.Less.CardInFor);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)] // the player's Less motion
    [InlineData(false, false, false)] // Windows' animation effects off
    [InlineData(true, false, false)]
    public void Less_motion_keeps_only_fades(bool lessMotion, bool windowsAnimates, bool moves)
    {
        var motion = PressMotion.For(lessMotion, windowsAnimates);

        Assert.Equal(moves, motion.Ring);
        Assert.Equal(moves, motion.Slide);
        Assert.Equal(moves, motion.Line);
        Assert.Equal(moves, motion.FlashOnShow);
    }

    [Fact]
    public void The_card_slides_in_from_the_cursors_side()
    {
        var card = new Box(100, 100, 300, 400);

        Assert.Equal((18.0, 0.0), PressMotion.SlideFrom(card, new Pos(900, 300), 18)); // cursor to the right
        Assert.Equal((-18.0, 0.0), PressMotion.SlideFrom(card, new Pos(20, 250), 18));
        Assert.Equal((0.0, 18.0), PressMotion.SlideFrom(card, new Pos(200, 800), 18)); // below

        var (x, y) = PressMotion.SlideFrom(card, new Pos(700, 800), 18); // below and to the right
        Assert.Equal(18, Math.Sqrt((x * x) + (y * y)), 6);
        Assert.True(x > 0 && y > 0);

        Assert.Equal((0.0, 0.0), PressMotion.SlideFrom(card, new Pos(200, 200), 18)); // the cursor is on it
    }

    [Fact]
    public void The_line_goes_to_the_nearest_edge_only_when_the_card_is_further_away()
    {
        var card = new Box(100, 100, 300, 400);

        Assert.Equal(new Pos(400, 300), PressMotion.LineEnd(card, new Pos(900, 300), PressMotion.MinLineLength));
        Assert.Null(PressMotion.LineEnd(card, new Pos(420, 300), PressMotion.MinLineLength));
        Assert.Null(PressMotion.LineEnd(card, new Pos(200, 200), PressMotion.MinLineLength));
    }
}
