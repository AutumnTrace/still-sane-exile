using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>One line of the notes preview: the line as typed and what SSE read from it.</summary>
public sealed record NoteLineViewModel(int Number, string Text, string Reading, bool IsProblem)
{
    public bool HasReading => Reading.Length > 0;

    /// <summary>The line as a screen reader says it (WPF reads a list row through ToString).</summary>
    public override string ToString() => $"Line {Number}: {Text}{(HasReading ? $". {Reading.Replace(" · ", ", ", StringComparison.Ordinal)}" : "")}";
}

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

/// <summary>A character's build plan: the guide link they saved, their own notes turned into a
/// checklist, and the gem step the HUD shows next. The link is only saved and opened in the browser on
/// a click; nothing is fetched, embedded or imported. A changed plan waits as a pending revision until
/// the player accepts it, and the plan in use until then is the old one.</summary>
public sealed partial class BuildPlanViewModel : ObservableObject
{
    public const int MaxAscendancies = 4;

    private readonly ShellViewModel _shell;
    private readonly Func<CampaignProgress?> _progress;
    private readonly Func<RoutePack?> _route;
    private readonly Action _changed;
    private readonly TimeProvider _clock = TimeProvider.System;
    private string? _character;
    private GuidePlanRow? _activeRow;
    private GuidePlanRow? _pendingRow;
    private BuildPlan _active = BuildPlan.Empty;
    private BuildPlan? _pending;
    private HashSet<string> _done = [];
    private PlanDraft _draft = PlanNotesParser.Parse(null);

    public BuildPlanViewModel(ShellViewModel shell, Func<CampaignProgress?> progress, Func<RoutePack?> route, Action changed)
    {
        _shell = shell;
        _progress = progress;
        _route = route;
        _changed = changed;
    }

    [ObservableProperty]
    public partial string? Message { get; set; }

    // ---- The guide link ----

    [ObservableProperty]
    public partial string LinkDraft { get; set; } = "";

    public string? SavedLink { get; private set; }

    public bool HasLink => SavedLink is not null;

    public string LinkHost => SavedLink is not null && Uri.TryCreate(SavedLink, UriKind.Absolute, out var u) ? u.Host : "";

    public string LinkKind => SavedLink is not null && Uri.TryCreate(SavedLink, UriKind.Absolute, out var u) && BuildLink.IsMobalytics(u)
        ? "Mobalytics guide"
        : "Build guide";

    [RelayCommand]
    private void SaveLink()
    {
        if (_character is null)
        {
            return;
        }

        if (!BuildLink.TryParse(LinkDraft, out var link, out var error))
        {
            Message = error;
            return;
        }

        SavedLink = link!.OriginalString;
        LinkDraft = "";
        Message = null;
        SaveBuild();
        Refresh();
    }

    /// <summary>Asks Windows to open the saved link in the default browser. Only on this click.</summary>
    [RelayCommand]
    private void OpenLink()
    {
        if (!BuildLink.TryParse(SavedLink, out var link, out var error))
        {
            Message = error;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(link!.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            Message = $"Windows could not open the browser: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ForgetLink()
    {
        SavedLink = null;
        SaveBuild();
        Refresh();
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

    // ---- Notes and preview ----

    /// <summary>Open while the player writes or corrects their notes.</summary>
    [ObservableProperty]
    public partial bool EditingNotes { get; set; }

    [ObservableProperty]
    public partial string NotesDraft { get; set; } = "";

    partial void OnNotesDraftChanged(string value)
    {
        _draft = PlanNotesParser.Parse(value, _route());
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(PreviewSummary));
    }

    public IReadOnlyList<NoteLineViewModel> Preview => _draft.Lines
        .Where(l => l.Kind != NoteLineKind.Blank)
        .Select(l => new NoteLineViewModel(l.Number, l.Text.Trim(), l.Problem ?? l.Reading, l.Problem is not null))
        .ToList();

    public string PreviewSummary
    {
        get
        {
            var problems = _draft.Problems.Count;
            var steps = _draft.Steps == 1 ? "1 step" : $"{_draft.Steps} steps";
            return problems == 0 ? $"{steps} read." : $"{steps} read · {problems} {(problems == 1 ? "line needs" : "lines need")} a look.";
        }
    }

    public bool HasPlan => !_active.IsEmpty;

    public string PlanSummary => _activeRow is null
        ? ""
        : $"Revision {_activeRow.Revision} · {_active.Entries.Count} steps · saved {_activeRow.CreatedAt.ToLocalTime():d MMM yyyy}";

    public string SaveLabel => HasPlan ? "Save as a new revision" : "Save plan";

    [RelayCommand]
    private void EditNotes()
    {
        NotesDraft = _pendingRow?.Notes ?? _activeRow?.Notes ?? NotesDraft;
        EditingNotes = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        NotesDraft = _pendingRow?.Notes ?? _activeRow?.Notes ?? "";
        EditingNotes = !HasPlan;
    }

    /// <summary>Saves what the preview shows. The first plan is used straight away; a change to a plan
    /// in use waits as a pending revision until the player accepts it.</summary>
    [RelayCommand]
    private void SavePlan()
    {
        if (_character is null)
        {
            return;
        }

        var draft = PlanNotesParser.Parse(NotesDraft, _route());
        if (draft.Plan.IsEmpty)
        {
            Message = "Nothing to save yet: no line was read as a gem step. Check the preview.";
            return;
        }

        var now = _clock.GetUtcNow();
        if (!HasPlan)
        {
            var first = _shell.Store.SaveGuidePlan(_character, GuidePlanRow.Active, NotesDraft, draft.Plan.ToJson(), now);
            Message = $"Plan saved as revision {first}. Your checklist is ready.";
        }
        else if (PlanDiff.Between(_active, draft.Plan).IsEmpty)
        {
            // Only comments or spacing changed: no step changes for the player to review.
            _shell.Store.SaveGuidePlan(_character, GuidePlanRow.Active, NotesDraft, draft.Plan.ToJson(), now);
            Message = "Saved. No gem steps changed.";
        }
        else
        {
            var revision = _shell.Store.SaveGuidePlan(_character, GuidePlanRow.Pending, NotesDraft, draft.Plan.ToJson(), now);
            Message = $"Saved as revision {revision}. Your current plan stays in use until you accept the changes.";
        }

        EditingNotes = false;
        Load();
        _changed();
    }

    // ---- Pending revision ----

    public bool HasPending => _pending is not null;

    public string PendingTitle => _pendingRow is null ? "" : $"Revision {_pendingRow.Revision} is waiting";

    public string AcceptLabel => _pendingRow is null ? "" : $"Use revision {_pendingRow.Revision}";

    public string KeepLabel => _activeRow is null ? "" : $"Keep revision {_activeRow.Revision}";

    public string PendingLead => _activeRow is null
        ? ""
        : $"Revision {_activeRow.Revision} stays in use, on the HUD too, until you accept. Ticks carry over for every step that did not change.";

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

    // ---- Checklist ----

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

    // ---- Plumbing ----

    public void Open(string? character)
    {
        _character = character;
        Message = null;
        LinkDraft = "";
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
        OnPropertyChanged(string.Empty);
    }

    private void Load()
    {
        if (_character is null)
        {
            (SavedLink, Ascendancies, _activeRow, _pendingRow, _active, _pending, _done) = (null, 0, null, null, BuildPlan.Empty, null, []);
            EditingNotes = false;
            NotesDraft = "";
            Refresh();
            return;
        }

        var build = _shell.Store.GuideBuild(_character);
        (SavedLink, Ascendancies) = (build.Link, build.Ascendancies);
        var plans = _shell.Store.GuidePlans(_character);
        _activeRow = plans.LastOrDefault(p => !p.IsPending);
        _pendingRow = plans.LastOrDefault(p => p.IsPending);
        _active = _activeRow is null ? BuildPlan.Empty : BuildPlan.FromJson(_activeRow.Milestones);
        _pending = _pendingRow is null ? null : BuildPlan.FromJson(_pendingRow.Milestones);
        _done = _shell.Store.GuidePlanDone(_character);
        EditingNotes = _activeRow is null;
        NotesDraft = _pendingRow?.Notes ?? _activeRow?.Notes ?? "";
        Refresh();
    }

    private void SetAscendancies(int value)
    {
        value = Math.Clamp(value, 0, MaxAscendancies);
        if (_character is null || value == Ascendancies)
        {
            return;
        }

        Ascendancies = value;
        SaveBuild();
        Refresh();
        _changed();
    }

    private void SaveBuild()
    {
        if (_character is not null)
        {
            _shell.Store.SaveGuideBuild(_character, new GuideBuildRow(SavedLink, Ascendancies), _clock.GetUtcNow());
        }
    }

    private static PlanStepViewModel Row(MilestoneCheck check)
    {
        var m = check.Entry.Milestone;
        var parts = new List<string>();
        parts.Add(check.Status switch
        {
            MilestoneStatus.WaitingForGem => $"waits for {string.Join(" and ", check.MissingGems)}",
            MilestoneStatus.WaitingForZone => $"waits until {m.Where}",
            _ => "",
        });
        if (m.Where is { } where && check.Status != MilestoneStatus.WaitingForZone)
        {
            parts.Add($"at {where}");
        }

        if (m.Uncut is { } u)
        {
            parts.Add(u.Level is { } level ? $"level {level} uncut {u.Kind.ToString().ToLowerInvariant()} gem" : $"uncut {u.Kind.ToString().ToLowerInvariant()} gem");
        }

        parts.Add(m.Stage.Label);
        parts.Add($"priority: {m.PriorityText}");
        var detail = string.Join(" · ", parts.Where(p => p.Length > 0));
        return new PlanStepViewModel(check.Entry.Key, m.Describe(), char.ToUpperInvariant(detail[0]) + detail[1..], check.Status);
    }
}
