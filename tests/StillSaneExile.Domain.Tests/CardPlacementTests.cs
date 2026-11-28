using StillSaneExile.Domain.Placement;

namespace StillSaneExile.Domain.Tests;

/// <summary>Where the quick price card goes: beside the game's tooltip towards the middle of the screen,
/// then the other side, below, above, and the screen edge when nothing fits; never over the tooltip, the
/// hovered item or the skill bar.</summary>
public class CardPlacementTests
{
    private static readonly Box Screen = new(0, 0, 1920, 1080);

    /// <summary>The card at the default scale (0.85 of 340 wide), about as tall as a rare's card.</summary>
    private const double W = 289;
    private const double H = 425;

    private static string Crossbow => Fixtures.Text("items/rare-crossbow-advanced.txt");

    [Fact]
    public void An_inventory_item_gets_the_card_left_of_its_tooltip_lined_up_with_its_top()
    {
        var cursor = new Pos(1560, 700); // the inventory, on the right
        var placed = CardPlacement.Place(CardPosition.Tooltip, Screen, cursor, W, H, Crossbow, spot: null);

        Assert.Equal(CardSpot.BesideTooltip, placed.Spot);
        var tooltip = placed.Tooltip!.Value;
        Assert.True(tooltip.Right <= cursor.X, "the game opens the tooltip towards the middle");
        Assert.Equal(tooltip.X - CardPlacement.Gap - W, placed.Card.X, 0);
        Assert.Equal(Math.Max(CardPlacement.SafeArea(Screen).Y, tooltip.Y), placed.Card.Y, 0);
        AssertClear(placed);
    }

    [Fact]
    public void A_stash_item_gets_the_card_right_of_its_tooltip()
    {
        var cursor = new Pos(380, 420); // the stash, on the left
        var placed = CardPlacement.Place(CardPosition.Tooltip, Screen, cursor, W, H, Crossbow, spot: null);

        Assert.Equal(CardSpot.BesideTooltip, placed.Spot);
        var tooltip = placed.Tooltip!.Value;
        Assert.True(tooltip.X >= cursor.X);
        Assert.Equal(tooltip.Right + CardPlacement.Gap, placed.Card.X, 0);
        AssertClear(placed);
    }

    [Fact]
    public void The_card_sits_much_closer_to_the_cursor_than_at_the_screen_edge()
    {
        var cursor = new Pos(1560, 700);
        var beside = CardPlacement.Place(CardPosition.Tooltip, Screen, cursor, W, H, Crossbow, spot: null).Card;
        var edge = CardPlacement.Place(CardPosition.Edge, Screen, cursor, W, H, Crossbow, spot: null).Card;

        Assert.True(beside.Nearest(cursor).DistanceTo(cursor) < edge.Nearest(cursor).DistanceTo(cursor) * 0.75);
    }

    [Fact]
    public void Without_room_beside_it_the_card_takes_the_other_side()
    {
        var tooltip = new Box(40, 0, 400, 860);
        var placed = CardPlacement.NextToTooltip(Screen, new Pos(493, 700), 300, 300, tooltip);

        Assert.Equal(CardSpot.OtherSideOfTooltip, placed.Spot);
        Assert.Equal(456, placed.Card.X);
        AssertClear(placed);
    }

    [Fact]
    public void Without_room_on_either_side_it_goes_below_then_above()
    {
        var below = CardPlacement.NextToTooltip(Screen, new Pos(1453, 250), 600, 300, new Box(500, 100, 900, 300));
        Assert.Equal(CardSpot.BelowTooltip, below.Spot);
        Assert.Equal(416, below.Card.Y);
        Assert.Equal(650, below.Card.X); // centred under the tooltip
        AssertClear(below);

        var above = CardPlacement.NextToTooltip(Screen, new Pos(1453, 650), 600, 300, new Box(500, 500, 900, 300));
        Assert.Equal(CardSpot.AboveTooltip, above.Spot);
        Assert.Equal(184, above.Card.Y);
        AssertClear(above);
    }

    [Fact]
    public void When_nothing_fits_it_goes_to_the_screen_edge()
    {
        var placed = CardPlacement.NextToTooltip(Screen, new Pos(1453, 650), 600, 800, new Box(500, 300, 900, 500));

        Assert.Equal(CardSpot.ScreenEdge, placed.Spot);
        Assert.Equal(CardPlacement.Edge(Screen, new Pos(1453, 650), 600, 800).Card, placed.Card);
    }

    [Fact]
    public void The_card_never_covers_the_skill_bar_or_leaves_the_screen()
    {
        var safe = CardPlacement.SafeArea(Screen);
        Assert.Equal(Screen.Bottom * (1 - CardPlacement.SkillBarShare), safe.Bottom, 3);
        Assert.Equal(CardPlacement.Gap, safe.Y);

        for (var x = 40; x < 1920; x += 120)
        {
            for (var y = 40; y < 1040; y += 100)
            {
                var placed = CardPlacement.Place(CardPosition.Tooltip, Screen, new Pos(x, y), W, H, Crossbow, spot: null);
                Assert.True(placed.Card.Within(Screen), $"off the screen for the cursor at {x},{y}");
                Assert.True(placed.Card.Bottom <= Screen.Height * (1 - CardPlacement.SkillBarShare), $"over the skill bar for the cursor at {x},{y}");
                if (placed.Spot != CardSpot.ScreenEdge)
                {
                    AssertClear(placed);
                }
            }
        }
    }

    [Fact]
    public void It_works_the_same_on_a_bigger_window_and_away_from_the_screen_origin()
    {
        var big = new Box(1920, 0, 3840, 2160); // a 4K game on the second monitor
        var small = CardPlacement.Place(CardPosition.Tooltip, Screen, new Pos(1560, 700), W, H, Crossbow, spot: null);
        var large = CardPlacement.Place(CardPosition.Tooltip, big, new Pos(1920 + 3120, 1400), W * 2, H * 2, Crossbow, spot: null);

        Assert.Equal(small.Spot, large.Spot);
        Assert.InRange(large.Card.X - 1920 - (small.Card.X * 2), -1, 1); // whole pixels either way
        Assert.InRange(large.Card.Y - (small.Card.Y * 2), -1, 1);
    }

    [Fact]
    public void A_message_card_has_no_tooltip_to_go_beside_and_goes_to_the_screen_edge()
    {
        var placed = CardPlacement.Place(CardPosition.Tooltip, Screen, new Pos(1560, 700), W, 200, itemText: null, spot: null);

        Assert.Equal(CardSpot.ScreenEdge, placed.Spot);
        Assert.Null(placed.Tooltip);
    }

    [Fact]
    public void The_screen_edge_is_the_half_away_from_the_cursor_as_before()
    {
        var left = CardPlacement.Edge(Screen, new Pos(1500, 500), W, H).Card;
        Assert.Equal(new Box(57, 172, W, H), left);

        var right = CardPlacement.Edge(Screen, new Pos(300, 500), W, H).Card;
        Assert.Equal(new Box(1920 - 57 - W, 291, W, H), right); // lower on the right: the map is up there

        var tall = CardPlacement.Edge(Screen, new Pos(300, 500), W, 800).Card;
        Assert.Equal(1080 - 183 - 800, tall.Y); // moved up to clear the skill bar
    }

    [Fact]
    public void A_fixed_spot_follows_the_window_size_and_stays_on_the_screen()
    {
        var spot = new FixedSpot(0.5, 0.25);
        Assert.Equal(new Box(960, 270, W, H), CardPlacement.Place(CardPosition.Fixed, Screen, new Pos(100, 100), W, H, Crossbow, spot).Card);
        Assert.Equal(CardSpot.Fixed, CardPlacement.Place(CardPosition.Fixed, Screen, new Pos(100, 100), W, H, Crossbow, spot).Spot);

        var corner = CardPlacement.Fixed(Screen, new FixedSpot(0.95, 0.9), W, H).Card;
        Assert.True(corner.Within(Screen));
        Assert.Equal(1920 - W, corner.X);

        var none = CardPlacement.Place(CardPosition.Fixed, Screen, new Pos(1500, 500), W, H, Crossbow, spot: null);
        Assert.Equal(CardSpot.ScreenEdge, none.Spot); // not dragged yet
    }

    [Fact]
    public void A_fixed_spot_is_stored_as_shares_of_the_window()
    {
        var spot = FixedSpot.Of(new Box(960, 270, W, H), Screen);
        Assert.Equal(new FixedSpot(0.5, 0.25), spot);
        Assert.Equal("0.5,0.25", spot.Format());
        Assert.Equal(spot, FixedSpot.Parse(spot.Format()));
        Assert.Equal(new FixedSpot(0.4123, 0.2), FixedSpot.Parse("0.4123,0.2"));

        Assert.Null(FixedSpot.Parse(null));
        Assert.Null(FixedSpot.Parse(""));
        Assert.Null(FixedSpot.Parse("left"));
        Assert.Null(FixedSpot.Parse("1.5,0.2"));
        Assert.Null(FixedSpot.Parse("0.2,-0.1"));
        Assert.Null(FixedSpot.Parse("0.2,0.3,0.4"));
    }

    [Fact]
    public void A_card_that_grows_keeps_its_place_and_moves_up_only_to_clear_the_skill_bar()
    {
        var placed = new Box(500, 300, W, 400);

        Assert.Equal(placed with { Height = 450 }, CardPlacement.Resized(placed, W, 450, Screen, keepClearOfSkillBar: true));
        Assert.Equal(placed, CardPlacement.Resized(placed with { Height = 500 }, W, 400, Screen, keepClearOfSkillBar: true)); // shrinking stays put

        var grown = CardPlacement.Resized(placed, W, 700, Screen, keepClearOfSkillBar: true);
        Assert.Equal(CardPlacement.SafeArea(Screen).Bottom, grown.Bottom, 0);
        Assert.Equal(500, grown.X);

        var fixedSpot = CardPlacement.Resized(placed, W, 700, Screen, keepClearOfSkillBar: false);
        Assert.Equal(300, fixedSpot.Y); // the player's own spot may cover the skill bar, never the screen's edge
        Assert.Equal(1080 - 900, CardPlacement.Resized(placed, W, 900, Screen, keepClearOfSkillBar: false).Y);
    }

    private static void AssertClear(PlacedCard placed)
    {
        Assert.True(placed.Card.Within(CardPlacement.SafeArea(Screen)), $"{placed.Card} is not inside the safe area");
        Assert.False(placed.Card.Overlaps(placed.Tooltip!.Value), $"{placed.Card} covers the tooltip {placed.Tooltip}");
        Assert.False(placed.Card.Overlaps(placed.Item!.Value), $"{placed.Card} covers the item {placed.Item}");
    }
}
