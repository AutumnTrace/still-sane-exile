namespace StillSaneExile.Domain.Pricing;

/// <summary>How loud a price is drawn, cheapest first. The price label borrows the ladder players already
/// read in their loot filter (white and red for a jackpot down to a dark tag for small change), so the
/// colour says roughly what an item is worth before the number is read.</summary>
public enum ValueTier
{
    /// <summary>Under 1 Exalted Orb.</summary>
    E,

    /// <summary>1 to 6 Exalted Orbs.</summary>
    D,

    /// <summary>6 to 30 Exalted Orbs.</summary>
    C,

    /// <summary>30 Exalted Orbs to 1 Divine Orb.</summary>
    B,

    /// <summary>1 to 10 Divine Orbs.</summary>
    A,

    /// <summary>10 Divine Orbs or more.</summary>
    S,
}

public static class ValueTiers
{
    /// <summary>The tier of a value known in both Divine and Exalted Orbs. The steps are round numbers in
    /// the unit players think in at that height, so they hold whatever the day's rate is.</summary>
    public static ValueTier Of(decimal divines, decimal exalteds) =>
        divines >= 10m ? ValueTier.S
        : divines >= 1m ? ValueTier.A
        : exalteds >= 30m ? ValueTier.B
        : exalteds >= 6m ? ValueTier.C
        : exalteds >= 1m ? ValueTier.D
        : ValueTier.E;
}
