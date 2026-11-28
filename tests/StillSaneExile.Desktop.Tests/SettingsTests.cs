using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Capture;
using StillSaneExile.Domain.Placement;
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
        Assert.Equal(CardPosition.Tooltip, s.CardPosition);
        Assert.Null(s.CardSpot);
        Assert.False(s.LessMotion);
        Assert.False(s.PressClick);
        Assert.Equal(0.85, s.PeekScale);
        Assert.True(s.AutoUpdate);
        Assert.True(s.CloseToTray);
        Assert.True(s.MinimizeToTray);
        Assert.False(s.TrayNoticeShown);
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
            CardPosition = CardPosition.Fixed,
            CardSpot = new FixedSpot(0.4123, 0.25),
            LessMotion = true,
            PressClick = true,
            PeekScale = 1.25,
            AutoUpdate = false,
            CloseToTray = false,
            MinimizeToTray = false,
            TrayNoticeShown = true,
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
        Assert.Equal(CardPosition.Fixed, after.CardPosition);
        Assert.Equal(new FixedSpot(0.4123, 0.25), after.CardSpot);
        Assert.True(after.LessMotion);
        Assert.True(after.PressClick);
        Assert.Equal(1.25, after.PeekScale);
        Assert.False(after.AutoUpdate);
        Assert.False(after.CloseToTray);
        Assert.False(after.MinimizeToTray);
        Assert.True(after.TrayNoticeShown);
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

    [Theory]
    [InlineData(null, null, CardPosition.Tooltip)]
    [InlineData(null, "Auto", CardPosition.Tooltip)] // "Opposite the cursor", the old default: the new default
    [InlineData(null, "Left", CardPosition.Edge)] // "Always left" or "Always right": a side was picked, the edge stays
    [InlineData(null, "Right", CardPosition.Edge)]
    [InlineData("fixed", "Left", CardPosition.Fixed)]
    [InlineData("tooltip", "Right", CardPosition.Tooltip)]
    [InlineData("edge", null, CardPosition.Edge)]
    public void A_card_placement_from_an_earlier_version_still_loads(string? position, string? anchor, CardPosition expected)
    {
        using var dir = new TempDir();
        var store = new Store(dir.Sub("sse.db"));
        if (position is not null)
        {
            store.Set("peek.position", position);
        }

        if (anchor is not null)
        {
            store.Set("peek.anchor", anchor);
        }

        Assert.Equal(expected, new Settings(store).CardPosition);
    }

    [Fact]
    public void Unreadable_or_out_of_range_values_fall_back_safely()
    {
        using var dir = new TempDir();
        var store = new Store(dir.Sub("sse.db"));
        store.Set("keys.peek", "Nonsense");
        store.Set("peek.position", "Upside down");
        store.Set("peek.spot", "1.5,0.2");
        store.Set("peek.scale", "9");
        store.Set("comparables.expiryDays", "0");
        var s = new Settings(store);

        Assert.Equal("Ctrl+C", s.PeekKey.ToString());
        Assert.Equal(CardPosition.Tooltip, s.CardPosition);
        Assert.Null(s.CardSpot);
        Assert.Equal(1.6, s.PeekScale);
        Assert.Equal(1, s.ComparableExpiryDays);

        store.Set("peek.scale", "0.2");
        Assert.Equal(0.5, s.PeekScale); // the slider's smallest
    }
}
