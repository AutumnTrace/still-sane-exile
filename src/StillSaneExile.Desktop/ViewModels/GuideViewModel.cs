using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>A zone in the picker.</summary>
public sealed record ZoneChoice(string Id, string Name, string Facts);

/// <summary>One step row on the guide page.</summary>
public sealed record GuideStepViewModel(string Id, string Text, string Detail, StepState State)
{
    public bool IsDone => State == StepState.Done;
    public bool IsSkipped => State == StepState.Skipped;
    public bool IsOpen => State == StepState.Open;
    public bool HasDetail => Detail.Length > 0;
}

public sealed record MissedRewardViewModel(string Reward, string Where);

/// <summary>The campaign guide page: characters, where the player says they are, the steps there, and
/// the two HUD lines. Works offline from the shipped route pack. Nothing moves or gets ticked unless
/// the player does it here: there is no zone detection, and changing zone never ticks a step.</summary>
public sealed partial class GuideViewModel : ObservableObject
{
    private const int MaxNameLength = 40;

    private readonly ShellViewModel _shell;
    private readonly RouteLibrary _routes;
    private readonly TimeProvider _clock = TimeProvider.System;
    private CampaignProgress? _progress;
    private bool _refreshing;

    public GuideViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _routes = new RouteLibrary(shell.Paths);
        _routes.Load();
        Zones = BuildZones(_routes.Pack);
        HudEnabled = shell.Settings.GuideHud;
        LoadCharacters(shell.Settings.GuideCharacter);
    }

    /// <summary>Raised when the player asks to see the HUD lines over this window for a moment.</summary>
    public event EventHandler? PreviewRequested;

    private RoutePack? Pack => _routes.Pack;

    public ObservableCollection<string> Characters { get; } = [];

    [ObservableProperty]
    public partial string? SelectedCharacter { get; set; }

    partial void OnSelectedCharacterChanged(string? value)
    {
        if (!_refreshing)
        {
            Open(value);
        }
    }

    [ObservableProperty]
    public partial string NewCharacterName { get; set; } = "";

    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>"Remove" was pressed once; the page asks before deleting anything.</summary>
    [ObservableProperty]
    public partial bool ConfirmingRemove { get; set; }

    public bool HasPack => Pack is not null;
    public bool HasCharacter => _progress is not null;
    public bool NeedsCharacter => HasPack && !HasCharacter;
    public bool HasCharacters => Characters.Count > 0;

    // ---- The route pack, and how far to trust it ----

    public bool IsDraft => Pack is { IsVerified: false };

    public string PackBadge => Pack?.Status switch
    {
        RouteStatus.Unverified => "DRAFT ROUTE",
        RouteStatus.Synthetic => "MADE-UP ROUTE",
        _ => "",
    };

    public string PackTitle => Pack?.Title ?? "No route pack";

    public string PackSummary => Pack switch
    {
        null => "",
        { Status: RouteStatus.Unverified } p => $"Not checked in game yet. Zone names, order and connections were written for SSE from general campaign knowledge for patch {p.Patch}. Anything we were unsure of is left out rather than guessed.",
        { Status: RouteStatus.Synthetic } => "Made up for tests. Not game data.",
        var p => $"Checked in game on {p.VerifiedDate}, patch {p.Patch}.",
    };

    public string PackDetails => Pack is { } p ? $"Revision {p.Revision} · league: {p.League} · source: {p.Source}" : "";

    public string? PackProblem => _routes.Problem;

    // ---- Where the player is ----

    public IReadOnlyList<ZoneChoice> Zones { get; private set; }

    /// <summary>The zone picker. Picking a zone moves the position only.</summary>
    public ZoneChoice? SelectedZone
    {
        get => Zones.FirstOrDefault(z => z.Id == _progress?.ZoneId);
        set
        {
            if (_refreshing || value is null || _progress is null || value.Id == _progress.ZoneId)
            {
                return;
            }

            _progress.Enter(value.Id);
            SavePosition();
            Refresh();
        }
    }

    public string ZoneTitle => _progress switch
    {
        null => "",
        { Zone: { } z } => z.Name,
        var p => $"Act {p.Act}",
    };

    public string ZoneFacts
    {
        get
        {
            if (_progress is not { } p)
            {
                return "";
            }

            if (p.Zone is not { } z)
            {
                return "Past the end of this route";
            }

            var parts = new List<string> { $"Act {z.Act}" };
            if (z.Town)
            {
                parts.Add("town");
            }

            if (z.Waypoint == true)
            {
                parts.Add("waypoint");
            }

            parts.Add(p.OnRoute ? $"stop {p.Leg + 1} of {p.Pack.Route.Count}" : "the route does not come here next");
            return string.Join(" · ", parts);
        }
    }

    public string? LegHint => _progress is { OnRoute: true } p ? p.CurrentLeg.Hint : null;

    public IReadOnlyList<GuideStepViewModel> Steps { get; private set; } = [];

    public bool HasSteps => Steps.Count > 0;

    public bool PastRoute => _progress?.PastRoute == true;

    public bool CanNext => _progress?.CanGoNext == true;

    public bool CanBack => _progress?.CanGoBack == true;

    public int Act => _progress?.Act ?? 1;

    public string ThenText => _progress?.Lines() is { } l ? l.Then : "";

    public string NextLabel => _progress switch
    {
        { NextLeg: { } leg } p => $"Next: {p.Pack.Zone(leg.Zone).Name}",
        { PastRoute: false } => "Past this route",
        _ => "Next",
    };

    public bool CanUndo => _progress?.LastMark is not null;

    public string UndoLabel => _progress?.LastMark is { } m && Pack?.FindStep(m.StepId) is { } s
        ? $"Undo “{StateWord(m.State)}: {s.Text}”"
        : "Undo";

    // ---- Missed and skipped ----

    public IReadOnlyList<MissedRewardViewModel> Missed { get; private set; } = [];

    public string MissedEmptyText => Pack?.HasPermanentRewards == true
        ? "None missed so far."
        : "This route lists no permanent rewards yet: none have been confirmed for it.";

    public IReadOnlyList<GuideStepViewModel> SkippedSteps { get; private set; } = [];

    // ---- HUD lines ----

    /// <summary>Shown over the game while it is in front. Off until the player switches it on.</summary>
    [ObservableProperty]
    public partial bool HudEnabled { get; set; }

    partial void OnHudEnabledChanged(bool value) => _shell.Settings.GuideHud = value;

    public HudLines? Hud => _progress?.Lines();

    public bool HasHudLines => Hud is not null;
    public string HudZone => Hud?.Zone ?? "";
    public string HudNow => Hud?.Now ?? "";
    public string HudThenLabel => Hud?.ThenLabel ?? "";
    public string HudThen => Hud?.Then ?? "";
    public bool HudIsDraft => IsDraft;

    // ---- Commands ----

    [RelayCommand]
    private void AddCharacter()
    {
        var name = NewCharacterName.Trim();
        if (Pack is null)
        {
            Message = "No route pack is loaded, so there is nothing to track yet.";
            return;
        }

        if (name.Length is 0 or > MaxNameLength)
        {
            Message = $"Type your character's name first (up to {MaxNameLength} letters).";
            return;
        }

        if (!_shell.Store.AddGuideCharacter(name, Pack.Id, new CampaignProgress(Pack).Position, _clock.GetUtcNow()))
        {
            Message = $"You already have a character called {name}.";
            return;
        }

        Message = null;
        NewCharacterName = "";
        Characters.Add(name);
        SelectedCharacter = name;
        OnPropertyChanged(nameof(HasCharacters));
    }

    [RelayCommand]
    private void RemoveCharacter()
    {
        if (SelectedCharacter is not { } name)
        {
            return;
        }

        if (!ConfirmingRemove)
        {
            ConfirmingRemove = true;
            return;
        }

        _shell.Store.RemoveGuideCharacter(name);
        ConfirmingRemove = false;
        Characters.Remove(name);
        SelectedCharacter = Characters.FirstOrDefault();
        OnPropertyChanged(nameof(HasCharacters));
    }

    [RelayCommand]
    private void KeepCharacter() => ConfirmingRemove = false;

    [RelayCommand]
    private void Next() => Move(p => p.Next());

    [RelayCommand]
    private void Back() => Move(p => p.Back());

    [RelayCommand]
    private void ActUp() => Move(p => p.SetAct(p.Act + 1));

    [RelayCommand]
    private void ActDown() => Move(p => p.SetAct(p.Act - 1));

    [RelayCommand]
    private void ToggleDone(GuideStepViewModel? step) => SetState(step, step?.IsDone == true ? StepState.Open : StepState.Done);

    [RelayCommand]
    private void Skip(GuideStepViewModel? step) => SetState(step, StepState.Skipped);

    [RelayCommand]
    private void Reopen(GuideStepViewModel? step) => SetState(step, StepState.Open);

    [RelayCommand]
    private void Undo()
    {
        if (_progress?.Undo() is { } mark && SelectedCharacter is { } name)
        {
            _shell.Store.RemoveGuideMark(name, mark.Seq);
        }

        Refresh();
    }

    [RelayCommand]
    private void PreviewHud() => PreviewRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Re-reads the route pack from the app folder. A pack that fails its checks is not used.</summary>
    [RelayCommand]
    private void ReloadPack()
    {
        _routes.Load();
        Zones = BuildZones(Pack);
        Open(SelectedCharacter);
        Message = PackProblem is null ? $"Route pack reloaded: {PackTitle}, revision {Pack?.Revision}." : null;
    }

    // ---- Plumbing ----

    private void LoadCharacters(string? preferred)
    {
        Characters.Clear();
        foreach (var c in _shell.Store.GuideCharacters())
        {
            Characters.Add(c);
        }

        var pick = Characters.FirstOrDefault(c => c.Equals(preferred, StringComparison.OrdinalIgnoreCase)) ?? Characters.FirstOrDefault();
        _refreshing = true;
        SelectedCharacter = pick;
        _refreshing = false;
        Open(pick);
    }

    private void Open(string? name)
    {
        ConfirmingRemove = false;
        _progress = name is null || Pack is null
            ? null
            : new CampaignProgress(Pack, _shell.Store.GuideMarks(name), _shell.Store.GuidePosition(name, Pack.Id));
        if (name is not null)
        {
            _shell.Settings.GuideCharacter = name;
        }

        Refresh();
    }

    private void Move(Action<CampaignProgress> move)
    {
        if (_progress is null)
        {
            return;
        }

        move(_progress);
        SavePosition();
        Refresh();
    }

    private void SetState(GuideStepViewModel? step, StepState state)
    {
        if (_progress is null || step is null || SelectedCharacter is not { } name)
        {
            return;
        }

        if (_progress.Mark(step.Id, state, _clock.GetUtcNow()) is { } mark)
        {
            _shell.Store.AddGuideMark(name, mark);
        }

        Refresh();
    }

    private void SavePosition()
    {
        if (_progress is not null && SelectedCharacter is { } name)
        {
            _shell.Store.SaveGuidePosition(name, _progress.Pack.Id, _progress.Position, _clock.GetUtcNow());
        }
    }

    /// <summary>Recomputes the lists and tells every binding (and the HUD) to re-read.</summary>
    private void Refresh()
    {
        if (_progress is { } p)
        {
            Steps = p.StepsHere().Select(s => Row(s.Step, s.State, s.EarlierVisit ? "left open on an earlier visit" : null)).ToList();
            Missed = p.MissedRewards()
                .Select(m => new MissedRewardViewModel(m.Reward.Text, $"{m.Zone.Name} · {m.Step.Text} · {(m.Reason == MissReason.Skipped ? "skipped" : "left behind")}"))
                .ToList();
            SkippedSteps = p.Skipped().Select(s => Row(s.Step, s.State, p.Pack.Zone(p.Pack.Route[s.Leg].Zone).Name)).ToList();
        }
        else
        {
            Steps = [];
            Missed = [];
            SkippedSteps = [];
        }

        _refreshing = true;
        OnPropertyChanged(string.Empty);
        _refreshing = false;
    }

    private static GuideStepViewModel Row(RouteStep step, StepState state, string? where)
    {
        var parts = new List<string>();
        if (where is not null)
        {
            parts.Add(where);
        }

        parts.Add(step.Kind switch
        {
            StepKind.Bonus => "optional",
            StepKind.HandIn => $"hand in to {step.Npc}",
            _ => "",
        });
        parts.AddRange(step.RewardFacts.Select(r => r.Permanent ? $"reward: {r.Text} (permanent)" : $"reward: {r.Text}"));
        var detail = string.Join(" · ", parts.Where(x => x.Length > 0));
        return new GuideStepViewModel(step.Id, step.Text, detail.Length > 0 ? char.ToUpperInvariant(detail[0]) + detail[1..] : "", state);
    }

    private static IReadOnlyList<ZoneChoice> BuildZones(RoutePack? pack) =>
        pack is null
            ? []
            : pack.Zones.Select(z => new ZoneChoice(z.Id, z.Name, string.Join(" · ", new[] { $"Act {z.Act}", z.Town ? "town" : "", z.Waypoint == true ? "waypoint" : "" }.Where(x => x.Length > 0)))).ToList();

    private static string StateWord(StepState state) => state switch
    {
        StepState.Done => "done",
        StepState.Skipped => "skipped",
        _ => "reopened",
    };
}
