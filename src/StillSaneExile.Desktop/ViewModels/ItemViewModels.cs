using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Reading;
using StillSaneExile.Domain.Rolls;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>One stat in the search: tick it to search for it, and set the number to search for.</summary>
public sealed partial class SearchRowViewModel : ObservableObject
{
    public const string AmbiguousHint = "Can’t tell which stat this is — Ctrl+Alt+C shows it";

    private readonly Action _changed;

    public SearchRowViewModel(WorksheetRow row, LineReading? reading, Action changed)
    {
        Row = row;
        Reading = reading;
        _changed = changed;
    }

    public WorksheetRow Row { get; }
    public LineReading? Reading { get; }

    public string Text => Row.Kind == RowKind.Derived && Row.Actual is { } a ? $"{Row.Label} {RollQuality.Num(a)}" : Row.Label;
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
    public override string ToString() => Text;

    public string Tier => Reading?.Tier is { } t ? $"T{t}" : Row.Kind switch
    {
        RowKind.Rune => "Rune",
        RowKind.Implicit => "Implicit",
        RowKind.Enchant => "Enchant",
        _ => "—",
    };

    /// <summary>What the peek card shows on the right of an unticked row: the tier, or nothing.</summary>
    public string TierOrBlank => Tier == "—" ? "" : Tier;

    public string? Hint =>
        IsAmbiguous ? AmbiguousHint
        : Row.Id == ComparisonWorksheet.PhysicalDpsId ? "Damage per second from physical hits. Best single number for a weapon."
        : Row.Kind == RowKind.Rune ? "From a rune — buyers rarely care"
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
        if (l.RangeFromSyntheticPack)
        {
            Note += " Demo range, not game data.";
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
            LineStatus.Resolved => l.RangeFromSyntheticPack ? "range from demo data" : "range from data pack",
            LineStatus.Ambiguous => "more than one explanation",
            LineStatus.RangeMismatch => "range mismatch",
            LineStatus.NotInPack => "not in data pack",
            _ => "no data pack",
        };
        return string.Join(" · ", new[] { source, l.AffixName is null ? null : $"\"{l.AffixName}\"", from }.Where(s => s is not null));
    }
}

/// <summary>One of the cheapest listings that match the search.</summary>
public sealed record MatchViewModel(string Price, string Stats, string How, string Age)
{
    /// <summary>What a screen reader says for the row (WPF reads a list row through ToString).</summary>
    public override string ToString() => $"{Price}, {Stats}, {How}, {Age}";
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
public sealed partial class ItemViewModel : ObservableObject
{
    public static readonly IReadOnlyList<StrictnessChoice> StrictnessChoices =
    [
        new("exact", "My exact rolls", 0m),
        new("10", "10% lower", 0.1m),
        new("20", "20% lower", 0.2m),
    ];

    private const string DefaultStrictness = "10";
    private readonly Session _session;
    private readonly IReadOnlyList<Listing>? _listings;

    public ItemViewModel(ItemSnapshot item, Session session)
    {
        Item = item;
        _session = session;
        Reading = ItemReader.Read(item, session.Pack);
        Worksheet = NewWorksheet();
        SearchRows = new ObservableCollection<SearchRowViewModel>(Worksheet.Rows.Select(r => new SearchRowViewModel(r, ReadingFor(r), OnSearchChanged)));
        RollRows = Reading.Lines
            .Where(l => l.Line.Components.Count > 0 && (l.Source is not (ModSource.Implicit or ModSource.Rune or ModSource.Enchant) || l.Rolls.Any(r => r.HasBar)))
            .Select(l => new RollRowViewModel(l, item.Copy == CopyKind.Advanced))
            .ToList();
        SetKey = ComparisonSet.KeyFor(item);
        _listings = ShowsWorksheet ? session.Listings(item, Worksheet) : null;
        RefreshPrice();
        OnSearchChanged();
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

    /// <summary>"Expert Bombard Crossbow · Item level 82", "Stack of 7".</summary>
    public string Subtitle => string.Join(" · ", new[]
    {
        Item.Name is null ? null : Item.BaseType,
        Item.Rarity == ItemRarity.Unique ? "Unique" : null,
        Item.StackSize is { } s ? $"Stack of {s}" : null,
        Item.ItemLevel is { } il && Item.Rarity is not ItemRarity.Currency ? $"Item level {il}" : null,
    }.Where(s => s is not null));

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

    /// <summary>Whether this profile has listings to search (the demo does; a live profile not yet).</summary>
    public bool HasListings => _listings is not null;

    [ObservableProperty]
    public partial PriceLine? Price { get; set; }

    [ObservableProperty]
    public partial PriceAnswer Answer { get; set; } = PriceAnswer.NoPrice(noMarketData: true);

    [ObservableProperty]
    public partial IReadOnlyList<MatchViewModel> Matches { get; set; } = [];

    [ObservableProperty]
    public partial bool NoMatches { get; set; }

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

    public bool HasItemLevel => Worksheet.SuggestedItemLevel is not null;

    public string ItemLevelLabel => $"Item level {Worksheet.SuggestedItemLevel} or higher";

    public bool UseItemLevel
    {
        get => Worksheet.MinItemLevel is not null;
        set
        {
            if (UseItemLevel != value)
            {
                Worksheet.MinItemLevel = value ? Worksheet.SuggestedItemLevel : null;
                OnPropertyChanged();
                OnSearchChanged();
            }
        }
    }

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

    private void OnSearchChanged()
    {
        OnCount = SearchRows.Count(r => r.IsOn) + (IsUnique ? 1 : 0);
        Trade = TradeQuery.Build(Worksheet, Item, _session.TradeLeague, _session.Trade);
        Instructions = $"Trade site link: {Trade.Url}{Environment.NewLine}{Environment.NewLine}{Worksheet.ToManualInstructions(_session.TradeLeague)}";
        SearchStatus = null;
        UpdateAnswer();
    }

    private void UpdateAnswer()
    {
        if (Price is null)
        {
            return;
        }

        var price = new PriceText(_session.Converter, _session.DisplayCurrency, _session.Now);
        Matches = [];
        NoMatches = false;

        if (Item.Rarity == ItemRarity.Rare && Item.States.HasFlag(ItemStates.Unidentified))
        {
            Answer = PriceAnswer.Unidentified();
        }
        else if (ShowsWorksheet && _listings is not null)
        {
            var result = MarketSearch.Run(Worksheet, _listings, _session.Converter, _session.DisplayCurrency, _session.Now);
            Answer = PriceAnswer.FromSearch(result, price, IsUnique);
            Matches = result.Matches.Take(5).Select(m => ToMatch(m, price)).ToList();
            NoMatches = result.Count == 0;
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
            Answer = PriceAnswer.Baseline(Price);
        }
        else
        {
            Answer = PriceAnswer.NoPrice(_session.Book.Quotes.Count == 0, ShowsWorksheet, ShowsComparables);
        }
    }

    private MatchViewModel ToMatch(PricedListing m, PriceText price)
    {
        var shown = SearchRows.Where(r => r.IsOn).Take(3)
            .Select(r => m.Listing.Stats.TryGetValue(r.Row.Id, out var v) ? $"{r.ShortLabel} {RollQuality.Num(v)}" : null)
            .Where(s => s is not null)
            .ToList();
        var stats = shown.Count > 0 ? string.Join(" · ", shown) : IsUnique ? Title : $"Any {Labels.Singular(Item.ItemClass)}";
        return new MatchViewModel(price.One(m.Value), stats, m.Listing.BuyNow ? "Buy now" : "Message seller", ValueFormat.Age(_session.Now - m.Listing.ListedAt));
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

        OnPropertyChanged(nameof(BuyNowOnly));
        OnPropertyChanged(nameof(UseItemLevel));
        OnSearchChanged();
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
        return plain.Length > 18 ? plain[..17].TrimEnd() + "…" : plain;
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
