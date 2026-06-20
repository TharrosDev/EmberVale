using Godot;

namespace Embervale.Loot;

/// <summary>
/// A designer-authored drop table: a list of independently-rolled
/// <see cref="LootEntry"/> rows plus an optional gold roll and a quality term that
/// raises the rarity ceiling for everything dropped from it. Attached to enemies
/// (and later chests/nodes) via a <see cref="LootComponent"/> and resolved by the
/// <see cref="LootGenerator"/>.
///
/// New drop table = a <c>.tres</c> under <c>data/loot/</c>; no code change.
/// </summary>
[GlobalClass]
public partial class LootTable : Resource
{
    /// <summary>Rows of the table. Untyped so authored <c>.tres</c> sub-resource
    /// arrays bind cleanly; elements are read back as <see cref="LootEntry"/>.</summary>
    [Export] public Godot.Collections.Array Entries { get; set; } = new();

    [ExportGroup("Gold")]
    [Export] public string GoldItemId { get; set; } = "item.currency.gold";
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float GoldChance { get; set; }
    [Export] public int GoldMin { get; set; }
    [Export] public int GoldMax { get; set; }

    [ExportGroup("Quality")]
    /// <summary>Additive bias toward higher rarities for affixed drops (see
    /// <see cref="LootRarity.Roll"/>).</summary>
    [Export] public float QualityBonus { get; set; }
}
