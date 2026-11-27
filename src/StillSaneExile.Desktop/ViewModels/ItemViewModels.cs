using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Reading;
using StillSaneExile.Domain.Rolls;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>One roll bar: numbers, endpoints and tier are always printed, so color is optional.</summary>
public sealed class RollBarViewModel
{
    public RollBarViewModel(RollAssessment a)
    {
        Assessment = a;
        var fill = (double)(a.Fill ?? 0m);
        var pos = (double)(a.Position ?? 0m);
        FillWidth = new GridLength(Math.Max(fill, 0.0001), GridUnitType.Star);
        RestWidth = new GridLength(Math.Max(1 - fill, 0.0001), GridUnitType.Star);
        MarkerBefore = new GridLength(Math.Max(pos, 0.0001), GridUnitType.Star);
        MarkerAfter = new GridLength(Math.Max(1 - pos, 0.0001), GridUnitType.Star);
    }

    public RollAssessment Assessment { get; }
    public bool HasBar => Assessment.HasBar;
    public bool HasAnything => HasBar || Caption.Length > 0;
    public bool IsScored => Assessment.Status == RollStatus.Scored;
    public bool IsPositionOnly => Assessment.Status == RollStatus.PositionOnly;
    public GridLength FillWidth { get; }
    public GridLength RestWidth { get; }
    public GridLength MarkerBefore { get; }
    public GridLength MarkerAfter { get; }

    public string Percent => IsScored ? RollQuality.Percent(Assessment.Fill) : IsPositionOnly ? "pos." : "";

    public string Caption => Assessment.Status switch
    {
        RollStatus.Scored => $"{RollQuality.Num(Assessment.Value)} in {RollQuality.Num(Assessment.Min)}–{RollQuality.Num(Assessment.Max)} · {RollQuality.Distance(Assessment)}",
        RollStatus.PositionOnly => $"{RollQuality.Num(Assessment.Value)} in {RollQuality.Num(Assessment.Min)}–{RollQuality.Num(Assessment.Max)} · direction not verified",
        RollStatus.Fixed => "Fixed",
        RollStatus.RangeMismatch => $"Range mismatch ({RollQuality.Num(Assessment.Min)}–{RollQuality.Num(Assessment.Max)})",
        _ => "",
    };
}

public sealed class ModRowViewModel
{
    public ModRowViewModel(LineReading l)
    {
        Reading = l;
        Bars = l.Rolls.Select(r => new RollBarViewModel(r)).ToList();
        var candidates = new List<string>();
        if (l.Status == LineStatus.Ambiguous)
        {
            candidates.AddRange(l.Candidates.Take(4).Select(c => c.Description));
            if (l.Candidates.Count > 4)
            {
                candidates.Add($"+{l.Candidates.Count - 4} more");
            }
        }

        Candidates = candidates;
    }

    public LineReading Reading { get; }
    public string Text => Reading.Line.Display;

    public string MetaLine => string.Join(" · ", new[] { SourceLabel, AffixName is null ? null : $"\"{AffixName}\"", StatusLabel }.Where(s => s is not null));
    public string Template => Reading.Line.Template;
    public ModSource Source => Reading.Source;
    public IReadOnlyList<RollBarViewModel> Bars { get; }
    public IReadOnlyList<string> Candidates { get; }
    public bool HasBars => Bars.Any(b => b.HasBar);
    public string? Note => Reading.Note;
    public bool HasNote => !string.IsNullOrEmpty(Reading.Note);
    public bool IsAmbiguous => Reading.Status == LineStatus.Ambiguous;

    /// <summary>Warning icon: anything a player should not take at face value.</summary>
    public bool Uncertain => Reading.Status is LineStatus.Ambiguous or LineStatus.RangeMismatch
        || Bars.Any(b => b.Assessment.Status == RollStatus.RangeMismatch) || Reading.RangeFromSyntheticPack;

    public string SourceLabel => Reading.Source switch
    {
        ModSource.Explicit when Reading.Group.Side == AffixSide.Prefix => "Prefix",
        ModSource.Explicit when Reading.Group.Side == AffixSide.Suffix => "Suffix",
        ModSource.Explicit => "Explicit",
        ModSource.Implicit => "Implicit",
        ModSource.Rune => "Rune",
        ModSource.Enchant => "Enchant",
        ModSource.Unique => "Unique",
        ModSource.Desecrated => "Desecrated",
        ModSource.Fractured => "Fractured",
        ModSource.Crafted => "Crafted",
        _ => "Unknown",
    };

    public string TierLabel => Reading.TierLabel;

    public string? AffixName => Reading.AffixName;

    public string StatusLabel => Reading.Status switch
    {
        LineStatus.FromAdvancedCopy => "range from game",
        LineStatus.Resolved => Reading.RangeFromSyntheticPack ? "synthetic pack range" : "pack range",
        LineStatus.Ambiguous => "ambiguous",
        LineStatus.RangeMismatch => "range mismatch",
        LineStatus.NotInPack => Reading.Line.Components.Count == 0 ? "no numbers" : "not in pack",
        _ => "no pack",
    };
}

public sealed partial class WorksheetRowViewModel : ObservableObject
{
    private readonly Action _changed;

    public WorksheetRowViewModel(WorksheetRow row, Action changed)
    {
        Row = row;
        _changed = changed;
        _min = Fmt(row.Min);
        _max = Fmt(row.Max);
    }

    public WorksheetRow Row { get; }
    public string Label => Row.Label;
    public string Actual => Row.Actual is { } a ? RollQuality.Num(a) : "";
    public string KindLabel => Row.Kind.ToString();
    public bool Supported => Row.Supported;

    public bool Selected
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

    private string _min;

    public string Min
    {
        get => _min;
        set
        {
            if (SetProperty(ref _min, value))
            {
                Row.Min = Parse(value);
                _changed();
            }
        }
    }

    private string _max;

    public string Max
    {
        get => _max;
        set
        {
            if (SetProperty(ref _max, value))
            {
                Row.Max = Parse(value);
                _changed();
            }
        }
    }

    public string MatchLabel => Row.Match switch { MatchType.Minimum => "min", MatchType.Maximum => "max", _ => "range" };

    public string GroupLabel => Row.Group == FilterGroup.Mandatory ? "AND" : "OPT";

    public void CycleMatch()
    {
        Row.Match = Row.Match switch { MatchType.Minimum => MatchType.Range, MatchType.Range => MatchType.Maximum, _ => MatchType.Minimum };
        OnPropertyChanged(nameof(MatchLabel));
        _changed();
    }

    public void ToggleGroup()
    {
        Row.Group = Row.Group == FilterGroup.Mandatory ? FilterGroup.Optional : FilterGroup.Mandatory;
        OnPropertyChanged(nameof(GroupLabel));
        _changed();
    }

    public void Refresh()
    {
        _min = Fmt(Row.Min);
        _max = Fmt(Row.Max);
        OnPropertyChanged(nameof(Min));
        OnPropertyChanged(nameof(Max));
        OnPropertyChanged(nameof(GroupLabel));
        OnPropertyChanged(nameof(Selected));
    }

    private static string Fmt(decimal? v) => v is null ? "" : RollQuality.Num(v);

    private static decimal? Parse(string s) =>
        decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
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
    public string Tags => string.Join(" · ", new[] { Seeded ? "demo seed" : "player-entered", Expired ? "expired" : null, Comparable.ListingLink is null && Comparable.ListingId is null ? "no link" : null }.Where(t => t is not null));
}

/// <summary>Everything the inspector shows for one item snapshot.</summary>
public sealed partial class ItemViewModel : ObservableObject
{
    private readonly Session _session;

    public ItemViewModel(ItemSnapshot item, Session session)
    {
        Item = item;
        _session = session;
        Reading = ItemReader.Read(item, session.Pack);
        Rows = Reading.Lines.Select(l => new ModRowViewModel(l)).ToList();
        Worksheet = WorksheetFactory.Create(Reading);
        Worksheet.ListingDenomination = session.ListingDenomination;
        WorksheetRows = new ObservableCollection<WorksheetRowViewModel>(Worksheet.Rows.Select(r => new WorksheetRowViewModel(r, OnWorksheetChanged)));
        SetKey = ComparisonSet.KeyFor(item);
        RefreshPrice();
        OnWorksheetChanged();
    }

    public ItemSnapshot Item { get; }
    public ItemReading Reading { get; }
    public IReadOnlyList<ModRowViewModel> Rows { get; }
    public ComparisonWorksheet Worksheet { get; }
    public ObservableCollection<WorksheetRowViewModel> WorksheetRows { get; }
    public string SetKey { get; }

    public string Title => Item.Name ?? Item.BaseType;
    public string? BaseLine => Item.Name is null ? null : Item.BaseType;
    public string Rarity => Item.Rarity.ToString();
    public string ClassLine => string.Join(" · ", new[]
    {
        Item.ItemClass,
        Item.ItemLevel is { } il ? $"Item level {il}" : null,
        Item.Requirements.Level is { } rl ? $"Requires level {rl}" : null,
        Item.Quality is { } q ? $"Quality {q}%" : null,
    }.Where(s => s is not null));

    public string CopyLabel => Item.Copy == CopyKind.Advanced ? "Advanced copy: tiers and ranges from the game" : "Normal copy: ranges only where the pack has exactly one explanation";
    public string StatesLabel => Item.States == ItemStates.None ? "" : Item.States.ToString();
    public string? PhysicalDps => Reading.PhysicalDps is { } p ? RollQuality.Num(Math.Round(p, 1)) : null;
    public IEnumerable<KeyValuePair<string, string>> Properties => Item.Properties.Where(p => p.Key is not ("Requires" or "Item Level" or "Quality"));
    public IReadOnlyList<string> FlavourText => Item.FlavourText;
    public IReadOnlyList<string> Unparsed => Item.UnparsedLines;
    public bool HasAmbiguity => Reading.HasAmbiguity;
    public string CapturedAt => Item.CapturedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    [ObservableProperty]
    public partial PriceLine? Price { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ComparableViewModel> Comparables { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> PriceWarnings { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> WorksheetWarnings { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<RelaxationSuggestion> Relaxations { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> MarketControls { get; set; } = [];

    [ObservableProperty]
    public partial string Instructions { get; set; } = "";

    public bool ShowsComparables => Item.Rarity is ItemRarity.Rare or ItemRarity.Magic or ItemRarity.Normal;

    public bool ShowsWorksheet => Item.Rarity is not (ItemRarity.Currency or ItemRarity.Gem);

    public bool HasRows => Rows.Count > 0;

    /// <summary>Up to four rows for the HUD: worksheet preset stats first, then rows with bars.</summary>
    public IReadOnlyList<ModRowViewModel> PeekRows
    {
        get
        {
            var preset = WorksheetFactory.PresetFor(Item.ItemClass);
            var explicitRows = Rows.Where(r => r.Source is not (ModSource.Implicit or ModSource.Rune or ModSource.Enchant) && r.Reading.Line.Components.Count > 0).ToList();
            var pool = explicitRows.Count > 0 ? explicitRows : Rows.Where(r => r.Reading.Line.Components.Count > 0).ToList();
            return pool
                .OrderByDescending(r => preset.Contains(r.Template))
                .ThenByDescending(r => r.HasBars)
                .Take(4)
                .ToList();
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
    }

    public void OnWorksheetChanged()
    {
        WorksheetWarnings = Worksheet.Warnings().ToList();
        Relaxations = Worksheet.SuggestRelaxations();
        MarketControls = Worksheet.MarketControls().ToList();
        Instructions = Worksheet.ToManualInstructions(_session.League);
    }

    public void ApplyRelaxation(RelaxationSuggestion s)
    {
        s.Apply(Worksheet);
        foreach (var r in WorksheetRows)
        {
            r.Refresh();
        }

        OnPropertyChanged(nameof(Worksheet));
        OnWorksheetChanged();
    }
}
