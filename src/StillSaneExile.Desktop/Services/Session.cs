using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Data;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Services;

/// <summary>Everything that depends on the active profile and league, built in one piece and swapped
/// atomically: price book, converter, data pack, clock and comparables. Nothing from one league or
/// profile can leak into another because a new session shares no state with the old one.</summary>
public sealed class Session
{
    private readonly Store _store;
    private readonly IReadOnlyList<(string SetKey, Comparable Comparable)> _seeded;

    private Session(Store store, ProfileKind kind, string league, string displayCurrency, string? listingDenomination,
        PriceBook book, DataPack? pack, string packStatus, TimeProvider clock, IReadOnlyList<(string, Comparable)> seeded)
    {
        _store = store;
        Kind = kind;
        League = league;
        DisplayCurrency = displayCurrency;
        ListingDenomination = listingDenomination;
        Book = book;
        Pack = pack;
        PackStatus = packStatus;
        Clock = clock;
        _seeded = seeded;
        Converter = new CurrencyConverter(book, StalenessPolicy.Hourly);
    }

    public ProfileKind Kind { get; }
    public bool IsDemo => Kind == ProfileKind.Demo;
    public string ProfileId => IsDemo ? "demo" : "live";
    public string League { get; }
    public string DisplayCurrency { get; }
    public string? ListingDenomination { get; }
    public PriceBook Book { get; }
    public CurrencyConverter Converter { get; }
    public DataPack? Pack { get; }
    public string PackStatus { get; }
    public TimeProvider Clock { get; }
    public DateTimeOffset Now => Clock.GetUtcNow();

    public string PriceStatus => Book.Quotes.Count == 0
        ? "No market data (economy cache arrives in Milestone 2)"
        : $"{(Book.IsSynthetic ? "Synthetic prices" : "Prices")} · snapshot {ValueFormat.Age(Now - Book.CreatedAt)}";

    public static Session Create(Store store, Settings settings, AppPaths paths)
    {
        var kind = settings.Profile;
        if (kind == ProfileKind.Live && string.IsNullOrWhiteSpace(settings.LiveLeague))
        {
            kind = ProfileKind.Demo; // a live profile needs an explicitly confirmed league first
        }

        if (kind == ProfileKind.Demo)
        {
            var book = PriceBook.FromJson(File.ReadAllText(Path.Combine(paths.Fixtures, "demo", "pricebook.json")));
            var load = DataPackLoader.Load(File.ReadAllText(Path.Combine(paths.Fixtures, "packs", "synthetic-demo.json")));
            PriceBookGuard.EnsureAllowed(ProfileKind.Demo, Settings.DemoLeague, book);
            return new Session(store, kind, book.League, settings.DisplayCurrency(kind), settings.ListingDenomination(kind), book,
                load.Pack, load.Pack is null ? $"Pack failed to load: {load.Error}" : "Synthetic demo pack (not game data)",
                new DemoTimeProvider(DemoTimeProvider.Start), LoadSeeded(paths));
        }

        var league = settings.LiveLeague!;
        var empty = PriceBook.Empty(league);
        PriceBookGuard.EnsureAllowed(ProfileKind.Live, league, empty);
        return new Session(store, kind, league, settings.DisplayCurrency(kind), settings.ListingDenomination(kind), empty,
            pack: null, "No verified data pack yet: ranges come from advanced copies only", TimeProvider.System, []);
    }

    public IReadOnlyList<Comparable> Comparables(string setKey) =>
        _seeded.Where(s => s.SetKey == setKey).Select(s => s.Comparable)
            .Concat(_store.Comparables(ProfileId, League, setKey))
            .ToList();

    public bool IsSeeded(string comparableId) => _seeded.Any(s => s.Comparable.Id == comparableId);

    private static List<(string, Comparable)> LoadSeeded(AppPaths paths)
    {
        var file = Path.Combine(paths.Fixtures, "demo", "comparables.json");
        if (!File.Exists(file))
        {
            return [];
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
        var rows = JsonSerializer.Deserialize<List<SeededComparable>>(File.ReadAllText(file), options) ?? [];
        return rows.Select(r => (r.SetKey, new Comparable(r.Id, r.League, r.Amount, r.Currency, r.InspectedAt, r.SellerAlias, r.ListingId, null, r.Notes, r.Origin, r.ExpiresAt))).ToList();
    }

    private sealed record SeededComparable(string SetKey, string Id, string League, decimal Amount, string Currency, DateTimeOffset InspectedAt,
        string? SellerAlias, string? ListingId, string? Notes, ComparableOrigin Origin, DateTimeOffset? ExpiresAt);
}
