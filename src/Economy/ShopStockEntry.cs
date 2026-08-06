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
}
