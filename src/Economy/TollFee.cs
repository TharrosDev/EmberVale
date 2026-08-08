namespace Embervale.Economy;

/// <summary>Why a crossing is free, charged, or refused (Phase 38M).</summary>
public enum TollOutcome
{
    /// <summary>No toll is authored on this crossing — the case every region but the Crossway is in.</summary>
    Free,

    /// <summary>A permit exempts. Bought once from the warden and carried forever.</summary>
    PermitHeld,

    /// <summary>A one-crossing pass covers it, and is spent doing so.</summary>
    PassSpent,

    /// <summary>The toll is owed and affordable.</summary>
    Charged,

    /// <summary>The toll is owed and the purse is short. The road stays shut.</summary>
    CannotAfford,
}

/// <summary>
/// What a region crossing costs (Phase 38M). Pure and Godot-free like <see cref="ShopPricing"/>,
/// <see cref="ShopStock"/>, <see cref="TravelFee"/> and <see cref="ServiceRules"/> — 38C learned this
/// the hard way when the vendor purse arithmetic started inside a Godot <c>Node</c> and could be
/// neither unit-tested nor driven headlessly, and 38L met the same wall from the other side (the test
/// project throws an <c>AccessViolationException</c> constructing any Godot <c>Resource</c>, so this
/// takes primitives).
///
/// <b>Fast travel does not come through here.</b> A jump already pays <see cref="TravelFee"/>, and
/// charging it twice for one journey is precisely the toll-booth feel 38C designed against. The road
/// is tolled; the map is not. That a toll-free route across the realm therefore exists is the
/// decision, not an oversight — it turns the 38C fee into the thing the wardens are competing with.
/// </summary>
public static class TollFee
{
    /// <summary>
    /// Resolves one crossing. <b>The order is the behaviour</b>, the way
    /// <see cref="ServiceRules.Resolve"/> is: a free road is free before anything is looked up; a
    /// permit answers before a pass, so a permit holder never burns a bribe they are still carrying
    /// (the mirror of 37A's rule that already-held comes before the price); and only then does the
    /// purse decide. Affordability goes through <see cref="ShopPricing.CanAfford"/> rather than a
    /// second <c>&gt;=</c> — one economy, one test.
    /// </summary>
    public static TollOutcome Resolve(bool hasPermit, bool hasPass, int fee, int goldHeld)
    {
        if (fee <= 0)
        {
            return TollOutcome.Free;
        }

        if (hasPermit)
        {
            return TollOutcome.PermitHeld;
        }

        if (hasPass)
        {
            return TollOutcome.PassSpent;
        }

        return ShopPricing.CanAfford(fee, goldHeld) ? TollOutcome.Charged : TollOutcome.CannotAfford;
    }
}
