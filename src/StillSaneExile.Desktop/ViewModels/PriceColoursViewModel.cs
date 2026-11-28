using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>One colour of the price label in Settings: its name drawn in its own colours, and where it starts.</summary>
public sealed partial class ColourStepViewModel : ObservableObject
{
    private readonly Action _changed;

    public ColourStepViewModel(ValueTier tier, string name, Action changed)
    {
        Tier = tier;
        Name = name;
        _changed = changed;
    }

    public ValueTier Tier { get; }

    /// <summary>"White and red", "Red", …: the label's own colours, named.</summary>
    public string Name { get; }

    /// <summary>The start as typed: a number, in <see cref="Currency"/>.</summary>
    [ObservableProperty]
    public partial string Amount { get; set; } = "";

    /// <summary>"divine" or "exalted".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InDivine), nameof(InExalted))]
    public partial string Currency { get; set; } = "divine";

    /// <summary>This step is the one that does not start below the step above it, or its number can't be read.</summary>
    [ObservableProperty]
    public partial bool IsWrong { get; set; }

    public bool InDivine => Currency == "divine";
    public bool InExalted => Currency == "exalted";

    public string AmountName => $"Where {Name.ToLowerInvariant()} starts";
    public string DivineName => $"{Name} in Divine Orbs";
    public string ExaltedName => $"{Name} in Exalted Orbs";

    /// <summary>The start, or null while the box holds no number above zero.</summary>
    public ValueStep? Step =>
        decimal.TryParse(Amount.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) && v > 0 && v <= ValueLadder.MaxAmount
            ? new ValueStep(v, Currency)
            : null;

    public void Show(ValueStep step)
    {
        Amount = step.Amount.ToString("0.##", CultureInfo.InvariantCulture);
        Currency = step.InDivine ? "divine" : "exalted";
    }

    /// <summary>What a screen reader says for the row (WPF reads a list row through ToString).</summary>
    public override string ToString() => Step is { } s ? $"{Name} from {s}" : Name;

    partial void OnAmountChanged(string value) => _changed();

    partial void OnCurrencyChanged(string value) => _changed();

    [RelayCommand]
    private void SetCurrency(string currency) => Currency = currency;
}

/// <summary>One made-up item in the preview, with its made-up price drawn in the draft's colours.</summary>
public sealed record ColourExampleViewModel(string Name, string Base, ItemRarity Rarity, string Price, ValueTier? Tier)
{
    /// <summary>What a screen reader says for the row (WPF reads a list row through ToString).</summary>
    public override string ToString() => $"{Name}, {Price}";
}

/// <summary>Settings, Price colours: where each colour of the price label starts. The rows are a draft: the
/// preview of example items follows every change at once, and nothing else does until Apply saves it, when
/// every open card and price check is coloured again.</summary>
public sealed partial class PriceColoursViewModel : ObservableObject
{
    /// <summary>Made-up items at made-up prices across the whole ladder, so moving a step shows on some of them.</summary>
    private static readonly (string Name, string Base, ItemRarity Rarity, decimal Amount, string Currency)[] Samples =
    [
        ("Sanity's Last Loop", "Gold Ring", ItemRarity.Unique, 25m, "divine"),
        ("Doom Thirst", "Siege Crossbow", ItemRarity.Rare, 4m, "divine"),
        ("Storm Mark", "Siege Crossbow", ItemRarity.Rare, 70m, "exalted"),
        ("Gale Stride", "Dragonscale Boots", ItemRarity.Rare, 12m, "exalted"),
        ("Merciless Siege Crossbow", "Magic crossbow", ItemRarity.Magic, 3m, "exalted"),
        ("Siege Crossbow", "Crafting base", ItemRarity.Normal, 0.4m, "exalted"),
    ];

    private readonly ShellViewModel _shell;
    private bool _loading;

    public PriceColoursViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Steps =
        [
            new(ValueTier.S, "White and red", Changed),
            new(ValueTier.A, "Red", Changed),
            new(ValueTier.B, "Orange", Changed),
            new(ValueTier.C, "Amber", Changed),
            new(ValueTier.D, "Tan", Changed),
        ];
        Load();
    }

    /// <summary>The five steps with a start of their own, loudest first; the dark tag is everything under the last.</summary>
    public IReadOnlyList<ColourStepViewModel> Steps { get; }

    /// <summary>The ladder as saved: what the cards use now.</summary>
    public ValueLadder Saved => _shell.Settings.PriceColours;

    /// <summary>The rows as a ladder, or null while one holds no number.</summary>
    public ValueLadder? Draft => Steps.Select(s => s.Step).ToList() is var steps && steps.All(s => s is not null)
        ? new ValueLadder(steps[0]!, steps[1]!, steps[2]!, steps[3]!, steps[4]!)
        : null;

    /// <summary>"Under 1 ex": where the dark tag starts.</summary>
    public string DarkFrom => Steps[^1].Step is { } d ? $"under {d}" : "under tan";

    /// <summary>Why Apply is off, in one line; null when the draft is fine.</summary>
    public string? Problem
    {
        get
        {
            if (Steps.FirstOrDefault(s => s.Step is null) is { } empty)
            {
                return $"Type a number above 0 for {empty.Name.ToLowerInvariant()}.";
            }

            if (Draft!.OutOfOrder(Rate) is { } tier)
            {
                var i = Steps.ToList().FindIndex(s => s.Tier == tier);
                return $"{Steps[i].Name} has to start below {Steps[i - 1].Name.ToLowerInvariant()}.";
            }

            return null;
        }
    }

    public bool HasProblem => Problem is not null;

    public bool IsChanged => Draft is not { } d || d != Saved;

    public bool CanApply => Problem is null && IsChanged;

    public bool IsDefault => Draft == ValueLadder.Default;

    public bool CanUseDefault => !IsDefault;

    /// <summary>Exalted Orbs to one Divine Orb at the session's prices, or null when there is no rate yet.</summary>
    private decimal? Rate => _shell.Session?.PriceText().ExaltedPerDivine();

    /// <summary>"At today's rate, 1 div = 120 ex.", or why the preview has no colours.</summary>
    public string RateLine => Rate is { } r
        ? $"At today's rate, 1 div = {ValueFormat.Amount(Math.Round(r))} ex."
        : "No Divine to Exalted rate yet: the preview has colours once prices are loaded.";

    /// <summary>The example items, coloured by the draft (or by the saved steps while the draft is wrong).</summary>
    public IReadOnlyList<ColourExampleViewModel> Examples
    {
        get
        {
            var ladder = Problem is null ? Draft! : Saved;
            var rate = Rate;
            return Samples.Select(s =>
            {
                ValueTier? tier = rate is { } r
                    ? ladder.Of(s.Currency == "divine" ? s.Amount : s.Amount / r, s.Currency == "divine" ? s.Amount * r : s.Amount)
                    : null;
                return new ColourExampleViewModel(s.Name, s.Base, s.Rarity, ValueFormat.Money(s.Amount, s.Currency), tier);
            }).ToList();
        }
    }

    /// <summary>The rows back to the saved steps (on opening Settings, after Apply, and on Undo).</summary>
    public void Load() => Show(Saved);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        _shell.Settings.PriceColours = Draft!;
        _shell.ReloadSession(); // every open card and price check is priced again, in the new colours
        Load();
    }

    [RelayCommand(CanExecute = nameof(IsChanged))]
    private void Undo() => Load();

    /// <summary>The default steps in the rows; Apply still decides.</summary>
    [RelayCommand(CanExecute = nameof(CanUseDefault))]
    private void UseDefault() => Show(ValueLadder.Default);

    private void Show(ValueLadder ladder)
    {
        _loading = true;
        try
        {
            foreach (var s in Steps)
            {
                s.Show(ladder.Start(s.Tier));
            }
        }
        finally
        {
            _loading = false;
        }

        Changed();
    }

    private void Changed()
    {
        if (_loading)
        {
            return;
        }

        var wrong = Steps.FirstOrDefault(s => s.Step is null)?.Tier ?? Draft?.OutOfOrder(Rate);
        foreach (var s in Steps)
        {
            s.IsWrong = s.Tier == wrong;
        }

        OnPropertyChanged(nameof(Draft));
        OnPropertyChanged(nameof(DarkFrom));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(IsDefault));
        OnPropertyChanged(nameof(CanUseDefault));
        OnPropertyChanged(nameof(RateLine));
        OnPropertyChanged(nameof(Examples));
        ApplyCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        UseDefaultCommand.NotifyCanExecuteChanged();
    }
}
