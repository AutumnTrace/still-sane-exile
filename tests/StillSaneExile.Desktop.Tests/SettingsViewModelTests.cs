using System.Windows.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Capture;
using StillSaneExile.Domain.Placement;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Tests;

public class SettingsViewModelTests
{
    private static readonly Hotkey Mouse4 = Hotkey.Parse("Mouse4")!;

    [Fact]
    public void The_default_bindings_raise_no_warnings() => Sta.Run(() =>
    {
        using var app = new TestApp();

        Assert.Empty(app.Shell.SettingsPage.KeyWarnings);
    });

    [Fact]
    public void Two_bindings_on_one_side_button_are_flagged() => Sta.Run(() =>
    {
        using var app = new TestApp(s =>
        {
            s.PeekKey = Mouse4;
            s.OpenLastKey = Mouse4;
        });

        var warning = Assert.Single(app.Shell.SettingsPage.KeyWarnings);
        Assert.Equal("Quick price and Change the search share Mouse 4 (back). Give each its own key or button.", warning);
    });

    [Fact]
    public void A_binding_on_one_of_the_game_s_default_keys_is_flagged() => Sta.Run(() =>
    {
        using var app = new TestApp(s =>
        {
            s.PeekKey = Hotkey.Parse("Space")!;
            s.OpenLastKey = Hotkey.Parse("Ctrl+Q")!;
        });

        var warnings = app.Shell.SettingsPage.KeyWarnings;
        Assert.Contains("Quick price (Space) may clash with the game's default dodge roll key.", warnings);
        Assert.Contains("Change the search (Ctrl+Q) may clash with the game's default skill key.", warnings);
        Assert.Equal(2, warnings.Count);
    });

    [Fact]
    public void Recording_a_side_button_binds_it() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var page = app.Shell.SettingsPage;
        page.StartRecordingCommand.Execute("peek");

        Assert.True(page.RecordMouse(MouseButton.XButton2, ModifierKeys.None));

        Assert.Equal(Hotkey.Parse("Mouse5"), app.Settings.PeekKey);
        Assert.Equal("Mouse 5 (forward)", page.PeekKey);
        Assert.Null(page.Recording);
    });

    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    [InlineData(MouseButton.Middle)]
    public void Left_right_and_middle_click_cannot_be_bound(MouseButton button) => Sta.Run(() =>
    {
        using var app = new TestApp();
        var page = app.Shell.SettingsPage;
        page.StartRecordingCommand.Execute("peek");

        page.RecordMouse(button, ModifierKeys.None);

        Assert.Equal(Hotkey.Parse("Ctrl+C"), app.Settings.PeekKey);
        Assert.Equal("peek", page.Recording);
    });

    [Fact]
    public void A_side_button_is_never_recorded_unless_Change_was_pressed() => Sta.Run(() =>
    {
        using var app = new TestApp();

        Assert.False(app.Shell.SettingsPage.RecordMouse(MouseButton.XButton1, ModifierKeys.None));
        Assert.Equal(Hotkey.Parse("Ctrl+C"), app.Settings.PeekKey);
    });

    [Fact]
    public void A_side_button_is_not_said_to_read_the_hovered_item() => Sta.Run(() =>
    {
        using var app = new TestApp(s => s.PeekKey = Mouse4);
        var page = app.Shell.SettingsPage;

        Assert.Contains("Mouse 4 (back): reads the clipboard once per press", page.ReadsExplanation);
        Assert.Contains("the last item you copied with Ctrl+C", page.ReadsExplanation);
        Assert.DoesNotContain("hovered", page.ReadsExplanation);
        Assert.DoesNotContain("Hover", page.PeekHowTo);
    });

    [Fact]
    public void The_quick_price_card_offers_two_ways_to_hide() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var page = app.Shell.SettingsPage;
        Assert.False(page.ToggleMode); // a new player holds, and pins by moving onto the card
        Assert.Contains("Move onto the card to keep it open", page.PeekHowTo);
        Assert.Contains("where the mouse cursor is", page.ReadsExplanation);
        Assert.Equal("Let go to hide · move onto it to keep it", page.SamplePeek.ReleaseHint);

        page.SetPeekModeCommand.Execute("toggle");

        Assert.Equal(HoldMode.Toggle, app.Settings.PeekMode);
        Assert.Contains("Press again to hide", page.PeekHowTo);
        Assert.Contains("When you press it, the app checks where the mouse cursor is", page.ReadsExplanation); // for the ring and the card's place
        Assert.DoesNotContain("moving onto the card", page.ReadsExplanation); // never watched while held
        Assert.Equal("Press again to hide", page.SamplePeek.ReleaseHint);
        Assert.Contains(page.GuideSteps, s => s.Title == "Press again to hide");

        page.SetPeekModeCommand.Execute("hold");

        Assert.Equal(HoldMode.Hold, app.Settings.PeekMode);
        Assert.Contains(page.GuideSteps, s => s.Title == "Move onto it to keep it");
    });

    [Fact]
    public void The_card_placement_saves_and_says_what_it_does() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var page = app.Shell.SettingsPage;
        Assert.Equal(CardPosition.Tooltip, page.Position); // the default
        Assert.Contains("Beside the game's tooltip", page.PositionNote);
        Assert.Contains("isn't checked in the game yet", page.PositionNote); // a guess, and it says so

        page.SetPositionCommand.Execute("edge");
        Assert.Equal(CardPosition.Edge, app.Settings.CardPosition);
        Assert.Contains("away from the cursor", page.PositionNote);

        page.SetPositionCommand.Execute("fixed");
        Assert.Equal(CardPosition.Fixed, app.Settings.CardPosition);
        Assert.Contains("drag it by its text", page.PositionNote);
        Assert.DoesNotContain("Press again to hide", page.PositionNote);

        page.SetPeekModeCommand.Execute("toggle");
        Assert.Contains("Dragging needs a card you kept open", page.PositionNote);

        page.SetPositionCommand.Execute("tooltip");
        Assert.Equal(CardPosition.Tooltip, app.Settings.CardPosition);
    });

    [Fact]
    public void Less_motion_and_the_click_are_off_until_switched_on() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var page = app.Shell.SettingsPage;
        Assert.False(page.LessMotion);
        Assert.False(page.PressClick);

        page.LessMotion = true;
        page.PressClick = true;

        Assert.True(app.Settings.LessMotion);
        Assert.True(app.Settings.PressClick);
        page.Load();
        Assert.True(page.LessMotion);
        Assert.True(page.PressClick);
    });

    [Fact]
    public void poe_ninja_s_leagues_are_offered_and_a_pick_still_needs_confirming() => Sta.Run(() =>
    {
        Sta.UseDispatcherContext();
        using var app = new TestApp();
        using var prices = LivePricesRig.For(app, new FakeNinja());
        app.Shell.Prices = prices;
        var page = app.Shell.SettingsPage;

        page.LoadLeaguesCommand.Execute(null);
        Sta.Until(() => page.LeagueChoices.Count > 0, "the league list");

        Assert.Equal(["Runes of Aldur", "HC Runes of Aldur", "Standard", "Hardcore"], page.LeagueChoices);
        Assert.False(page.CanLoadLeagues);
        page.PickLeagueCommand.Execute("HC Runes of Aldur");
        Assert.Equal("HC Runes of Aldur", page.LeagueDraft);
        Assert.Null(app.Settings.LiveLeague); // not until "Confirm league"
        Assert.True(app.Shell.Session.IsDemo);

        page.ConfirmLeagueCommand.Execute(null);

        Assert.Equal("HC Runes of Aldur", app.Settings.LiveLeague);
        Assert.Equal("HC Runes of Aldur", app.Shell.Session.League);
        Assert.Equal("HC Runes of Aldur", prices.League);
        Assert.StartsWith("Live profile on HC Runes of Aldur. Its prices come from poe.ninja and refresh every hour", page.Message, StringComparison.Ordinal);
    });

    [Fact]
    public void A_league_typed_in_another_case_takes_poe_ninja_s_spelling_and_an_unknown_one_is_flagged() => Sta.Run(() =>
    {
        Sta.UseDispatcherContext();
        using var app = new TestApp();
        using var prices = LivePricesRig.For(app, new FakeNinja());
        app.Shell.Prices = prices;
        var page = app.Shell.SettingsPage;
        page.LoadLeaguesCommand.Execute(null);
        Sta.Until(() => page.LeagueChoices.Count > 0, "the league list");

        page.LeagueDraft = "standard";
        page.ConfirmLeagueCommand.Execute(null);
        Assert.Equal("Standard", app.Settings.LiveLeague);

        page.LeagueDraft = "Runes of Aldor";
        page.ConfirmLeagueCommand.Execute(null);
        Assert.Equal("Runes of Aldor", app.Settings.LiveLeague);
        Assert.Contains("poe.ninja lists no league by that name", page.Message, StringComparison.Ordinal);
    });

    [Fact]
    public void Offline_the_league_can_still_be_typed() => Sta.Run(() =>
    {
        Sta.UseDispatcherContext();
        using var app = new TestApp(s => s.Onboarded = false);
        using var prices = LivePricesRig.For(app, new FakeNinja { Offline = true });
        app.Shell.Prices = prices;
        var page = app.Shell.SettingsPage;

        page.ChooseLiveCommand.Execute(null); // onboarding's "A live league" asks for the list
        Sta.Until(() => !page.LoadingLeagues && page.LeagueListStatus?.StartsWith("Couldn't", StringComparison.Ordinal) == true, "the failed list");

        Assert.Empty(page.LeagueChoices);
        Assert.Equal("Couldn't get the league list from poe.ninja. Type the league name exactly as the game shows it.", page.LeagueListStatus);
        page.LeagueDraft = "Runes of Aldur";
        page.ConfirmLeagueCommand.Execute(null);
        Assert.Equal("Runes of Aldur", app.Settings.LiveLeague);
    });

    [Fact]
    public void Opening_Settings_asks_for_leagues_only_when_live_prices_are_already_in_use() => Sta.Run(() =>
    {
        Sta.UseDispatcherContext();
        var ninja = new FakeNinja();
        using (var demo = new TestApp())
        {
            using var prices = LivePricesRig.For(demo, ninja);
            demo.Shell.Prices = prices;
            demo.Shell.GoSettingsCommand.Execute(null);
            Sta.Drain();
            Assert.Empty(ninja.Requests);
        }

        using var live = new TestApp(s =>
        {
            s.LiveLeague = "Standard";
            s.Profile = ProfileKind.Live;
        });
        using var livePrices = LivePricesRig.For(live, ninja);
        live.Shell.Prices = livePrices;
        live.Shell.GoSettingsCommand.Execute(null);
        Sta.Until(() => live.Shell.SettingsPage.LeagueChoices.Count > 0, "the league list");

        Assert.Single(ninja.Requests);
        Assert.EndsWith("/poe2/api/data/index-state", ninja.Requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);
    });

    [Fact]
    public void The_game_s_copy_key_still_reads_the_item_it_just_copied() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var page = app.Shell.SettingsPage;

        Assert.Contains("Ctrl+C: reads the clipboard once, right after the game copies the hovered item.", page.ReadsExplanation);
        Assert.StartsWith("Hover an item", page.PeekHowTo);
    });

    [Fact]
    public void The_close_and_minimize_switches_are_saved_as_they_change() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var settings = app.Shell.SettingsPage;
        Assert.True(settings.CloseToTray);
        Assert.True(settings.MinimizeToTray);

        settings.CloseToTray = false;
        settings.MinimizeToTray = false;

        var reopened = new Services.Settings(app.Store);
        Assert.False(reopened.CloseToTray);
        Assert.False(reopened.MinimizeToTray);

        settings.CloseToTray = true;

        Assert.True(new Services.Settings(app.Store).CloseToTray);
    });
}
