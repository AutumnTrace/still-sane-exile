using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Tests;

/// <summary>Settings, Price colours: a draft the preview follows, applied only on Apply.</summary>
public class PriceColoursTests
{
    private const string Crossbow = "rare-crossbow-advanced.txt";

    [Fact]
    public void The_preview_follows_the_draft_and_the_cards_wait_for_Apply() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "test")!;
        Assert.Equal("1.8–2.5 div", item.Answer.Big);
        Assert.Equal(ValueTier.A, item.Answer.Tier); // red: 1 to 10 div
        var colours = app.Shell.SettingsPage.Colours;
        Assert.False(colours.ApplyCommand.CanExecute(null)); // nothing changed yet
        Assert.Equal(ValueTier.A, colours.Examples.Single(e => e.Name == "Doom Thirst").Tier); // 4 div

        // Day one of a league: white and red from 1 div, red from 40 ex.
        colours.Steps[0].Amount = "1";
        colours.Steps[1].Amount = "40";
        colours.Steps[1].SetCurrencyCommand.Execute("exalted");

        Assert.Null(colours.Problem);
        Assert.True(colours.ApplyCommand.CanExecute(null));
        Assert.Equal(ValueTier.S, colours.Examples.Single(e => e.Name == "Doom Thirst").Tier); // the preview follows
        Assert.Equal(ValueTier.A, colours.Examples.Single(e => e.Name == "Storm Mark").Tier); // 70 ex
        Assert.Equal(ValueTier.A, app.Shell.Inspect.Selected!.Answer.Tier); // the cards don't, yet
        Assert.Equal(ValueLadder.Default, app.Settings.PriceColours);

        colours.ApplyCommand.Execute(null);

        Assert.Equal("1 div;40 ex;30 ex;6 ex;1 ex", app.Settings.PriceColours.Format());
        Assert.Equal(ValueTier.S, app.Shell.Inspect.Selected!.Answer.Tier); // the open price check is coloured again
        Assert.Equal(app.Settings.PriceColours, app.Shell.Session.Colours);
        Assert.False(colours.ApplyCommand.CanExecute(null));
        Assert.Contains("White and red from 1 div, red from 40 ex", app.Shell.SettingsPage.GuideSteps[1].Text, StringComparison.Ordinal);
    });

    [Fact]
    public void A_step_that_does_not_start_below_the_one_above_cannot_be_applied() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var colours = app.Shell.SettingsPage.Colours;

        colours.Steps[1].Amount = "12"; // red from 12 div, above white and red's 10 div
        Assert.Equal("Red has to start below white and red.", colours.Problem);
        Assert.True(colours.Steps[1].IsWrong);
        Assert.False(colours.ApplyCommand.CanExecute(null));
        Assert.Equal(ValueTier.A, colours.Examples.Single(e => e.Name == "Doom Thirst").Tier); // the preview keeps the saved steps

        colours.Steps[1].Amount = "";
        Assert.Equal("Type a number above 0 for red.", colours.Problem);

        colours.UndoCommand.Execute(null);
        Assert.Null(colours.Problem);
        Assert.Equal("1", colours.Steps[1].Amount);
        Assert.False(colours.Steps[1].IsWrong);
    });

    [Fact]
    public void Default_steps_fill_the_rows_and_Apply_still_decides() => Sta.Run(() =>
    {
        using var app = new TestApp();
        app.Settings.PriceColours = ValueLadder.Default with { S = new ValueStep(2m, "divine") };
        app.Shell.ReloadSession();
        var colours = app.Shell.SettingsPage.Colours;
        Assert.Equal("2", colours.Steps[0].Amount);

        colours.UseDefaultCommand.Execute(null);

        Assert.Equal("10", colours.Steps[0].Amount);
        Assert.Equal(2m, app.Settings.PriceColours.S.Amount);
        colours.ApplyCommand.Execute(null);
        Assert.Equal(ValueLadder.Default, app.Settings.PriceColours);
    });
}
