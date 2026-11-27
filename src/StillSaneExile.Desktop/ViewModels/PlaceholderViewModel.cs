using CommunityToolkit.Mvvm.ComponentModel;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>A destination that is not built yet. It says so plainly and lists what is planned.</summary>
public sealed partial class PlaceholderViewModel(string title, string milestone, string summary, IReadOnlyList<string> planned) : ObservableObject
{
    public string Title { get; } = title;
    public string Milestone { get; } = milestone;
    public string Summary { get; } = summary;
    public IReadOnlyList<string> Planned { get; } = planned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContext))]
    public partial string? Context { get; set; }

    public bool HasContext => Context is not null;

    public static PlaceholderViewModel Craft() => new(
        "Craft",
        "Milestone 5 · not available yet",
        "Plan a craft on the same item snapshot you inspected, with explicit success and failure branches, priced from the same price book as Wealth.",
        [
            "Pin the mods to keep, pick target stats or minimum tiers, allowed methods and a currency budget.",
            "Unreachable targets come with a reason: item level, family conflict, full affix side, or method not modelled.",
            "Starts with verified Augmentation, Regal and Exalted operations on one weapon family. Essences, omens, desecration and runeforging only after each is verified for the patch.",
            "Odds are labelled exact, estimated or unknown. Recommendations stop when unknown odds or prices would change the answer.",
            "After each real step you paste the new item and replan. No button performs a craft in game.",
        ]);

    public static PlaceholderViewModel Wealth() => new(
        "Wealth",
        "Milestone 3 · not available yet",
        "Track what you own from explicit snapshots, tab by tab, and see how much of it was observed and priced.",
        [
            "\"Save visible stash\" previews recognized holdings and omissions; you confirm the tab before anything is stored.",
            "Manual quantities and a documented CSV import (league, tab, item id, variant, quantity, snapshot time).",
            "History keeps quantities and the price book used: value at the time, or revalue at today's rates.",
            "Session rate only from comparable holdings at fixed prices; transfers between tracked tabs net to zero.",
            "Held-key price chips never create history on their own.",
        ]);

    public static PlaceholderViewModel Guide() => new(
        "Guide",
        "Milestone 4 · not available yet",
        "Campaign route and build steps in two short HUD lines: the next objective and what to do with your next uncut gem.",
        [
            "Versioned zone graph with original route text, optional bonuses and backtracking. Manual zone picker works offline.",
            "Build plans: paste a Mobalytics link to save it and open it in your browser; enter or import the steps yourself. Automatic import only through an authorized export.",
            "Stage-aware gem actions (cut, upgrade, move support) that never leak later-stage loadouts into early prompts.",
            "Skip, undo, resume after restart, and a list of missed permanent rewards. Zone entry never marks a quest done.",
        ]);
}
