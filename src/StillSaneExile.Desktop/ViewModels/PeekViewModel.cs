using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>The HUD card: item name/base, up to four relevant rolls, one price or comparison status.</summary>
public sealed class PeekViewModel
{
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public IReadOnlyList<ModRowViewModel> Rows { get; init; } = [];
    public string? PhysicalDps { get; init; }
    public string? PriceHeadline { get; init; }
    public bool PriceUncertain { get; init; }
    public bool HasAmbiguity { get; init; }
    public bool IsDemo { get; init; }
    public bool IsMessage { get; init; }
    public string? MessageText { get; init; }
    public string Footer { get; init; } = "";
    public bool HasPdps => PhysicalDps is not null;
    public bool HasPrice => PriceHeadline is not null;

    public static PeekViewModel From(ItemViewModel item, bool demo, string releaseHint, string openLastKey) => new()
    {
        Title = item.Title,
        Subtitle = string.Join(" · ", new[] { item.BaseLine, item.Item.ItemLevel is { } il ? $"iLvl {il}" : null }.Where(s => s is not null)),
        Rows = item.PeekRows,
        PhysicalDps = item.PhysicalDps,
        PriceHeadline = item.Price?.Headline,
        PriceUncertain = item.Price?.Uncertain ?? false,
        HasAmbiguity = item.HasAmbiguity,
        IsDemo = demo,
        Footer = $"{releaseHint} · {openLastKey} opens the full inspector",
    };

    public static PeekViewModel Message(string text, bool demo, string releaseHint) => new()
    {
        Title = "Still Sane, Exile",
        IsMessage = true,
        MessageText = text,
        IsDemo = demo,
        Footer = releaseHint,
    };

    public static string ReleaseHint(Domain.Capture.HoldMode mode) =>
        mode == Domain.Capture.HoldMode.Toggle ? "Press again to dismiss" : "Release to hide";

    public static string Money(decimal v, string d) => ValueFormat.Money(v, d);
}
