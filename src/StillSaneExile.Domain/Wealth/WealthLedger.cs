using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Domain.Wealth;

public enum ImportStatus
{
    /// <summary>Not stored yet; applying adds it.</summary>
    New,

    /// <summary>The same quantity is already stored for the same holding and time: applying skips it,
    /// which is what makes importing the same file twice harmless.</summary>
    AlreadyStored,

    /// <summary>A different quantity is stored for the same holding and time. Two counts can't both be
    /// true at one instant, so the import stops until the file is fixed.</summary>
    Conflict,
}

/// <summary>How one import row compares with what is stored. <see cref="OlderThanStored"/> means the
/// ledger already has a later count for this holding, so the row becomes history and the current
/// quantity doesn't change.</summary>
public sealed record ImportCheck(HoldingEntry Entry, ImportStatus Status, long? StoredQuantity, bool OlderThanStored)
{
    public string? Error => Status == ImportStatus.Conflict
        ? $"Already stored as {StoredQuantity} for this tab, item and time; this file says {Entry.Quantity}."
        : null;
}

/// <summary>The rules of the wealth ledger, kept free of storage so they can be tested: what "current"
/// means, how an import compares with what is stored, and when a typed quantity is recorded. All
/// observations passed in belong to one profile and one league.</summary>
public static class WealthLedger
{
    /// <summary>The latest observation of each holding, leaving out holdings whose latest count is 0.
    /// Sorted by tab, then item, then variant.</summary>
    public static IReadOnlyList<HoldingEntry> Current(IEnumerable<HoldingEntry> observations) =>
        observations
            .GroupBy(o => o.Key)
            .Select(g => g.MaxBy(o => o.SnapshotAt)!)
            .Where(o => o.Quantity > 0)
            .OrderBy(o => o.Tab, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.ItemId, StringComparer.Ordinal)
            .ThenBy(o => o.Variant ?? "", StringComparer.Ordinal)
            .ToList();

    /// <summary>Compares import rows with the stored observations: new, already stored (same quantity at
    /// the same time) or in conflict (a different quantity at the same time).</summary>
    public static IReadOnlyList<ImportCheck> Check(IEnumerable<HoldingEntry> entries, IEnumerable<HoldingEntry> stored)
    {
        var at = new Dictionary<(HoldingKey, DateTimeOffset), long>();
        var latest = new Dictionary<HoldingKey, DateTimeOffset>();
        foreach (var s in stored)
        {
            at[(s.Key, s.SnapshotAt)] = s.Quantity;
            if (!latest.TryGetValue(s.Key, out var l) || s.SnapshotAt > l)
            {
                latest[s.Key] = s.SnapshotAt;
            }
        }

        return entries.Select(e =>
        {
            var older = latest.TryGetValue(e.Key, out var l) && l > e.SnapshotAt;
            return at.TryGetValue((e.Key, e.SnapshotAt), out var q)
                ? new ImportCheck(e, q == e.Quantity ? ImportStatus.AlreadyStored : ImportStatus.Conflict, q, older)
                : new ImportCheck(e, ImportStatus.New, null, older);
        }).ToList();
    }

    /// <summary>When a typed quantity is recorded: now, or one second after the latest stored count of
    /// the same holding if that is not earlier. A typed quantity always means "what I have now", and the
    /// demo clock starts at the same instant on every run, so "now" can be behind what an earlier demo
    /// run saved.</summary>
    public static DateTimeOffset ManualTime(DateTimeOffset now, HoldingKey key, IEnumerable<HoldingEntry> stored)
    {
        var latest = stored.Where(s => s.Key == key).Select(s => (DateTimeOffset?)s.SnapshotAt).Max();
        return latest is { } l && l >= now ? l.AddSeconds(1) : now.ToUniversalTime();
    }
}

/// <summary>What a holding is worth in the display currency, or why it has no value.</summary>
public sealed record HoldingValue(bool Priced, decimal Value, string Denomination, Quote? Quote, string Note)
{
    public static HoldingValue Unpriced(string display, string why) => new(false, 0m, display, null, why);
}

/// <summary>Values holdings with the active price book and nothing else. An item the book doesn't price
/// is counted but not valued: no guesses, and no rates from another league.</summary>
public static class HoldingValuation
{
    /// <summary>The price book quotes the item, or the item is the book's own primary currency (an
    /// Exalted Orb is worth one Exalted Orb, but only where the book has rates to convert it).</summary>
    public static bool IsPriced(PriceBook book, string itemId, string? variant) =>
        book.Find(itemId, variant) is { Amount: > 0 } || IsPrimary(book, itemId, variant);

    public static HoldingValue Value(HoldingEntry h, PriceBook book, CurrencyConverter converter, string display, DateTimeOffset now)
    {
        if (!string.Equals(h.League, book.League, StringComparison.Ordinal))
        {
            return HoldingValue.Unpriced(display, "This price book is for another league.");
        }

        var quote = book.Find(h.ItemId, h.Variant) is { Amount: > 0 } q ? q : null;
        if (quote is null && !IsPrimary(book, h.ItemId, h.Variant))
        {
            return HoldingValue.Unpriced(display, book.Quotes.Count == 0 ? "No prices in this league yet." : "Not in this league's price book.");
        }

        // A currency shown in its own denomination is exactly its count, not a round trip through a rate.
        if (h.Variant is null && h.ItemId.Equals(display, StringComparison.OrdinalIgnoreCase))
        {
            return new HoldingValue(true, h.Quantity, display, quote, "");
        }

        var c = quote is null
            ? converter.Convert(h.Quantity, h.ItemId, display, now)
            : converter.Convert(quote.Amount * h.Quantity, quote.Denomination, display, now);
        var from = quote?.Denomination ?? h.ItemId;
        return c.Ok
            ? new HoldingValue(true, c.Value, c.Denomination, quote, "")
            : HoldingValue.Unpriced(display, $"Priced in {Currencies.Long(from)}, with no rate to {Currencies.Long(display)}.");
    }

    /// <summary>What the price book knows about the item (its name and kind), or null.</summary>
    public static MarketIdentity? Identity(PriceBook book, string itemId, string? variant) =>
        book.Identities?.FirstOrDefault(i => i.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase)
            && (variant is null || i.Variant is null || string.Equals(i.Variant, variant, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The item's name from the price book's identities, or null when the book doesn't know it.</summary>
    public static string? Name(PriceBook book, string itemId, string? variant) => Identity(book, itemId, variant)?.Name;

    private static bool IsPrimary(PriceBook book, string itemId, string? variant) =>
        variant is null && book.Quotes.Count > 0 && book.PrimaryCurrency.Equals(itemId, StringComparison.OrdinalIgnoreCase);
}
