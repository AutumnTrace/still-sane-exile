using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>The answer block shown on the peek card and at the top of the price check: what the item
/// is worth in plain words, how sure that is, and how many are for sale.</summary>
/// <param name="Lead">Small line above the price, e.g. "Items like yours sell for about".</param>
/// <param name="Big">The price itself, or a short status such as "No matches".</param>
/// <param name="Line">Optional line under the price, e.g. "Cheapest 1.5 div · 1 div = 120 ex".</param>
/// <param name="Note">How sure the price is, in words ("Good price guess — 29 for sale.").</param>
/// <param name="NoteIsWarning">True when the note asks the player to be careful.</param>
/// <param name="Count">How many are for sale, when that is known.</param>
/// <param name="PeekNote">The shorter note the peek card shows under the price.</param>
public sealed record PriceAnswer(string Lead, string Big, string? Line, string Note, bool NoteIsWarning, int? Count, string PeekNote)
{
    public bool HasCount => Count is not null;
    public bool HasLine => !string.IsNullOrEmpty(Line);

    public static PriceAnswer FromSearch(SearchResult r, PriceText price, bool unique)
    {
        var n = r.Count;
        var lead = unique ? "This unique sells for about" : "Items like yours sell for about";
        var cheapest = r.Cheapest is { } c ? price.One(c.Value) : "";
        return r.Confidence switch
        {
            SearchConfidence.NoMatches => new("Items like yours", "No matches", "Your search is too strict",
                "Untick a stat or lower a number to see prices.", true, 0, "No matches · loosen the search"),
            SearchConfidence.TooFew => new(unique ? "Cheapest one for sale" : "Cheapest item like yours", cheapest, null,
                $"Only {n} for sale — too few to trust. Try loosening the search.", true, n, $"Only {n} for sale — too few to trust"),
            SearchConfidence.Rough => new(lead, price.Range(r.Low!.Value, r.High!.Value), $"Cheapest {cheapest}",
                $"Rough price — only {n} for sale.", false, n, $"Rough price · only {n} for sale"),
            _ => new(lead, price.Range(r.Low!.Value, r.High!.Value), Join($"Cheapest {cheapest}", price.DivineRate()),
                $"Good price guess — {n} for sale.", false, n, $"Good price guess · cheapest {cheapest}"),
        };
    }

    /// <summary>From the asking prices the player wrote down (no listings to search in this profile).</summary>
    public static PriceAnswer? FromWrittenDown(EvidenceSummary e, PriceText price)
    {
        const string lead = "From prices you wrote down";
        var n = e.IndependentSellers;
        return e.Level switch
        {
            EvidenceLevel.OneAsk => new(lead, price.One(e.Low!.Value), null,
                "Only 1 for sale — too few to trust.", true, 1, "Only 1 price written down"),
            EvidenceLevel.ThinMarket => new(lead, price.Range(e.Low!.Value, e.High!.Value), null,
                $"Only {n} for sale — rough price.", false, n, $"Only {n} for sale — rough price"),
            EvidenceLevel.Range => new(lead, price.Range(e.Low!.Value, e.High!.Value), $"Middle price {price.One(e.Median!.Value)}",
                $"Good price guess — {n} sellers.", false, n, $"Good price guess · {n} sellers"),
            _ => null,
        };
    }

    /// <param name="noMarketData">The profile has no prices at all (a live league before its first poe.ninja
    /// refresh, or with live prices off).</param>
    /// <param name="searchable">The item has a search to open on the trade site.</param>
    /// <param name="canWriteDown">Asking prices can be written down for it (rare, magic, normal). SSE fetches
    /// no listings for these, so they point to the trade site whether or not the league has prices.</param>
    public static PriceAnswer NoPrice(bool noMarketData, bool searchable = false, bool canWriteDown = false)
    {
        var note = canWriteDown ? "Open the search on the trade site, then write down what you find."
            : searchable ? "Open the search on the trade site to compare."
            : noMarketData ? "There is no price list for this league yet."
            : "This item is not in the price list.";
        var line = noMarketData && !canWriteDown ? "No prices for this league yet: see Settings, Live prices." : null;
        var peek = canWriteDown ? "No price yet · compare on trade"
            : noMarketData ? "No price yet · no prices loaded"
            : searchable ? "No price yet · compare on trade"
            : "Not in the price list";
        return new(searchable ? "Items like yours" : "Price", "No price yet", line, note, false, null, peek);
    }

    /// <summary>An item copied from the game while the demo profile is on: the demo's prices are made up
    /// for its practice items, so this item gets none rather than an invented one that looks real.</summary>
    public static PriceAnswer PracticeOnly(bool searchable, bool canWriteDown) =>
        new(searchable ? "Items like yours" : "Price", "No real price", "Demo prices are made up and only cover the practice items.",
            canWriteDown ? "Pick a live league in Settings for real prices, or open the search on the trade site and write down what you find."
                : "Pick a live league in Settings for real prices.",
            true, null, "Demo · no real prices for game items");

    public static PriceAnswer Unidentified() =>
        new("Unidentified item", "No price", null, "Only the base is known. Identify it, then check again.", true, null, "Identify it to price it");

    /// <summary>Currency and other stackables: what the stack is worth, and the rate used.</summary>
    public static PriceAnswer Stack(ItemSnapshot item, MarketIdentity? identity, PriceLine line, CurrencyConverter converter, PriceText price, DateTimeOffset now)
    {
        var book = converter.Book;
        if (identity is null || line.Kind == PriceKind.Unpriced)
        {
            return NoPrice(book.Quotes.Count == 0);
        }

        var display = price.Display;
        var quote = book.Find(identity.Id, identity.Variant);
        var unit = quote is not null
            ? converter.Convert(quote.Amount, quote.Denomination, display, now)
            : converter.Convert(1m, identity.Id, display, now);
        if (!unit.Ok)
        {
            return new("Price", line.Headline, null, line.Detail ?? "", true, null, line.Headline);
        }

        var stack = item.StackSize ?? 1;
        var total = unit.Value * stack;
        var isMoney = Currencies.Display.Contains(identity.Id, StringComparer.OrdinalIgnoreCase);
        var big = isMoney && !identity.Id.Equals(display, StringComparison.OrdinalIgnoreCase)
            ? $"{ValueFormat.Amount(stack)} {Currencies.Short(identity.Id)} ≈ {ValueFormat.Money(total, display)}"
            : price.One(total);
        var checkedAgo = $"checked {ValueFormat.Age(now - book.CreatedAt)}";
        var note = Join(price.DivineRate(), checkedAgo) + (line.Freshness is Freshness.Fresh or null ? "" : " · may be out of date");
        return new(stack > 1 ? "This stack is worth" : "This is worth", big, stack > 1 && !isMoney ? $"{price.One(unit.Value)} each" : null,
            note, line.Freshness is not (Freshness.Fresh or null), null, note);
    }

    /// <summary>A price book baseline (e.g. a unique) when there are no listings to search. The card says
    /// how old the figure is, and when it may be out of date.</summary>
    public static PriceAnswer Baseline(PriceLine line, DateTimeOffset? now = null)
    {
        var age = line.ObservedTo is { } at && now is { } n ? $" · {ValueFormat.Age(n - at)}" : "";
        var stale = line.Freshness is Freshness.Stale or Freshness.RankingSuppressed ? " · may be out of date" : "";
        return new("This usually sells for about", line.Headline, null, line.Detail ?? "Rolls can change the price.", true, null,
            $"Typical price · rolls can change it{age}{stale}");
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
}
