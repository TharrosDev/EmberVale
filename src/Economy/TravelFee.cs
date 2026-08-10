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
    ///
    /// <b>A mount makes a LOCAL jump free (39B)</b>, and only a local one. The 15 gold buys a seat on
    /// somebody's cart, so a player with their own horse is paying for a thing they already own —
    /// which is the same shape as the two discounts already here: a deed frees travel to your land,
    /// and 38M's 250-gold permit frees the Crossway. At 400 gold against 15 it is about twenty-seven
    /// jumps to break even, so the mount stops being a pure sink and becomes an investment; the row
    /// in <c>docs/DESIGN.md</c> §6 says so rather than leaving the table wrong.
    ///
    /// ⚠️ <b>There is no default on <paramref name="mounted"/> on purpose.</b> A defaulted pricing
    /// input is how a caller silently charges the wrong number — the whole point of this type is that
    /// the price shown and the price taken are one function call, and a caller that forgot the third
    /// argument would still compile and still be wrong. There are two callers; both answer it.
    ///
    /// ⚠️ <b>Cross-region is deliberately NOT free, and that is a design line rather than an
    /// oversight.</b> A realm boundary is 38M's tolled crossing and a much longer road; a horse
    /// shortens a walk across the Ember Crown, it does not carry the player through the Crossway for
    /// nothing. Making both free would leave <see cref="CrossRegionFee"/> reachable only by a
    /// mountless player, which is a fee that exists for the poor alone.
    /// </summary>
    public static int For(bool ownedHolding, bool crossRegion, bool mounted)
    {
        if (ownedHolding)
        {
            return 0;
        }

        if (crossRegion)
        {
            return CrossRegionFee;
        }

        return mounted ? 0 : LocalFee;
    }
}
