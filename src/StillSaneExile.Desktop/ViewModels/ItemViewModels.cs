using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Market;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Reading;
using StillSaneExile.Domain.Rolls;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>One stat in the search: tick it to search for it, and set the number to search for.</summary>
public sealed partial class SearchRowViewModel : ObservableObject
{
    public const string AmbiguousHint = "Can’t tell which stat this is — Ctrl+Alt+C shows it";

    public const string CorruptionUnknownHint = "Can’t tell if corruption added this — Ctrl+Alt+C shows it";

    private readonly Action _changed;
    private readonly bool _corruptedItem;

    /// <param name="corruptedItem">The copy says the item is corrupted.</param>
    public SearchRowViewModel(WorksheetRow row, LineReading? reading, Action changed, bool corruptedItem = false)
    {
        Row = row;
        Reading = reading;
        Roll = RollView.From(reading);
        _changed = changed;
        _corruptedItem = corruptedItem;
    }

    public WorksheetRow Row { get; }
    public LineReading? Reading { get; }

    /// <summary>Corruption added this stat, as the game says in an advanced copy: a red "corrupted" tag
    /// beside it.</summary>
    public bool FromCorruption => Reading?.Group.FromCorruption == true;

    /// <summary>The checkbox's name: the stat, and "corrupted" when corruption added it.</summary>
    public string SpokenText => FromCorruption ? $"{Text}, corrupted" : Text;

    /// <summary>The stat's tier and where its roll sits in the tier's range, under its text; null when
    /// neither is known (a normal copy without a data pack, a derived number).</summary>
    public RollView? Roll { get; }

    public bool HasRoll => Roll is not null;

    public string Text => Row.Kind == RowKind.Derived && Row.Actual is { } a ? $"{Row.Label} {RollQuality.Num(a)}" : Row.Label;

    /// <summary>A number worked out from the item (physical DPS), drawn like the game's properties (grey and
    /// white) rather than in the blue of a stat.</summary>
    public bool IsProperty => Row.Kind == RowKind.Derived;
    public string ShortLabel => Labels.Short(Row);
    public bool IsAmbiguous => Reading?.Status == LineStatus.Ambiguous;
    public bool IsAtMost => Row.Match == MatchType.Maximum;
    public decimal Step => Row.Actual is >= 100 ? 5 : 1;

    /// <summary>What a screen reader calls the number box ("Minimum for 17% increased Attack Speed")
    /// and its − and + buttons, so each says which stat and which bound it changes.</summary>
    public string BoundName => $"{(IsAtMost ? "Maximum" : "Minimum")} for {Text}";

    public string LowerName => $"Lower the {(IsAtMost ? "maximum" : "minimum")} for {Text}";

    public string RaiseName => $"Raise the {(IsAtMost ? "maximum" : "minimum")} for {Text}";

    /// <summary>The row as a screen reader says it (WPF reads a list row through ToString).</summary>
    public override string ToString() => Roll is { } r ? $"{SpokenText}, {r.Spoken}" : SpokenText;

    /// <summary>Where a stat comes from when that is not an ordinary affix: "Rune", "Implicit", "Enchant",
    /// else nothing. The tier sits under the stat, with its roll.</summary>
    public string Kind => Row.Kind switch
    {
        RowKind.Rune => "Rune",
        RowKind.Implicit => "Implicit",
        RowKind.Enchant => "Enchant",
        _ => "",
    };

    public string? Hint =>
        IsAmbiguous ? AmbiguousHint
        : Row.Id == ComparisonWorksheet.PhysicalDpsId ? "Damage per second from physical hits. Best single number for a weapon."
        : Row.Kind == RowKind.Rune ? "From a rune — buyers rarely care"
        // A normal copy ends corruption enchants and anointments alike in "(enchant)"; an advanced one tells them apart.
        : Row.Kind == RowKind.Enchant && _corruptedItem && Reading is { Group.FromAdvancedCopy: false } ? CorruptionUnknownHint
        : !Row.Supported ? "Not in the trade site link: set it by hand there"
        : Reading?.Line.Components.Count > 1 && Row.Actual is { } a ? $"Searched by the average ({RollQuality.Num(Math.Round(a, 1))})"
        : null;

    public bool HasHint => Hint is not null;

    public bool IsOn
    {
        get => Row.Selected;
        set
        {
            if (Row.Selected != value)
            {
                Row.Selected = value;
                OnPropertyChanged();
                _changed();
            }
        }
    }

    /// <summary>The number searched for: the minimum, or the maximum for a lower-is-better stat.</summary>
    public string Bound
    {
        get
        {
            var v = IsAtMost ? Row.Max : Row.Min;
            return v is null ? "" : RollQuality.Num(v);
        }

        set
        {
            var parsed = decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Max(0, v) : (decimal?)null;
            SetBound(parsed, notify: false);
        }
    }

    /// <summary>"≥ 319" for the peek card.</summary>
    public string BoundLabel => Bound.Length == 0 ? "any" : $"{(IsAtMost ? "≤" : "≥")} {Bound}";

    [RelayCommand]
    private void Increase() => SetBound(((IsAtMost ? Row.Max : Row.Min) ?? 0) + Step, notify: true);

    [RelayCommand]
    private void Decrease() => SetBound(Math.Max(0, ((IsAtMost ? Row.Max : Row.Min) ?? 0) - Step), notify: true);

    /// <summary>Re-reads the row after the whole search changed (strictness, reset).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Bound));
        OnPropertyChanged(nameof(BoundLabel));
        OnPropertyChanged(nameof(IsOn));
    }

    /// <summary>Changing a number puts the stat in the search.</summary>
    private void SetBound(decimal? value, bool notify)
    {
        if (IsAtMost)
        {
            Row.Max = value;
        }
        else
        {
            Row.Min = value;
        }

        Row.Selected = true;
        if (notify)
        {
            OnPropertyChanged(nameof(Bound));
        }

        OnPropertyChanged(nameof(BoundLabel));
        OnPropertyChanged(nameof(IsOn));
        _changed();
    }
}

/// <summary>One roll in "How good are your rolls?": tier, a bar toward the best number, and a note.</summary>
public sealed class RollRowViewModel
{
    public RollRowViewModel(LineReading l, bool advancedCopy)
    {
        Text = l.Line.Display;
        TierText = l.Tier is { } t ? (l.TierCount is { } c ? $"Tier {t} of {c}" : $"Tier {t}") : "";
        var scored = l.Rolls.Where(r => r.Status == RollStatus.Scored).ToList();
        var placed = l.Rolls.Where(r => r.Status == RollStatus.PositionOnly).ToList();

        if (scored.Count > 0)
        {
            var fill = (double)scored.Average(r => r.Fill!.Value);
            HasFill = true;
            FillWidth = Star(fill);
            RestWidth = Star(1 - fill);
        }
        else if (placed.Count > 0)
        {
            var pos = (double)placed.Average(r => r.Position!.Value);
            HasMarker = true;
            FillWidth = Star(pos);
            RestWidth = Star(1 - pos);
        }

        (Note, NoteIsWarning) = NoteFor(l, scored, placed, advancedCopy);
        if (l.RangeCaveat is { } caveat)
        {
            Note += " " + caveat;
        }

        var explanations = l.Status == LineStatus.Ambiguous
            ? l.Candidates.Take(4).Select(c => $"Could be: {c.Description}").Concat(l.Candidates.Count > 4 ? [$"…and {l.Candidates.Count - 4} more"] : [])
            : [];
        Details = string.Join("\n", new[] { Meta(l) }.Concat(l.Rolls.Select(RollQuality.Describe)).Concat(explanations));
    }

    public string Text { get; }
    public string TierText { get; }
    public bool HasFill { get; }
    public bool HasMarker { get; }
    public bool HasBar => HasFill || HasMarker;
    public GridLength FillWidth { get; } = Star(0);
    public GridLength RestWidth { get; } = Star(1);
    public string Note { get; }
    public bool NoteIsWarning { get; }

    /// <summary>The full reading, for the tooltip: source, affix name, where the range came from, each number,
    /// and for a line that could be two stats, the explanations that fit.</summary>
    public string Details { get; }

    /// <summary>What a screen reader says for the row (WPF reads a list row through ToString).</summary>
    public override string ToString() => string.Join(", ", new[] { Text, TierText, Note }.Where(s => s.Length > 0));

    private static GridLength Star(double v) => new(Math.Max(v, 0.0001), GridUnitType.Star);

    private static (string, bool) NoteFor(LineReading l, List<RollAssessment> scored, List<RollAssessment> placed, bool advancedCopy)
    {
        if (l.Status == LineStatus.Ambiguous)
        {
            return (SearchRowViewModel.AmbiguousHint, true);
        }

        if (l.Rolls.Any(r => r.Status == RollStatus.RangeMismatch))
        {
            return ("This number is outside the range we know. The data may be out of date.", true);
        }

        if (scored.Count > 0)
        {
            var best = string.Join(" to ", scored.Select(r => RollQuality.Num(r.Direction == StatDirection.LowerIsBetter ? r.Min : r.Max)));
            var perfect = scored.All(r => r.DistanceFromBest == 0);
            if (l.Tier == 1)
            {
                return perfect ? ("Best possible roll.", false)
                    : scored.Count == 1 ? ($"Best possible: {best}. You’re {RollQuality.Num(scored[0].DistanceFromBest)} short.", false)
                    : ($"Best possible: {best}.", false);
            }

            return perfect ? ("Best roll in this tier.", false)
                : (l.Tier is null ? $"Best for this range: {best}." : $"Best in this tier: {best}.", false);
        }

        if (placed.Count > 0)
        {
            var r = placed[0];
            return ($"Range {RollQuality.Num(r.Min)}–{RollQuality.Num(r.Max)}. Which end is better isn’t checked yet.", false);
        }

        if (l.Rolls.Count > 0 && l.Rolls.All(r => r.Status == RollStatus.Fixed))
        {
            return ("This number can’t change.", false);
        }

        return advancedCopy ? ("No range known for this line.", false) : ("No range known — a Ctrl+Alt+C copy shows it.", false);
    }

    private static string Meta(LineReading l)
    {
        var source = l.Source switch
        {
            ModSource.Explicit when l.Group.Side == AffixSide.Prefix => "Prefix",
            ModSource.Explicit when l.Group.Side == AffixSide.Suffix => "Suffix",
            ModSource.Explicit => "Explicit",
            var s => s.ToString(),
        };
        var from = l.Status switch
        {
            LineStatus.FromAdvancedCopy => "range from the game",
            LineStatus.Resolved => l.RangeCaveat is null ? "range from data pack" : "range from an unverified data pack",
            LineStatus.Ambiguous => "more than one explanation",
            LineStatus.RangeMismatch => "range mismatch",
            LineStatus.NotInPack => "not in data pack",
            _ => "no data pack",
        };
        return string.Join(" · ", new[] { source, l.AffixName is null ? null : $"\"{l.AffixName}\"", from }.Where(s => s is not null));
    }
}

/// <summary>One of the cheapest listings the search found: its price, the listed item's numbers for the
/// stats searched, whether it can be bought at once, and how long ago it was listed. Never the seller.</summary>
/// <param name="Tier">The price label's colour step; null draws the price plain.</param>
public sealed record MatchViewModel(string Price, string Stats, bool BuyNow, string Age, ValueTier? Tier = null)
{
    /// <summary>"Instant buyout" or "In person", as the trade site says it.</summary>
    public string How => BuyNow ? "Instant buyout" : "In person";

    /// <summary>The card's shorter right-hand column: "2 h", with "in person" when it can't be bought at once.</summary>
    public string Short
    {
        get
        {
            var age = Age == "just now" ? "now" : Age.EndsWith(" ago", StringComparison.Ordinal) ? Age[..^4] : Age;
            return BuyNow ? age : age.Length > 0 ? $"{age} · in person" : "in person";
        }
    }

    public bool InPerson => !BuyNow;

    /// <summary>What a screen reader says for the row (WPF reads a list row through ToString).</summary>
    public override string ToString() => string.Join(", ", new[] { Price, Stats, How, Age.Length > 0 ? $"listed {Age}" : "" }.Where(s => s.Length > 0));
}

public sealed record StrictnessChoice(string Key, string Label, decimal Tolerance)
{
    /// <summary>What a screen reader says for the choice (WPF reads a list row through ToString).</summary>
    public override string ToString() => Label;
}

public sealed class ComparableViewModel(Comparable c, string display, bool seeded, DateTimeOffset now)
{
    public Comparable Comparable { get; } = c;
    public string Id => Comparable.Id;
    public string Price => $"{ValueFormat.Amount(Comparable.Amount)} {Currencies.Short(Comparable.Currency)}";
    public string Converted { get; } = display;
    public string Seller => Comparable.SellerAlias ?? "seller not noted";
    public string Age => ValueFormat.Age(now - Comparable.InspectedAt);
    public string? Notes => Comparable.Notes;
    public bool Seeded { get; } = seeded;
    public bool Expired => Comparable.ExpiresAt is { } e && e <= now;
    public string Tags => string.Join(" · ", new[] { Seeded ? "demo" : "you wrote this", Expired ? "expired" : null, Comparable.ListingLink is null && Comparable.ListingId is null ? "no link" : null, Notes }.Where(t => t is not null));

    /// <summary>What a screen reader says for the row (WPF reads a list row through ToString).</summary>
    public override string ToString() => $"{Price}{(Converted.Length > 0 ? $" {Converted}" : "")}, {Seller}, {Age}";
}

/// <summary>Everything the price check shows for one item snapshot. The Inspect page and the in-game
/// price check window bind to the same object, so a change in one shows in the other.</summary>
/// <remarks>Changing the search (a tick, a number, strictness, buy-now, the Corrupted tag) never searches: it marks
/// the result shown as old ("Search to update") until the player clicks Search. The demo's practice items
/// get their first result at once, from synthetic listings; a live item gets nothing from the trade site
/// until Search is clicked, so opening a card (a key press) never makes a request.</remarks>
public sealed partial class ItemViewModel : ObservableObject
{
    public static readonly IReadOnlyList<StrictnessChoice> StrictnessChoices =
    [
        new("exact", "My exact rolls", 0m),
        new("10", "10% lower", 0.1m),
        new("20", "20% lower", 0.2m),
    ];

    /// <summary>The most listings shown: the cheapest of the last search.</summary>
    public const int ListingRows = 10;

    /// <summary>The line under an old result.</summary>
    public const string StaleText = "Search to update";

    public const string SearchingText = "Searching the trade site…";

    public const string NeedsLiveLeague = "Search needs a live league: the demo's listings are made up and only cover the practice items. Pick a live league in Settings.";

    public const string SearchOffText = "Searching the trade site from SSE is off (Settings, Trade site search). Open this search on the trade site instead.";

    private const string DefaultStrictness = "10";
    private readonly Session _session;
    private readonly ITradeSite? _site;
    private readonly IReadOnlyList<Listing>? _listings;

    /// <summary>The search as it stands, and the one the result shown was found with (null: no result yet).</summary>
    private string _currentKey = "";
    private string? _resultKey;
    private SearchResult? _result;
    private IReadOnlyList<SearchRowViewModel> _resultRows = [];

    /// <summary>The last trade site answer, kept so a session change can re-price it without asking again.</summary>
    private TradePage? _page;

    /// <param name="site">Where Search asks for live listings; null in the demo's onboarding sample and tests.</param>
    public ItemViewModel(ItemSnapshot item, Session session, ITradeSite? site = null)
    {
        Item = item;
        _session = session;
        _site = site;
        Reading = ItemReader.Read(item, session.Pack);
        Worksheet = NewWorksheet();
        SearchRows = new ObservableCollection<SearchRowViewModel>(Worksheet.Rows.Select(r => new SearchRowViewModel(r, ReadingFor(r), OnSearchChanged, IsCorrupted)));
        RollRows = Reading.Lines
            .Where(l => l.Line.Components.Count > 0 && (l.Source is not (ModSource.Implicit or ModSource.Rune or ModSource.Enchant) || l.Rolls.Any(r => r.HasBar)))
            .Select(l => new RollRowViewModel(l, item.Copy == CopyKind.Advanced))
            .ToList();
        SetKey = ComparisonSet.KeyFor(item);
        _listings = ShowsWorksheet ? session.Listings(item, Worksheet) : null;
        OnSearchChanged();
        if (_listings is not null && !IsUnidentified)
        {
            RunDemoSearch(); // the practice items open with a result, as the demo always did
        }

        RefreshPrice();
    }

    public ItemSnapshot Item { get; }
    public ItemReading Reading { get; }
    public ComparisonWorksheet Worksheet { get; }
    public ObservableCollection<SearchRowViewModel> SearchRows { get; }
    public IReadOnlyList<RollRowViewModel> RollRows { get; }
    public string SetKey { get; }
    public bool IsDemo => _session.IsDemo;

    public string Title => Item.Name ?? Item.BaseType;
    public ItemRarity Rarity => Item.Rarity;
    public bool IsUnique => Item.Rarity == ItemRarity.Unique;
    public string UniqueNameRow => $"Name: {Item.Name}";

    /// <summary>"Siege Crossbow · Item level 82", "Stack of 7".</summary>
    public string Subtitle => string.Join(" · ", new[]
    {
        Item.Name is null ? null : Item.BaseType,
        Item.Rarity == ItemRarity.Unique ? "Unique" : null,
        Item.StackSize is { } s ? $"Stack of {s}" : null,
        Item.ItemLevel is { } il && Item.Rarity is not ItemRarity.Currency ? $"Item level {il}" : null,
    }.Where(s => s is not null));

    /// <summary>The second line of the header, as the game's tooltip has it: the base of a named item.</summary>
    public string? HeaderBase => Item.Name is null ? null : Item.BaseType;

    public bool HasHeaderBase => HeaderBase is not null;

    /// <summary>The rest of <see cref="Subtitle"/> under the name and base: "Item level 82", "Stack of 7".</summary>
    public string HeaderDetail => string.Join(" · ", new[]
    {
        Item.Rarity == ItemRarity.Unique ? "Unique" : null,
        Item.StackSize is { } s ? $"Stack of {s}" : null,
        Item.ItemLevel is { } il && Item.Rarity is not ItemRarity.Currency ? $"Item level {il}" : null,
    }.Where(s => s is not null));

    public bool HasHeaderDetail => HeaderDetail.Length > 0;

    /// <summary>The name in the session list, with the stack size for currency ("Exalted Orb ×7").</summary>
    public string ListTitle => Item.Rarity == ItemRarity.Currency && Item.StackSize is > 1 ? $"{Title} ×{Item.StackSize}" : Title;

    /// <summary>"Rare crossbow · 13:30".</summary>
    public string ListSubtitle => $"{Labels.Kind(Item)} · {ListTime}";

    private string ListTime => Item.CapturedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The session list row as a screen reader says it: "Doom Thirst, Rare crossbow, checked at
    /// 13:30". WPF names a list row after its data object's ToString.</summary>
    public override string ToString() =>
        $"{Title}{(Item.Rarity == ItemRarity.Currency && Item.StackSize is > 1 ? $", stack of {Item.StackSize}" : "")}, {Labels.Kind(Item)}, checked at {ListTime}";

    public string CapturedAt => Item.CapturedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public IReadOnlyList<string> Unparsed => Item.UnparsedLines;
    public bool HasAmbiguity => Reading.HasAmbiguity;
    public bool HasRollRows => RollRows.Count > 0;

    /// <summary>"1 empty prefix slot — an Exalted Orb could add a stat." Only when an advanced copy shows
    /// every affix's side, so the count is known rather than guessed.</summary>
    public string? OpenSlotsLine
    {
        get
        {
            var affixes = Item.Mods.Where(g => g.Source is ModSource.Explicit or ModSource.Fractured or ModSource.Desecrated or ModSource.Crafted).ToList();
            if (Item.Rarity != ItemRarity.Rare || Item.Copy != CopyKind.Advanced || Item.States.HasFlag(ItemStates.Corrupted) || affixes.Any(g => g.Side == AffixSide.None))
            {
                return null;
            }

            var prefixes = 3 - affixes.Count(g => g.Side == AffixSide.Prefix);
            var suffixes = 3 - affixes.Count(g => g.Side == AffixSide.Suffix);
            static string Slots(int n, string side) => $"{n} empty {side} slot{(n == 1 ? "" : "s")}";
            var open = (prefixes > 0, suffixes > 0) switch
            {
                (true, true) => $"{Slots(prefixes, "prefix")} and {Slots(suffixes, "suffix")}",
                (true, false) => Slots(prefixes, "prefix"),
                (false, true) => Slots(suffixes, "suffix"),
                _ => null,
            };
            return open is null ? "No empty slots: all six stats are taken." : $"{open} — an Exalted Orb could add a stat.";
        }
    }

    public bool ShowsComparables => Item.Rarity is ItemRarity.Rare or ItemRarity.Magic or ItemRarity.Normal;

    public bool ShowsWorksheet => Item.Rarity is not (ItemRarity.Currency or ItemRarity.Gem);

    /// <summary>Whether Search finds listings here: the demo's practice items (synthetic), or a live profile
    /// with "Search the trade site from SSE" on.</summary>
    public bool HasListings => _listings is not null || LiveSearchOn;

    private bool LiveSearchOn => !_session.IsDemo && _site is { IsOn: true };

    private bool IsUnidentified => Item.Rarity == ItemRarity.Rare && Item.States.HasFlag(ItemStates.Unidentified);

    /// <summary>The item has a search to run: Search shows (in the demo, for an item copied from the game,
    /// it says why there is nothing to search).</summary>
    public bool CanSearch => ShowsWorksheet && !IsUnidentified;

    [ObservableProperty]
    public partial PriceLine? Price { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeekLine), nameof(PeekLineIsWarning), nameof(PeekShowsCount), nameof(NoteLine), nameof(NoteLineIsWarning))]
    public partial PriceAnswer Answer { get; set; } = PriceAnswer.NoPrice(noMarketData: true);

    /// <summary>The cheapest listings of the last search, at most <see cref="ListingRows"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatches), nameof(ListingsTitle), nameof(ListingsHead))]
    public partial IReadOnlyList<MatchViewModel> Matches { get; set; } = [];

    [ObservableProperty]
    public partial bool NoMatches { get; set; }

    public bool HasMatches => Matches.Count > 0;

    /// <summary>"Cheapest 10 of 1,605", "All 5 for sale".</summary>
    public string ListingsTitle => _result is not { } r || Matches.Count == 0 ? ""
        : r.Count > Matches.Count ? $"Cheapest {Matches.Count} of {r.Count.ToString("N0", CultureInfo.InvariantCulture)}"
        : $"All {Matches.Count} for sale";

    /// <summary><see cref="ListingsTitle"/> as the card's small heading: "CHEAPEST 10 OF 1,605".</summary>
    public string ListingsHead => ListingsTitle.ToUpperInvariant();

    /// <summary>A Search is on its way to the trade site.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmed), nameof(PeekLine), nameof(PeekLineIsWarning), nameof(PeekShowsCount), nameof(SearchLine), nameof(HasSearchLine), nameof(SearchLineIsWarning))]
    public partial bool IsSearching { get; set; }

    /// <summary>What happened to the last Search click, when there is something to say: the trade site asks
    /// to wait, could not be reached, Search needs a live league...</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeekLine), nameof(PeekLineIsWarning), nameof(PeekShowsCount), nameof(SearchLine), nameof(HasSearchLine))]
    public partial string? SearchNote { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeekLineIsWarning), nameof(SearchLineIsWarning))]
    public partial bool SearchNoteIsWarning { get; set; }

    /// <summary>The result shown was found with a search the player has changed since.</summary>
    public bool IsStale => _resultKey is not null && _resultKey != _currentKey;

    /// <summary>The price and listings are drawn faded: they belong to an older search, or a new one is running.</summary>
    public bool IsDimmed => IsStale || IsSearching;

    /// <summary>The line under the price on the card: what Search is doing, else "Search to update" under an
    /// old result, else how sure the price is.</summary>
    public string PeekLine => IsSearching ? SearchingText : SearchNote ?? (IsStale ? StaleText : Answer.PeekNote);

    public bool PeekLineIsWarning => !IsSearching && (SearchNote is not null ? SearchNoteIsWarning : !IsStale && Answer.NoteIsWarning);

    /// <summary>The card puts "29 for sale" before its line, unless the line already says how many.</summary>
    public bool PeekShowsCount => Answer.HasCount && !PeekLine.Contains("for sale", StringComparison.Ordinal);

    /// <summary>The price check's note under the price: "Search to update" under an old result.</summary>
    public string NoteLine => IsStale ? $"{StaleText}: the price and listings are from your search before the changes." : Answer.Note;

    public bool NoteLineIsWarning => !IsStale && Answer.NoteIsWarning;

    /// <summary>Next to the price check's Search button.</summary>
    public string? SearchLine => IsSearching ? SearchingText : SearchNote ?? (IsStale ? StaleText : null);

    public bool HasSearchLine => SearchLine is not null;

    public bool SearchLineIsWarning => !IsSearching && SearchNote is not null && SearchNoteIsWarning;

    [ObservableProperty]
    public partial IReadOnlyList<ComparableViewModel> Comparables { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> PriceWarnings { get; set; } = [];

    [ObservableProperty]
    public partial string Instructions { get; set; } = "";

    [ObservableProperty]
    public partial string Strictness { get; set; } = DefaultStrictness;

    [ObservableProperty]
    public partial int OnCount { get; set; }

    /// <summary>Feedback after "Copy search", shown under the buttons.</summary>
    [ObservableProperty]
    public partial string? SearchStatus { get; set; }

    /// <summary>The search on the official trade site, rebuilt whenever the search changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TradeUrl), nameof(ByHand), nameof(HasByHand))]
    public partial TradeSearch? Trade { get; set; }

    public string TradeUrl => Trade?.Url ?? TradeLink.Site;

    /// <summary>"Set by hand: +18% to Fire Resistance", when the link can't carry everything ticked.</summary>
    public string? ByHand => Trade is { ByHand.Count: > 0 } t ? $"Set by hand: {string.Join(", ", t.ByHand)}" : null;

    public bool HasByHand => ByHand is not null;

    public string TradeTooltip => IsDemo
        ? "Opens this search on the official trade site in your browser. The demo league is made up, so the link searches Standard."
        : $"Opens this search on the official trade site in your browser, in {_session.TradeLeague}.";

    public int TotalCount => SearchRows.Count + (IsUnique ? 1 : 0);

    public bool BuyNowOnly
    {
        get => Worksheet.BuyNowOnly;
        set
        {
            if (Worksheet.BuyNowOnly != value)
            {
                Worksheet.BuyNowOnly = value;
                OnPropertyChanged();
                OnSearchChanged();
            }
        }
    }

    /// <summary>The copy says the item is corrupted.</summary>
    public bool IsCorrupted => Item.States.HasFlag(ItemStates.Corrupted);

    /// <summary>The "Corrupted" tag by the item level: on a corrupted item with a search to change.</summary>
    public bool ShowsCorruptedTag => IsCorrupted && ShowsWorksheet;

    /// <summary>A corrupted item's search: corrupted items only (the tag on, as it starts), or corrupted or not
    /// (off). Like the other search choices it never searches by itself. An item that is not corrupted keeps
    /// searching for items that are not.</summary>
    public bool CorruptedOnly
    {
        get => Worksheet.Corrupted == true;
        set
        {
            if (IsCorrupted && CorruptedOnly != value)
            {
                Worksheet.Corrupted = value ? true : null;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CorruptedTip));
                OnSearchChanged();
            }
        }
    }

    public string CorruptedTip => CorruptedOnly
        ? "Searching corrupted items only. Click to search corrupted or not."
        : "Searching corrupted or not. Click to search corrupted items only.";

    [RelayCommand]
    private void ToggleCorrupted() => CorruptedOnly = !CorruptedOnly;

    /// <summary>On the card, when a normal copy left every stat without a tier or range: the game's advanced
    /// copy prints them. Null when there is nothing to add.</summary>
    public string? AdvancedCopyHint =>
        Item.Copy == CopyKind.Normal && SearchRows.Any(r => r.Reading is { Line.Components.Count: > 0 }) && !SearchRows.Any(r => r.HasRoll)
            ? IsUnique ? "Ctrl+Alt+C for ranges" : "Ctrl+Alt+C for tiers"
            : null;

    public bool HasAdvancedCopyHint => AdvancedCopyHint is not null;

    public void RefreshPrice()
    {
        var comps = ShowsComparables ? _session.Comparables(SetKey) : [];
        Price = ItemPricer.Price(Item, _session.Converter, _session.DisplayCurrency, comps, _session.League, _session.Now);
        PriceWarnings = Price.Evidence?.Warnings().ToList() ?? [];
        Comparables = comps
            .Select(c =>
            {
                var conv = _session.Converter.Convert(c.Amount, c.Currency, _session.DisplayCurrency, _session.Now);
                var display = !conv.Ok ? "no rate"
                    : conv.Path == ConversionPath.Identity ? ""
                    : $"= {ValueFormat.Money(conv.Value, conv.Denomination)}";
                return new ComparableViewModel(c, display, _session.IsSeeded(c.Id), _session.Now);
            })
            .OrderBy(c => c.Comparable.InspectedAt)
            .ToList();
        UpdateAnswer();
    }

    /// <summary>The search changed: the link and the copied filters follow at once; the price and listings
    /// stay as they were, marked old, until the player clicks Search.</summary>
    private void OnSearchChanged()
    {
        OnCount = SearchRows.Count(r => r.IsOn) + (IsUnique ? 1 : 0);
        Trade = TradeQuery.Build(Worksheet, Item, _session.TradeLeague, _session.Trade);
        Instructions = $"Trade site link: {Trade.Url}{Environment.NewLine}{Environment.NewLine}{Worksheet.ToManualInstructions(_session.TradeLeague)}";
        SearchStatus = null;
        _currentKey = SearchKey();
        SearchNote = null;
        NotifySearchState();
    }

    /// <summary>Everything the search is made of, so a result can tell whether it is still the current one
    /// (ticking a stat off and on again makes it current again).</summary>
    private string SearchKey() => string.Join("|",
        Trade?.Json ?? "",
        Worksheet.BuyNowOnly,
        Worksheet.Corrupted,
        Worksheet.MinItemLevel,
        string.Join(";", Worksheet.Rows.Where(r => r.Selected).Select(r => $"{r.Id}:{r.Min}:{r.Max}:{r.Match}:{r.Group}")));

    private void NotifySearchState()
    {
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(IsDimmed));
        OnPropertyChanged(nameof(PeekLine));
        OnPropertyChanged(nameof(PeekLineIsWarning));
        OnPropertyChanged(nameof(PeekShowsCount));
        OnPropertyChanged(nameof(NoteLine));
        OnPropertyChanged(nameof(NoteLineIsWarning));
        OnPropertyChanged(nameof(SearchLine));
        OnPropertyChanged(nameof(HasSearchLine));
        OnPropertyChanged(nameof(SearchLineIsWarning));
    }

    /// <summary>Search: the practice items search their synthetic listings at once; a live item asks the
    /// trade site (one search, one fetch of the ten cheapest), only now, on this click. While the trade site
    /// asks to wait, or cannot be reached, the last result stays and the line under it says why.</summary>
    [RelayCommand]
    private async Task Search()
    {
        if (!CanSearch)
        {
            return;
        }

        if (_listings is not null)
        {
            RunDemoSearch();
            SearchNote = null;
            UpdateAnswer();
            NotifySearchState();
            return;
        }

        if (_session.IsDemo)
        {
            Say(NeedsLiveLeague, warning: true);
            return;
        }

        if (_site is not { IsOn: true } site)
        {
            Say(SearchOffText, warning: false);
            return;
        }

        var trade = Trade!;
        if (!trade.Identifies)
        {
            var missing = trade.ByHand.Where(b => b.StartsWith("Name", StringComparison.Ordinal) || b.StartsWith("Unique name", StringComparison.Ordinal) || b.StartsWith("Item category", StringComparison.Ordinal));
            Say($"The trade site's filters don't know this item ({string.Join(", ", missing)}), so SSE doesn't search for it. Open the search on the trade site and set that by hand.", warning: true);
            return;
        }

        var key = _currentKey;
        var rows = SearchRows.Where(r => r.IsOn).ToList();
        var buyNow = Worksheet.BuyNowOnly;
        SearchNote = null;
        IsSearching = true;
        try
        {
            ApplyPage(await site.SearchAsync(trade.League, trade.Json, buyNow), key, rows);
        }
        catch (ProviderHoldException e)
        {
            Say(TradeClient.HoldText(e.Decision, _session.Now), warning: true);
        }
        catch (TradeSiteException e) when (e.StatusCode == HttpStatusCode.TooManyRequests)
        {
            Say(e.RetryAfter is { } wait ? $"The trade site asks to wait {TradeClient.Wait(wait)}." : "The trade site asks SSE to slow down. Try again in a minute.", warning: true);
        }
        catch (TradeSiteException e)
        {
            Say(e.SiteMessage is { } why ? $"The trade site turned this search down: {why.TrimEnd('.')}." : $"The trade site answered {(int)e.StatusCode!}. The last result stays.", warning: true);
        }
        catch (HttpRequestException)
        {
            Say("Couldn't reach the trade site. The last result stays.", warning: true);
        }
        catch (TaskCanceledException)
        {
            Say("The trade site took too long to answer. The last result stays.", warning: true);
        }
        catch (JsonException)
        {
            Say("The trade site's answer could not be read; the site may have changed. The last result stays.", warning: true);
        }
        finally
        {
            IsSearching = false;
            NotifySearchState();
        }
    }

    private void Say(string text, bool warning)
    {
        SearchNoteIsWarning = warning;
        SearchNote = text;
    }

    /// <summary>A trade site answer for the search <paramref name="key"/>, priced with this session's rates.</summary>
    private void ApplyPage(TradePage page, string key, IReadOnlyList<SearchRowViewModel> rows)
    {
        _page = page;
        var listings = page.Listings.Select(l => MarketSearch.FromTradeSite(l, Worksheet, _session.Trade)).ToList();
        _result = MarketSearch.Summarize(listings, page.Total, _session.Converter, _session.DisplayCurrency, _session.Now);
        _resultKey = key;
        _resultRows = rows;
        UpdateAnswer();
        NotifySearchState();
    }

    /// <summary>After a session change in the same league (an hourly price refresh, another display
    /// currency): the player's search and the last trade site answer carry over to the same item, re-priced
    /// with the new rates, so the site is not asked again.</summary>
    public void CarryOver(ItemViewModel old)
    {
        ArgumentNullException.ThrowIfNull(old);
        if (_session.IsDemo || old._page is not { } page || old._resultKey is not { } key
            || !string.Equals(old._session.League, _session.League, StringComparison.Ordinal) || old.SetKey != SetKey)
        {
            return;
        }

        Worksheet.CopySearchFrom(old.Worksheet);
        Strictness = old.Strictness;
        foreach (var r in SearchRows)
        {
            r.Refresh();
        }

        OnSearchChoicesChanged();
        OnSearchChanged();
        var searched = old._resultRows.Select(r => r.Row.Id).ToHashSet(StringComparer.Ordinal);
        ApplyPage(page, key, SearchRows.Where(r => searched.Contains(r.Row.Id)).ToList());
    }

    /// <summary>The demo's synthetic market searched with the worksheet as it stands.</summary>
    private void RunDemoSearch()
    {
        _result = MarketSearch.Run(Worksheet, _listings!, _session.Converter, _session.DisplayCurrency, _session.Now);
        _resultKey = _currentKey;
        _resultRows = SearchRows.Where(r => r.IsOn).ToList();
    }

    private void UpdateAnswer()
    {
        if (Price is null)
        {
            return;
        }

        var price = _session.PriceText();
        Matches = [];
        NoMatches = false;

        if (IsUnidentified)
        {
            Answer = PriceAnswer.Unidentified();
        }
        else if (_result is { } result)
        {
            Answer = PriceAnswer.FromSearch(result, price, IsUnique);
            Matches = result.Matches.Take(ListingRows).Select(m => ToMatch(m, price)).ToList();
            NoMatches = result.Confidence == SearchConfidence.NoMatches;
        }
        else if (Price.Evidence is { } evidence && PriceAnswer.FromWrittenDown(evidence, price) is { } written)
        {
            Answer = written;
        }
        else if (!_session.HasPricesFor(Item))
        {
            Answer = PriceAnswer.PracticeOnly(ShowsWorksheet, ShowsComparables);
        }
        else if (Price.Kind == PriceKind.Commodity || Item.Rarity == ItemRarity.Currency)
        {
            Answer = PriceAnswer.Stack(Item, ItemPricer.Identify(Item, _session.Book), Price, _session.Converter, price, _session.Now);
        }
        else if (Price.Kind == PriceKind.UniqueBaseline)
        {
            Answer = PriceAnswer.Baseline(Price, _session.IsDemo ? null : _session.Now, price);
        }
        else if (ShowsWorksheet && LiveSearchOn)
        {
            Answer = PriceAnswer.SearchFirst(ShowsComparables);
        }
        else
        {
            Answer = PriceAnswer.NoPrice(_session.Book.Quotes.Count == 0, ShowsWorksheet, ShowsComparables);
        }
    }

    /// <summary>One listing as a row: its price in the display currency and the listed item's numbers for
    /// the stats the search used (up to three), e.g. "Phys DPS 341 · Attack speed 17". A unique searched by
    /// name alone shows its first rolls instead.</summary>
    private MatchViewModel ToMatch(PricedListing m, PriceText price)
    {
        IEnumerable<SearchRowViewModel> rows = _resultRows.Count > 0 ? _resultRows : IsUnique ? SearchRows : [];
        var shown = rows
            .Select(r => m.Listing.Stats.TryGetValue(r.Row.Id, out var v) ? Labels.WithValue(r.Row, v) : null)
            .Where(s => s is not null)
            .Take(3)
            .ToList();
        var stats = shown.Count > 0 ? string.Join(" · ", shown) : IsUnique ? Title : $"Any {Labels.Singular(Item.ItemClass)}";
        var age = m.Listing.HasListedAt ? ValueFormat.Age(_session.Now - m.Listing.ListedAt) : "";
        return new MatchViewModel(price.One(m.Value), stats, m.Listing.BuyNow, age, price.Tier(m.Value));
    }

    [RelayCommand]
    private void SetStrictness(string key)
    {
        if (StrictnessChoices.FirstOrDefault(c => c.Key == key) is not { } choice)
        {
            return;
        }

        Strictness = key;
        Worksheet.SetStartingBounds(choice.Tolerance);
        foreach (var r in SearchRows)
        {
            r.Refresh();
        }

        OnSearchChanged();
    }

    /// <summary>Back to the search the app started with for this item.</summary>
    [RelayCommand]
    private void Reset()
    {
        Worksheet.CopySearchFrom(NewWorksheet());
        Strictness = DefaultStrictness;
        foreach (var r in SearchRows)
        {
            r.Refresh();
        }

        OnSearchChoicesChanged();
        OnSearchChanged();
    }

    /// <summary>The worksheet's own choices were replaced at once (reset, carried over): the controls re-read them.</summary>
    private void OnSearchChoicesChanged()
    {
        OnPropertyChanged(nameof(BuyNowOnly));
        OnPropertyChanged(nameof(CorruptedOnly));
        OnPropertyChanged(nameof(CorruptedTip));
    }

    /// <summary>Opens this search on the official trade site in the default browser, only on a click.
    /// SSE itself sends nothing to the site: the browser opens the link.</summary>
    [RelayCommand]
    private void OpenTrade()
    {
        try
        {
            Process.Start(new ProcessStartInfo(TradeUrl) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            SearchStatus = "Windows couldn't open your browser. Copy search has the link.";
        }
    }

    [RelayCommand]
    private void CopySearch()
    {
        try
        {
            Clipboard.SetText(Instructions);
            SearchStatus = "Search copied: the trade site link, then the same filters as text.";
        }
        catch (ExternalException)
        {
            SearchStatus = "The clipboard is busy. Try again.";
        }
    }

    private ComparisonWorksheet NewWorksheet()
    {
        var w = WorksheetFactory.Create(Reading, trade: _session.Trade);
        w.ListingDenomination = _session.ListingDenomination;
        return w;
    }

    private LineReading? ReadingFor(WorksheetRow row) =>
        Reading.Lines.FirstOrDefault(l => l.Line.Template == row.Template && Labels.KindOf(l.Source) == row.Kind);
}

/// <summary>Short words for lists: stat names in the cheapest-matches list, item kinds in the session list.</summary>
public static class Labels
{
    private static readonly Dictionary<string, string> ShortStats = new(StringComparer.Ordinal)
    {
        [ComparisonWorksheet.PhysicalDpsId] = "Phys DPS",
        [ComparisonWorksheet.LocalPhysPercent] = "% Phys",
        [ComparisonWorksheet.LocalFlatPhys] = "Added phys",
        [ComparisonWorksheet.LocalAttackSpeed] = "Attack speed",
        ["#% increased Critical Hit Chance"] = "Crit",
        ["# to Accuracy Rating"] = "Accuracy",
        ["#% to Fire Resistance"] = "Fire res",
        ["#% to Cold Resistance"] = "Cold res",
        ["#% to Lightning Resistance"] = "Lightning res",
        ["#% to Chaos Resistance"] = "Chaos res",
        ["#% increased Movement Speed"] = "Move speed",
        ["# to maximum Life"] = "Life",
        ["# to maximum Mana"] = "Mana",
        ["# to Strength"] = "Str",
        ["# to Dexterity"] = "Dex",
        ["# to Intelligence"] = "Int",
    };

    public static string Short(WorksheetRow row)
    {
        if (ShortStats.TryGetValue(row.Id, out var s) || ShortStats.TryGetValue(row.Template, out s))
        {
            return s;
        }

        var plain = row.Template.Replace("#", "").Replace("%", "").Replace("+", "").Replace(" increased ", " ").Trim();
        plain = plain.StartsWith("to ", StringComparison.Ordinal) ? plain[3..] : plain;
        plain = string.Join(' ', plain.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return plain.Length > 18 ? plain[..17].TrimEnd() + "…" : plain;
    }

    /// <summary>A listed item's number for a stat, short: "Phys DPS 341", "3 Charm Slots", "Life 85",
    /// "14% Rarity of Items found".</summary>
    public static string WithValue(WorksheetRow row, decimal value)
    {
        var number = RollQuality.Num(Math.Round(value, Math.Abs(value) >= 100 ? 0 : 1));
        if (!ShortStats.ContainsKey(row.Id) && !ShortStats.ContainsKey(row.Template) && row.Template.Count(c => c == '#') == 1)
        {
            var filled = row.Template.Replace("#", number, StringComparison.Ordinal).Replace(" increased ", " ", StringComparison.Ordinal);
            filled = filled.StartsWith("Has ", StringComparison.Ordinal) ? filled[4..] : filled;
            if (filled.Length <= 26)
            {
                return filled;
            }
        }

        return $"{Short(row)} {number}";
    }

    /// <summary>"Rare crossbow", "Unique ring", "Currency".</summary>
    public static string Kind(ItemSnapshot item) => item.Rarity switch
    {
        ItemRarity.Currency => "Currency",
        ItemRarity.Gem => "Gem",
        ItemRarity.Unknown or ItemRarity.Other => Capitalize(Singular(item.ItemClass)),
        var r => $"{r} {Singular(item.ItemClass)}",
    };

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>"Crossbows" → "crossbow", "Quarterstaves" → "quarterstaff"; boots and gloves stay plural.</summary>
    public static string Singular(string itemClass)
    {
        var c = itemClass.Trim().ToLowerInvariant();
        return c switch
        {
            "" => "item",
            "boots" or "gloves" => c,
            _ when c.EndsWith("staves", StringComparison.Ordinal) => c[..^6] + "staff",
            _ when c.EndsWith('s') => c[..^1],
            _ => c,
        };
    }

    public static RowKind KindOf(ModSource source) => source switch
    {
        ModSource.Implicit => RowKind.Implicit,
        ModSource.Rune => RowKind.Rune,
        ModSource.Enchant => RowKind.Enchant,
        ModSource.Explicit or ModSource.Fractured or ModSource.Desecrated or ModSource.Crafted or ModSource.Unique => RowKind.Explicit,
        _ => RowKind.Other,
    };
}
