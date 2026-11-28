using System.Globalization;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Capture;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Services;

public enum PeekAnchor
{
    /// <summary>Opposite half of the screen from the cursor, where the game draws its own tooltip.</summary>
    Auto,
    Left,
    Right,
}

/// <summary>Typed view over the settings table. Defaults are the product defaults from the spec.</summary>
public sealed class Settings(Store store)
{
    public const string DemoLeague = "Demo League (synthetic)";

    public bool Onboarded
    {
        get => store.Get("onboarded") == "1";
        set => store.Set("onboarded", value ? "1" : "0");
    }

    public ProfileKind Profile
    {
        get => store.Get("profile") == "live" ? ProfileKind.Live : ProfileKind.Demo;
        set => store.Set("profile", value == ProfileKind.Live ? "live" : "demo");
    }

    /// <summary>The live league the player explicitly confirmed. Null until they do.</summary>
    public string? LiveLeague
    {
        get => store.Get("live.league");
        set => store.Set("live.league", value ?? "");
    }

    public string DisplayCurrency(ProfileKind profile) => store.Get($"{Key(profile)}.currency") ?? "divine";

    public void SetDisplayCurrency(ProfileKind profile, string currency) => store.Set($"{Key(profile)}.currency", currency);

    public string? ListingDenomination(ProfileKind profile) => NullIfEmpty(store.Get($"{Key(profile)}.listingDenomination"));

    public void SetListingDenomination(ProfileKind profile, string? currency) => store.Set($"{Key(profile)}.listingDenomination", currency ?? "");

    public Hotkey PeekKey
    {
        get => Hotkey.Parse(store.Get("keys.peek")) ?? Hotkey.Parse("Ctrl+C")!;
        set => store.Set("keys.peek", value.ToString());
    }

    public Hotkey OpenLastKey
    {
        get => Hotkey.Parse(store.Get("keys.openLast")) ?? Hotkey.Parse("F3")!;
        set => store.Set("keys.openLast", value.ToString());
    }

    /// <summary>For stash and Aldur labels (Milestone 2). Until then it takes stash pictures, one per press,
    /// only while <see cref="CollectStashPictures"/> is on.</summary>
    public Hotkey StashKey
    {
        get => Hotkey.Parse(store.Get("keys.stash")) ?? Hotkey.Parse("RightAlt")!;
        set => store.Set("keys.stash", value.ToString());
    }

    /// <summary>"Collect stash pictures": each physical press of the stash key with the game in front saves
    /// one picture of the game window in the data folder. Off until the player switches it on.</summary>
    public bool CollectStashPictures
    {
        get => store.Get("stash.collect") == "1";
        set => store.Set("stash.collect", value ? "1" : "0");
    }

    public HoldMode PeekMode
    {
        get => store.Get("peek.mode") == "toggle" ? HoldMode.Toggle : HoldMode.Hold;
        set => store.Set("peek.mode", value == HoldMode.Toggle ? "toggle" : "hold");
    }

    public PeekAnchor Anchor
    {
        get => Enum.TryParse<PeekAnchor>(store.Get("peek.anchor"), out var a) ? a : PeekAnchor.Auto;
        set => store.Set("peek.anchor", value.ToString());
    }

    public const double MinPeekScale = 0.5;
    public const double MaxPeekScale = 1.6;

    /// <summary>The card's size over the game. 0.85 by default: the card is meant to stay small.</summary>
    public const double DefaultPeekScale = 0.85;

    public double PeekScale
    {
        get => double.TryParse(store.Get("peek.scale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? Math.Clamp(s, MinPeekScale, MaxPeekScale) : DefaultPeekScale;
        set => store.Set("peek.scale", value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Where each colour of the price label starts (Settings, Price colours). The same for every
    /// profile and league; anything stored that does not read back is the default.</summary>
    public ValueLadder PriceColours
    {
        get => ValueLadder.Parse(store.Get("prices.colours")) ?? ValueLadder.Default;
        set => store.Set("prices.colours", value.Format());
    }

    /// <summary>Fetch the live league's prices from poe.ninja, hourly. On by default, but it only ever runs
    /// for a live profile with a confirmed league, so confirming one is what turns it on.</summary>
    public bool LivePrices
    {
        get => store.Get("prices.live") != "0";
        set => store.Set("prices.live", value ? "1" : "0");
    }

    /// <summary>"Search the trade site from SSE": the price check's Search asks the official trade site
    /// for listings in the live league. On by default; it only ever sends anything when the player clicks
    /// Search in a live profile.</summary>
    public bool TradeSearch
    {
        get => store.Get("trade.search") != "0";
        set => store.Set("trade.search", value ? "1" : "0");
    }

    /// <summary>Check GitHub Releases for updates at startup and every few hours. On by default.</summary>
    public bool AutoUpdate
    {
        get => store.Get("updates.auto") != "0";
        set => store.Set("updates.auto", value ? "1" : "0");
    }

    /// <summary>The newest version that has run with this data, so the first start after an update can
    /// show "What's new". A downgrade leaves it alone. Builds up to 0.2.0 did not store it.</summary>
    public string? LastRunVersion
    {
        get => NullIfEmpty(store.Get("app.lastRunVersion"));
        set => store.Set("app.lastRunVersion", value ?? "");
    }

    /// <summary>The pinned guide lines over the game. Off until the player switches them on.</summary>
    public bool GuideHud
    {
        get => store.Get("guide.hud") == "1";
        set => store.Set("guide.hud", value ? "1" : "0");
    }

    /// <summary>The character the guide page opened last, so a restart resumes where the player was.</summary>
    public string? GuideCharacter
    {
        get => NullIfEmpty(store.Get("guide.character"));
        set => store.Set("guide.character", value ?? "");
    }

    public int ComparableExpiryDays
    {
        get => int.TryParse(store.Get("comparables.expiryDays"), out var d) ? Math.Clamp(d, 1, 60) : 3;
        set => store.Set("comparables.expiryDays", value.ToString(CultureInfo.InvariantCulture));
    }

    private static string Key(ProfileKind p) => p == ProfileKind.Live ? "live" : "demo";

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
