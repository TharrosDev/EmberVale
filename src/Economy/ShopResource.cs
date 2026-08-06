using System.Collections.Generic;
using Embervale.Loot;
using Godot;

namespace Embervale.Economy;

/// <summary>
/// A shop's wares and its spread (Phase 38A), authored as a <c>.tres</c> under <c>data/shops/</c> and
/// indexed by <see cref="ShopDatabase"/>. A <see cref="VendorComponent"/> names one by id, so a new
/// merchant is a resource plus a component in a scene, with no code — the same shape
/// <c>PropertyResource</c> + <c>PropertyDeedComponent</c> have.
///
/// <b>This resource stays immutable</b> even now that stock depletes (Phase 38B). It is shared by
/// every vendor naming it and it is not <c>ISaveable</c>, so writing a remaining count into it would
/// both leak between merchants and vanish on reload. Runtime stock lives in
/// <see cref="ShopStockService"/>, keyed by <see cref="Id"/>. Vendor purses are still 38C's gold sink.
/// </summary>
[GlobalClass]
public partial class ShopResource : Resource
{
    /// <summary>Stable id, e.g. <c>shop.ember_crown.goods</c>.</summary>
    [Export] public string Id { get; set; } = "shop.unknown";

    /// <summary>Player-facing name. A <c>Loc</c> key — it reaches the interaction prompt and the
    /// window title, and CLAUDE.md §6 admits no literals in either.</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    /// <summary>
    /// Whose standing the merchant prices by (Phase 38C). Empty means standing has no effect at all.
    ///
    /// Authored <b>here rather than read off the vendor entity's <c>FactionComponent</c></b>, even
    /// though every town NPC already carries one: <c>ShopOpenedEvent</c> carries no vendor entity, the
    /// <c>shop</c> dev command has no vendor at all (so the console would silently price without a
    /// discount and disagree with the game), and <c>ContentValidator</c> cannot scan a <c>.tscn</c>, so
    /// an entity-sourced faction would be unvalidatable. <c>CompanionResource.FactionId</c> is the same
    /// call already made elsewhere.
    /// </summary>
    [Export] public string FactionId { get; set; } = string.Empty;

    [ExportGroup("Wares")]

    /// <summary>
    /// The shop's authored rows, sold as plain instances (<c>ItemInstance.Plain</c>). Untyped so
    /// authored <c>.tres</c> sub-resource arrays bind cleanly; read it back through
    /// <see cref="StockList"/>. The validator rejects an empty list, an unknown id, gold, and any
    /// <c>ItemType.Quest</c> item.
    /// </summary>
    [Export] public Godot.Collections.Array Stock { get; set; } = new();

    /// <summary>
    /// Whole in-game days between restocks; <c>0</c> means this shop never restocks, which is only
    /// legal when every row is unlimited. Restock is evaluated when the shop is <em>opened</em>, not
    /// on a tick — see <see cref="ShopStockService"/>.
    /// </summary>
    [Export] public int RestockDays { get; set; }

    /// <summary>
    /// Optional pool rolled through <see cref="LootGenerator"/> at each restock, at a quality scaled
    /// by the player's level (<see cref="ShopStock.QualityForLevel"/>) — the "leveled" half of 38B.
    /// A <c>LootTable</c> rather than a bespoke type because it already carries drop chances,
    /// quantities, <c>RollAffixes</c> and a quality bonus, and its item ids are already cross-checked
    /// by the validator's loot pass.
    /// </summary>
    [Export] public LootTable? LeveledTable { get; set; }

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

    /// <summary>
    /// Gold the merchant can spend buying from the player before they run dry, refilled at each restock
    /// (Phase 38C). <c>0</c> is unlimited, which is 38A/38B's behaviour and stays the default.
    ///
    /// This is a sink from the other end: it caps how fast a player can convert a field of corpses into
    /// coin, without a single new piece of timing machinery — 38B's restock clock now governs income as
    /// well as stock. ⚠️ A positive purse with <c>RestockDays = 0</c> is a merchant permanently out of
    /// money, the same shape as a finite stock row with no clock, and <c>--validate</c> rejects it for
    /// the same reason.
    /// </summary>
    [Export] public int PurseGold { get; set; }

    /// <summary>
    /// The authored rows as a typed list. Deliberately <b>does not</b> filter malformed rows the way
    /// <c>CraftingRecipeResource.IngredientList</c> does: an empty id or a negative quantity is
    /// something <c>--validate</c> has to be able to see and report, and a silent skip is how
    /// <c>ValidateLootTables</c> can pass a table with a blank entry in it.
    /// </summary>
    public List<ShopStockEntry> StockList()
    {
        var list = new List<ShopStockEntry>();
        foreach (Variant element in Stock)
        {
            if (element.As<ShopStockEntry>() is { } entry)
            {
                list.Add(entry);
            }
        }

        return list;
    }
}
