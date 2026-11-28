using Microsoft.Win32;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Desktop.Tests;

/// <summary>Hold the key to see the card; move onto it while holding to keep it (pinned); let go
/// without moving and it hides. Pinned, it stays until closed, replaced by the next press, or hidden by
/// another program, minimize, lock or a display change.</summary>
public class HudPinTests
{
    private const string Crossbow = "rare-crossbow-advanced.txt";
    private const string Ring = "unique-ring.txt";
    private static readonly Hotkey CtrlC = Hotkey.Parse("Ctrl+C")!;
    private static readonly Hotkey F3 = Hotkey.Parse("F3")!;

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
        Assert.True(rig.Peek.IsVisible);
        return card;
    }

    [Fact]
    public void Letting_go_without_moving_onto_the_card_hides_it() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Cursor.MoveTo(new ScreenPoint(1400, 500)); // looking around the inventory
        rig.Cursor.MoveTo(new ScreenPoint(900, 650));
        var card = Assert.Single(rig.Peek.Shown);
        Assert.False(card.IsPinned);
        Assert.False(rig.Peek.TakesClicks);

        rig.Keys.Release(CtrlC);
        Sta.Drain();

        Assert.False(rig.Peek.IsVisible);
        Assert.Equal(1, rig.Clipboard.Reads);
    });

    [Fact]
    public void Moving_onto_the_card_while_holding_pins_it_and_letting_go_keeps_it() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        Assert.False(rig.Peek.TakesClicks); // click-through while held, until the cursor gets there

        rig.Cursor.MoveTo(FakeCursor.OnCard);

        var card = Assert.Single(rig.Peek.Shown);
        Assert.True(card.IsPinned);
        Assert.True(rig.Peek.TakesClicks);
        Assert.False(rig.Peek.HasFocus); // takes clicks, but takes nothing until clicked
        Assert.False(rig.Cursor.IsRunning);

        rig.Keys.Release(CtrlC);
        Sta.Drain();

        Assert.True(rig.Peek.IsVisible);
        Assert.Single(rig.Peek.Shown);
        Assert.Equal(1, rig.Clipboard.Reads);
        Assert.Equal(1, rig.Hud.Counters.Reads);
        Assert.Equal(0, rig.Shell.KeysSent);
    });

    [Fact]
    public void A_card_that_opens_under_the_cursor_pins_only_when_the_cursor_comes_back() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        rig.Cursor.Position = FakeCursor.OnCard; // a card at a fixed spot over the item

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Cursor.Poll();
        var card = Assert.Single(rig.Peek.Shown);
        Assert.False(card.IsPinned);

        rig.Cursor.MoveTo(FakeCursor.Away);
        rig.Cursor.MoveTo(FakeCursor.OnCard);

        Assert.True(card.IsPinned);
    });

    [Fact]
    public void Clicking_the_pinned_card_and_going_back_to_the_game_keep_it() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        PinnedCard(rig);

        rig.ClickCard();
        Sta.Drain();
        Assert.True(rig.Peek.HasFocus);
        Assert.True(rig.Peek.IsVisible);

        rig.BackToGame();
        Sta.Drain();
        Assert.True(rig.Peek.IsVisible);
        Assert.Equal(0, rig.Peek.FocusReturnedTo);
    });

    [Theory]
    [InlineData("close button")]
    [InlineData("Esc")]
    public void Closing_a_pinned_card_hides_it_and_hands_focus_back_to_the_game(string how) => Sta.Run(() =>
    {
        using var rig = new HudRig();
        PinnedCard(rig);
        rig.ClickCard(); // ticks a stat, say
        Sta.Drain();

        if (how == "Esc")
        {
            rig.Peek.PressEscape();
        }
        else
        {
            rig.Peek.ClickClose();
        }

        Sta.Drain();

        Assert.False(rig.Peek.IsVisible);
        Assert.Equal(FakeForeground.GameWindow, rig.Peek.FocusReturnedTo);
        Assert.Equal(1, rig.Clipboard.Reads);

        rig.BackToGame();
        rig.Keys.Press(CtrlC); // the next item: a fresh card
        Sta.Drain();
        rig.Keys.Release(CtrlC);
        Sta.Drain();
        Assert.Equal(2, rig.Clipboard.Reads);
        Assert.Equal(2, rig.Peek.Shown.Count);
        Assert.False(rig.Peek.IsVisible);
    });

    [Fact]
    public void The_next_press_replaces_a_pinned_card_with_exactly_one_read() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        var first = PinnedCard(rig);
        rig.Clipboard.Text = rig.App.ItemText(Ring);

        rig.Keys.Press(CtrlC);
        for (var i = 0; i < 20; i++)
        {
            rig.Keys.Press(CtrlC); // auto-repeat
        }

        rig.Keys.Press(CtrlC, injected: true);
        Sta.Drain();

        Assert.Equal(2, rig.Clipboard.Reads);
        Assert.Equal(2, rig.Peek.Shown.Count);
        var second = rig.Peek.Shown[1];
        Assert.Equal("Sanity's Last Loop", second.Title);
        Assert.False(second.IsPinned);
        Assert.False(rig.Peek.TakesClicks); // click-through again while held
        Assert.True(rig.Cursor.IsRunning);
        Assert.True(first.IsPinned); // the old card object is gone from the window

        rig.Keys.Release(CtrlC);
        Sta.Drain();
        Assert.False(rig.Peek.IsVisible); // this one was not moved onto
        Assert.Equal(20, rig.Hud.Counters.AutoRepeatIgnored);
        Assert.Equal(1, rig.Hud.Counters.InjectedIgnored);
    });

    [Fact]
    public void Synthetic_input_never_reads_replaces_or_releases_a_pinned_card() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Cursor.MoveTo(FakeCursor.OnCard);
        rig.Keys.Release(CtrlC, injected: true); // not a real release
        Sta.Drain();
        Assert.True(rig.Peek.IsVisible);
        rig.Keys.Release(CtrlC);

        rig.Keys.Press(CtrlC, injected: true);
        rig.Keys.Release(CtrlC, injected: true);
        Sta.Drain();

        Assert.Equal(1, rig.Clipboard.Reads);
        var card = Assert.Single(rig.Peek.Shown);
        Assert.True(card.IsPinned);
        Assert.True(rig.Peek.IsVisible);
        Assert.Equal(1, rig.Hud.Counters.InjectedIgnored);
    });

    [Fact]
    public void Pressing_the_key_while_the_pinned_card_has_the_focus_reads_nothing() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        PinnedCard(rig);
        rig.ClickCard();
        Sta.Drain();

        rig.Keys.Press(CtrlC); // the game does not have the keys, so it copied nothing
        rig.Keys.Release(CtrlC);
        Sta.Drain();

        Assert.Equal(1, rig.Clipboard.Reads);
        Assert.Single(rig.Peek.Shown);
        Assert.True(rig.Peek.IsVisible);
    });

    [Theory]
    [InlineData("another program")]
    [InlineData("SSE's own window")]
    [InlineData("minimize")]
    [InlineData("lock")]
    [InlineData("display change")]
    public void Another_program_minimize_lock_or_a_display_change_hide_a_pinned_card(string what) => Sta.Run(() =>
    {
        using var rig = new HudRig();
        PinnedCard(rig);
        rig.ClickCard();
        Sta.Drain();

        switch (what)
        {
            case "another program":
                rig.Foreground.Switch(ForegroundKind.Other);
                break;
            case "SSE's own window":
                rig.Foreground.Switch(ForegroundKind.ThisApp, window: FakeShellWindow.MainWindow);
                break;
            case "minimize":
                rig.Foreground.Switch(ForegroundKind.Other, "minimize");
                break;
            case "lock":
                rig.Hud.SessionChanged(SessionSwitchReason.SessionLock);
                break;
            case "display change":
                rig.Hud.DisplayChanged();
                break;
        }

        Sta.Drain();

        Assert.False(rig.Peek.IsVisible);
        Assert.Equal(0, rig.Peek.FocusReturnedTo); // nothing is pulled back to the game
    });

    [Fact]
    public void The_cursor_is_watched_only_while_the_key_is_held_and_the_card_shows() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        Assert.False(rig.Cursor.IsRunning);

        rig.Foreground.Current = ForegroundKind.Other; // no read, no card
        rig.Keys.Press(CtrlC);
        Sta.Drain();
        Assert.False(rig.Cursor.IsRunning);
        rig.Keys.Release(CtrlC);

        rig.Foreground.Current = ForegroundKind.Game;
        rig.Clipboard.HoldResults = true;
        rig.Keys.Press(CtrlC);
        Sta.Drain();
        Assert.False(rig.Cursor.IsRunning); // the game's copy has not landed yet

        rig.Clipboard.CompleteRead();
        Sta.Drain();
        Assert.True(rig.Cursor.IsRunning);
        Assert.Equal(1, rig.Cursor.Starts);

        rig.Keys.Release(CtrlC);
        Sta.Drain();
        Assert.False(rig.Cursor.IsRunning);
        Assert.False(rig.Peek.IsVisible);

        rig.Clipboard.HoldResults = false;
        rig.Keys.Press(CtrlC);
        Sta.Drain();
        Assert.True(rig.Cursor.IsRunning);
        rig.Foreground.Switch(ForegroundKind.Other);
        Sta.Drain();
        Assert.False(rig.Cursor.IsRunning);
    });

    [Fact]
    public void Toggle_mode_is_unchanged_and_never_watches_the_cursor() => Sta.Run(() =>
    {
        using var rig = new HudRig(s => s.PeekMode = HoldMode.Toggle);
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        Assert.False(rig.Cursor.IsRunning);
        rig.Cursor.MoveTo(FakeCursor.OnCard);
        rig.Keys.Release(CtrlC);
        Sta.Drain();

        var card = Assert.Single(rig.Peek.Shown);
        Assert.True(rig.Peek.IsVisible); // a snapshot until the next press
        Assert.False(card.IsPinned);
        Assert.False(rig.Peek.TakesClicks);
        Assert.Equal("Press again to hide", card.ReleaseHint);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        Assert.False(rig.Peek.IsVisible);
        rig.Keys.Release(CtrlC);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        Assert.True(rig.Peek.IsVisible);
        Assert.Equal(2, rig.Clipboard.Reads);
        Assert.Equal(0, rig.Cursor.Starts);
    });

    [Fact]
    public void All_filters_on_a_pinned_card_opens_the_price_check_for_its_item() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        var card = PinnedCard(rig);

        rig.Peek.ClickMore();
        Sta.Drain();

        Assert.True(rig.PriceCheck.IsVisible);
        Assert.Same(card.Item, rig.PriceCheck.Item);
        Assert.Equal(FakeForeground.GameWindow, rig.PriceCheck.ShownOver);
        Assert.False(rig.Peek.IsVisible);
        Assert.Equal(1, rig.Clipboard.Reads);
    });

    [Fact]
    public void F3_while_the_pinned_card_has_the_focus_opens_the_price_check_over_the_game() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        var card = PinnedCard(rig);
        rig.ClickCard();
        Sta.Drain();

        rig.Keys.Press(F3);
        rig.Keys.Release(F3);
        Sta.Drain();

        Assert.True(rig.PriceCheck.IsVisible);
        Assert.Same(card.Item, rig.PriceCheck.Item);
        Assert.Equal(FakeForeground.GameWindow, rig.PriceCheck.ShownOver);
        Assert.False(rig.Peek.IsVisible);
        Assert.Equal(0, rig.Window.BroughtToFront);
    });

    [Fact]
    public void Ticking_a_stat_on_the_pinned_card_marks_its_price_old_until_Search_and_reads_nothing() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        var card = PinnedCard(rig);
        var item = card.Item!;
        var before = card.Answer;
        var listed = item.Matches;
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var dps = card.LiveRows.OfType<SearchRowViewModel>().First(r => r.IsOn);
        dps.IsOn = false;

        Assert.Same(before, card.Answer); // ticking never searches
        Assert.Same(listed, item.Matches);
        Assert.True(item.IsStale);
        Assert.True(item.IsDimmed);
        Assert.Equal(ItemViewModel.StaleText, item.PeekLine);
        Assert.Contains(nameof(PeekViewModel.OnCount), changed);
        Assert.Equal(1, card.OnCount);

        dps.IsOn = true; // back to the search the result was found with
        Assert.False(item.IsStale);
        dps.IsOn = false;

        Assert.True(card.ShowsSearch);
        item.SearchCommand.Execute(null);

        Assert.NotEqual(before, card.Answer);
        Assert.NotSame(listed, item.Matches);
        Assert.Contains(nameof(PeekViewModel.Answer), changed);
        Assert.False(item.IsStale);
        Assert.Equal(card.Answer!.PeekNote, item.PeekLine);
        Assert.Same(item.Answer, card.Answer); // the price check page shows the same search
        Assert.Equal(1, rig.Clipboard.Reads);
        Assert.True(rig.Peek.IsVisible);
    });
}
