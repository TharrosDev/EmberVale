using System;
using Embervale.Factions;
using Embervale.Items;

namespace Embervale.Economy;

/// <summary>
/// The whole of the money arithmetic (Phase 38A) — a buy/sell spread over
/// <see cref="ItemInstance.Value"/>, and the two questions a refusal hangs off. Pure and Godot-free
/// on purpose, exactly as <see cref="Housing.PropertyClaim"/> is: prices are player-facing numbers, so
/// the rounding is pinned by <c>ShopPricingTests</c> rather than by reading. The test project may not
/// construct Godot objects, which is why every parameter here is a plain value — the same reason
/// <c>ItemPresentation</c> takes them.
///
/// There is exactly one price authority in the game and this is not it:
/// <see cref="ItemInstance.Value"/> already folds in rarity and affix count, so the spread applies to
/// rolled loot for free and no second table can drift from it.
///
/// <b>Deliberately not a <c>Resolve</c>-style outcome table</b> like <c>PropertyClaim</c>'s. A
/// purchase's other refusal — no room in the pack — is not knowable from pure inputs: only
/// <c>InventoryComponent.AddInstance</c>'s return can say whether an existing stack had space. An
/// enum member for it would have to be handed the answer, so the panel owns that branch and this owns
/// the arithmetic.
/// </summary>
public static class ShopPricing
{
    /// <summary>
    /// What a merchant shaves off her asking price for her own trade (Phase 38F). Small on purpose: the
    /// specialty is meant to be felt on the <em>sell</em> side, where the player has a routing decision
    /// to make. A deep buy-side discount would just make one shop the place to buy everything it stocks.
    /// </summary>
    public const float SpecialtyBuyDiscount = 0.95f;

    /// <summary>
    /// What a merchant pays over the odds for her own trade (Phase 38F) — the number that makes
    /// <em>where</em> the player sells matter for the first time. It is deliberately large enough to be
    /// worth walking across town for.
    ///
    /// ⚠️ This cannot invert the spread no matter how it is authored: <see cref="SellPrice"/> clamps its
    /// fraction to <c>0..1</c>, so the bonus can raise a payout to the item's value and no further, while
    /// <see cref="BuyPrice"/> clamps its markup to <c>&gt;= 1</c>. What it <em>can</em> do is narrow the
    /// spread to nothing, which is frictionless churn rather than a money printer — that is a content
    /// bug, so <c>--validate</c> holds every shop to a margin the arithmetic cannot enforce.
    /// </summary>
    public const float SpecialtySellBonus = 1.25f;

    /// <summary>
    /// What the vendor charges. Rounds <b>up</b> and floors at <c>1</c>: a <c>Value = 1</c> trinket
    /// against a fractional markup must never round its way to free, which is an infinite item.
    /// The markup is clamped to <c>&gt;= 1</c> — the validator rejects a smaller one, this makes a
    /// hand-edited <c>.tres</c> harmless anyway.
    /// </summary>
    public static int BuyPrice(int baseValue, float markup) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(0, baseValue) * Math.Max(1f, markup)));

    /// <summary>
    /// What the vendor pays. Rounds <b>down</b> and floors at <c>0</c> — a negative payout would have
    /// the player paying to hand things over. The fraction is clamped to <c>0..1</c>, which is what
    /// makes <c>SellPrice &lt;= BuyPrice</c> true by construction for <em>any</em> authored spread and
    /// closes the buy-low-sell-higher money printer in the arithmetic rather than only in the data.
    /// </summary>
    public static int SellPrice(int baseValue, float fraction) =>
        Math.Max(0, (int)Math.Floor(Math.Max(0, baseValue) * Math.Clamp(fraction, 0f, 1f)));

    /// <summary>Whether the player can pay. Read by both the Buy button's enabled state and the press
    /// itself, so the two cannot drift — the same rule every Phase 37 refusal follows.</summary>
    public static bool CanAfford(int price, int goldHeld) => goldHeld >= price;

    /// <summary>
    /// What a merchant's standing with the player does to their asking price (Phase 38C). Below
    /// <c>1</c> is a discount, above it a surcharge.
    ///
    /// This is the <b>first thing in the game to read a reputation tier and change a number</b>.
    /// <c>ReputationComponent</c> has existed since Phase 16 with exactly two behavioural readers, and
    /// both ask the same boolean (<c>IsHostile</c>) — standing was otherwise written and displayed but
    /// never consulted.
    ///
    /// The hostile half of the ramp carries a <b>surcharge rather than nothing</b>: a seven-step ramp
    /// where three steps are inert is not "standing modifies prices". <c>Hated</c> and <c>Hostile</c>
    /// still get a number here because a faction's <c>HostileThreshold</c> is authored per faction — a
    /// clan may deal with someone the villagers would turn away, and refusing to trade is
    /// <c>ReputationComponent.IsHostile</c>'s call, not this table's.
    /// </summary>
    public static float PriceMultiplierFor(ReputationTier tier) => tier switch
    {
        ReputationTier.Hated => 1.35f,
        ReputationTier.Hostile => 1.25f,
        ReputationTier.Unfriendly => 1.15f,
        ReputationTier.Neutral => 1f,
        ReputationTier.Friendly => 0.95f,
        ReputationTier.Honored => 0.9f,
        _ => 0.85f,
    };

    /// <summary>
    /// The markup to hand <see cref="BuyPrice"/> once standing is taken into account — the one home for
    /// the multiplication, so no call site can apply it twice or forget it.
    ///
    /// <b>No new invariant appears here, and that is the point.</b> <see cref="BuyPrice"/> already
    /// clamps to <c>&gt;= 1</c> and <see cref="SellPrice"/> to <c>&lt;= 1</c>, so
    /// <c>sell &lt;= value &lt;= buy</c> holds for <em>any</em> multiplier: a discount cannot invert the
    /// spread into a money printer. The 38A clamp earns its keep a second time.
    ///
    /// What the clamp does hide is a content bug — a shop authored near <c>1.0</c> bottoms out partway
    /// up the ramp, so its best two tiers pay the same price. <c>--validate</c> reports that, because
    /// the arithmetic cannot.
    ///
    /// <b>Only the buy side moves.</b> A merchant who likes you paying more for your loot is symmetric
    /// and tempting, but with both clamps in play a generous sell fraction converges on
    /// <c>sell == buy</c> — frictionless churn that is pointless rather than exploitable — and standing
    /// already modifies prices without it.
    /// </summary>
    public static float MarkupFor(float markup, ReputationTier tier, bool specialty = false) =>
        markup * PriceMultiplierFor(tier) * (specialty ? SpecialtyBuyDiscount : 1f);

    /// <summary>
    /// The fraction to hand <see cref="SellPrice"/> once the merchant's trade is taken into account
    /// (Phase 38F) — the sell side's counterpart to <see cref="MarkupFor"/>, so the multiplication has
    /// one home on each side of the spread and no call site can apply it twice or forget it.
    ///
    /// <b>Standing is deliberately absent here.</b> Only the buy side moves with reputation
    /// (<c>CLAUDE.md</c> §8) and 38F does not reopen that: a specialty is a property of the
    /// <em>merchant's trade</em>, not of how she feels about the player, so the two are different
    /// questions that happen to both end in a price.
    /// </summary>
    public static float SellFractionFor(float fraction, bool specialty) =>
        fraction * (specialty ? SpecialtySellBonus : 1f);

    /// <summary>
    /// What a flat-priced service costs at a given standing (Phase 38D). Services have a price rather
    /// than a value and a markup, so they need their own entry point — but it reuses the same
    /// <see cref="PriceMultiplierFor"/> table, so a merchant and an innkeeper of the same faction move
    /// together and there is no second discount ramp to drift.
    ///
    /// Rounds <b>up</b> and floors at <c>1</c> for anything priced, so a discount can never make a
    /// service free — the same rule and the same reason as <see cref="BuyPrice"/>. A base price of
    /// <c>0</c> stays <c>0</c>: that is a service authored as genuinely free, not one discounted into it.
    /// </summary>
    public static int ServicePrice(int basePrice, ReputationTier tier)
    {
        if (basePrice <= 0)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(basePrice * PriceMultiplierFor(tier)));
    }

    /// <summary>
    /// Whether a vendor will take this at all. Two refusals, both load-bearing: a
    /// <see cref="ItemType.Quest"/> item sold off would silently strand a Collect objective with no
    /// way to recover it, and gold-for-gold is nonsense the spread would turn into a slow leak.
    /// </summary>
    public static bool Sellable(ItemType type, bool isCurrency) =>
        !isCurrency && type != ItemType.Quest;
}
