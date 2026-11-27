using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Capture;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Tests;

public class SettingsTests
{
    [Fact]
    public void A_new_profile_starts_with_the_product_defaults()
    {
        using var dir = new TempDir();
        var s = new Settings(new Store(dir.Sub("sse.db")));

        Assert.False(s.Onboarded);
        Assert.Equal(ProfileKind.Demo, s.Profile);
        Assert.Null(s.LiveLeague);
        Assert.Equal("divine", s.DisplayCurrency(ProfileKind.Demo));
        Assert.Equal("divine", s.DisplayCurrency(ProfileKind.Live));
        Assert.Null(s.ListingDenomination(ProfileKind.Demo));
        Assert.Null(s.ListingDenomination(ProfileKind.Live));
        Assert.Equal("Ctrl+C", s.PeekKey.ToString());
        Assert.Equal("F3", s.OpenLastKey.ToString());
        Assert.Equal("RightAlt", s.StashKey.ToString());
        Assert.Equal(HoldMode.Hold, s.PeekMode);
        Assert.Equal(PeekAnchor.Auto, s.Anchor);
        Assert.Equal(0.85, s.PeekScale);
        Assert.True(s.AutoUpdate);
        Assert.True(s.LivePrices);
        Assert.True(s.TradeSearch);
        Assert.Equal(3, s.ComparableExpiryDays);
    }

    [Fact]
    public void Settings_survive_a_restart()
    {
        using var dir = new TempDir();
        var before = new Settings(new Store(dir.Sub("sse.db")))
        {
            Onboarded = true,
            LiveLeague = "Standard",
            Profile = ProfileKind.Live,
            PeekKey = Hotkey.Parse("Ctrl+Shift+D")!,
            PeekMode = HoldMode.Toggle,
            Anchor = PeekAnchor.Left,
            PeekScale = 1.25,
            AutoUpdate = false,
            TradeSearch = false,
            ComparableExpiryDays = 7,
        };
        before.SetDisplayCurrency(ProfileKind.Live, "exalted");

        var after = new Settings(new Store(dir.Sub("sse.db")));

        Assert.True(after.Onboarded);
        Assert.Equal("Standard", after.LiveLeague);
        Assert.Equal(ProfileKind.Live, after.Profile);
        Assert.Equal(Hotkey.Parse("Ctrl+Shift+D"), after.PeekKey);
        Assert.Equal(HoldMode.Toggle, after.PeekMode);
        Assert.Equal(PeekAnchor.Left, after.Anchor);
        Assert.Equal(1.25, after.PeekScale);
        Assert.False(after.AutoUpdate);
        Assert.False(after.TradeSearch);
        Assert.Equal(7, after.ComparableExpiryDays);
        Assert.Equal("exalted", after.DisplayCurrency(ProfileKind.Live));
        Assert.Equal("divine", after.DisplayCurrency(ProfileKind.Demo));
    }

    [Theory]
    [InlineData(null, HoldMode.Hold)]
    [InlineData("hold", HoldMode.Hold)]
    [InlineData("toggle", HoldMode.Toggle)]
    [InlineData("sideways", HoldMode.Hold)]
    public void A_stored_quick_price_mode_still_loads(string? stored, HoldMode expected)
    {
        using var dir = new TempDir();
        var store = new Store(dir.Sub("sse.db"));
        if (stored is not null)
        {
            store.Set("peek.mode", stored);
        }

        Assert.Equal(expected, new Settings(store).PeekMode);
    }

    [Fact]
    public void Unreadable_or_out_of_range_values_fall_back_safely()
    {
        using var dir = new TempDir();
        var store = new Store(dir.Sub("sse.db"));
        store.Set("keys.peek", "Nonsense");
        store.Set("peek.anchor", "Upside down");
        store.Set("peek.scale", "9");
        store.Set("comparables.expiryDays", "0");
        var s = new Settings(store);

        Assert.Equal("Ctrl+C", s.PeekKey.ToString());
        Assert.Equal(PeekAnchor.Auto, s.Anchor);
        Assert.Equal(1.6, s.PeekScale);
        Assert.Equal(1, s.ComparableExpiryDays);

        store.Set("peek.scale", "0.2");
        Assert.Equal(0.5, s.PeekScale); // the slider's smallest
    }
}
