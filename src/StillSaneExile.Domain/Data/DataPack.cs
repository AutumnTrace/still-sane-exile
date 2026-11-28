using System.Text.Json;
using System.Text.Json.Serialization;
using StillSaneExile.Domain.Items;

namespace StillSaneExile.Domain.Data;

/// <summary>A versioned bundle of base/modifier facts. Every pack states where its data came from and
/// what it may be used for. A synthetic pack is test data and is labelled as such wherever it is used.
/// Bases and essences are optional: the craft simulator needs them, reading an item does not.</summary>
public sealed record DataPack(
    int SchemaVersion,
    string Id,
    string Title,
    string Provenance,
    string Rights,
    string? VerifiedDate,
    IReadOnlyList<string> CompatibleBuilds,
    IReadOnlyList<string> Locales,
    IReadOnlyList<ModFamily> Families,
    IReadOnlyList<CraftBaseData>? Bases = null,
    IReadOnlyList<EssenceData>? Essences = null)
{
    public const int SupportedSchemaVersion = 1;

    public bool IsSynthetic => Provenance.Equals("synthetic", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A family of modifiers that occupy the same slot (only one per item). Hybrid families have
/// several lines; their tiers list one range per component, in line order.</summary>
public sealed record ModFamily(
    string Id,
    string Group,
    AffixSide Side,
    IReadOnlyList<string> ItemClasses,
    IReadOnlyList<FamilyLine> Lines,
    IReadOnlyList<FamilyTier> Tiers,
    bool TierOneIsBest = true)
{
    public bool IsHybrid => Lines.Count > 1;

    public int ComponentCount => Lines.Sum(l => l.Components);

    public bool AppliesTo(string itemClass) =>
        ItemClasses.Count == 0 || ItemClasses.Any(c => c.Equals(itemClass, StringComparison.OrdinalIgnoreCase));

    /// <summary>Index of the first component that belongs to line <paramref name="lineIndex"/>.</summary>
    public int ComponentOffset(int lineIndex) => Lines.Take(lineIndex).Sum(l => l.Components);
}

public sealed record FamilyLine(string Template, StatDirection Direction, string Unit, int Components = 1);

/// <param name="Weight">How often the tier rolls against the others that can (the craft simulator); null when the
/// pack does not say.</param>
public sealed record FamilyTier(int Tier, string Name, int Level, IReadOnlyList<decimal[]> Ranges, int? Weight = null)
{
    public (decimal Lo, decimal Hi) Range(int component) => (Ranges[component][0], Ranges[component][1]);
}

/// <summary>A base the craft simulator can start from: its physical damage and attack speed before any
/// modifier, and its implicit line.</summary>
public sealed record CraftBaseData(string Name, string ItemClass, decimal[] Physical, decimal AttacksPerSecond, string? Implicit = null);

/// <summary>An essence: the stat it guarantees, as a family and tier of the same pack.</summary>
public sealed record EssenceData(string Id, string Name, string Family, int Tier);

public sealed record PackLoadResult(DataPack? Pack, string? Error);

public static class DataPackLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Validates the whole pack before returning it. A pack that fails any check is not
    /// partially applied: the caller keeps its last good pack.</summary>
    public static PackLoadResult Load(string json)
    {
        DataPack? pack;
        try
        {
            pack = JsonSerializer.Deserialize<DataPack>(json, Options);
        }
        catch (JsonException ex)
        {
            return new PackLoadResult(null, $"Pack is not valid JSON: {ex.Message}");
        }

        if (pack is null)
        {
            return new PackLoadResult(null, "Pack is empty.");
        }

        if (pack.SchemaVersion != DataPack.SupportedSchemaVersion)
        {
            return new PackLoadResult(null, $"Pack schema {pack.SchemaVersion} is not supported (expected {DataPack.SupportedSchemaVersion}). Dependent rules stay off.");
        }

        if (string.IsNullOrWhiteSpace(pack.Provenance) || string.IsNullOrWhiteSpace(pack.Rights))
        {
            return new PackLoadResult(null, "Pack must state provenance and rights.");
        }

        foreach (var f in pack.Families)
        {
            var expected = f.ComponentCount;
            foreach (var t in f.Tiers)
            {
                if (t.Ranges.Count != expected)
                {
                    return new PackLoadResult(null, $"Family {f.Id} tier {t.Tier}: {t.Ranges.Count} ranges for {expected} components.");
                }

                if (t.Ranges.Any(r => r.Length != 2 || r[0] > r[1]))
                {
                    return new PackLoadResult(null, $"Family {f.Id} tier {t.Tier}: each range must be [min, max] with min ≤ max.");
                }

                if (t.Weight is < 0)
                {
                    return new PackLoadResult(null, $"Family {f.Id} tier {t.Tier}: a weight can't be below 0.");
                }
            }
        }

        foreach (var b in pack.Bases ?? [])
        {
            if (b.Physical is not [var lo, var hi] || lo > hi || b.AttacksPerSecond <= 0)
            {
                return new PackLoadResult(null, $"Base {b.Name}: physical damage must be [min, max] and attacks per second above 0.");
            }
        }

        foreach (var e in pack.Essences ?? [])
        {
            if (pack.Families.FirstOrDefault(f => f.Id == e.Family) is not { } family || family.Tiers.All(t => t.Tier != e.Tier))
            {
                return new PackLoadResult(null, $"Essence {e.Id}: family {e.Family} has no tier {e.Tier}.");
            }
        }

        var normalized = pack with
        {
            Families = pack.Families
                .Select(f => f with { Lines = f.Lines.Select(l => l with { Template = StatText.NormalizeTemplate(l.Template) }).ToList() })
                .ToList(),
        };
        return new PackLoadResult(normalized, null);
    }
}
