using Embervale.Stats;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Designer-authored template for a procedural affix. Each <c>.tres</c> under
/// <c>data/affixes/</c> declares a stat it boosts, the rolled value range, the
/// minimum rarity at which it can appear, and which gear families it fits. The
/// <see cref="AffixDatabase"/> indexes them; <see cref="Embervale.Loot.LootGenerator"/>
/// filters the pool per item and rolls concrete <see cref="ItemAffix"/>es from it.
///
/// New affix = drop a <c>.tres</c> here, no code change.
/// </summary>
[GlobalClass]
public partial class AffixDefinition : Resource
{
    /// <summary>Stable id, e.g. "affix.prefix.vicious". The save/database key.</summary>
    [Export] public string Id { get; set; } = "affix.unknown";

    /// <summary>Name fragment woven into the item name, e.g. "Vicious" / "of the Bear".</summary>
    [Export] public string Label { get; set; } = string.Empty;

    [Export] public AffixKind Kind { get; set; } = AffixKind.Prefix;

    [Export] public StatType Stat { get; set; } = StatType.Armor;

    [Export] public ModifierType ModifierType { get; set; } = ModifierType.Flat;

    [Export] public float MinValue { get; set; } = 1f;
    [Export] public float MaxValue { get; set; } = 5f;

    /// <summary>Lowest rarity at which this affix can be rolled.</summary>
    [Export] public ItemRarity MinRarity { get; set; } = ItemRarity.Uncommon;

    /// <summary>Relative weight when selecting affixes from the eligible pool.</summary>
    [Export] public float Weight { get; set; } = 1f;

    [ExportGroup("Applicable Gear Families")]
    [Export] public bool ForWeapons { get; set; } = true;
    [Export] public bool ForArmor { get; set; } = true;
    [Export] public bool ForAccessories { get; set; } = true;

    /// <summary>True if this affix may roll on the given equippable at the given rarity.</summary>
    public bool AppliesTo(EquippableItemResource item, ItemRarity rarity)
    {
        if (rarity < MinRarity)
        {
            return false;
        }

        return EquipmentSlots.FamilyOf(item.Slot) switch
        {
            GearFamily.Weapon => ForWeapons,
            GearFamily.Armor => ForArmor,
            GearFamily.Accessory => ForAccessories,
            _ => false,
        };
    }

    /// <summary>
    /// Rolls a concrete affix. <paramref name="quality"/> (0..1) biases the value
    /// toward <see cref="MaxValue"/>: higher rarity / luckier drops roll higher.
    /// </summary>
    public ItemAffix Roll(RandomNumberGenerator rng, float quality)
    {
        float value = BlendValue(MinValue, MaxValue, quality, rng.Randf());
        return new ItemAffix(Id, Label, Kind, Stat, value, ModifierType);
    }

    /// <summary>
    /// Pure value blend: combines a random share with a quality-driven share so even high-quality
    /// rolls keep some spread, then lerps between <paramref name="min"/> and <paramref name="max"/>.
    /// Split out of <see cref="Roll"/> (which feeds it <c>rng.Randf()</c>) so the in-bounds /
    /// monotonic behaviour is unit-testable without Godot's RNG. Result is always within
    /// <c>[min, max]</c> (a degenerate <c>min &gt; max</c> clamps to <paramref name="min"/>).
    /// </summary>
    public static float BlendValue(float min, float max, float quality, float roll01)
    {
        quality = System.Math.Clamp(quality, 0f, 1f);
        roll01 = System.Math.Clamp(roll01, 0f, 1f);
        float t = System.Math.Clamp((roll01 * 0.6f) + (quality * 0.4f), 0f, 1f);
        float value = min + ((max - min) * t);
        return max >= min
            ? System.Math.Clamp(value, min, max)
            : min;
    }
}
