using Godot;

namespace Embervale.Loot;

/// <summary>
/// One row of a <see cref="LootTable"/>: a chance to drop a quantity of a specific
/// item (by <see cref="ItemDatabase"/> id). Equippable entries can opt into the
/// procedural pipeline (<see cref="RollAffixes"/>) so they roll a rarity and
/// affixes; mundane entries (materials, currency, consumables) drop as plain
/// stacks. Authored as a sub-resource inside a loot-table <c>.tres</c>.
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
}
