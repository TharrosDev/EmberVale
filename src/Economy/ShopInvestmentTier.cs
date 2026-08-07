using Godot;

namespace Embervale.Economy;

/// <summary>
/// One rung of a stake in a merchant (Phase 38I) — the sub-resource pattern
/// <see cref="ShopStockEntry"/> and <see cref="Loot.LootEntry"/> use.
///
/// A stake is the arc's late-game gold sink: it is permanent, it never comes back, and it buys two
/// things — the merchant's capacity to absorb loot (<see cref="PurseBonus"/>) and access to the stock
/// rows gated behind it (<c>ShopStockEntry.RequiredInvestment</c>).
///
/// ⚠️ <b>A stake deliberately moves no price.</b> Standing already owns the price ramp (38C) and 38F's
/// sweep contract says every new multiplier joins
/// <c>NoCombinationOfMultipliersLetsSellingBeatBuying</c> — 38I honours it by adding none. An investor
/// discount would also duplicate what reputation does, from a second authority that could drift.
/// </summary>
[GlobalClass]
public partial class ShopInvestmentTier : Resource
{
    /// <summary>
    /// Gold this rung costs. Must be positive and must rise across the ladder — <c>--validate</c>
    /// rejects a free stake and a rung cheaper than the one below it, because a ladder that gets
    /// cheaper is a mispriced ladder rather than a choice.
    /// </summary>
    [Export] public int Cost { get; set; } = 500;

    /// <summary>
    /// Gold added to the merchant's purse at every restock once this rung is held. <c>0</c> is a rung
    /// that buys access only, which is legal as long as some row gates on it.
    ///
    /// ⚠️ Meaningless on a shop with an unlimited purse (<c>PurseGold = 0</c>) or with no restock
    /// clock, and <c>--validate</c> rejects both: the first would be paying to make a bottomless
    /// merchant finite, the second a bonus that is never applied.
    /// </summary>
    [Export] public int PurseBonus { get; set; }
}
