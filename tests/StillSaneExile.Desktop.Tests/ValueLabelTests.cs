using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using StillSaneExile.Desktop.Views;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Tests;

/// <summary>The price label: currency words after a number become the game's pictures, screen readers hear
/// the currency by name, and the label takes the loot-filter colours of its tier.</summary>
public class ValueLabelTests
{
    [Fact]
    public void A_currency_word_after_a_number_becomes_its_picture()
    {
        Assert.Equal(new (string, string?)[] { ("95", null), ("", "exalted"), (" – 1.2", null), ("", "divine") }, ValueLabel.Split("95 ex – 1.2 div"));
        Assert.Equal(new (string, string?)[] { ("12.5", null), ("", "chaos") }, ValueLabel.Split("12.5 c"));
        Assert.Equal(new (string, string?)[] { ("≈ 180", null), ("", "divine"), (" baseline · check rolls", null) }, ValueLabel.Split("≈ 180 div baseline · check rolls"));
    }

    [Theory]
    [InlineData("No matches")]
    [InlineData("Search to update")]
    [InlineData("Excellent choice")] // "c" and "ex" inside words stay words
    [InlineData("Item level 82")]
    public void Other_text_stays_as_it_is(string text) =>
        Assert.Equal(new (string, string?)[] { (text, null) }, ValueLabel.Split(text));

    [Fact]
    public void Screen_readers_hear_the_currency_by_name()
    {
        Assert.Equal("2.1–2.8 Divine", ValueLabel.Spoken("2.1–2.8 div"));
        Assert.Equal("Cheapest 1.4 Divine · 1 Divine = 120 Exalted", ValueLabel.Spoken("Cheapest 1.4 div · 1 div = 120 ex"));
    }

    [Fact]
    public void Each_display_currency_has_its_picture_in_the_app_and_nothing_else_is_guessed() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        foreach (var currency in Currencies.Display)
        {
            var picture = Assert.IsAssignableFrom<ImageSource>(CurrencyIcon.Picture(currency));
            Assert.True(picture.Width > 0, currency);
        }

        Assert.Null(CurrencyIcon.Picture("regal"));
        Assert.Equal(Visibility.Collapsed, new CurrencyIcon { Currency = "regal" }.Visibility);
    });

    [Fact]
    public void A_label_takes_its_tier_colours_and_without_a_tier_draws_plain_text() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var label = new ValueLabel { Text = "2.1–2.8 div", Tier = ValueTier.A, Size = ValueLabelSize.Large };
        Assert.Same(Application.Current.Resources["TierAFill"], label.Background);
        Assert.Same(Application.Current.Resources["TierAEdge"], label.BorderBrush);
        Assert.Equal("2.1–2.8 Divine", AutomationProperties.GetName(label));

        label.Tier = null;
        Assert.Null(label.Background);
        Assert.Equal(new Thickness(0), label.BorderThickness);
    });
}
