namespace Embervale.Economy;

/// <summary>
/// What a fast-travel jump costs (Phase 38C). Pure and Godot-free like <see cref="ShopPricing"/> and
/// <see cref="ShopStock"/>, so the map screen's button state and the bootstrap's charge read the same
/// function and cannot drift — the Phase 37 rule that a refusal and the action behind it are one
/// decision.
///
/// Fast travel was free from Phase 25G until now. <c>docs/DESIGN.md</c> §6 names it as a sink from the
/// start ("gold drains into things the player *wants* … fast-travel/inn costs") and it is the purest
/// case for one: it buys nothing but convenience, which is exactly what §6 says money is for.
/// </summary>
public static class TravelFee
{
    /// <summary>A jump within the region you are already standing in.</summary>
    public const int LocalFee = 15;

    /// <summary>A jump across a realm boundary. Dearer because it replaces a much longer walk — and
    /// because a flat fee would make the cheap jump feel arbitrary rather than local.</summary>
    public const int CrossRegionFee = 40;

    /// <summary>
    /// The gold a jump costs. <b>Travel to a holding you own is free</b>, which is what keeps this from
    /// reading as a toll booth: your house becomes the anchor you can always afford to reach, and owning
    /// property gains an ongoing benefit rather than only the one-off 37A purchase. Resolved through the
    /// <c>TravelNodeId</c> that <c>PropertyResource</c> has authored since 37A, so there is no second
    /// link to keep in sync.
    /// </summary>
    public static int For(bool ownedHolding, bool crossRegion)
    {
        if (ownedHolding)
        {
            return 0;
        }

        return crossRegion ? CrossRegionFee : LocalFee;
    }
}
