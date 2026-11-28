namespace StillSaneExile.Domain.Guide;

/// <summary>One gem in a Path of Building skill group, as the code has it. SSE knows nothing else about
/// gems: no game data ships with it, so names, levels and notes are the build author's.</summary>
public sealed record PobGem(string Name, string? SkillId, string? GemId, int? Level, int? Quality, bool Enabled, string? Note)
{
    /// <summary>Path of Building writes the game's own ids for each gem: supports have a gem id under
    /// <c>Metadata/Items/Gem/SupportGem…</c> and a granted effect starting with <c>Support</c>. That is
    /// the only way SSE tells a support from a skill.</summary>
    public bool IsSupport =>
        (GemId?.Contains("/SupportGem", StringComparison.OrdinalIgnoreCase) ?? false)
        || (SkillId?.StartsWith("Support", StringComparison.Ordinal) ?? false);
}

/// <summary>A skill and the supports the author put with it.</summary>
public sealed record PobSkillGroup(string? Label, bool Enabled, string? Source, IReadOnlyList<PobGem> Gems)
{
    /// <summary>Granted by an item or the tree rather than cut from an uncut gem.</summary>
    public bool FromItem => Source is { Length: > 0 };

    public IEnumerable<PobGem> Skills => Gems.Where(g => g.Enabled && !g.IsSupport && g.Name.Length > 0);

    public IEnumerable<PobGem> Supports => Gems.Where(g => g.Enabled && g.IsSupport && g.Name.Length > 0);

    /// <summary>The skill the supports go into: the group's first skill.</summary>
    public PobGem? MainSkill => Skills.FirstOrDefault();
}

/// <summary>A loadout of skills. Guides often keep one per stage ("Act 1", "Act 2", "Endgame").</summary>
public sealed record PobSkillSet(int Id, string? Title, IReadOnlyList<PobSkillGroup> Groups);

/// <summary>One passive tree in the build. <see cref="Nodes"/> are the game's node ids, the same ids as
/// in Grinding Gear Games' passive tree export.</summary>
public sealed record PobTreeSpec(
    string? Title,
    string? TreeVersion,
    string? AscendancyId,
    IReadOnlyList<int> Nodes,
    IReadOnlyDictionary<int, int> WeaponSets,
    IReadOnlyDictionary<int, string> NodeNotes);

/// <summary>An item as Path of Building names it: rarity, name and base type. Its modifiers are not read.</summary>
public sealed record PobItem(int Id, string Rarity, string Name, string? BaseType);

public sealed record PobSlot(string Slot, PobItem Item);

public sealed record PobItemSet(int Id, string? Title, IReadOnlyList<PobSlot> Slots);

/// <summary>A number Path of Building worked out for the build when the code was made ("Life", "1520").</summary>
public sealed record PobStat(string Name, string Value);

/// <summary>What a Path of Building code for Path of Exile 2 carries, read locally from the text the
/// player pasted. Nothing is fetched.</summary>
public sealed record PobBuild
{
    public required string ClassName { get; init; }

    public string? Ascendancy { get; init; }

    public int Level { get; init; }

    /// <summary>The game version Path of Building aimed at, as written ("0_5").</summary>
    public string? TargetVersion { get; init; }

    public IReadOnlyList<PobSkillSet> SkillSets { get; init; } = [];

    /// <summary>Index into <see cref="SkillSets"/> of the set the author had open.</summary>
    public int ActiveSkillSet { get; init; }

    public IReadOnlyList<PobTreeSpec> Trees { get; init; } = [];

    /// <summary>Index into <see cref="Trees"/> of the tree the author had open.</summary>
    public int ActiveTree { get; init; }

    public IReadOnlyList<PobItemSet> ItemSets { get; init; } = [];

    public int ActiveItemSet { get; init; }

    /// <summary>The build's notes with Path of Building's colour codes taken out.</summary>
    public string Notes { get; init; } = "";

    public IReadOnlyList<PobStat> Stats { get; init; } = [];

    public string Title => Ascendancy is { Length: > 0 } a ? $"{a} ({ClassName})" : ClassName;

    public PobTreeSpec? Tree => Trees.Count == 0 ? null : Trees[Math.Clamp(ActiveTree, 0, Trees.Count - 1)];

    public PobItemSet? Items => ItemSets.Count == 0 ? null : ItemSets[Math.Clamp(ActiveItemSet, 0, ItemSets.Count - 1)];

    /// <summary>Every gem name in every skill set, compared without case.</summary>
    public IReadOnlySet<string> GemKeys => SkillSets.SelectMany(s => s.Groups).SelectMany(g => g.Gems).Select(g => GemName.Key(g.Name)).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
}
