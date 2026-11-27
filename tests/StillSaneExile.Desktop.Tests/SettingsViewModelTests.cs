using System.Windows.Input;
using StillSaneExile.Desktop.Infrastructure;

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
    public void The_game_s_copy_key_still_reads_the_item_it_just_copied() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var page = app.Shell.SettingsPage;

        Assert.Contains("Ctrl+C: reads the clipboard once, right after the game copies the hovered item.", page.ReadsExplanation);
        Assert.StartsWith("Hover an item", page.PeekHowTo);
    });
}
