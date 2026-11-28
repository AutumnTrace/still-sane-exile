using StillSaneExile.Domain.Data;

namespace StillSaneExile.Domain.Craft;

public enum CraftRarity
{
    Normal,
    Magic,
    Rare,
}

/// <summary>Regular, Greater or Perfect: the graded orbs only roll stats from a minimum level up.</summary>
public enum OrbGrade
{
    Regular,
    Greater,
    Perfect,
}

/// <summary>Where an orb sits on the bench: what starts a craft, what adds or changes stats, what finishes it.</summary>
public enum CraftGroup
{
    Start,
    Change,
    Finish,
}

public enum Omen
{
    SinistralExaltation,
    DextralExaltation,
    GreaterExaltation,
    Whittling,
    SinistralErasure,
    DextralErasure,
    SinistralAnnulment,
    DextralAnnulment,
}

/// <summary>One stat on the simulated item: a tier of a family, its rolled numbers (one per component, in line
/// order), and whether an essence put it there or a Fracturing Orb locked it.</summary>
public sealed record CraftMod(int Id, string Family, int Tier, IReadOnlyList<decimal> Values, bool Essence = false, bool Fractured = false);

/// <summary>The simulated item between two clicks. Immutable: history keeps every one.</summary>
public sealed record CraftItem(CraftRarity Rarity, string? Name, bool Corrupted, IReadOnlyList<CraftMod> Mods)
{
    /// <summary>A fresh Normal base, as a craft starts.</summary>
    public static CraftItem Normal { get; } = new(CraftRarity.Normal, null, false, []);
}

/// <summary>What one click is made with: the item level, the orb grade and the omens that are on.</summary>
public sealed record CraftSetup(int ItemLevel, OrbGrade Grade, IReadOnlySet<Omen> Omens)
{
    public static CraftSetup Of(int itemLevel, OrbGrade grade = OrbGrade.Regular, params Omen[] omens) => new(itemLevel, grade, omens.ToHashSet());
}

/// <summary>An orb or essence on the bench. <see cref="Id"/> is poe.ninja's id: its picture and its price
/// book line share it.</summary>
public sealed record CraftCurrency(string Id, string Name, string Short, string Tip, CraftGroup Group, bool Graded, EssenceData? Essence = null);

/// <summary>An omen: on until the next orb it changes is used, then gone.</summary>
/// <param name="Id">poe.ninja's id, as for orbs.</param>
/// <param name="Title">What it does, in three words: "Exalted → prefix".</param>
/// <param name="Orb">The orb it changes.</param>
public sealed record OmenInfo(Omen Omen, string Id, string Name, string Title, string Orb);

/// <summary>What a click did: the new item, or why nothing happened (then <see cref="Item"/> is null).</summary>
/// <param name="Fresh">The stats added or changed by this click.</param>
/// <param name="OmensUsed">The omens this click used up.</param>
public sealed record CraftOutcome(CraftItem? Item, string Message, IReadOnlySet<int> Fresh, IReadOnlyList<Omen> OmensUsed)
{
    public bool Ok => Item is not null;

    public static CraftOutcome No(string why) => new(null, why, new HashSet<int>(), []);
}

/// <summary>One tier that can roll next, with its weight against the others.</summary>
public sealed record PoolEntry(ModFamily Family, FamilyTier Tier, int Weight);

/// <summary>The chance that the next use of an orb adds one of the stats the player wants.</summary>
/// <param name="Estimate">True when the figure is approximate (two stats added at once); false when exact
/// for the data in use.</param>
public sealed record CraftOdds(string Currency, string Label, double Chance, bool Estimate);

/// <summary>A weapon's numbers as the simulated item has them.</summary>
public sealed record CraftWeapon(decimal Low, decimal High, decimal AttacksPerSecond, decimal PhysicalDps);
