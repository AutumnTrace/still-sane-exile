using System.Globalization;
using System.Text;
using System.Text.Json;

namespace StillSaneExile.Domain.Pricing;

/// <summary>Which of poe.ninja's PoE2 overviews a category comes from.</summary>
public enum PoeNinjaOverview
{
    /// <summary><c>economy/exchange/current/overview</c>: currency and other exchange items, valued in the primary currency.</summary>
    Exchange,

    /// <summary><c>economy/stash/current/item/overview</c>: unique items, one line per unique (and variant).</summary>
    Items,
}

/// <summary>One <c>type=</c> of one overview, e.g. Exchange/Currency or Items/UniqueArmours.</summary>
public sealed record PoeNinjaCategory(PoeNinjaOverview Overview, string Type)
{
    public override string ToString() => $"{Overview}/{Type}";
}

/// <summary>The categories SSE asks poe.ninja for on each refresh. It is configuration, not code
/// (<c>data/poe-ninja.json</c>): a type poe.ninja renames or empties just comes back empty or 404 and is
/// skipped. Exchange types go first, Currency first of all, because its rates value everything else.</summary>
public sealed record PoeNinjaCatalog(IReadOnlyList<PoeNinjaCategory> Categories)
{
    /// <summary>The types that returned data on 27 November 2026.</summary>
    public static PoeNinjaCatalog Default { get; } = new(
    [
        .. new[] { "Currency", "Fragments", "Essences", "Runes", "SoulCores", "Idols", "UncutGems", "LineageSupportGems", "Ritual", "Delirium", "Expedition", "Verisium" }
            .Select(t => new PoeNinjaCategory(PoeNinjaOverview.Exchange, t)),
        .. new[] { "UniqueWeapons", "UniqueArmours", "UniqueAccessories", "UniqueFlasks", "UniqueCharms", "UniqueJewels", "UniqueTablets", "PrecursorTablets" }
            .Select(t => new PoeNinjaCategory(PoeNinjaOverview.Items, t)),
    ]);

    /// <summary>Reads <c>{"exchange": [...], "items": [...]}</c>. Blank and repeated types are dropped.
    /// Throws <see cref="InvalidDataException"/> when nothing usable is left.</summary>
    public static PoeNinjaCatalog Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, PoeNinjaJson.Tolerant);
            var list = new List<PoeNinjaCategory>();
            foreach (var (key, overview) in new[] { ("exchange", PoeNinjaOverview.Exchange), ("items", PoeNinjaOverview.Items) })
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(key, out var types) || types.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var t in types.EnumerateArray())
                {
                    var type = t.ValueKind == JsonValueKind.String ? t.GetString()?.Trim() : null;
                    if (!string.IsNullOrEmpty(type) && !list.Any(c => c.Overview == overview && c.Type.Equals(type, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(new PoeNinjaCategory(overview, type));
                    }
                }
            }

            return list.Count > 0 ? new PoeNinjaCatalog(list) : throw new InvalidDataException("The poe.ninja category list names no types.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The poe.ninja category list is not valid JSON: {e.Message}", e);
        }
    }
}

/// <summary>A league poe.ninja has economy data for. <see cref="Name"/> is what its API takes and what SSE stores.</summary>
public sealed record PoeNinjaLeague(string Name, string DisplayName, bool Hardcore);

public static class PoeNinjaLeagues
{
    /// <summary>The current economy leagues from <c>data/index-state</c>, in poe.ninja's order. Private and
    /// unindexed leagues, nameless entries and repeats are left out; old leagues are never offered.</summary>
    public static IReadOnlyList<PoeNinjaLeague> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, PoeNinjaJson.Tolerant);
        var list = new List<PoeNinjaLeague>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("economyLeagues", out var leagues) || leagues.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var l in leagues.EnumerateArray())
        {
            var name = PoeNinjaJson.Text(l, "name");
            if (name is null || PoeNinjaJson.Bool(l, "isPrivate") == true || PoeNinjaJson.Bool(l, "indexed") == false
                || list.Any(x => x.Name.Equals(name, StringComparison.Ordinal)))
            {
                continue;
            }

            list.Add(new PoeNinjaLeague(name, PoeNinjaJson.Text(l, "displayName") ?? name, PoeNinjaJson.Bool(l, "hardcore") == true));
        }

        return list;
    }
}

/// <summary>One overview answer as it arrived: its category, body and when SSE received it.</summary>
public sealed record PoeNinjaPayload(PoeNinjaCategory Category, string Json, DateTimeOffset ReceivedAt);

/// <summary>The book built from one refresh, and what was skipped on the way, in plain words.
/// <see cref="ExchangePrices"/> and <see cref="UniquePrices"/> count item lines, not the core rates.</summary>
public sealed record PoeNinjaBookResult(PriceBook Book, IReadOnlyList<string> Notes, int ExchangePrices, int UniquePrices)
{
    /// <summary>Whether poe.ninja priced anything for the league. Rates alone are not a price list.</summary>
    public bool HasPrices => ExchangePrices + UniquePrices > 0;
}

/// <summary>Variant keys for unique quotes. poe.ninja lists corrupted copies (and some variants) on
/// lines of their own; they stay separate quotes so a clean item never gets a corrupted price.</summary>
public static class UniqueVariant
{
    public const string Corrupted = "corrupted";

    public static string? Of(string? variant, bool corrupted)
    {
        var v = string.IsNullOrWhiteSpace(variant) ? null : variant.Trim();
        return corrupted ? (v is null ? Corrupted : $"{v}, {Corrupted}") : v;
    }

    public static bool IsCorrupted(string? variant) =>
        variant is not null && (variant.Equals(Corrupted, StringComparison.Ordinal) || variant.EndsWith(", " + Corrupted, StringComparison.Ordinal));

    /// <summary>The plain line for its corruption state: no variant beyond "corrupted" itself.</summary>
    public static bool IsPlain(string? variant) => variant is null || variant.Equals(Corrupted, StringComparison.Ordinal);
}

/// <summary>Turns poe.ninja's PoE2 overview answers into one immutable <see cref="PriceBook"/> for a league.
/// Exchange lines become exchange estimates and unique lines category baselines, both in the answer's
/// primary currency (Divine Orbs). The Currency answer's <c>core.rates</c> (how many Exalted and Chaos
/// Orbs one Divine buys) become quotes both ways, so the converter can show any price in ex, div or c.
/// The values are poe.ninja's own aggregates; nothing here looks at individual listings.</summary>
public static class PoeNinjaBook
{
    public const string Source = "poe.ninja";

    /// <summary>poe.ninja does not say what window an estimate covers. SSE records the hour before the
    /// answer arrived, which is the cadence it refreshes at.</summary>
    public static TimeSpan ObservationWindow { get; } = TimeSpan.FromHours(1);

    public static PoeNinjaBookResult Build(string league, IEnumerable<PoeNinjaPayload> payloads, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(league);
        ArgumentNullException.ThrowIfNull(payloads);

        var notes = new List<string>();
        var names = new Dictionary<string, (string Name, string? Category)>(StringComparer.Ordinal);
        var exchange = new List<(string Id, decimal Value, decimal? VolumeValue, string Primary, PoeNinjaPayload From)>();
        var uniques = new Dictionary<(string Name, string Base, string? Variant), (Quote Quote, MarketIdentity Identity, decimal Listings)>();
        var volumeValue = new Dictionary<string, decimal>(StringComparer.Ordinal);
        Core? rates = null;
        string? primary = null;

        foreach (var payload in payloads)
        {
            var type = payload.Category.Type;
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(payload.Json, PoeNinjaJson.Tolerant);
            }
            catch (JsonException)
            {
                notes.Add($"{type}: the answer was not readable JSON, so it was skipped.");
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;
                var core = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("core", out var c) && c.ValueKind == JsonValueKind.Object ? c : (JsonElement?)null;
                var payloadPrimary = core is { } k ? PoeNinjaJson.Text(k, "primary") : null;
                if (payloadPrimary is null)
                {
                    notes.Add($"{type}: no reference currency in the answer, so it was skipped.");
                    continue;
                }

                primary ??= payloadPrimary;
                Names(core!.Value, "items", names);
                Names(root, "items", names);
                var payloadRates = Rates(core.Value);
                if (payloadRates.Count > 0 && (rates is null || (!rates.FromCurrency && IsCurrency(payload.Category))))
                {
                    rates = new Core(payloadPrimary, payloadRates, payload, IsCurrency(payload.Category));
                }

                var lines = root.TryGetProperty("lines", out var l) && l.ValueKind == JsonValueKind.Array ? l : (JsonElement?)null;
                if (lines is null || lines.Value.GetArrayLength() == 0)
                {
                    notes.Add($"{type}: no prices in this league.");
                    continue;
                }

                if (payload.Category.Overview == PoeNinjaOverview.Exchange)
                {
                    foreach (var line in lines.Value.EnumerateArray())
                    {
                        var id = PoeNinjaJson.Text(line, "id");
                        var value = PoeNinjaJson.Number(line, "primaryValue");
                        var volume = PoeNinjaJson.Number(line, "volumePrimaryValue");
                        if (id is null || value is not > 0 || id.Equals(payloadPrimary, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (volume is > 0 && payloadPrimary.Equals(primary, StringComparison.Ordinal))
                        {
                            volumeValue.TryAdd(id, volume.Value);
                        }

                        exchange.Add((id, value.Value, volume, payloadPrimary, payload));
                    }
                }
                else
                {
                    AddUniques(league, payload, payloadPrimary, lines.Value, uniques, notes);
                }
            }
        }

        var quotes = new List<Quote>();
        var identities = new List<MarketIdentity>();
        var takenNames = new HashSet<string>(StringComparer.Ordinal);
        var quoted = new HashSet<string>(StringComparer.Ordinal);

        void Identify(string id, string? category)
        {
            if (!names.TryGetValue(id, out var n) || identities.Any(i => i.Id.Equals(id, StringComparison.Ordinal)) || !takenNames.Add(n.Name))
            {
                return;
            }

            var kind = (n.Category ?? category) is "Currency" ? IdentityKind.Currency : IdentityKind.Commodity;
            identities.Add(new MarketIdentity(id, kind, n.Name));
        }

        // Rates first: the primary currency priced in each other one, and back, from one core.
        if (rates is not null)
        {
            Identify(rates.Primary, "Currency");
            foreach (var (currency, rate) in rates.Values)
            {
                if (currency.Equals(rates.Primary, StringComparison.Ordinal))
                {
                    continue;
                }

                var primaryUnits = volumeValue.TryGetValue(currency, out var v) ? v : (decimal?)null;
                var at = rates.From.ReceivedAt;
                var provenance = $"poe.ninja exchange overview, {rates.From.Category.Type}: core rate, {ValueFormat.Amount(rate)} {currency} per {rates.Primary}";
                quotes.Add(Quote(rates.Primary, null, rate, currency, ObservationKind.ExchangeEstimate, league, at, primaryUnits, provenance));
                quotes.Add(Quote(currency, null, 1m / rate, rates.Primary, ObservationKind.ExchangeEstimate, league, at, primaryUnits * rate, provenance));
                quoted.Add(currency);
                Identify(currency, "Currency");
            }
        }
        else
        {
            notes.Add("No exchange rates came back, so prices show in Divine Orbs only.");
        }

        var unnamed = 0;
        foreach (var (id, value, volume, denomination, from) in exchange)
        {
            if (quoted.Contains(id))
            {
                continue; // a core rate already covers it, or an earlier category listed it
            }

            if (!names.ContainsKey(id))
            {
                unnamed++;
                continue; // a price SSE could never match to an item copy
            }

            quoted.Add(id);
            quotes.Add(Quote(id, null, value, denomination, ObservationKind.ExchangeEstimate, league, from.ReceivedAt,
                volume is > 0 ? volume.Value / value : null, $"poe.ninja exchange overview, {from.Category.Type}"));
            Identify(id, from.Category.Type == "Currency" ? "Currency" : null);
        }

        if (unnamed > 0)
        {
            notes.Add($"{unnamed} exchange line(s) had no item name and were skipped.");
        }

        foreach (var (quote, identity, _) in uniques.Values)
        {
            quotes.Add(quote);
            identities.Add(identity);
        }

        var book = new PriceBook(
            SnapshotIdFor(createdAt),
            league,
            MarketEra: null,
            PrimaryCurrency: rates?.Primary ?? primary ?? "divine",
            IsSynthetic: false,
            createdAt,
            quotes,
            identities);
        var exchangePriced = exchange.Select(e => e.Id).Where(quoted.Contains).Distinct(StringComparer.Ordinal).Count();
        return new PoeNinjaBookResult(book, notes, exchangePriced, uniques.Count);
    }

    /// <summary>"poe.ninja-20261127T140512Z": sortable, and the same text as the snapshot's file name.</summary>
    public static string SnapshotIdFor(DateTimeOffset createdAt) =>
        "poe.ninja-" + createdAt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static void AddUniques(string league, PoeNinjaPayload payload, string denomination, JsonElement lines,
        Dictionary<(string, string, string?), (Quote, MarketIdentity, decimal)> uniques, List<string> notes)
    {
        var noBase = 0;
        foreach (var line in lines.EnumerateArray())
        {
            var name = PoeNinjaJson.Text(line, "name");
            var baseType = PoeNinjaJson.Text(line, "baseType");
            var value = PoeNinjaJson.Number(line, "primaryValue");
            if (name is null || value is not > 0)
            {
                continue;
            }

            if (baseType is null)
            {
                noBase++; // a unique is never identified by its name alone
                continue;
            }

            var variant = UniqueVariant.Of(PoeNinjaJson.Text(line, "variant"), PoeNinjaJson.Bool(line, "corrupted") == true);
            var listings = PoeNinjaJson.Number(line, "listingCount") ?? 0m;
            var key = (name, baseType, variant);
            if (uniques.TryGetValue(key, out var seen) && seen.Item3 >= listings)
            {
                continue; // the same unique twice: keep the line with more listings behind it
            }

            var id = "unique." + Slug(PoeNinjaJson.Text(line, "detailsId") ?? $"{name} {baseType}");
            var provenance = $"poe.ninja item overview, {payload.Category.Type}{(variant is null ? "" : $", {variant}")}";
            var quote = Quote(id, variant, value.Value, denomination, ObservationKind.CategoryBaseline, league, payload.ReceivedAt,
                listings > 0 ? listings : null, provenance);
            uniques[key] = (quote, new MarketIdentity(id, IdentityKind.Unique, name, baseType, variant), listings);
        }

        if (noBase > 0)
        {
            notes.Add($"{payload.Category.Type}: {noBase} unique line(s) had no base type and were skipped.");
        }
    }

    private static Quote Quote(string id, string? variant, decimal amount, string denomination, ObservationKind kind, string league,
        DateTimeOffset receivedAt, decimal? volume, string provenance) =>
        new(id, variant, amount, denomination, kind, league, Source, receivedAt - ObservationWindow, receivedAt, receivedAt, volume, provenance);

    private static bool IsCurrency(PoeNinjaCategory category) =>
        category.Overview == PoeNinjaOverview.Exchange && category.Type.Equals("Currency", StringComparison.OrdinalIgnoreCase);

    private static void Names(JsonElement parent, string property, Dictionary<string, (string, string?)> names)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (PoeNinjaJson.Text(item, "id") is { } id && PoeNinjaJson.Text(item, "name") is { } name)
            {
                names.TryAdd(id, (name, PoeNinjaJson.Text(item, "category")));
            }
        }
    }

    private static List<(string Currency, decimal Rate)> Rates(JsonElement core)
    {
        var list = new List<(string, decimal)>();
        if (!core.TryGetProperty("rates", out var rates) || rates.ValueKind != JsonValueKind.Object)
        {
            return list;
        }

        foreach (var p in rates.EnumerateObject())
        {
            if (p.Name.Length > 0 && PoeNinjaJson.Number(p.Value) is > 0 and var rate)
            {
                list.Add((p.Name, rate));
            }
        }

        return list;
    }

    /// <summary>Lower-case letters and digits, anything else one hyphen: "mageblood-utility-belt".</summary>
    internal static string Slug(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        return sb.ToString().TrimEnd('-');
    }

    private sealed record Core(string Primary, List<(string Currency, decimal Rate)> Values, PoeNinjaPayload From, bool FromCurrency);
}

/// <summary>Reads poe.ninja's JSON without trusting its shape: a missing or mistyped field is null, never an exception.</summary>
internal static class PoeNinjaJson
{
    public static readonly JsonDocumentOptions Tolerant = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public static string? Text(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            && v.GetString() is { } s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    public static bool? Bool(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

    public static decimal? Number(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) ? Number(v) : null;

    /// <summary>Decimal when it fits; exponent forms such as <c>5.9E-05</c> go through double.</summary>
    public static decimal? Number(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (v.TryGetDecimal(out var d))
        {
            return d;
        }

        return v.TryGetDouble(out var x) && double.IsFinite(x) && Math.Abs(x) < 7.9e27 ? (decimal)x : null;
    }
}
