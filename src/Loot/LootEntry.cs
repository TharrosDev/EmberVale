using Embervale.Items;
using Godot;

namespace Embervale.Loot;

/// <summary>
/// One row of a <see cref="LootTable"/>: a chance to drop a quantity of a specific
/// item (by <see cref="ItemDatabase"/> id). Equippable entries can opt into the
/// procedural pipeline (<see cref="RollAffixes"/>) so they roll a rarity and
/// affixes; mundane entries (materials, currency, consumables) drop as plain
/// stacks. Authored as a sub-resource inside a loot-table <c>.tres</c>.
///
/// A row can instead point at another table (<see cref="TablePath"/>), join a pick-one
/// <see cref="Group"/>, be gated to a level range, carry a rarity floor, or drop only once per
/// save. Every one of those fields is absent-default, so a row authored before they existed
/// behaves exactly as it did.
/// </summary>
[GlobalClass]
public partial class LootEntry : Resource
{
    /// <summary>Item id resolved through the <see cref="Embervale.Items.ItemDatabase"/>.</summary>
    [Export] public string ItemId { get; set; } = string.Empty;

    /// <summary>Independent drop probability for this entry, 0..1.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float DropChance { get; set; } = 1f;

    [Export] public int MinQuantity { get; set; } = 1;
    [Export] public int MaxQuantity { get; set; } = 1;

    /// <summary>If true and the item is equippable, roll a rarity + affixes for it.</summary>
    [Export] public bool RollAffixes { get; set; }

    [ExportGroup("Nesting and grouping")]
    /// <summary>Rolls another table instead of dropping <see cref="ItemId"/> (which is then
    /// ignored). <c>{tier}</c> in the path is replaced by the tier the roll is made at, so one
    /// family table reaches <c>res://data/loot/tiers/Tier3Gear.tres</c> in the third realm. The
    /// quantity range is how many times the nested table is rolled.</summary>
    [Export] public string TablePath { get; set; } = string.Empty;

    /// <summary>Rows sharing a non-empty group are one roll: exactly one eligible member is picked
    /// by <see cref="Weight"/>, and then its own <see cref="DropChance"/> applies. A group whose
    /// members all have <see cref="DropChance"/> 1 is therefore a guaranteed pick-one.</summary>
    [Export] public string Group { get; set; } = string.Empty;

    /// <summary>Relative weight inside a <see cref="Group"/>; unused by an ungrouped row.</summary>
    [Export] public float Weight { get; set; } = 1f;

    [ExportGroup("Gates")]
    /// <summary>Lowest level this row drops at (0 = no floor). Read against the roll's reference
    /// level; a level-less roll never passes a floor.</summary>
    [Export] public int MinLevel { get; set; }

    /// <summary>Highest level this row drops at (0 = no ceiling).</summary>
    [Export] public int MaxLevel { get; set; }

    /// <summary>Rarity floor for gear this row rolls, and for every piece a nested table rolls.</summary>
    [Export] public ItemRarity MinRarity { get; set; } = ItemRarity.Common;

    /// <summary>Drops at most once per save (a boss's signature piece on the first kill). The claim
    /// is recorded in the looter's <see cref="LootLedger"/>; a roll with no ledger drops it every time.</summary>
    [Export] public bool OncePerSave { get; set; }

    /// <summary>True when this row rolls another table rather than an item.</summary>
    public bool IsNested => TablePath.Length > 0;

    /// <summary>True when <paramref name="level"/> is inside this row's level gate.</summary>
    public bool AllowsLevel(int level)
    {
        return (MinLevel <= 0 || level >= MinLevel) && (MaxLevel <= 0 || level <= MaxLevel);
    }
}
