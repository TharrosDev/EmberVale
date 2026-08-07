using Embervale.Factions;
using Godot;

namespace Embervale.Economy;

/// <summary>
/// One row of a shop's authored stock (Phase 38B) — the sub-resource pattern
/// <see cref="Loot.LootEntry"/> and <see cref="Crafting.RecipeIngredient"/> use.
///
/// <see cref="Quantity"/> is what separates the spec's three kinds of stock: <c>0</c> is a static
/// listing that never runs out (38A's whole behaviour, and the default), anything above it is finite
/// and refills on the shop's <c>RestockDays</c> clock. There is no mode enum, because the number
/// already says which one it is.
/// </summary>
[GlobalClass]
public partial class ShopStockEntry : Resource
{
    /// <summary>The ware, e.g. <c>item.potion.health</c>.</summary>
    [Export] public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Units held between restocks. <c>0</c> means <b>unlimited</b> — a materials stall that never
    /// runs out of ore. A positive quantity requires the shop to author <c>RestockDays</c>, or the
    /// first player through the door empties it for the rest of the run; <c>--validate</c> rejects
    /// that pairing rather than leaving it to be discovered.
    /// </summary>
    [Export] public int Quantity { get; set; }

    /// <summary>
    /// The lowest standing that may buy this row (Phase 38I). <see cref="ReputationTier.Hated"/> is
    /// the bottom of the ramp, so <b>the default is ungated</b> and no sentinel value is needed — the
    /// same trick <see cref="Quantity"/>'s <c>0</c> plays for unlimited stock.
    /// </summary>
    [Export] public ReputationTier RequiredTier { get; set; } = ReputationTier.Hated;

    /// <summary>
    /// A story flag the player must hold for this row to be sold (Phase 38I). Empty is ungated.
    ///
    /// ⚠️ <c>--validate</c> rejects a flag nothing ever writes, through the same reader/writer
    /// cross-reference that guards <c>RegionResource.UnlockFlagId</c>. A mistyped flag here is a shelf
    /// that never opens, silently and permanently.
    /// </summary>
    [Export] public string RequiredFlagId { get; set; } = string.Empty;

    /// <summary>
    /// How many rungs of a stake in this merchant the player must hold (Phase 38I). <c>0</c> is
    /// ungated; anything above the shop's authored ladder is unreachable stock and <c>--validate</c>
    /// rejects it.
    /// </summary>
    [Export] public int RequiredInvestment { get; set; }

    /// <summary>Whether this row is gated at all — the cheap test the "every row is gated" validator
    /// rule and the window's ordering both read, so they cannot disagree.</summary>
    public bool IsGated =>
        RequiredTier > ReputationTier.Hated ||
        RequiredFlagId.Length > 0 ||
        RequiredInvestment > 0;
}
