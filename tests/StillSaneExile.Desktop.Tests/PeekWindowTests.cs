using System.Windows;
using System.Windows.Interop;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Desktop.Views;
using StillSaneExile.Domain.Placement;

namespace StillSaneExile.Desktop.Tests;

/// <summary>The real card window and its cues, over a stand-in for the game far off the screen: the
/// placement goes through real window rectangles, the cues and the fade run, and nothing is activated.
/// What it looks like over the game is still for the owner to see.</summary>
public class PeekWindowTests
{
    private const string Crossbow = "rare-crossbow-advanced.txt";

    [Fact]
    public void The_card_goes_beside_the_guessed_tooltip_fades_on_release_and_never_activates() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var game = new Window
        {
            Left = -20000,
            Top = 0,
            Width = 1920,
            Height = 1080,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        var peek = new PeekWindow();
        var activated = false;
        peek.Activated += (_, _) => activated = true; // at any moment, not just at the end
        try
        {
            game.Show();
            var over = new WindowInteropHelper(game).Handle;
            var origin = game.PointToScreen(new Point(0, 0));
            // Windows keeps a window within the screen (1024 × 768 on CI), so the sizes are read back.
            var (width, height) = (game.ActualWidth, game.ActualHeight);
            var physical = game.PointToScreen(new Point(width, height));
            var at = new Point(width * 0.81, height * 0.65); // the inventory, in device-independent pixels
            var cursor = new ScreenPoint((int)(origin.X + ((physical.X - origin.X) * 0.81)), (int)(origin.Y + ((physical.Y - origin.Y) * 0.65)));
            var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "test")!;
            var card = PeekViewModel.From(item, true, "Let go to hide", "F3");
            IPeekSurface surface = peek;

            surface.KeyHeard(new PressCue(cursor, over, LessMotion: false, Click: false));
            surface.ShowCard(card, new CardRequest(CardPosition.Tooltip, 0.85, over, cursor, item.Item.RawText, Spot: null, LessMotion: false));
            Sta.Drain();

            Assert.True(surface.IsVisible);
            Assert.False(peek.IsActive);
            Assert.False(surface.IsNear(cursor)); // beside the tooltip, which is beside the item
            Assert.True(peek.Left + peek.ActualWidth < game.Left + at.X, "the card is not on the middle's side of the cursor");
            Assert.True(peek.Left > game.Left && peek.Top >= game.Top, "the card is not on the game window");
            Assert.True(peek.Top + peek.ActualHeight <= game.Top + (height * (1 - CardPlacement.SkillBarShare)) + 28, "the card covers the skill bar");
            var (left, top) = (peek.Left, peek.Top);

            surface.ShowCard(PeekViewModel.From(item, true, "Let go to hide", "F3"), new CardRequest(CardPosition.Tooltip, 0.85, over, new ScreenPoint(cursor.X - 500, cursor.Y), item.Item.RawText, null, false, SameItem: true));
            Sta.Drain();
            Assert.Equal((left, top), (peek.Left, peek.Top)); // the same item: it stays put and flashes

            surface.HideCard(fade: true);
            Assert.False(surface.IsVisible); // gone as far as the HUD is concerned, while it fades
            Assert.False(surface.IsNear(cursor));
            Sta.Until(() => !peek.IsVisible, "the card to fade out");

            surface.Missed(new PressCue(cursor, over, LessMotion: false, Click: false), Services.HudController.MissNote);
            surface.Missed(new PressCue(null, over, LessMotion: true, Click: false), Services.HudController.MissNote);
            Sta.Drain();
            surface.ClearCues();

            // The card never took the focus. (Whether the stand-in game window is active is not asked: it is a
            // window of this test process, and Windows hands it the focus when another test's window closes.)
            Assert.False(activated);
            Assert.False(peek.IsActive);
        }
        finally
        {
            peek.Close();
            game.Close();
        }
    });

    [Fact]
    public void A_fixed_spot_puts_the_card_there_whatever_the_cursor() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var game = new Window { Left = -20000, Top = 0, Width = 1920, Height = 1080, WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false };
        var peek = new PeekWindow();
        try
        {
            game.Show();
            var over = new WindowInteropHelper(game).Handle;
            var origin = game.PointToScreen(new Point(0, 0));
            var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "test")!;
            IPeekSurface surface = peek;

            surface.ShowCard(PeekViewModel.From(item, true, "Let go to hide", "F3"), new CardRequest(CardPosition.Fixed, 0.85, over, new ScreenPoint((int)origin.X + 10, (int)origin.Y + 10), item.Item.RawText, new FixedSpot(0.5, 0.05), LessMotion: true));
            Sta.Drain();

            // The visible card's corner, inside the room the window keeps around it for the slide and the flash.
            // The spot is near the top so the whole card fits under it even where Windows keeps the stand-in
            // game window within a small screen (1024 × 768 on CI): a card that would not fit is moved up to
            // stay on the game window, which is right, but not what this test is about.
            Assert.Equal(game.Left + (game.ActualWidth * 0.5), peek.Left + 22, 0);
            Assert.Equal(game.Top + (game.ActualHeight * 0.05), peek.Top + 20, 0);
        }
        finally
        {
            peek.Close();
            game.Close();
        }
    });
}
