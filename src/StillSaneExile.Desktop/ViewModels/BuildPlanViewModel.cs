using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Guide;
using StillSaneExile.Domain.Items;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>A step on the gem checklist.</summary>
public sealed record PlanStepViewModel(string Key, string Text, string Detail, MilestoneStatus Status)
{
    public bool IsDone => Status == MilestoneStatus.Done;
    public bool IsDue => Status == MilestoneStatus.Due;
    public bool IsWaiting => Status is MilestoneStatus.WaitingForGem or MilestoneStatus.WaitingForZone;

    /// <summary>The step as a screen reader says it (WPF reads a list row through ToString).</summary>
    public override string ToString() =>
        $"{Text}{(Detail.Length > 0 ? $", {Detail.Replace(" · ", ", ", StringComparison.Ordinal)}" : "")}{(IsDone ? ", done" : IsDue ? ", due now" : "")}";
}

/// <summary>One line of a revision diff: "+", "−" or "~", the step, and what changed.</summary>
public sealed record PlanChangeViewModel(string Sign, string Text, string Detail)
{
    /// <summary>The change as a screen reader says it: the sign as a word.</summary>
    public override string ToString() => $"{Sign switch { "+" => "Added", "~" => "Changed", _ => "Removed" }}: {Text}{(Detail.Length > 0 ? $", {Detail}" : "")}";
}

/// <summary>A skill and its supports, one row of the build's skill list.</summary>
public sealed record SkillGroupViewModel(string Skill, string Supports, string Detail, string Notes)
{
    public bool HasSupports => Supports.Length > 0;
    public bool HasDetail => Detail.Length > 0;
    public bool HasNotes => Notes.Length > 0;

    /// <summary>The row as a screen reader says it (WPF reads a list row through ToString).</summary>
    public override string ToString() =>
        string.Join(", ", new[] { Skill, HasSupports ? $"supports {Supports}" : "", Detail.Replace(" · ", ", ", StringComparison.Ordinal), Notes }.Where(s => s.Length > 0));
}

/// <summary>One of the build's skill sets, for the picker above the skill list.</summary>
public sealed record SkillSetChoice(int Index, string Title, bool IsShown)
{
    public string? Tag => IsShown ? "active" : null;

    public override string ToString() => Title;
}

/// <summary>An item the build wears, one row of the item list.</summary>
public sealed record BuildItemViewModel(string Slot, string Name, string Detail, ItemRarity Rarity)
{
    public bool HasDetail => Detail.Length > 0;

    /// <summary>The row as a screen reader says it (WPF reads a list row through ToString).</summary>
    public override string ToString() => $"{Slot}: {Name}{(HasDetail ? $", {Detail}" : "")}";
}

/// <summary>A character's build: the Path of Building code they pasted, the gem steps it gives (from
/// its notes and skill sets) and what else it carries, for the guide page and the HUD's "Next gem".
/// The code is read on this PC; nothing is fetched. A pasted build that changes the steps waits as a
/// pending revision until the player accepts it, and the build in use until then is the old one.</summary>
public sealed partial class BuildPlanViewModel : ObservableObject
{
    public const int MaxAscendancies = 4;

    private readonly ShellViewModel _shell;
    private readonly Func<CampaignProgress?> _progress;
    private readonly Func<RoutePack?> _route;
    private readonly Action _changed;
    private readonly TimeProvider _clock = TimeProvider.System;
    private string? _character;
    private string? _savedLink;
    private GuidePlanRow? _activeRow;
    private GuidePlanRow? _pendingRow;
    private BuildPlan _active = BuildPlan.Empty;
    private BuildPlan? _pending;
    private PobPlan? _staging;
    private HashSet<string> _done = [];
    private int? _shownSet;

    public BuildPlanViewModel(ShellViewModel shell, Func<CampaignProgress?> progress, Func<RoutePack?> route, Action changed)
    {
        _shell = shell;
        _progress = progress;
        _route = route;
        _changed = changed;
    }

    [ObservableProperty]
    public partial string? Message { get; set; }

    // ---- Paste a build ----

    [ObservableProperty]
    public partial string CodeDraft { get; set; } = "";

    /// <summary>The build in use, read from the code saved with the active revision.</summary>
    public PobBuild? Build { get; private set; }

    public bool HasBuild => Build is not null;

    /// <summary>Steps the player wrote in SSE 0.6 or earlier, before builds could be pasted. They stay
    /// in use until a build is pasted.</summary>
    public bool HasWrittenPlan => _activeRow is { Code: null } && !_active.IsEmpty;

    public string PasteLabel => HasBuild || HasWrittenPlan ? "Paste a newer code to update the build." : "Paste your build's Path of Building code.";

    public string BuildTitle => Build?.Title ?? "";

    public string BuildFacts => Build is not { } b || _activeRow is not { } row
        ? ""
        : string.Join(" · ", new[]
        {
            $"Level {b.Level}",
            b.TargetVersion is { } v ? $"Path of Building, game version {v.Replace('_', '.')}" : "Path of Building",
            $"pasted {row.CreatedAt.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)}",
            $"revision {row.Revision}",
        });

    public string StatsLine => Build is not { } b || b.Stats.Count == 0
        ? ""
        : string.Join(" · ", Shown.Select(s => b.Stats.FirstOrDefault(x => x.Name == s.Key) is { } stat ? $"{s.Value} {Number(stat.Value)}" : null).OfType<string>());

    public bool HasStats => StatsLine.Length > 0;

    private static readonly KeyValuePair<string, string>[] Shown =
    [
        new("Life", "Life"),
        new("EnergyShield", "Energy Shield"),
        new("Mana", "Mana"),
        new("Spirit", "Spirit"),
    ];

    /// <summary>Reads the pasted code. The first build is used straight away; one that changes the gem
    /// steps of the build in use waits as a pending revision until the player accepts it.</summary>
    [RelayCommand]
    private void ReadCode()
    {
        if (_character is null)
        {
            return;
        }

        if (!PobCode.TryRead(CodeDraft, out var build, out var error))
        {
            Message = error;
            return;
        }

        var plan = PobPlanner.Plan(build!, _route());
        var code = CodeDraft.Trim();
        var now = _clock.GetUtcNow();
        var steps = plan.Plan.Entries.Count == 1 ? "1 gem step" : $"{plan.Plan.Entries.Count} gem steps";
        if (_activeRow is null)
        {
            _shell.Store.SaveGuidePlan(_character, GuidePlanRow.Active, build!.Notes, plan.Plan.ToJson(), now, code);
            Message = $"Read {build.Title}, level {build.Level}: {steps}.";
        }
        else if (PlanDiff.Between(_active, plan.Plan).IsEmpty)
        {
            // Only the tree, items or notes text changed: no step changes for the player to review.
            _shell.Store.SaveGuidePlan(_character, GuidePlanRow.Active, build!.Notes, plan.Plan.ToJson(), now, code);
            Message = $"Build updated: {build.Title}, level {build.Level}. No gem steps changed.";
        }
        else
        {
            var revision = _shell.Store.SaveGuidePlan(_character, GuidePlanRow.Pending, build!.Notes, plan.Plan.ToJson(), now, code);
            Message = $"Read as revision {revision}. The steps in use stay until you accept the changes.";
        }

        CodeDraft = "";
        _shownSet = null;
        Load();
        _changed();
    }

    // ---- Stage ----

    public int Ascendancies { get; private set; }

    public int Act => _progress()?.Act ?? 1;

    public string StageLine => Ascendancies switch
    {
        0 => $"Act {Act} · before your first ascendancy",
        1 => $"Act {Act} · 1 ascendancy done",
        var n => $"Act {Act} · {n} ascendancies done",
    };

    [RelayCommand]
    private void AscendancyUp() => SetAscendancies(Ascendancies + 1);

    [RelayCommand]
    private void AscendancyDown() => SetAscendancies(Ascendancies - 1);

    // ---- Steps ----

    public bool HasPlan => !_active.IsEmpty;

    public string EmptyStepsText => HasBuild
        ? "This build gives no gem steps: its skill sets have no gems SSE can cut, and its notes name none."
        : "No build yet. Paste its Path of Building code on the right: its gems become steps here, one stage at a time, and the next one goes on the HUD.";

    public string StepsSource
    {
        get
        {
            if (!HasPlan)
            {
                return "";
            }

            if (!HasBuild)
            {
                return "Your written steps from an earlier version of SSE. They stay in use until you paste a build.";
            }

            var notes = _active.Milestones.Count(m => m.Origin == PobPlanner.NotesOrigin);
            var sets = _active.Entries.Count - notes;
            return $"{Count(notes)} from the build's notes, {Count(sets)} from its skill sets.";
        }
    }

    /// <summary>Later skill sets whose title names no stage: their gems are listed, never prompted.</summary>
    public string UnstagedText => _staging?.Unstaged.ToList() is { Count: > 0 } sets
        ? $"SSE can't tell when {string.Join(" and ", sets.Select(s => $"“{s.Set.Title ?? $"set {s.Set.Id}"}”"))} starts, so its gems are not steps. To get them, name the set after a stage in Path of Building (Act 2, after ascendancy) and paste the code again."
        : "";

    public bool HasUnstaged => UnstagedText.Length > 0;

    public IReadOnlyList<PlanStepViewModel> Checklist { get; private set; } = [];

    public int HiddenCount { get; private set; }

    public bool HasHidden => HiddenCount > 0;

    public string HiddenText => HiddenCount == 1
        ? "1 step for a later stage stays hidden until you get there."
        : $"{HiddenCount} steps for later stages stay hidden until you get there.";

    public string? NextGem { get; private set; }

    [RelayCommand]
    private void ToggleStep(PlanStepViewModel? step)
    {
        if (_character is null || step is null)
        {
            return;
        }

        var done = !step.IsDone;
        _shell.Store.SetGuidePlanDone(_character, step.Key, done, _clock.GetUtcNow());
        if (done)
        {
            _done.Add(step.Key);
        }
        else
        {
            _done.Remove(step.Key);
        }

        Refresh();
        _changed();
    }

    // ---- Pending revision ----

    public bool HasPending => _pending is not null;

    public string PendingTitle => _pendingRow is null ? "" : $"Revision {_pendingRow.Revision} is waiting";

    public string AcceptLabel => _pendingRow is null ? "" : $"Use revision {_pendingRow.Revision}";

    public string KeepLabel => _activeRow is null ? "" : $"Keep revision {_activeRow.Revision}";

    public string PendingLead => _activeRow is null
        ? ""
        : $"The newer code changes the gem steps. Revision {_activeRow.Revision} stays in use, on the HUD too, until you accept. Ticks carry over for every step that did not change.";

    public IReadOnlyList<PlanChangeViewModel> PendingChanges
    {
        get
        {
            if (_pending is null)
            {
                return [];
            }

            var diff = PlanDiff.Between(_active, _pending);
            return diff.Added.Select(m => new PlanChangeViewModel("+", m.Describe(), m.Stage.Label))
                .Concat(diff.Removed.Select(m => new PlanChangeViewModel("−", m.Describe(), m.Stage.Label)))
                .Concat(diff.Changed.Select(c => new PlanChangeViewModel("~", c.New.Describe(), string.Join(" · ", c.What))))
                .ToList();
        }
    }

    [RelayCommand]
    private void AcceptPending()
    {
        if (_character is null || _pendingRow is null)
        {
            return;
        }

        _shell.Store.AcceptGuidePlan(_character, _pendingRow.Revision);
        Message = $"Now using revision {_pendingRow.Revision}.";
        _shownSet = null;
        Load();
        _changed();
    }

    [RelayCommand]
    private void DiscardPending()
    {
        if (_character is null || _pendingRow is null)
        {
            return;
        }

        _shell.Store.DiscardGuidePlan(_character, _pendingRow.Revision);
        Message = $"Kept revision {_activeRow?.Revision}.";
        Load();
    }

    // ---- What else the build carries ----

    public IReadOnlyList<SkillSetChoice> SkillSets { get; private set; } = [];

    public bool HasSkillSetChoice => SkillSets.Count > 1;

    public string ShownSetStage { get; private set; } = "";

    public IReadOnlyList<SkillGroupViewModel> Skills { get; private set; } = [];

    public bool HasSkills => Skills.Count > 0;

    [RelayCommand]
    private void ShowSkillSet(SkillSetChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        _shownSet = choice.Index;
        Refresh();
    }

    public string NotesText => Build?.Notes ?? "";

    public bool HasNotes => NotesText.Length > 0;

    public string ItemSetTitle => Build?.Items is { } set ? set.Title ?? $"Item set {set.Id}" : "";

    public IReadOnlyList<BuildItemViewModel> Items { get; private set; } = [];

    public bool HasItems => Items.Count > 0;

    // ---- Plumbing ----

    public void Open(string? character)
    {
        _character = character;
        Message = null;
        CodeDraft = "";
        _shownSet = null;
        Load();
    }

    /// <summary>Recomputes the checklist for where the player is now (zone, act, ascendancies).</summary>
    public void Refresh()
    {
        var position = PlanPosition.From(_progress(), Ascendancies);
        var checks = PlanGate.CheckAll(_active, position, _done);
        Checklist = checks.Where(c => c.Visible).Select(Row).ToList();
        HiddenCount = checks.Count(c => !c.Visible);
        NextGem = PlanGate.Next(_active, position, _done)?.Milestone.Describe();
        RefreshSkills(position);
        OnPropertyChanged(string.Empty);
    }

    private void Load()
    {
        if (_character is null)
        {
            (_savedLink, Ascendancies, _activeRow, _pendingRow, _active, _pending, _done, Build, _staging) = (null, 0, null, null, BuildPlan.Empty, null, [], null, null);
            Items = [];
            Refresh();
            return;
        }

        var saved = _shell.Store.GuideBuild(_character);
        (_savedLink, Ascendancies) = (saved.Link, saved.Ascendancies);
        var plans = _shell.Store.GuidePlans(_character);
        _activeRow = plans.LastOrDefault(p => !p.IsPending);
        _pendingRow = plans.LastOrDefault(p => p.IsPending);
        _active = _activeRow is null ? BuildPlan.Empty : BuildPlan.FromJson(_activeRow.Milestones);
        _pending = _pendingRow is null ? null : BuildPlan.FromJson(_pendingRow.Milestones);
        _done = _shell.Store.GuidePlanDone(_character);
        Build = _activeRow?.Code is { } code && PobCode.TryRead(code, out var build, out _) ? build : null;

        // Only to say which sets gave steps; the steps in use are the saved ones.
        _staging = Build is null ? null : PobPlanner.Plan(Build, _route());
        Items = Build?.Items?.Slots.Select(s => new BuildItemViewModel(s.Slot, s.Item.Name, s.Item.BaseType ?? "", RarityOf(s.Item.Rarity))).ToList() ?? [];
        Refresh();
    }

    /// <summary>The skill set shown: the one the player picked, else the latest set for a stage they
    /// have reached, else the one the author had open.</summary>
    private void RefreshSkills(PlanPosition position)
    {
        if (Build is not { } b || b.SkillSets.Count == 0)
        {
            (SkillSets, Skills, ShownSetStage) = ([], [], "");
            return;
        }

        var stages = _staging?.Sets ?? [];
        var reached = stages.Select((s, i) => (s, i)).LastOrDefault(x => x.s.Stage is { } st && st.ReachedBy(position.Act, position.Ascendancies));
        var shown = Math.Clamp(_shownSet ?? (reached.s is not null ? reached.i : b.ActiveSkillSet), 0, b.SkillSets.Count - 1);
        SkillSets = b.SkillSets.Select((s, i) => new SkillSetChoice(i, s.Title ?? $"Set {s.Id}", i == shown)).ToList();
        var stage = shown < stages.Count ? stages[shown].Stage : null;
        ShownSetStage = stage is not null
            ? $"Gives steps from {stage.Label}."
            : shown == 0 ? "" : "Its title names no stage, so it gives no steps.";
        Skills = b.SkillSets[shown].Groups.Where(g => g.Gems.Any(x => x.Name.Length > 0)).Select(Group).ToList();
    }

    private static SkillGroupViewModel Group(PobSkillGroup g)
    {
        var main = g.MainSkill ?? g.Gems.FirstOrDefault(x => x.Name.Length > 0);
        var detail = new List<string>();
        if (g.Label is { } label && label != main?.Name)
        {
            detail.Add(label);
        }

        if (main?.Level is { } level)
        {
            detail.Add($"level {level} in Path of Building");
        }

        if (!g.Enabled)
        {
            detail.Add("switched off in the build");
        }

        if (g.FromItem)
        {
            detail.Add("from an item, not cut");
        }

        var notes = g.Gems.Where(x => x.Note is not null).Select(x => x == main ? x.Note! : $"{x.Name}: {x.Note}");
        return new SkillGroupViewModel(
            main?.Name ?? "",
            string.Join(", ", g.Supports.Where(x => x != main).Select(x => x.Name)),
            string.Join(" · ", detail),
            string.Join(" ", notes));
    }

    private void SetAscendancies(int value)
    {
        value = Math.Clamp(value, 0, MaxAscendancies);
        if (_character is null || value == Ascendancies)
        {
            return;
        }

        Ascendancies = value;
        _shell.Store.SaveGuideBuild(_character, new GuideBuildRow(_savedLink, Ascendancies), _clock.GetUtcNow());
        Refresh();
        _changed();
    }

    private static PlanStepViewModel Row(MilestoneCheck check)
    {
        var m = check.Entry.Milestone;
        var parts = new List<string>
        {
            check.Status switch
            {
                MilestoneStatus.WaitingForGem => $"waits for {string.Join(" and ", check.MissingGems)}",
                MilestoneStatus.WaitingForZone => $"waits until {m.Where}",
                _ => "",
            },
        };
        if (m.Where is { } where && check.Status != MilestoneStatus.WaitingForZone)
        {
            parts.Add($"at {where}");
        }

        if (m.Uncut is { } u)
        {
            parts.Add(u.Level is { } level ? $"level {level} uncut {u.Kind.ToString().ToLowerInvariant()} gem" : $"uncut {u.Kind.ToString().ToLowerInvariant()} gem");
        }

        parts.Add(m.Stage.Label);
        if (m.Origin is { } origin)
        {
            parts.Add($"from {origin}");
        }

        parts.Add($"priority: {m.PriorityText}");
        var detail = string.Join(" · ", parts.Where(p => p.Length > 0));
        return new PlanStepViewModel(check.Entry.Key, m.Describe(), char.ToUpperInvariant(detail[0]) + detail[1..], check.Status);
    }

    private static string Count(int n) => n == 1 ? "1 step" : $"{n} steps";

    /// <summary>Path of Building's numbers have decimals ("812.4"); the page shows whole ones.</summary>
    private static string Number(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? Math.Round(n).ToString("N0", CultureInfo.InvariantCulture) : value;

    private static ItemRarity RarityOf(string rarity) => rarity switch
    {
        "NORMAL" => ItemRarity.Normal,
        "MAGIC" => ItemRarity.Magic,
        "RARE" => ItemRarity.Rare,
        "UNIQUE" or "RELIC" => ItemRarity.Unique,
        _ => ItemRarity.Unknown,
    };
}
