using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StillSaneExile.Domain.Capture;
using StillSaneExile.Domain.Items;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>One line of "Searched with": ticked stats show the number searched for, unticked ones their tier.</summary>
public sealed record PeekRowViewModel(bool On, string Text, string Right, string? Hint)
{
    public bool Off => !On;
    public bool HasHint => Hint is not null;

    /// <summary>The row as a screen reader says it (WPF reads a list row through ToString).</summary>
    public override string ToString() => $"{Text}, {(On ? "searched" : "not searched")}{(Right.Length > 0 ? $", {Right}" : "")}";
}

/// <summary>The quick peek card: the item, what it is worth and exactly which stats the price used.
/// Pinned (the player moved onto it while holding the key), its stats can be ticked: they are the
/// item's own search rows, so the price on the card follows, and so does the price check page.</summary>
public sealed partial class PeekViewModel : ObservableObject
{
    /// <summary>Rows beyond this are left off the card (unticked ones first); F3 shows them all.</summary>
    public const int MaxRows = 8;

    /// <summary>On cards from a binding that is not the game's copy key: the clipboard held this item
    /// already, and it need not be the one under the cursor.</summary>
    public const string LastCopiedNote = "Showing the last item you copied";

    /// <summary>The footer of a pinned card.</summary>
    public const string PinnedHint = "Pinned · tick stats to change the search";

    public string Title { get; init; } = "";
    public ItemRarity Rarity { get; init; } = ItemRarity.Unknown;
    public string? Subtitle { get; init; }
    public string? Note { get; init; }
    public bool IsDemo { get; init; }

    /// <summary>The item the card is about; null on a message card.</summary>
    public ItemViewModel? Item { get; private init; }

    /// <summary>The price as the item's search has it now.</summary>
    public PriceAnswer? Answer => Item?.Answer;

    /// <summary>"Searched with" as it was when the card opened (the held card).</summary>
    public IReadOnlyList<PeekRowViewModel> Rows { get; init; } = [];

    /// <summary>The same rows in the same order, live (the pinned card): the unique's name row, then the
    /// item's own <see cref="SearchRowViewModel"/>s, whose ticks change the search.</summary>
    public IReadOnlyList<object> LiveRows { get; private init; } = [];

    public int OnCount => Item?.OnCount ?? 0;
    public int Total => Item?.TotalCount ?? 0;
    public bool IsMessage { get; init; }
    public string? MessageText { get; init; }
    public string ReleaseHint { get; init; } = "";
    public string ChangeKey { get; init; } = "";

    /// <summary>Numbered guide pins (onboarding's "How to read a price check").</summary>
    public bool ShowPins { get; init; }

    /// <summary>The player moved onto the card while holding the key: it stays after release, with a
    /// close button and stats to tick.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStaticRows), nameof(ShowsLiveRows), nameof(FooterHint), nameof(ShowsChangeKey), nameof(ShowsMore))]
    public partial bool IsPinned { get; set; }

    public bool HasAnswer => Answer is not null;
    public bool HasRows => Rows.Count > 0;
    public bool HasChangeKey => ChangeKey.Length > 0;
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
    public bool HasNote => !string.IsNullOrEmpty(Note);

    public bool ShowsStaticRows => HasRows && !IsPinned;
    public bool ShowsLiveRows => LiveRows.Count > 0 && IsPinned;

    /// <summary>"F3 change the search" on the held card.</summary>
    public bool ShowsChangeKey => HasChangeKey && !IsPinned;

    /// <summary>"All filters" on the pinned card: the full price check (F3) for this item.</summary>
    public bool ShowsMore => HasChangeKey && IsPinned;

    /// <summary>How to hide it while held; what to do with it once pinned.</summary>
    public string FooterHint => IsPinned ? PinnedHint : ReleaseHint;

    public static PeekViewModel From(ItemViewModel item, bool demo, string releaseHint, string changeKey, bool pins = false, string? note = null)
    {
        var rows = new List<PeekRowViewModel>();
        var live = new List<object>();
        if (item.IsUnique)
        {
            var name = new PeekRowViewModel(true, item.UniqueNameRow, "exact", null);
            rows.Add(name);
            live.Add(name);
        }

        if (item.ShowsWorksheet)
        {
            var ordered = item.SearchRows.Where(r => r.IsOn).Concat(item.SearchRows.Where(r => !r.IsOn)).Take(MaxRows).ToList();
            rows.AddRange(ordered.Select(r => r.IsOn
                ? new PeekRowViewModel(true, r.Text, r.BoundLabel, r.IsAmbiguous ? r.Hint : null)
                : new PeekRowViewModel(false, r.Text, r.TierOrBlank, r.IsAmbiguous ? r.Hint : null)));
            live.AddRange(ordered);
        }

        var card = new PeekViewModel
        {
            Title = item.Title,
            Rarity = item.Rarity,
            Subtitle = item.Subtitle,
            Note = note,
            IsDemo = demo,
            Item = item,
            Rows = rows,
            LiveRows = live,
            ReleaseHint = releaseHint,
            ChangeKey = item.ShowsWorksheet ? changeKey : "",
            ShowPins = pins,
        };

        // Weak, so a card that has closed is not kept alive by the item it showed.
        PropertyChangedEventManager.AddHandler(item, card.OnItemChanged, string.Empty);
        return card;
    }

    public static PeekViewModel Message(string text, bool demo, string releaseHint) => new()
    {
        Title = "Still Sane, Exile?",
        IsMessage = true,
        MessageText = text,
        IsDemo = demo,
        ReleaseHint = releaseHint,
    };

    public static string ReleaseHintFor(HoldMode mode) => mode == HoldMode.Toggle ? "Press again to hide" : "Let go to hide · move onto it to keep it";

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ItemViewModel.Answer):
                OnPropertyChanged(nameof(Answer));
                OnPropertyChanged(nameof(HasAnswer));
                break;
            case nameof(ItemViewModel.OnCount):
                OnPropertyChanged(nameof(OnCount));
                break;
        }
    }
}
