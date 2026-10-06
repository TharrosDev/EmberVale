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
    // Authored default; overridable per .tres. Mirrors GameIds.Currency.Gold (kept literal so the
    // Godot [Export] default-value generator — which doesn't see file usings — compiles cleanly).
    [Export] public string GoldItemId { get; set; } = "item.currency.gold";
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float GoldChance { get; set; }
    [Export] public int GoldMin { get; set; }
    [Export] public int GoldMax { get; set; }

    [ExportGroup("Quality")]
    /// <summary>Additive bias toward higher rarities for affixed drops (see
    /// <see cref="LootRarity.Roll"/>).</summary>
    [Export] public float QualityBonus { get; set; }

    // --- Level-aware generation (ics). Every field is absent-default: a table authored before
    // these existed rolls level-less, scattered on the ground, exactly as it did. ---

    [ExportGroup("Tier")]
    /// <summary>The realm tier this table rolls at, 1..6. 0 (the default) takes the tier of the
    /// realm the roll happens in, which is what lets one family table serve an enemy in every
    /// realm; a boss table pins its own.</summary>
    [Export(PropertyHint.Range, "0,6,1")] public int Tier { get; set; }

    /// <summary>Narrows the tier's level band from below (0 = the band's own floor).</summary>
    [Export] public int MinItemLevel { get; set; }

    /// <summary>Narrows the tier's level band from above (0 = the band's own ceiling).</summary>
    [Export] public int MaxItemLevel { get; set; }

    [ExportGroup("Delivery")]
    /// <summary>When the owner dies, its drops go into a reward chest stood at the death position
    /// instead of being scattered on the ground (a boss).</summary>
    [Export] public bool DropsAsChest { get; set; }
}
