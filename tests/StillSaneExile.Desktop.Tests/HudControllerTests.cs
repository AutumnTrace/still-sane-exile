using System.Windows.Controls;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Views;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Tests;

public class HudControllerTests
{
    private const string Crossbow = "rare-crossbow-advanced.txt";
    private static readonly Hotkey CtrlC = Hotkey.Parse("Ctrl+C")!;
    private static readonly Hotkey F3 = Hotkey.Parse("F3")!;

    [Fact]
    public void A_physical_press_over_the_game_reads_once_and_shows_the_card() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.Equal(1, rig.Clipboard.Reads);
        var card = Assert.Single(rig.Peek.Shown);
        Assert.False(card.IsMessage);
        Assert.Equal("Doom Thirst", card.Title);
        Assert.True(rig.Peek.IsVisible);
        Assert.Equal(FakeForeground.GameWindow, rig.Peek.ShownOver);
        Assert.Equal(1, rig.Hud.Counters.Reads);
        Assert.Equal(1, rig.Shell.ItemsRead);
        Assert.Equal(0, rig.Shell.KeysSent);
    });

    [Fact]
    public void Auto_repeat_and_synthetic_input_never_read_again() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        for (var i = 0; i < 30; i++)
        {
            rig.Keys.Press(CtrlC); // Windows auto-repeat while the key is held
        }

        rig.Keys.Press(CtrlC, injected: true);
        rig.Keys.Release(CtrlC, injected: true);
        Sta.Drain();

        Assert.Equal(1, rig.Clipboard.Reads);
        Assert.Single(rig.Peek.Shown);
        Assert.True(rig.Peek.IsVisible); // a synthetic release does not count as letting go
        Assert.Equal(30, rig.Hud.Counters.AutoRepeatIgnored);
        Assert.Equal(1, rig.Hud.Counters.InjectedIgnored);

        rig.Keys.Release(CtrlC);
        rig.Keys.Press(CtrlC, injected: true); // a fresh press, but sent by software
        Sta.Drain();

        Assert.Equal(1, rig.Clipboard.Reads);
        Assert.Single(rig.Peek.Shown);
        Assert.Equal(2, rig.Hud.Counters.InjectedIgnored);
    });

    [Fact]
    public void Letting_go_hides_the_card() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        Assert.True(rig.Peek.IsVisible);

        rig.Keys.Release(CtrlC);
        Sta.Drain();
        Assert.False(rig.Peek.IsVisible);
    });

    [Fact]
    public void A_result_that_arrives_after_release_is_dropped() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        rig.Clipboard.HoldResults = true;

        rig.Keys.Press(CtrlC);
        Sta.Drain(); // waiting for the game's copy
        rig.Keys.Release(CtrlC);
        Sta.Drain();
        rig.Clipboard.CompleteRead();
        Sta.Drain();

        Assert.Equal(1, rig.Clipboard.Reads);
        Assert.Empty(rig.Peek.Shown);
        Assert.False(rig.Peek.IsVisible);
        Assert.Equal(1, rig.Hud.Counters.StaleResultsDropped);
    });

    [Fact]
    public void A_newer_press_supersedes_a_result_still_on_its_way() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        rig.Clipboard.HoldResults = true;

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Keys.Release(CtrlC);
        rig.Clipboard.HoldResults = false;
        rig.Keys.Press(CtrlC);
        Sta.Drain();
        Assert.Single(rig.Peek.Shown);

        rig.Clipboard.CompleteRead(); // the first press's copy lands last
        Sta.Drain();

        Assert.Single(rig.Peek.Shown);
        Assert.Equal(2, rig.Clipboard.Reads);
        Assert.Equal(1, rig.Hud.Counters.StaleResultsDropped);
    });

    [Fact]
    public void Focus_change_hides_the_card_and_the_held_key_does_not_read_again() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        Assert.True(rig.Peek.IsVisible);

        rig.Foreground.Switch(ForegroundKind.Other);
        Sta.Drain();
        Assert.False(rig.Peek.IsVisible);

        rig.Foreground.Switch(ForegroundKind.Game);
        rig.Keys.Press(CtrlC); // still held: auto-repeat
        Sta.Drain();

        Assert.Equal(1, rig.Clipboard.Reads);
        Assert.Single(rig.Peek.Shown);
        Assert.False(rig.Peek.IsVisible);
    });

    [Fact]
    public void Focus_change_while_reading_drops_the_late_result() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        rig.Clipboard.HoldResults = true;

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Foreground.Switch(ForegroundKind.Other, "minimize");
        rig.Clipboard.CompleteRead();
        Sta.Drain();

        Assert.Empty(rig.Peek.Shown);
        Assert.Equal(1, rig.Hud.Counters.StaleResultsDropped);
    });

    [Fact]
    public void Changing_settings_hides_the_card() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);

        rig.Keys.Press(CtrlC);
        Sta.Drain();
        rig.Hud.ApplySettings();

        Assert.False(rig.Peek.IsVisible);
    });

    [Fact]
    public void With_another_app_in_front_nothing_is_read() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = "Something typed in another program";
        rig.Foreground.Current = ForegroundKind.Other;

        rig.Keys.Press(CtrlC);
        rig.Keys.Release(CtrlC);
        Sta.Drain();

        Assert.Equal(0, rig.Clipboard.Reads);
        Assert.Empty(rig.Peek.Shown);
        Assert.Equal(0, rig.Hud.Counters.Reads);
        Assert.Equal(1, rig.Hud.Counters.NotForegroundIgnored);
    });

    [Fact]
    public void Practice_peek_shows_the_selected_demo_item_without_reading_the_clipboard() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Shell.Inspect.LoadPracticeCommand.Execute(rig.Shell.Inspect.PracticeItems[0]);
        rig.Foreground.Current = ForegroundKind.ThisApp;

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.Equal(0, rig.Clipboard.Reads);
        var card = Assert.Single(rig.Peek.Shown);
        Assert.Equal("Doom Thirst", card.Title);
        Assert.True(card.IsDemo);
        Assert.Equal(FakeShellWindow.MainWindow, rig.Peek.ShownOver);
    });

    [Theory]
    [InlineData("settings page")]
    [InlineData("live profile")]
    [InlineData("another app in front")]
    public void Practice_peek_needs_the_demo_the_price_check_page_and_SSE_in_front(string missing) => Sta.Run(() =>
    {
        using var rig = new HudRig(s =>
        {
            if (missing == "live profile")
            {
                s.LiveLeague = "Standard";
                s.Profile = ProfileKind.Live;
            }
        });
        rig.Shell.Inspect.AddFromText(rig.App.ItemText(Crossbow), "test");
        rig.Foreground.Current = missing == "another app in front" ? ForegroundKind.Other : ForegroundKind.ThisApp;
        if (missing == "settings page")
        {
            rig.Shell.Navigate(rig.Shell.SettingsPage);
        }

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.Empty(rig.Peek.Shown);
        Assert.Equal(0, rig.Clipboard.Reads);
        Assert.Equal(0, rig.Hud.Counters.Reads);
    });

    [Fact]
    public void Ctrl_C_while_typing_in_SSE_is_only_a_copy() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Shell.Inspect.LoadPracticeCommand.Execute(rig.Shell.Inspect.PracticeItems[0]);
        rig.Foreground.Current = ForegroundKind.ThisApp;
        rig.Window.IsTyping = true; // e.g. selecting the "Asking price" text to copy it

        rig.Keys.Press(CtrlC);
        rig.Keys.Release(CtrlC);
        Sta.Drain();

        Assert.Empty(rig.Peek.Shown);
        Assert.Equal(0, rig.Clipboard.Reads);
        Assert.Equal(0, rig.Hud.Counters.Reads);
        Assert.Equal(0, rig.Shell.ItemsRead);

        rig.Window.IsTyping = false; // focus moved to the item list
        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.Single(rig.Peek.Shown);
        Assert.Equal(1, rig.Hud.Counters.Reads);
    });

    [Fact]
    public void A_text_box_focused_in_SSE_does_not_stop_the_peek_over_the_game() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        rig.Window.IsTyping = true; // SSE keeps its focused box while the game is in front

        rig.Keys.Press(CtrlC);
        Sta.Drain();

        Assert.Equal(1, rig.Clipboard.Reads);
        Assert.Single(rig.Peek.Shown);
    });

    [Fact]
    public void Text_inputs_are_recognised_as_typing() => Sta.Run(() =>
    {
        Assert.True(MainWindow.IsTextInput(new TextBox()));
        Assert.True(MainWindow.IsTextInput(new RichTextBox()));
        Assert.True(MainWindow.IsTextInput(new PasswordBox()));
        Assert.True(MainWindow.IsTextInput(new ComboBox { IsEditable = true }));
        Assert.False(MainWindow.IsTextInput(new ComboBox()));
        Assert.False(MainWindow.IsTextInput(new Button()));
        Assert.False(MainWindow.IsTextInput(new ListBox()));
        Assert.False(MainWindow.IsTextInput(null));
    });

    [Fact]
    public void F3_over_the_game_opens_the_price_check_for_the_last_item_without_reading() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Clipboard.Text = rig.App.ItemText(Crossbow);
        rig.Keys.Press(CtrlC);
        rig.Keys.Release(CtrlC);
        Sta.Drain();

        rig.Keys.Press(F3);
        rig.Keys.Release(F3);
        Sta.Drain();

        Assert.True(rig.PriceCheck.IsVisible);
        Assert.Equal("Doom Thirst", rig.PriceCheck.Item?.Title);
        Assert.Equal(FakeForeground.GameWindow, rig.PriceCheck.ShownOver);
        Assert.Equal(1, rig.Clipboard.Reads);

        rig.Keys.Press(F3);
        Sta.Drain();
        Assert.False(rig.PriceCheck.IsVisible);
    });

    [Fact]
    public void F3_in_SSE_brings_its_window_to_the_price_check_page() => Sta.Run(() =>
    {
        using var rig = new HudRig();
        rig.Shell.Navigate(rig.Shell.SettingsPage);
        rig.Foreground.Current = ForegroundKind.ThisApp;

        rig.Keys.Press(F3);
        Sta.Drain();

        Assert.Same(rig.Shell.Inspect, rig.Shell.CurrentPage);
        Assert.Equal(1, rig.Window.BroughtToFront);
        Assert.False(rig.PriceCheck.IsVisible);
    });
}
