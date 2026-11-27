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
        "Crafting",
        "Milestone 5 · not available yet",
        "Plan a craft on the same item snapshot you inspected, with explicit success and failure branches, priced from the same price book as Wealth.",
        [
            "Pin the mods to keep, pick target stats or minimum tiers, allowed methods and a currency budget.",
            "Unreachable targets come with a reason: item level, family conflict, full affix side, or method not modelled.",
            "Starts with verified Augmentation, Regal and Exalted operations on one weapon family. Essences, omens, desecration and runeforging only after each is verified for the patch.",
            "Odds are labelled exact, estimated or unknown. Recommendations stop when unknown odds or prices would change the answer.",
            "After each real step you paste the new item and replan. No button performs a craft in game.",
        ]);
}
