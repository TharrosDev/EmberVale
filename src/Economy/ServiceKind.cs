namespace Embervale.Economy;

/// <summary>
/// What a <see cref="ServiceResource"/> actually does for the player (Phase 38D). One component covers
/// all of them, branching on this the way <c>WorldEventDirector</c> branches on
/// <see cref="World.WorldEventKind"/> — the four share a price, a standing discount, an ordered refusal
/// and a gold charge, and differ only in the verb.
///
/// <b>There is no <c>Repair</c> member, deliberately.</b> No durability or condition concept exists
/// anywhere in the game — <c>StatType</c> has no such member and nothing in <c>src/</c> mentions wear.
/// 38D's own brief says repair lands only "if durability is adopted in 40", and 40B's rule is that cut
/// systems leave no stub, so a kind that resolved to nothing would be worse than its absence. The
/// deferral is recorded in <c>docs/DESIGN.md</c> §6 against Phase 40A.
/// </summary>
// APPEND ONLY: ordinals persist in .tres — never reorder/insert/remove (EnumStabilityTests).
public enum ServiceKind
{
    /// <summary>Teaches crafting recipes and grants XP for gold. Sells <em>access</em>, never a perk
    /// rank — see the §6 rule this resolves.</summary>
    Trainer,

    /// <summary>Opens a persistent vault: 37B's two-way storage without the property gate.</summary>
    Bank,

    /// <summary>Rests until a given hour, refilling every resource stat and advancing the clock.</summary>
    Inn,

    /// <summary>Sells the right to a mount. A stub only in that Phase 39A owns the mount itself; the
    /// purchase, its persistence and its refusal to sell twice are all real.</summary>
    Stable,

    /// <summary>
    /// Sells the right to cross a tolled road (Phase 38M) — a warden's permit, or the bribe his man
    /// takes at the side of the gate. Both are the same verb: pay, receive a flag
    /// <see cref="World.RegionResource.TollPermitFlagId"/> or
    /// <see cref="World.RegionResource.TollPassFlagId"/> names, and in the bribe's case pay part of
    /// the price in standing. Nothing else about it is new, which is 38D's branch-on-kind design
    /// collecting its rent: the price, the standing discount, the hostile refusal and the
    /// charge-before-the-verb ordering all come free.
    /// </summary>
    Passage,

    /// <summary>
    /// A warden's search (Phase 38O): every contraband stack out of the pack and into
    /// <see cref="ContrabandImpound"/>. Free, and the only service in the game the player would
    /// rather not buy — which is why it is a service at all rather than a bespoke component. It
    /// inherits the prompt battery, the hostile refusal and the "nothing to do here" state for free,
    /// and the last of those is what makes an honest traveller's press say *nothing to declare*
    /// instead of firing an empty search.
    /// </summary>
    Search,

    /// <summary>
    /// The bailiff's counter (Phase 38O): pay the fine, take the goods back.
    ///
    /// ⚠️ <b>A separate member from <see cref="Search"/>, and that is the opposite call to 38M's.</b>
    /// The permit and the bribe were one member because they are the same verb at three different
    /// numbers — pay, receive a flag. These two are not: one takes and one gives, they have opposite
    /// preconditions, and the price of this one is not authored at all but computed from what is held
    /// (<see cref="ContrabandLaw.Fine"/>). A single member would have needed a branch inside every
    /// shared function to tell them apart, which is the enum doing its job badly.
    /// </summary>
    Redeem,
}
