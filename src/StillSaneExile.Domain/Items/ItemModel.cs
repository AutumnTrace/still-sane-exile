namespace StillSaneExile.Domain.Items;

/// <summary>Which in-game copy produced the text. Advanced copy carries affix headers and roll ranges.</summary>
public enum CopyKind
{
    Normal,
    Advanced,
}

public enum ItemRarity
{
    Unknown,
    Normal,
    Magic,
    Rare,
    Unique,
    Currency,
    Gem,
    Other,
}

/// <summary>Where a modifier line comes from. Sources are never merged: an implicit and an explicit
/// with the same wording stay two separate groups.</summary>
public enum ModSource
{
    Explicit,
    Implicit,
    Rune,
    Enchant,
    Unique,
    Desecrated,
    Fractured,
    Crafted,
    Unknown,
}

public enum AffixSide
{
    None,
    Prefix,
    Suffix,
}

public enum StatDirection
{
    /// <summary>No verified direction: draw a position marker, never a "good/bad" fill.</summary>
    Unknown,
    HigherIsBetter,
    LowerIsBetter,
    /// <summary>Depends on the build (for example a trade-off stat). Position marker only.</summary>
    BuildDependent,
}

/// <summary>Who vouches for the min/max of a component.</summary>
public enum RangeProvenance
{
    None,
    /// <summary>The game printed the range in an advanced copy.</summary>
    AdvancedCopy,
    /// <summary>A loaded data pack resolved exactly one explanation for the displayed value.</summary>
    DataPack,
    /// <summary>The game printed no range in an advanced copy: the value cannot roll.</summary>
    FixedByGame,
}

[Flags]
public enum ItemStates
{
    None = 0,
    Corrupted = 1,
    Unidentified = 2,
    Mirrored = 4,
    Fractured = 8,
    Desecrated = 16,

    /// <summary>Sanctified: its values are fixed for good (docs/GAME.md §1.7).</summary>
    Sanctified = 32,
}

/// <summary>One number inside a modifier line, e.g. the "26" in "Adds 26 to 43 Physical Damage".</summary>
public sealed record StatComponent(
    decimal Value,
    decimal? Min,
    decimal? Max,
    StatDirection Direction,
    RangeProvenance RangeSource,
    string Unit);

/// <summary>A single displayed modifier line with its numbers pulled out.</summary>
public sealed record ModLine(
    string Text,
    string Template,
    IReadOnlyList<StatComponent> Components,
    bool IsReminderText = false)
{
    /// <summary>The line as the game shows it in a normal tooltip: advanced-copy ranges removed.</summary>
    public string Display => StatText.WithoutRanges(Text);
}

/// <summary>An affix (or implicit, rune effect, enchant…) and the lines it produced. A hybrid affix
/// is one group with several lines.</summary>
/// <param name="FromCorruption">The game says corruption added it: an advanced copy heads it
/// "{ Corruption Enhancement }". A normal copy ends corruption enchants and anointments alike in
/// "(enchant)", so there it stays false.</param>
public sealed record ModGroup(
    ModSource Source,
    AffixSide Side,
    string? AffixName,
    int? Tier,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ModLine> Lines,
    bool FromAdvancedCopy,
    string? FamilyId = null,
    bool FromCorruption = false);

public sealed record Requirements(int? Level, int? Strength, int? Dexterity, int? Intelligence);

/// <summary>Weapon numbers exactly as displayed. Displayed values already include local mods and quality.</summary>
public sealed record WeaponProperties(
    decimal? PhysicalMin,
    decimal? PhysicalMax,
    decimal? AttacksPerSecond,
    decimal? CriticalHitChance,
    decimal? ReloadTime);

/// <summary>Immutable record of one item copy. Inspect, Craft and Wealth all read the same snapshot.</summary>
public sealed record ItemSnapshot(
    string CaptureId,
    CopyKind Copy,
    string Locale,
    string? Patch,
    DateTimeOffset CapturedAt,
    string ItemClass,
    ItemRarity Rarity,
    string? Name,
    string BaseType,
    string? StableId,
    int? ItemLevel,
    Requirements Requirements,
    int? Quality,
    WeaponProperties? Weapon,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<ModGroup> Mods,
    ItemStates States,
    string? Sockets,
    int? StackSize,
    int? MaxStackSize,
    string? Note,
    IReadOnlyList<string> FlavourText,
    IReadOnlyList<string> UnparsedLines,
    string RawText)
{
    public string DisplayName => Name is null ? BaseType : $"{Name}, {BaseType}";

    public IEnumerable<ModGroup> ModsFrom(ModSource source) => Mods.Where(m => m.Source == source);
}
