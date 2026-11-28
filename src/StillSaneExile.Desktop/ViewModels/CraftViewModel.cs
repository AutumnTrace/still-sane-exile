using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Craft;
using StillSaneExile.Domain.Data;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>An orb or essence on the bench: its picture, whether it can be used on the item now (and why
/// not), how often it was used, and whether it is in hand.</summary>
public sealed partial class OrbViewModel(CraftCurrency currency, CraftViewModel bench) : ObservableObject
{
    public CraftCurrency Currency { get; } = currency;
    public string Id => Currency.Id;
    public string Name => Currency.Name;
    public string Short => Currency.Short;

    /// <summary>"Exalted Orb. Adds 1 stat to a Rare with room", with why it can't be used now.</summary>
    public string Tip => Why is null ? $"{Currency.Name}. {Currency.Tip}" : $"{Currency.Name}. {Currency.Tip}. {Why}";

    /// <summary>Why it can't be used on the item now; null when it can.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUse), nameof(Tip))]
    public partial string? Why { get; set; }

    public bool CanUse => Why is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCount))]
    public partial int Count { get; set; }

    public bool HasCount => Count > 0;

    /// <summary>Picked up: it follows the cursor until it is used on the item or put back.</summary>
    [ObservableProperty]
    public partial bool IsHeld { get; set; }

    /// <summary>Clicked: used at once, or picked up, as Settings says.</summary>
    [RelayCommand]
    private void Click() => bench.Click(this);

    /// <summary>What a screen reader says for the orb (WPF reads a list row through ToString).</summary>
    public override string ToString() => Name;
}

public sealed partial class OmenViewModel(OmenInfo info, Action changed) : ObservableObject
{
    public OmenInfo Info { get; } = info;
    public string Id => Info.Id;
    public string Title => Info.Title;
    public string Name => Info.Name;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [RelayCommand]
    private void Toggle()
    {
        IsOn = !IsOn;
        changed();
    }

    public override string ToString() => $"{Title}, {Name}";
}

/// <summary>One line on the simulated item: a stat with its tier, or an open slot.</summary>
public sealed record CraftRowViewModel(string Text, string Tier, string? Tag, bool IsFresh, bool IsOpen, bool IsEssence, bool IsFractured)
{
    public bool HasTag => Tag is not null;

    public override string ToString() => IsOpen ? Text : string.Join(", ", new[] { Text.Replace("\n", ", ", StringComparison.Ordinal), Tier, Tag }.Where(s => !string.IsNullOrEmpty(s)));
}

public sealed record CraftSideViewModel(string Title, string Count, IReadOnlyList<CraftRowViewModel> Rows)
{
    public override string ToString() => $"{Title}, {Count}";
}

/// <summary>A tier choice of a wanted stat: "T1", "T2+", "any".</summary>
public sealed record WantLevelViewModel(string Label, int Tier, bool IsChosen, string Name, ICommand Pick)
{
    public string Active => IsChosen ? "active" : "";

    public override string ToString() => Label;
}

public sealed record WantViewModel(string Label, string Status, bool IsMet, bool IsOnItem, IReadOnlyList<WantLevelViewModel> Levels, string RemoveName, ICommand Remove)
{
    public override string ToString() => $"{Label}, {Status}";
}

public sealed record OddsViewModel(string Label, string Chance, string Kind)
{
    public override string ToString() => $"{Label}: {Chance}, {Kind}";
}

/// <param name="wanted">Ticked to start with; changing the tick afterwards calls <paramref name="want"/>.</param>
public sealed partial class PoolRowViewModel(string label, string sub, string percent, bool canRoll, bool onItem, bool wanted, Action<bool> want) : ObservableObject
{
    private bool _wanted = wanted;

    public string Label { get; } = label;
    public string Sub { get; } = sub;
    public string Percent { get; } = percent;
    public bool CanRoll { get; } = canRoll;
    public bool OnItem { get; } = onItem;
    public string WantName => $"Want {Label}";

    /// <summary>Ticked: wanted, with any tier to start with.</summary>
    public bool IsWanted
    {
        get => _wanted;
        set
        {
            if (_wanted != value)
            {
                _wanted = value;
                OnPropertyChanged();
                want(value);
            }
        }
    }

    public override string ToString() => $"{Label}, {Percent}, {Sub}";
}

public sealed record PoolSideViewModel(string Title, IReadOnlyList<PoolRowViewModel> Rows)
{
    public override string ToString() => Title;
}

public sealed record HistoryRowViewModel(int Number, string Label, string Message, string BackName, ICommand Back)
{
    public override string ToString() => $"{Number}. {Label}: {Message}";
}

public sealed record GradeViewModel(OrbGrade Grade, string Label, string Tip, bool IsChosen, ICommand Pick)
{
    public string Active => IsChosen ? "active" : "";
    public string Name => $"{Label} orbs";

    public override string ToString() => Label;
}

/// <summary>The craft simulator: a made-up item on the bench, orbs and omens to use on it, what is wanted
/// and the odds of getting it, the stat pool and the history with what it cost. Practice only: it never
/// touches the game. It runs on a data pack with weights and a base; only the demo's made-up pack has them,
/// and the owner chose (28 Nov 2026) to offer it in every profile, labelled as made-up practice data: in a
/// live league the costs come from the league's real prices. The one exception to "a live profile never
/// sees made-up data" (AGENTS.md, rule 7).</summary>
public sealed partial class CraftViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly Func<bool> _shiftDown;
    private readonly List<Step> _history = [];
    private CraftEngine? _engine;
    private Session? _session;
    private CraftItem _item = CraftItem.Normal;
    private IReadOnlySet<int> _fresh = new HashSet<int>();
    private Dictionary<string, int> _wants = new(StringComparer.Ordinal);
    private Dictionary<string, int> _used = new(StringComparer.Ordinal);
    private decimal _spent;
    private int _unpriced;

    /// <param name="shiftDown">Whether Shift is held when the item is clicked: a held orb then stays in hand.</param>
    public CraftViewModel(ShellViewModel shell, Func<bool>? shiftDown = null)
    {
        _shell = shell;
        _shiftDown = shiftDown ?? (() => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        Load();
    }

    private sealed record Step(string Label, string Message, CraftItem Before, Dictionary<string, int> UsedBefore, decimal SpentBefore, int UnpricedBefore, HashSet<Omen> OmensBefore);

    public ObservableCollection<OrbGroupViewModel> Groups { get; } = [];
    public ObservableCollection<OmenViewModel> Omens { get; } = [];

    /// <summary>The demo's made-up pack is in use (the badge says so).</summary>
    public bool HasData => _engine is not null;

    public bool NoData => !HasData;

    public string NoDataText => "The simulator's stat data could not be loaded, so there is nothing to craft. Reinstalling SSE puts it back.";

    public string BaseName => _engine?.Base.Name ?? "";

    public string? Implicit => _engine?.Base.Implicit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemLevelNote))]
    public partial int ItemLevel { get; set; } = 82;

    public string ItemLevelNote => _engine is null ? ""
        : ItemLevel >= _engine.Families.SelectMany(f => f.Tiers).Max(t => t.Level) ? "every tier can roll"
        : "top tiers need a higher item level";

    [ObservableProperty]
    public partial OrbGrade Grade { get; set; }

    public IReadOnlyList<GradeViewModel> Grades =>
    [
        new(OrbGrade.Regular, "Regular", "Any stat level", Grade == OrbGrade.Regular, PickGradeCommand),
        new(OrbGrade.Greater, "Greater", GradeTip(OrbGrade.Greater), Grade == OrbGrade.Greater, PickGradeCommand),
        new(OrbGrade.Perfect, "Perfect", GradeTip(OrbGrade.Perfect), Grade == OrbGrade.Perfect, PickGradeCommand),
    ];

    /// <summary>The orb in hand (Settings, Crafting: like in game), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHolding), nameof(HeldPicture), nameof(UseOnItemName))]
    [NotifyCanExecuteChangedFor(nameof(UseHeldCommand), nameof(DropCommand))]
    public partial OrbViewModel? Held { get; set; }

    public bool IsHolding => Held is not null;

    public string? HeldPicture => Held?.Id;

    public string UseOnItemName => Held is { } h ? $"Use {h.Name} on the item" : "The item";

    /// <summary>The line over the item: what the last click did, or why it did nothing.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial bool MessageIsError { get; set; }

    [ObservableProperty]
    public partial string Tab { get; set; } = "want";

    // The item as drawn.
    public ItemRarity Rarity => _item.Rarity switch
    {
        CraftRarity.Magic => ItemRarity.Magic,
        CraftRarity.Rare => ItemRarity.Rare,
        _ => ItemRarity.Normal,
    };

    public string? ItemName => _item.Rarity == CraftRarity.Rare ? _item.Name : null;

    public bool HasItemName => ItemName is not null;

    public string BaseLine => _engine is null ? "" : _item.Rarity == CraftRarity.Magic ? _engine.MagicName(_item) : _engine.Base.Name;

    public CraftWeapon? Weapon => _engine?.Weapon(_item);

    public IReadOnlyList<CraftSideViewModel> Sides => _engine is null || _item.Rarity == CraftRarity.Normal ? [] :
    [
        Side(AffixSide.Prefix, "Prefixes"),
        Side(AffixSide.Suffix, "Suffixes"),
    ];

    public bool IsEmpty => _item.Rarity == CraftRarity.Normal;

    public bool IsCorrupted => _item.Corrupted;

    /// <summary>What a Vaal Orb added besides the word Corrupted: "Corruption enchantment (not simulated)", "Extra socket".</summary>
    public IReadOnlyList<string> CorruptionLines => new[] { _item.Enchanted ? "Corruption enchantment (not simulated)" : null, _item.ExtraSocket ? "Extra socket" : null }
        .OfType<string>().ToList();

    /// <summary>"Transmutation and Augmentation roll stats of level 44 and up; Regal, Exalted and Chaos 35 and up."</summary>
    private static string GradeTip(OrbGrade grade) =>
        $"Transmutation and Augmentation roll stats of level {CraftEngine.MinLevel("transmute", grade)} and up; Regal, Exalted and Chaos {CraftEngine.MinLevel("exalted", grade)} and up. A stat whose best tier is lower still rolls.";

    // What I want.
    public IReadOnlyList<WantViewModel> Wants => _engine is null ? [] : _wants.Select(w => Want(w.Key, w.Value)).ToList();

    public bool NoWants => _wants.Count == 0;

    public IReadOnlyList<OddsViewModel> Odds => _engine is null ? [] : _engine.Odds(_item, Setup(), _wants).Select(o => new OddsViewModel(o.Label, Chance(o.Chance), o.Estimate ? "estimate" : "exact")).ToList();

    public bool NoOdds => Odds.Count == 0;

    public string NoOddsText =>
        _wants.Count == 0 ? "Mark a stat to see your odds."
        : _item.Corrupted ? "Corrupted: nothing more can change."
        : _engine is not null && _engine.Open(_item, _wants).Count == 0 ? "You have everything you marked."
        : "No orb can add a stat right now.";

    // Mod pool.
    public string PoolNote => _item.Corrupted ? "Corrupted: the pool no longer matters."
        : $"Chance each stat is the next one added{(PoolSides().Length == 1 ? $" (omen: {(PoolSides()[0] == AffixSide.Prefix ? "prefix" : "suffix")} only)" : "")}. Tick to mark it as wanted.";

    public IReadOnlyList<PoolSideViewModel> Pool => _engine is null ? [] : [PoolSide(AffixSide.Prefix, "Prefixes"), PoolSide(AffixSide.Suffix, "Suffixes")];

    // History.
    public IReadOnlyList<HistoryRowViewModel> History => _history.Select((h, i) => new HistoryRowViewModel(i + 1, h.Label, h.Message, $"Go back to before step {i + 1}", GoBackCommand)).Reverse().ToList();

    public bool NoHistory => _history.Count == 0;

    public int Clicks => _history.Count;

    /// <summary>What the clicks cost, in the display currency, at the session's prices.</summary>
    public string Spent => _session is null ? "" : _session.PriceText().One(_spent);

    public ValueTier? SpentTier => _session?.PriceText().Tier(_spent);

    /// <summary>"12 clicks · 3 not priced · demo prices".</summary>
    public string SpentNote => string.Join(" · ", new[]
    {
        $"{Clicks} click{(Clicks == 1 ? "" : "s")}",
        _unpriced > 0 ? $"{_unpriced} not priced" : null,
        _session?.IsDemo == true ? "demo prices" : null,
    }.Where(s => s is not null));

    public bool CanUndo => _history.Count > 0;

    /// <summary>A new session (profile, league, currency or prices changed): the bench starts over on its data.</summary>
    public void Load() => Load(random: null);

    private DataPack? _practicePack;

    /// <summary>The made-up pack the demo uses, read once for a live profile's bench; null if it can't be read.</summary>
    private DataPack? PracticePack()
    {
        if (_practicePack is null)
        {
            var file = System.IO.Path.Combine(_shell.Paths.Fixtures, "packs", "synthetic-demo.json");
            _practicePack = System.IO.File.Exists(file) ? DataPackLoader.Load(System.IO.File.ReadAllText(file)).Pack : null;
        }

        return _practicePack;
    }

    /// <summary>The bench from scratch with <paramref name="random"/>, so a craft can be repeated (tests,
    /// snapshots).</summary>
    public void Restart(Random random)
    {
        _engine = null;
        Load(random);
    }

    private void Load(Random? random)
    {
        var previous = _session;
        _session = _shell.Session;
        var pack = _session.IsDemo ? _session.Pack : PracticePack();
        if (_engine is not null && pack is not null && previous?.Kind == _session.Kind && _engine.Pack.Id == pack.Id)
        {
            Refresh(); // same data: the item on the bench stays, its costs follow the new prices
            return;
        }

        _engine = pack is { Bases: [var first, ..] } ? new CraftEngine(pack, first, random) : null;
        if (_engine is not null && _engine.Families.Count == 0)
        {
            _engine = null;
        }

        Groups.Clear();
        Omens.Clear();
        if (_engine is not null)
        {
            foreach (var (group, title) in new[] { (CraftGroup.Start, "Start"), (CraftGroup.Change, "Add and change"), (CraftGroup.Finish, "Finish") })
            {
                Groups.Add(new OrbGroupViewModel(title, _engine.Currencies.Where(c => c.Group == group).Select(c => new OrbViewModel(c, this)).ToList()));
            }

            foreach (var o in CraftEngine.OmenList)
            {
                Omens.Add(new OmenViewModel(o, Refresh));
            }

            _wants = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (family, tier) in new[] { ("syn.phys_pct", 2), ("syn.attack_speed", 3) })
            {
                if (_engine.Families.Any(f => f.Id == family))
                {
                    _wants[family] = tier;
                }
            }
        }

        StartOver(saying: null);
    }

    /// <summary>An orb was clicked: used at once (Settings: click to use), else picked up, like in game; a
    /// second click on the orb in hand puts it back. An orb that can't be used says why and stays put.</summary>
    public void Click(OrbViewModel orb)
    {
        if (!_shell.Settings.CraftPickUp)
        {
            Use(orb);
            return;
        }

        if (Held == orb)
        {
            Drop();
            return;
        }

        if (!orb.CanUse)
        {
            Say($"{orb.Name}: {orb.Why}", error: true);
            return;
        }

        if (Held is { } previous)
        {
            previous.IsHeld = false;
        }

        orb.IsHeld = true;
        Held = orb;
        Say($"{orb.Name} in hand: click the item to use it. Shift keeps it in hand; right-click or Esc puts it back.", error: false);
    }

    /// <summary>The item was clicked with an orb in hand: use it, and keep it in hand while Shift is held (and
    /// it can still be used).</summary>
    [RelayCommand(CanExecute = nameof(IsHolding))]
    private void UseHeld()
    {
        if (Held is not { } orb)
        {
            return;
        }

        var keep = _shiftDown();
        Use(orb);
        if (!keep || !orb.CanUse)
        {
            orb.IsHeld = false;
            if (Held == orb)
            {
                Held = null;
            }
        }
    }

    /// <summary>Puts the orb in hand back (right-click, Esc, or a second click on it).</summary>
    [RelayCommand(CanExecute = nameof(IsHolding))]
    private void Drop()
    {
        if (Held is { } orb)
        {
            orb.IsHeld = false;
            Held = null;
            Say("Put back.", error: false);
        }
    }

    [RelayCommand]
    private void PickGrade(OrbGrade grade) => Grade = grade;

    [RelayCommand]
    private void ShowTab(string tab) => Tab = tab;

    [RelayCommand]
    private void LowerItemLevel() => ItemLevel = Math.Max(1, ItemLevel - 1);

    [RelayCommand]
    private void RaiseItemLevel() => ItemLevel = Math.Min(100, ItemLevel + 1);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => GoBack(_history.Count);

    [RelayCommand]
    private void StartOverClicked() => StartOver("A fresh Normal base. The stats you marked are kept.");

    /// <summary>Back to just before step <paramref name="number"/> (1 is the first click).</summary>
    [RelayCommand]
    private void GoBack(int number)
    {
        if (number < 1 || number > _history.Count)
        {
            return;
        }

        var step = _history[number - 1];
        _item = step.Before;
        _used = step.UsedBefore;
        _spent = step.SpentBefore;
        _unpriced = step.UnpricedBefore;
        foreach (var o in Omens)
        {
            o.IsOn = step.OmensBefore.Contains(o.Info.Omen);
        }

        _history.RemoveRange(number - 1, _history.Count - number + 1);
        _fresh = new HashSet<int>();
        Say($"Went back to before step {number}.", error: false);
        Refresh();
    }

    partial void OnItemLevelChanged(int value) => Refresh();

    partial void OnGradeChanged(OrbGrade value) => Refresh();

    partial void OnTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsWantTab));
        OnPropertyChanged(nameof(IsPoolTab));
        OnPropertyChanged(nameof(IsHistoryTab));
    }

    public bool IsWantTab => Tab == "want";
    public bool IsPoolTab => Tab == "pool";
    public bool IsHistoryTab => Tab == "history";

    private void Use(OrbViewModel orb)
    {
        if (_engine is null)
        {
            return;
        }

        var setup = Setup();
        var outcome = _engine.Use(orb.Id, _item, setup);
        if (!outcome.Ok)
        {
            Say($"{orb.Name}: {outcome.Message}", error: true);
            _fresh = new HashSet<int>();
            Refresh();
            return;
        }

        var priceId = orb.Currency.Graded ? CraftEngine.PriceId(orb.Id, Grade) : orb.Id;
        var costs = new[] { Cost(priceId) }.Concat(outcome.OmensUsed.Select(o => Cost(CraftEngine.OmenList.First(x => x.Omen == o).Id))).ToList();
        var label = (orb.Currency.Graded && Grade != OrbGrade.Regular ? $"{Grade} " : "") + orb.Name
            + (outcome.OmensUsed.Count > 0 ? " + " + string.Join(", ", outcome.OmensUsed.Select(o => CraftEngine.OmenList.First(x => x.Omen == o).Name)) : "");
        _history.Add(new Step(label, outcome.Message, _item, new Dictionary<string, int>(_used, StringComparer.Ordinal), _spent, _unpriced,
            Omens.Where(o => o.IsOn).Select(o => o.Info.Omen).ToHashSet()));

        _item = outcome.Item!;
        _fresh = outcome.Fresh;
        _used[orb.Id] = _used.GetValueOrDefault(orb.Id) + 1;
        _spent += costs.Sum(c => c ?? 0);
        _unpriced += costs.Count(c => c is null);
        foreach (var o in Omens.Where(o => outcome.OmensUsed.Contains(o.Info.Omen)))
        {
            o.IsOn = false;
        }

        Say($"{label}: {outcome.Message}", error: false);
        Refresh();
    }

    /// <summary>The price of one of <paramref name="priceId"/> in the display currency, or null when the
    /// session's prices don't have it.</summary>
    private decimal? Cost(string priceId)
    {
        if (_session is null)
        {
            return null;
        }

        var quote = _session.Book.Find(priceId);
        var c = quote is not null
            ? _session.Converter.Convert(quote.Amount, quote.Denomination, _session.DisplayCurrency, _session.Now)
            : _session.Converter.Convert(1m, priceId, _session.DisplayCurrency, _session.Now);
        return c.Ok ? c.Value : null;
    }

    private void StartOver(string? saying)
    {
        _item = CraftItem.Normal;
        _history.Clear();
        _used = new Dictionary<string, int>(StringComparer.Ordinal);
        _spent = 0;
        _unpriced = 0;
        _fresh = new HashSet<int>();
        foreach (var o in Omens)
        {
            o.IsOn = false;
        }

        if (Held is { } h)
        {
            h.IsHeld = false;
            Held = null;
        }

        if (saying is null)
        {
            Message = _shell.Settings.CraftPickUp
                ? "Click an orb to pick it up, then click the item. Greyed orbs say why they can't be used."
                : "Click an orb to use it. Greyed orbs say why they can't be used.";
            MessageIsError = false;
        }
        else
        {
            Say(saying, error: false);
        }

        Refresh();
    }

    private void Say(string text, bool error)
    {
        Message = text;
        MessageIsError = error;
    }

    private CraftSetup Setup() => new(ItemLevel, Grade, Omens.Where(o => o.IsOn).Select(o => o.Info.Omen).ToHashSet());

    private void Refresh()
    {
        if (_engine is not null)
        {
            var setup = Setup();
            foreach (var orb in Groups.SelectMany(g => g.Orbs))
            {
                orb.Why = _engine.Check(orb.Id, _item, setup);
                orb.Count = _used.GetValueOrDefault(orb.Id);
            }
        }

        foreach (var name in new[]
        {
            nameof(HasData), nameof(NoData), nameof(BaseName), nameof(Implicit), nameof(ItemLevelNote), nameof(Grades), nameof(Rarity), nameof(ItemName),
            nameof(HasItemName), nameof(BaseLine), nameof(Weapon), nameof(Sides), nameof(IsEmpty), nameof(IsCorrupted), nameof(CorruptionLines), nameof(Wants), nameof(NoWants),
            nameof(Odds), nameof(NoOdds), nameof(NoOddsText), nameof(PoolNote), nameof(Pool), nameof(History), nameof(NoHistory), nameof(Clicks),
            nameof(Spent), nameof(SpentTier), nameof(SpentNote), nameof(CanUndo),
        })
        {
            OnPropertyChanged(name);
        }

        UndoCommand.NotifyCanExecuteChanged();
    }

    private CraftSideViewModel Side(AffixSide side, string title)
    {
        var e = _engine!;
        var max = CraftEngine.MaxPerSide(_item.Rarity);
        var mods = _item.Mods.Where(m => e.SideOf(m) == side).ToList();
        var rows = mods.Select(m =>
        {
            var fresh = _fresh.Contains(m.Id);
            var tag = m.Fractured ? "fractured" : m.Essence ? "essence" : fresh ? "new" : null;
            return new CraftRowViewModel(e.Text(m), $"T{m.Tier}", tag, fresh, false, m.Essence, m.Fractured);
        }).ToList();
        for (var i = mods.Count; i < max; i++)
        {
            rows.Add(new CraftRowViewModel($"open {(side == AffixSide.Prefix ? "prefix" : "suffix")}", "", null, false, true, false, false));
        }

        return new CraftSideViewModel(title, $"{mods.Count} / {max}", rows);
    }

    private WantViewModel Want(string familyId, int tier)
    {
        var e = _engine!;
        var f = e.Family(familyId);
        var label = CraftEngine.Label(f);
        var on = _item.Mods.FirstOrDefault(m => m.Family == familyId);
        var met = on is not null && CraftEngine.AtLeast(f, on.Tier, tier);
        var status = met ? $"got it · T{on!.Tier}" : on is not null ? $"on item · T{on.Tier} (too low)" : "not yet";
        var worst = f.TierOneIsBest ? f.Tiers.Max(t => t.Tier) : f.Tiers.Min(t => t.Tier);
        var best = f.TierOneIsBest ? f.Tiers.Min(t => t.Tier) : f.Tiers.Max(t => t.Tier);
        var next = f.TierOneIsBest ? best + 1 : best - 1;
        var third = f.TierOneIsBest ? best + 2 : best - 2;
        var choices = new List<(string Label, int Tier)> { ($"T{best}", best) };
        foreach (var (l, t) in new[] { ($"T{next}+", next), ($"T{third}+", third), ("any", worst) })
        {
            if (f.Tiers.Any(x => x.Tier == t) && choices.All(c => c.Tier != t))
            {
                choices.Add((l, t));
            }
        }

        var levels = choices.Select(c => new WantLevelViewModel(c.Label, c.Tier, c.Tier == tier, $"{c.Label} for {label}",
            new RelayCommand(() => { _wants[familyId] = c.Tier; Refresh(); }))).ToList();
        return new WantViewModel(label, status, met, on is not null, levels, $"Stop wanting {label}",
            new RelayCommand(() => { _wants.Remove(familyId); Refresh(); }));
    }

    private AffixSide[] PoolSides()
    {
        if (_item.Rarity != CraftRarity.Rare)
        {
            return [AffixSide.Prefix, AffixSide.Suffix];
        }

        var sin = Omens.Any(o => o.IsOn && o.Info.Omen == Omen.SinistralExaltation);
        var dex = Omens.Any(o => o.IsOn && o.Info.Omen == Omen.DextralExaltation);
        return sin && !dex ? [AffixSide.Prefix] : dex && !sin ? [AffixSide.Suffix] : [AffixSide.Prefix, AffixSide.Suffix];
    }

    private PoolSideViewModel PoolSide(AffixSide side, string title)
    {
        var e = _engine!;
        // The orb that adds the next stat: Transmutation on a Normal item, Augmentation on a Magic one, Exalted on a Rare.
        var minLevel = CraftEngine.MinLevel(_item.Rarity switch { CraftRarity.Normal => "transmute", CraftRarity.Magic => "aug", _ => "exalted" }, Grade);
        var asRarity = _item.Rarity == CraftRarity.Normal ? CraftRarity.Magic : _item.Rarity;
        var pool = e.Pool(_item, PoolSides(), ItemLevel, minLevel, asRarity);
        var total = pool.Sum(p => (double)p.Weight);
        var rows = e.Families.Where(f => f.Side == side).Select(f =>
        {
            var weight = pool.Where(p => p.Family.Id == f.Id).Sum(p => (double)p.Weight);
            var on = _item.Mods.FirstOrDefault(m => m.Family == f.Id);
            var canRoll = f.Tiers.Count(t => t.Level <= ItemLevel && t.Level >= minLevel);
            var sub = on is not null ? $"on item · T{on.Tier}"
                : canRoll == 0 ? "item level too low"
                : weight == 0 ? "no room on this side"
                : $"{canRoll} of {f.Tiers.Count} tiers can roll here";
            var percent = total > 0 && weight > 0 ? Chance(weight / total, percentOnly: true) : "—";
            var familyId = f.Id;
            return new PoolRowViewModel(CraftEngine.Label(f), sub, percent, weight > 0, on is not null, _wants.ContainsKey(f.Id), wanted =>
            {
                if (wanted)
                {
                    _wants.TryAdd(familyId, f.TierOneIsBest ? f.Tiers.Max(t => t.Tier) : f.Tiers.Min(t => t.Tier));
                }
                else
                {
                    _wants.Remove(familyId);
                }

                Refresh();
            });
        }).ToList();
        return new PoolSideViewModel(title, rows);
    }

    /// <summary>"1 in 7 (14%)", "certain", "no chance"; or "14%" alone.</summary>
    private static string Chance(double p, bool percentOnly = false)
    {
        var pct = p * 100 < 1 ? (p * 100).ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(p * 100).ToString("0", CultureInfo.InvariantCulture);
        if (percentOnly)
        {
            return $"{pct}%";
        }

        return p <= 0 ? "no chance" : p >= 0.995 ? "certain" : $"1 in {Math.Max(1, (int)Math.Round(1 / p))} ({pct}%)";
    }
}

public sealed record OrbGroupViewModel(string Title, IReadOnlyList<OrbViewModel> Orbs)
{
    public override string ToString() => Title;
}
