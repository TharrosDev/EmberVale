using System;
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
    /// Whether a vendor will take this at all. Two refusals, both load-bearing: a
    /// <see cref="ItemType.Quest"/> item sold off would silently strand a Collect objective with no
    /// way to recover it, and gold-for-gold is nonsense the spread would turn into a slow leak.
    /// </summary>
    public static bool Sellable(ItemType type, bool isCurrency) =>
        !isCurrency && type != ItemType.Quest;
}
