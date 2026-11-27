using StillSaneExile.Domain.Capture;
using StillSaneExile.Domain.Items;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>One line of "Searched with": ticked stats show the number searched for, unticked ones their tier.</summary>
public sealed record PeekRowViewModel(bool On, string Text, string Right, string? Hint)
{
    public bool Off => !On;
    public bool HasHint => Hint is not null;
}

/// <summary>The quick peek card: the item, what it is worth and exactly which stats the price used.</summary>
public sealed class PeekViewModel
{
    /// <summary>Rows beyond this are left off the card (unticked ones first); F3 shows them all.</summary>
    public const int MaxRows = 8;

    public string Title { get; init; } = "";
    public ItemRarity Rarity { get; init; } = ItemRarity.Unknown;
    public string? Subtitle { get; init; }
    public bool IsDemo { get; init; }
    public PriceAnswer? Answer { get; init; }
    public IReadOnlyList<PeekRowViewModel> Rows { get; init; } = [];
    public int OnCount { get; init; }
    public int Total { get; init; }
    public bool IsMessage { get; init; }
    public string? MessageText { get; init; }
    public string ReleaseHint { get; init; } = "";
    public string ChangeKey { get; init; } = "";

    /// <summary>Numbered guide pins (onboarding's "How to read a price check").</summary>
    public bool ShowPins { get; init; }

    public bool HasAnswer => Answer is not null;
    public bool HasRows => Rows.Count > 0;
    public bool HasChangeKey => ChangeKey.Length > 0;
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    public static PeekViewModel From(ItemViewModel item, bool demo, string releaseHint, string changeKey, bool pins = false)
    {
        var rows = new List<PeekRowViewModel>();
        if (item.IsUnique)
        {
            rows.Add(new PeekRowViewModel(true, item.UniqueNameRow, "exact", null));
        }

        if (item.ShowsWorksheet)
        {
            var on = item.SearchRows.Where(r => r.IsOn).Select(r => new PeekRowViewModel(true, r.Text, r.BoundLabel, r.IsAmbiguous ? r.Hint : null));
            var off = item.SearchRows.Where(r => !r.IsOn).Select(r => new PeekRowViewModel(false, r.Text, r.TierOrBlank, r.IsAmbiguous ? r.Hint : null));
            rows.AddRange(on.Concat(off).Take(MaxRows));
        }

        return new PeekViewModel
        {
            Title = item.Title,
            Rarity = item.Rarity,
            Subtitle = item.Subtitle,
            IsDemo = demo,
            Answer = item.Answer,
            Rows = rows,
            OnCount = item.OnCount,
            Total = item.TotalCount,
            ReleaseHint = releaseHint,
            ChangeKey = item.ShowsWorksheet ? changeKey : "",
            ShowPins = pins,
        };
    }

    public static PeekViewModel Message(string text, bool demo, string releaseHint) => new()
    {
        Title = "Still Sane, Exile?",
        IsMessage = true,
        MessageText = text,
        IsDemo = demo,
        ReleaseHint = releaseHint,
    };

    public static string ReleaseHintFor(HoldMode mode) => mode == HoldMode.Toggle ? "Press again to hide" : "Let go to hide";
}
