using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Data;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Desktop.Services;

/// <summary>Everything that depends on the active profile and league, built in one piece and swapped
/// atomically: price book, converter, data pack, clock and comparables. Nothing from one league or
/// profile can leak into another because a new session shares no state with the old one.</summary>
public sealed class Session
{
    private readonly Store _store;
    private readonly IReadOnlyList<(string SetKey, Comparable Comparable)> _seeded;
    private readonly IReadOnlySet<string> _practiceTexts;

    private Session(Store store, ProfileKind kind, string league, string displayCurrency, string? listingDenomination,
        PriceBook book, DataPack? pack, string packStatus, TimeProvider clock, IReadOnlyList<(string, Comparable)> seeded,
        TradeData trade, IReadOnlySet<string>? practiceTexts = null, bool livePrices = false, ValueLadder? colours = null)
    {
        Colours = colours ?? ValueLadder.Default;
        _store = store;
        _practiceTexts = practiceTexts ?? new HashSet<string>();
        Trade = trade;
        LivePricesOn = livePrices;
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

    /// <summary>Where each colour of the price label starts: the player's steps.</summary>
    public ValueLadder Colours { get; }

    /// <summary>Prices as the cards show them: this session's rates, display currency, clock and colours.</summary>
    public PriceText PriceText() => new(Converter, DisplayCurrency, Now, Colours);
    public DataPack? Pack { get; }
    public string PackStatus { get; }
    public TimeProvider Clock { get; }
    public DateTimeOffset Now => Clock.GetUtcNow();

    /// <summary>The trade site's filter ids, from the file that ships with the app.</summary>
    public TradeData Trade { get; }

    /// <summary>The league trade site links search: the confirmed live league, or Standard in the demo,
    /// whose league is made up.</summary>
    public string TradeLeague => IsDemo ? TradeLink.DemoLeague : League;

    /// <summary>A live profile with "Live prices from poe.ninja" switched on (always false for the demo).</summary>
    public bool LivePricesOn { get; }

    /// <summary>The status bar's tooltip line about the price book in use.</summary>
    public string PriceStatus => IsDemo
        ? $"{(Book.IsSynthetic ? "Synthetic prices" : "Prices")} · snapshot {ValueFormat.Age(Now - Book.CreatedAt)}"
        : Book.Quotes.Count > 0
            ? $"poe.ninja prices for {League}, fetched {LocalTime(Book.CreatedAt, Now)} · {Book.Quotes.Count} prices · snapshot {Book.SnapshotId}"
            : LivePricesOn ? $"No poe.ninja prices for {League} yet" : "Live prices are off";

    /// <summary>The status bar's one line about where prices come from. Clock times rather than ages, so
    /// the line stays true however long it sits there.</summary>
    public string FooterStatus => IsDemo ? "Demo prices, made up for practice"
        : Book.Quotes.Count > 0 ? $"Prices from poe.ninja, fetched {LocalTime(Book.CreatedAt, Now)}"
        : LivePricesOn ? "No prices yet: they load from poe.ninja (see Settings)"
        : "Live prices are off: switch them on in Settings";

    /// <summary>"14:05", or "26 Nov 14:05" when it was not today.</summary>
    public static string LocalTime(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var format = local.Date == now.ToLocalTime().Date ? "HH:mm" : "d MMM HH:mm";
        return local.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Whether this session's prices may be shown for <paramref name="item"/>. The demo's prices
    /// are made up, so they cover its practice items only: an item copied from the game gets no price
    /// rather than an invented one that looks real (a Mageblood "worth 25–35 ex").</summary>
    public bool HasPricesFor(ItemSnapshot item) => !IsDemo || _practiceTexts.Contains(PracticeKey(item.RawText));

    /// <summary>Listings the price check searches. The demo profile gets synthetic ones made around its
    /// practice items; anything else has none until live prices arrive, and says so.</summary>
    public IReadOnlyList<Listing>? Listings(ItemSnapshot item, ComparisonWorksheet worksheet)
    {
        if (!IsDemo || !HasPricesFor(item))
        {
            return null;
        }

        var key = ComparisonSet.KeyFor(item);
        var perDivine = Converter.Convert(1m, "divine", "exalted", Now);
        return DemoMarket.For(worksheet, key, DemoAnchor(item, key), item.ItemLevel, perDivine.Ok ? perDivine.Value : null, Now);
    }

    /// <summary>What items like this one ask in the demo, in Exalted Orbs: the seeded demo asks for this
    /// item, else the price book's baseline for a unique, else a plain default by rarity.</summary>
    private decimal DemoAnchor(ItemSnapshot item, string key)
    {
        var seeded = _seeded.Where(s => s.SetKey == key).Select(s => s.Comparable).ToList();
        if (seeded.Count > 0 && EvidencePolicy.Summarize(seeded, League, "exalted", Converter, Now).Median is { } median)
        {
            return median;
        }

        if (item.Rarity == ItemRarity.Unique && ItemPricer.Identify(item, Book) is { } id && Book.Find(id.Id, id.Variant) is { } quote
            && Converter.Convert(quote.Amount, quote.Denomination, "exalted", Now) is { Ok: true } baseline)
        {
            return baseline.Value;
        }

        return item.Rarity switch
        {
            ItemRarity.Rare => 80m,
            ItemRarity.Unique => 30m,
            ItemRarity.Magic => 6m,
            _ => 3m,
        };
    }

    /// <param name="fresh">A book that just arrived from poe.ninja. Used only when it is for the confirmed
    /// league; otherwise the newest snapshot on disk for that league is loaded, so prices work offline.</param>
    public static Session Create(Store store, Settings settings, AppPaths paths, PriceBook? fresh = null)
    {
        var kind = settings.Profile;
        if (kind == ProfileKind.Live && string.IsNullOrWhiteSpace(settings.LiveLeague))
        {
            kind = ProfileKind.Demo; // a live profile needs an explicitly confirmed league first
        }

        if (kind == ProfileKind.Demo)
        {
            return Demo(store, settings, paths);
        }

        var league = settings.LiveLeague!;
        var book = settings.LivePrices ? LiveBook(league, fresh, paths) ?? PriceBook.Empty(league) : PriceBook.Empty(league);
        PriceBookGuard.EnsureAllowed(ProfileKind.Live, league, book);
        return new Session(store, kind, league, settings.DisplayCurrency(kind), settings.ListingDenomination(kind), book,
            pack: null, "No verified data pack yet: ranges come from advanced copies only", TimeProvider.System, [], TradeDataFile.Load(paths), livePrices: settings.LivePrices, colours: settings.PriceColours);
    }

    /// <summary>The live book for exactly <paramref name="league"/>: never synthetic, never another league's.</summary>
    private static PriceBook? LiveBook(string league, PriceBook? fresh, AppPaths paths)
    {
        var book = fresh is not null && string.Equals(fresh.League, league, StringComparison.Ordinal)
            ? fresh
            : new PriceBookArchive(paths.Prices).Newest(league);
        return book is { IsSynthetic: false } && string.Equals(book.League, league, StringComparison.Ordinal) ? book : null;
    }

    /// <summary>The demo profile's session, whatever profile is active (onboarding shows a demo sample).</summary>
    public static Session Demo(Store store, Settings settings, AppPaths paths)
    {
        var book = PriceBook.FromJson(File.ReadAllText(Path.Combine(paths.Fixtures, "demo", "pricebook.json")));
        var load = DataPackLoader.Load(File.ReadAllText(Path.Combine(paths.Fixtures, "packs", "synthetic-demo.json")));
        PriceBookGuard.EnsureAllowed(ProfileKind.Demo, Settings.DemoLeague, book);
        return new Session(store, ProfileKind.Demo, book.League, settings.DisplayCurrency(ProfileKind.Demo), settings.ListingDenomination(ProfileKind.Demo), book,
            load.Pack, load.Pack is null ? $"Pack failed to load: {load.Error}" : "Synthetic demo pack (not game data)",
            new DemoTimeProvider(DemoTimeProvider.Start), LoadSeeded(paths), TradeDataFile.Load(paths), LoadPracticeTexts(paths), colours: settings.PriceColours);
    }

    /// <summary>The practice items' text, so the demo can tell them from items copied from the game.</summary>
    private static HashSet<string> LoadPracticeTexts(AppPaths paths)
    {
        var dir = Path.Combine(paths.Fixtures, "items");
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.txt").Select(f => PracticeKey(File.ReadAllText(f))).ToHashSet(StringComparer.Ordinal)
            : [];
    }

    /// <summary>Line endings and surrounding blank lines don't make a different item.</summary>
    private static string PracticeKey(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

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
