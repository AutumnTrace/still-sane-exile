using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Domain.Capture;
using StillSaneExile.Domain.Placement;

namespace StillSaneExile.Desktop.Tests;

/// <summary>How the HUD answers a press: a ring at the cursor straight away, before the read; a red ring
/// and a note instead of a card when the game copied nothing; the same card, flashed, when the item it
/// shows is checked again; a quick fade on letting go. Every check here is with fakes: what it looks like
/// over the game is for the owner to see in the game.</summary>
public class HudFeedbackTests
{
    private const string Crossbow = "rare-crossbow-advanced.txt";
    private const string Ring = "unique-ring.txt";
    private static readonly Hotkey CtrlC = Hotkey.Parse("Ctrl+C")!;

    /// <summary>Peek the crossbow, move onto the card while holding, let go.</summary>
    private static PeekViewModel PinnedCard(HudRig rig)
    {
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Cursor.MoveTo(FakeCursor.OnCard);
        rig.Keys.Release(CtrlC);
        Sta.Drain();
        var card = Assert.Single(rig.Peek.Shown);
        Assert.True(card.IsPinned);
        return card;
    }

    [Fact]
    public void A_press_is_answered_at_the_cursor_before_the_game_has_copied_anything() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        rig.Clipboard.HoldResults = true;

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        var cue = Assert.Single(rig.Peek.Heard);
        Assert.Equal(FakeCursor.Away, cue.Cursor);
        Assert.Equal(FakeForeground.GameWindow, cue.Over);
        Assert.Empty(rig.Peek.Shown);
        Assert.Equal(0, rig.Clipboard.Reads);

        rig.Clipboard.CompleteRead();
        Sta.Drain();

        var request = Assert.Single(rig.Peek.Requests);
        Assert.Equal(FakeCursor.Away, request.Cursor); // the card is placed by where the cursor was at the press
        Assert.Equal(rig.App.ItemText(Crossbow), request.ItemText); // to guess the game's tooltip from
        Assert.Equal(CardPosition.Tooltip, request.Position);
        Assert.False(request.SameItem);
        Assert.Single(rig.Peek.Heard);
        Assert.Equal(1, rig.Clipboard.Reads);
    });

    [Fact]
    public void Auto_repeat_synthetic_input_and_other_programs_get_no_ring() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        for (var i = 0; i < 10; i++)
        {
            rig.Keys.Press(CtrlC);
        }

        rig.Keys.Press(CtrlC, injected: true);
        rig.Keys.Release(CtrlC);
        rig.Keys.Press(CtrlC, injected: true);
        rig.Foreground.Current = ForegroundKind.Other;
        rig.Keys.Release(CtrlC);
        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.Single(rig.Peek.Heard);
        Assert.Equal(1, rig.Clipboard.Reads);
    });

    [Fact]
    public void Nothing_under_the_cursor_shows_a_red_ring_and_a_note_instead_of_a_card() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.NothingCopied = true;

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.Empty(rig.Peek.Shown);
        Assert.False(rig.Peek.IsVisible);
        var (cue, note) = Assert.Single(rig.Peek.Misses);
        Assert.Equal(HudController.MissNote, note);
        Assert.Equal(FakeCursor.Away, cue.Cursor);
        Assert.Equal(FakeForeground.GameWindow, cue.Over);
        Assert.Equal(0, rig.Clipboard.Reads); // the game copied nothing, so nothing was read
        Assert.False(rig.Cursor.IsRunning); // no card to move onto

        rig.Keys.Release(CtrlC);
        Sta.Drain();
        Assert.Single(rig.Peek.Misses);
        Assert.Equal(0, rig.Hud.Counters.StaleResultsDropped);
    });

    [Fact]
    public void A_quick_tap_over_nothing_still_gets_its_note() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.NothingCopied = true;
        rig.Clipboard.HoldResults = true;

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Keys.Release(CtrlC); // let go before the game's copy could have landed
        Sta.Drain();
        rig.Clipboard.CompleteRead();
        Sta.Drain();

        Assert.Single(rig.Peek.Misses);
        Assert.Empty(rig.Peek.Shown);
    });

    [Fact]
    public void A_miss_never_shows_after_another_program_came_to_the_front() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.NothingCopied = true;
        rig.Clipboard.HoldResults = true;

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        var cleared = rig.Peek.CuesCleared;
        rig.Foreground.Switch(ForegroundKind.Other);
        rig.Clipboard.CompleteRead();
        Sta.Drain();

        Assert.Empty(rig.Peek.Misses);
        Assert.True(rig.Peek.CuesCleared > cleared); // the ring went with the game's foreground
    });

    [Fact]
    public void A_miss_that_lands_after_a_newer_press_never_shows() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.NothingCopied = true;
        rig.Clipboard.HoldResults = true;
        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Keys.Release(CtrlC);

        rig.Clipboard.HoldResults = false;
        rig.Clipboard.NothingCopied = false;
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        rig.Keys.Press(CtrlC); // the next item, copied at once
        Sta.Drain();
        rig.Clipboard.NothingCopied = true;
        rig.Clipboard.CompleteRead(); // the first press's empty wait ends last
        Sta.Drain();

        Assert.Empty(rig.Peek.Misses);
        Assert.Single(rig.Peek.Shown);
        Assert.True(rig.Peek.IsVisible);
    });

    [Fact]
    public void With_press_again_to_hide_a_miss_leaves_nothing_to_hide() => Sta.Run(() =>
    {
        using var rig = new HudRig(s => s.PeekMode = HoldMode.Toggle);
        rig.Clipboard.NothingCopied = true;
        rig.Keys.Press(CtrlC);
        rig.Keys.Release(CtrlC);
        Sta.Drain();
        Assert.Single(rig.Peek.Misses);

        rig.Clipboard.NothingCopied = false;
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        rig.Keys.Press(CtrlC); // reads, rather than hiding a card that never showed
        Sta.Drain();

        Assert.Single(rig.Peek.Shown);
        Assert.True(rig.Peek.IsVisible);
        Assert.Equal(1, rig.Clipboard.Reads);
    });

    [Fact]
    public void A_miss_takes_a_card_kept_open_from_before_away() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        PinnedCard(rig);
        rig.Clipboard.NothingCopied = true;

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.False(rig.Peek.IsVisible);
        Assert.Single(rig.Peek.Misses);
        Assert.Single(rig.Peek.Shown);
    });

    [Fact]
    public void Pressing_again_over_the_item_a_kept_card_shows_keeps_its_search_and_only_flashes_it() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        var first = PinnedCard(rig);
        var item = first.Item!;
        var dps = first.LiveRows.OfType<SearchRowViewModel>().First(r => r.IsOn);
        dps.IsOn = false;
        item.SearchCommand.Execute(null); // the player changed the search on the card
        var answer = item.Answer;
        var recent = rig.Shell.Inspect.Recent.Count;

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.Equal(2, rig.Peek.Shown.Count);
        Assert.True(rig.Peek.Requests[1].SameItem); // no new card: it stays where it is and flashes
        Assert.Same(item, rig.Peek.Shown[1].Item);
        Assert.Same(answer, rig.Peek.Shown[1].Answer); // the search is kept
        Assert.Equal(1, rig.Peek.Shown[1].OnCount); // and so are its ticks
        Assert.Equal(recent, rig.Shell.Inspect.Recent.Count);
        Assert.Equal(2, rig.Clipboard.Reads);
        Assert.Equal(2, rig.Peek.Heard.Count);

        rig.Keys.Release(CtrlC); // not moved onto again: it hides, as any card that was replaced
        Sta.Drain();
        Assert.False(rig.Peek.IsVisible);
    });

    [Fact]
    public void Another_item_is_a_new_card() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        PinnedCard(rig);
        rig.Clipboard.Text = rig.App.ItemText(Ring);

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.False(rig.Peek.Requests[1].SameItem);
        Assert.Equal("Sanity's Last Loop", rig.Peek.Shown[1].Title);
    });

    [Fact]
    public void The_same_item_after_its_card_has_gone_is_a_new_card() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Keys.Release(CtrlC);
        Sta.Drain();
        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.Equal(2, rig.Peek.Requests.Count);
        Assert.False(rig.Peek.Requests[1].SameItem);
    });

    [Fact]
    public void Letting_go_fades_the_card_and_anything_else_in_front_hides_it_at_once() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Keys.Release(CtrlC);
        Sta.Drain();
        Assert.Equal([true], rig.Peek.HiddenFading);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        var cleared = rig.Peek.CuesCleared;
        rig.Foreground.Switch(ForegroundKind.Other);
        Sta.Drain();
        Assert.Equal([true, false], rig.Peek.HiddenFading);
        Assert.True(rig.Peek.CuesCleared > cleared);
    });

    [Fact]
    public void Press_again_to_hide_fades_the_card_too() => Sta.Run(() =>
    {
        using var rig = new HudRig(s => s.PeekMode = HoldMode.Toggle);
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Keys.Release(CtrlC);
        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.Equal([true], rig.Peek.HiddenFading);
        Assert.Single(rig.Peek.Heard); // hiding is not a read: no ring
    });

    [Fact]
    public void The_players_placement_motion_and_click_go_with_every_press() => Sta.Run(() =>
    {
        using var rig = new HudRig(s =>
        {
            s.CardPosition = CardPosition.Fixed;
            s.CardSpot = new FixedSpot(0.5, 0.3);
            s.LessMotion = true;
            s.PressClick = true;
            s.PeekScale = 1.2;
        });
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        var cue = Assert.Single(rig.Peek.Heard);
        Assert.True(cue.LessMotion);
        Assert.True(cue.Click);
        var request = Assert.Single(rig.Peek.Requests);
        Assert.Equal(CardPosition.Fixed, request.Position);
        Assert.Equal(new FixedSpot(0.5, 0.3), request.Spot);
        Assert.True(request.LessMotion);
        Assert.Equal(1.2, request.Scale);
    });

    [Fact]
    public void A_new_profile_answers_without_a_click_and_with_full_motion() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        var cue = Assert.Single(rig.Peek.Heard);
        Assert.False(cue.Click);
        Assert.False(cue.LessMotion);
        Assert.Null(Assert.Single(rig.Peek.Requests).Spot);
    });

    [Fact]
    public void Dragging_a_kept_card_with_fixed_spot_saves_where_it_goes_and_reads_nothing() => Sta.Run(() =>
    {
        using var rig = new HudRig(s => s.CardPosition = CardPosition.Fixed);
        rig.Shell.OpenSettings("card");
        Assert.Contains("Until then it shows at the screen edge", rig.Shell.SettingsPage.PositionNote);
        rig.Shell.GoInspectCommand.Execute(null);
        PinnedCard(rig);

        rig.Peek.DragTo(new FixedSpot(0.45, 0.2));

        Assert.Equal(new FixedSpot(0.45, 0.2), rig.Shell.Settings.CardSpot);
        Assert.StartsWith("Where you last dragged it", rig.Shell.SettingsPage.PositionNote);
        Assert.Equal(1, rig.Clipboard.Reads);

        rig.Peek.ClickClose();
        rig.BackToGame();
        rig.Keys.Press(CtrlC);
        Sta.Drain();
        Assert.Equal(new FixedSpot(0.45, 0.2), rig.Peek.Requests[^1].Spot);
    });

    [Fact]
    public void A_message_card_has_no_item_text_to_place_by() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = "Some chat line the player copied";

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        var card = Assert.Single(rig.Peek.Shown);
        Assert.True(card.IsMessage);
        Assert.Null(Assert.Single(rig.Peek.Requests).ItemText);
        Assert.Empty(rig.Peek.Misses); // the game did copy something: it was just not an item
    });

    [Fact]
    public void Practice_cues_go_over_SSE_and_the_card_is_placed_by_the_practice_item() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Shell.Inspect.LoadPracticeCommand.Execute(rig.Shell.Inspect.PracticeItems[0]);
        rig.Foreground.Current = ForegroundKind.ThisApp;

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.Equal(FakeShellWindow.MainWindow, Assert.Single(rig.Peek.Heard).Over);
        var request = Assert.Single(rig.Peek.Requests);
        Assert.Equal(FakeShellWindow.MainWindow, request.Over);
        Assert.Equal(rig.Shell.Inspect.Selected!.Item.RawText, request.ItemText);
        Assert.Equal(0, rig.Clipboard.Reads);
    });
}
