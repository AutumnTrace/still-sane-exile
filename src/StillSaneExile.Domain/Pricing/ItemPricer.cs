using StillSaneExile.Domain.Items;

namespace StillSaneExile.Domain.Pricing;

public enum PriceKind
{
    /// <summary>Fungible item with an exchange estimate: unit and stack value.</summary>
    Commodity,
    /// <summary>Unique category baseline. Not a price for this specimen's rolls.</summary>
    UniqueBaseline,
    /// <summary>Rare/magic: whatever the player's comparables support.</summary>
    Comparables,
    Unpriced,
}

/// <summary>The one price line the HUD and inspector show for an item.</summary>
public sealed record PriceLine(PriceKind Kind, string Headline, string? Detail, Freshness? Freshness, bool Uncertain, EvidenceSummary? Evidence = null);

public static class ItemPricer
{
    public static MarketIdentity? Identify(ItemSnapshot item, PriceBook book)
    {
        var ids = book.Identities ?? [];
        return item.Rarity switch
        {
            ItemRarity.Currency => ids.FirstOrDefault(i => i.Kind is IdentityKind.Currency or IdentityKind.Commodity
                && i.Name.Equals(item.BaseType, StringComparison.Ordinal)),
            ItemRarity.Unique => ids.FirstOrDefault(i => i.Kind == IdentityKind.Unique
                && i.Name.Equals(item.Name, StringComparison.Ordinal)
                && string.Equals(i.BaseType, item.BaseType, StringComparison.Ordinal)),
            _ => null,
        };
    }

    public static PriceLine Price(
        ItemSnapshot item,
        CurrencyConverter converter,
        string displayCurrency,
        IEnumerable<Comparable> comparables,
        string league,
        DateTimeOffset now)
    {
        var book = converter.Book;
        if (item.Rarity is ItemRarity.Rare or ItemRarity.Magic or ItemRarity.Normal)
        {
            if (item.States.HasFlag(ItemStates.Unidentified) && item.Rarity == ItemRarity.Rare)
            {
                return new PriceLine(PriceKind.Unpriced, "Unidentified · Unpriced", "Only the base is known; compare bases on trade if it matters.", null, true);
            }

            var evidence = EvidencePolicy.Summarize(comparables, league, displayCurrency, converter, now);
            return new PriceLine(PriceKind.Comparables, evidence.Headline,
                evidence.Level == EvidenceLevel.None ? "Open the worksheet and compare on the official trade site." : "Asking prices you recorded. A sale is not guaranteed.",
                null, evidence.Level != EvidenceLevel.Range, evidence);
        }

        var identity = Identify(item, book);
        var quote = identity is null ? null : book.Find(identity.Id, identity.Variant);
        if (identity is null || (quote is null && identity.Kind != IdentityKind.Currency))
        {
            return new PriceLine(PriceKind.Unpriced, "Unpriced", book.Quotes.Count == 0 ? "No market data in this profile yet." : "Not in the current price book.", null, true);
        }

        // A currency that is itself a denomination (e.g. the primary currency) has no quote of its own:
        // value one unit through the converter instead.
        var unit = quote is not null
            ? converter.Convert(quote.Amount, quote.Denomination, displayCurrency, now)
            : converter.Convert(1m, identity.Id, displayCurrency, now);
        if (quote is null && !unit.Ok)
        {
            return new PriceLine(PriceKind.Unpriced, "Unpriced", unit.Explanation, null, true);
        }

        var shown = unit.Ok ? unit : new Conversion(true, quote!.Amount, quote.Denomination, ConversionPath.NoRate, Freshness.Fresh, quote.ObservedTo, "original denomination");
        var observedTo = quote?.ObservedTo ?? unit.OldestObservation ?? now;
        var freshness = new[] { shown.Freshness, StalenessPolicy.Hourly.Classify(observedTo, now) }.Max();
        var age = ValueFormat.Age(now - observedTo);
        var kindLabel = quote?.Kind switch
        {
            ObservationKind.HistoricalTradeAverage => "historical trade average",
            ObservationKind.CategoryBaseline => "category baseline",
            ObservationKind.PlayerEntered => "player-entered",
            _ => "exchange estimate",
        };
        var volume = quote?.Volume is { } v ? $" · volume {ValueFormat.Amount(v)}" : "";
        var staleTag = freshness switch
        {
            Freshness.Stale => " · stale",
            Freshness.RankingSuppressed => " · too old to rank",
            _ => "",
        };

        if (item.Rarity == ItemRarity.Unique)
        {
            return new PriceLine(PriceKind.UniqueBaseline,
                $"≈ {ValueFormat.Money(shown.Value, shown.Denomination)} baseline · check rolls",
                $"{kindLabel}, {age}{volume}{staleTag}. Perfect or corrupted specimens can sell far from the baseline.",
                freshness, true);
        }

        var stack = item.StackSize ?? 1;
        var headline = stack > 1
            ? $"{ValueFormat.Money(shown.Value, shown.Denomination)} each · {ValueFormat.Money(shown.Value * stack, shown.Denomination)} for {stack}"
            : ValueFormat.Money(shown.Value, shown.Denomination);
        return new PriceLine(PriceKind.Commodity, headline, $"{kindLabel}, {age}{volume}{staleTag}", freshness, freshness != Freshness.Fresh || !unit.Ok);
    }
}
