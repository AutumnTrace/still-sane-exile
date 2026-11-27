using System.Text.Json;
using System.Text.Json.Serialization;

namespace StillSaneExile.Domain.Pricing;

public enum ObservationKind
{
    /// <summary>Current exchange estimate (e.g. a poe.ninja exchange overview).</summary>
    ExchangeEstimate,
    /// <summary>Volume-weighted average from GGG's historical exchange digest.</summary>
    HistoricalTradeAverage,
    /// <summary>Category baseline for a unique/variant: not a price for a specific roll.</summary>
    CategoryBaseline,
    /// <summary>A value the player typed in. Has an expiry.</summary>
    PlayerEntered,
}

/// <summary>"1 <see cref="ItemId"/> costs <see cref="Amount"/> <see cref="Denomination"/>", as observed
/// over [<see cref="ObservedFrom"/>, <see cref="ObservedTo"/>] in one league.</summary>
public sealed record Quote(
    string ItemId,
    string? Variant,
    decimal Amount,
    string Denomination,
    ObservationKind Kind,
    string League,
    string Source,
    DateTimeOffset ObservedFrom,
    DateTimeOffset ObservedTo,
    DateTimeOffset FetchedAt,
    decimal? Volume,
    string Provenance,
    DateTimeOffset? ExpiresAt = null);

public enum IdentityKind
{
    Currency,
    Commodity,
    Unique,
}

/// <summary>Maps what an item copy establishes (rarity + name + base) to a market id. Identity is
/// never taken from the display name alone: a unique needs its base too, a currency its rarity.</summary>
public sealed record MarketIdentity(string Id, IdentityKind Kind, string Name, string? BaseType = null, string? Variant = null);

/// <summary>An immutable price snapshot for one league. Wealth history stores the snapshot id it used.</summary>
public sealed record PriceBook(
    string SnapshotId,
    string League,
    string? MarketEra,
    string PrimaryCurrency,
    bool IsSynthetic,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Quote> Quotes,
    IReadOnlyList<MarketIdentity>? Identities = null)
{
    public static PriceBook Empty(string league) =>
        new($"empty:{league}", league, null, "exalted", IsSynthetic: false, DateTimeOffset.MinValue, [], []);

    public Quote? Find(string itemId, string? variant = null) =>
        Quotes.FirstOrDefault(q => q.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(q.Variant, variant, StringComparison.OrdinalIgnoreCase)
            && q.League == League);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static PriceBook FromJson(string json) =>
        JsonSerializer.Deserialize<PriceBook>(json, Options) ?? throw new InvalidDataException("Price book is empty.");
}

public enum ProfileKind
{
    Demo,
    Live,
}

/// <summary>Enforces "never show demo prices in a live profile" and "never carry rates across leagues".</summary>
public static class PriceBookGuard
{
    public static void EnsureAllowed(ProfileKind profile, string league, PriceBook book)
    {
        if (profile == ProfileKind.Live && book.IsSynthetic)
        {
            throw new InvalidOperationException("A synthetic (demo) price book cannot be used by a live profile.");
        }

        if (profile == ProfileKind.Demo && !book.IsSynthetic && book.Quotes.Count > 0)
        {
            throw new InvalidOperationException("The demo profile only uses synthetic prices.");
        }

        if (!string.Equals(book.League, league, StringComparison.Ordinal) && book.Quotes.Count > 0)
        {
            throw new InvalidOperationException($"Price book league '{book.League}' does not match the profile league '{league}'.");
        }
    }
}
