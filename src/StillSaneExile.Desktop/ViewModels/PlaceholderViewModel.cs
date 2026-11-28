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

    /// <summary>My wealth, switched off while its design is rethought. Holdings saved by earlier versions
    /// stay in the data folder untouched.</summary>
    public static PlaceholderViewModel Wealth() => new(
        "My wealth",
        "Switched off for now · coming back later",
        "Keeping count of what you hold, tab by tab, is being rethought. Counts and CSV imports saved by earlier versions stay in your data folder, untouched.",
        [
            "\"Save visible stash\" previews recognized holdings and omissions; you confirm the tab before anything is stored.",
            "History keeps quantities and the price book used: value at the time, or revalue at today's rates.",
            "Session rate only from comparable holdings at fixed prices; transfers between tracked tabs net to zero.",
            "Held-key price chips never create history on their own.",
        ]);
}
