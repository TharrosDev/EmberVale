using Godot;

namespace Embervale.Economy;

/// <summary>
/// A shop's wares and its spread (Phase 38A), authored as a <c>.tres</c> under <c>data/shops/</c> and
/// indexed by <see cref="ShopDatabase"/>. A <see cref="VendorComponent"/> names one by id, so a new
/// merchant is a resource plus a component in a scene, with no code — the same shape
/// <c>PropertyResource</c> + <c>PropertyDeedComponent</c> have.
///
/// <b>Stock is static and does not deplete.</b> Phase 38B owns quantities, restock timers and
/// leveled pools; adding depletion here without a restock clock would only mean a shop that can be
/// permanently emptied. That is also why this resource is not <c>ISaveable</c> and the vendor holds no
/// purse: nothing about a shop mutates, so there is no state to persist. Vendor purses are 38C's
/// gold sink.
/// </summary>
[GlobalClass]
public partial class ShopResource : Resource
{
    /// <summary>Stable id, e.g. <c>shop.ember_crown.goods</c>.</summary>
    [Export] public string Id { get; set; } = "shop.unknown";

    /// <summary>Player-facing name. A <c>Loc</c> key — it reaches the interaction prompt and the
    /// window title, and CLAUDE.md §6 admits no literals in either.</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    [ExportGroup("Wares")]

    /// <summary>
    /// Item ids the shop offers, sold as plain instances (<c>ItemInstance.Plain</c>) — rolled and
    /// leveled stock is 38B. The validator rejects an empty list, an unknown id, gold, and any
    /// <c>ItemType.Quest</c> item.
    /// </summary>
    [Export] public Godot.Collections.Array<string> StockItemIds { get; set; } = new();

    [ExportGroup("Spread")]

    /// <summary>
    /// Multiplier on an item's value when the player buys. Must be at least <c>1</c>: a vendor
    /// selling below base value is a vendor the player farms.
    /// </summary>
    [Export] public float BuyMarkup { get; set; } = 1.5f;

    /// <summary>
    /// Fraction of an item's value the vendor pays when the player sells. Must be above <c>0</c> and
    /// <b>below <see cref="BuyMarkup"/></b> — the two together are the spread, and inverting them
    /// prints money. <c>--validate</c> rejects that, and <see cref="ShopPricing"/> clamps so a
    /// hand-edited resource cannot do it either.
    /// </summary>
    [Export] public float SellFraction { get; set; } = 0.4f;
}
