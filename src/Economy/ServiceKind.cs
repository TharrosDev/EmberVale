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

    /// <summary>
    /// The consignment clerk's counter (Phase 38P): take the gold everything on the broker's shelf has
    /// earned so far.
    ///
    /// <b>The other half of a pair again, and for 38O's reason rather than 38M's.</b> Listing an item
    /// lives in the vendor window because the player has to choose <em>which</em> one; collecting has
    /// nothing to choose, so it is a service and inherits the price, the standing discount, the hostile
    /// refusal, the charge ordering and the whole prompt battery from 38D for about ten lines. The
    /// "nothing to do here" state is what makes an early press say <em>nothing has sold yet</em>
    /// instead of firing an empty payout.
    ///
    /// ⚠️ It is always free, and <c>--validate</c> enforces it: a fee to be handed money already owed
    /// is a second commission the broker's own cut has already taken.
    /// </summary>
    Collect,

    /// <summary>
    /// The appraiser's scales (Phase 38P2): opens a valuation of everything in the pack — who pays
    /// most for each thing, and what a broker would list it for.
    ///
    /// It is a <b>service that changes nothing</b>, which is new. Every other kind moves gold, goods,
    /// standing, the clock or a flag; this one only reads. That is still the right home for it: the
    /// prompt battery, the hostile refusal and the "nothing to do here" state are exactly what an
    /// appraiser needs, and it inherits all three for the cost of a member and a published event —
    /// the same three lines <see cref="Bank"/> spends opening a vault.
    ///
    /// ⚠️ Always free, and <c>--validate</c> enforces it. <see cref="ServiceRules"/> refuses any
    /// service the player cannot afford <em>before</em> the verb runs, so a fee would fail closed on
    /// the player with an empty purse and a full pack — precisely the person who walked over to ask
    /// what is worth carrying. 38O's priced-search rule, third instance.
    /// </summary>
    Appraise,

    /// <summary>
    /// A master's commission (Phase 38Q): he makes something you already know how to make, charges for
    /// his hands, and <b>supplies whatever materials you did not bring</b> at his own counter price.
    ///
    /// ⚠️ <b>The materials are the whole feature, and without them this kind should not exist.</b>
    /// <c>town_hub</c> has a free public forge twenty metres from the smith, so a master charging for
    /// labour alone is strictly worse than walking — correct, validated, saved and completely
    /// imperceptible, which is the failure that got 38G parked. What the player buys here is not
    /// having to go and dig up two iron ingots.
    ///
    /// ⚠️ <b>It is the first kind that is charged AFTER its verb, and the first that must be PRICED.</b>
    /// Every other service's verb cannot fail once the gold is taken, so 38D charges first; a
    /// commission fails cleanly whenever the pack is full and rolls itself back, so charging first
    /// would be the only way to lose money for nothing — see <c>CraftingComponent.Commission</c>.
    /// And 38O's priced-service rule (a fee that fails closed on the player who needs the counter
    /// most) has been the right call three times running and is the wrong call here: a free master
    /// hands out materials at cost, which is the shop spread deleted. <c>--validate</c> enforces
    /// both directions, so neither can be "tidied" into the other.
    /// </summary>
    Commission,

    /// <summary>
    /// The caravan board (Phase 38Q2): a rotating set of supply contracts — deliver so many of a good,
    /// take gold and standing.
    ///
    /// ⚠️ <b>It is a service and not a quest giver, and that distinction is the brief.</b>
    /// <c>QuestLogPanel</c> deliberately omits a Contracts heading, on the rule that the journal shows
    /// the states the data actually has — so a haulage job must never reach it. Being a service means
    /// the prompt battery, the hostile refusal and the free-service rule all arrive for the cost of a
    /// member and a published event, exactly as <see cref="Appraise"/> did.
    ///
    /// ⚠️ <b>Always available, unlike every other read-only kind.</b> <see cref="Appraise"/>,
    /// <see cref="Collect"/> and <see cref="Search"/> all answer "nothing to do here" from the player's
    /// pack; a board does not, because <em>reading</em> it is the point even empty-handed. An
    /// already-held state would tell a player with nothing on them that the board is closed.
    ///
    /// ⚠️ <b>Free, and <c>--validate</c> enforces it.</b> 38O's priced-service rule, fourth instance
    /// and the plainest of them: the player is being <em>paid</em> here. Note this is the opposite
    /// ruling to <see cref="Commission"/> one member above, which must be priced because it hands over
    /// goods — the two sit together on purpose so the distinction is visible from one screen.
    /// </summary>
    Contracts,

    /// <summary>
    /// A sword for hire (Phase 38R): gold buys a companion onto the roster.
    ///
    /// ⚠️ <b>It is the second kind charged AFTER its verb, and for exactly 38Q's reason.</b>
    /// <c>CompanionRoster.Recruit</c> returns false on a full party — so the verb can fail, and
    /// charging at the counter would be the one path that takes the money and delivers nobody. The
    /// full-party refusal is deliberately *not* folded into the already-held state: "you already
    /// travel with her" and "you have no room for her" are different sentences, and a player told the
    /// first about a mercenary they have never met would go looking for someone who is not there.
    ///
    /// ⚠️ <b>It must be PRICED, and <c>--validate</c> enforces it</b> — 38Q's ruling, second instance.
    /// A free companion is what <c>DialogueEffect.RecruitCompanion</c> already does, which is how Kael
    /// joins: a story recruit, earned. What makes this a *service* rather than a second route to the
    /// same thing is the coin, so a free one is the feature deleted and the plumbing kept.
    /// </summary>
    Mercenary,
}
