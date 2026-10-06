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

    /// <summary>Lowest item level this affix can roll at. 0 (the absent-default) rolls at any
    /// level, including the level-less roll every pre-ics caller makes; a gated affix needs a roll
    /// made at <c>itemLevel &gt;= MinItemLevel</c>.</summary>
    [Export] public int MinItemLevel { get; set; }

    /// <summary>Presentation hint: the rolled value reads as a percentage ("+5%") rather than a
    /// flat amount. It does not change how the modifier applies; that is <see cref="ModifierType"/>.</summary>
    [Export] public bool IsPercent { get; set; }

    /// <summary>Exclusivity group: one item never rolls two affixes sharing a non-empty group
    /// (e.g. tiers of one affix all in "power"). Empty (the absent-default) excludes nothing.</summary>
    [Export] public string Group { get; set; } = string.Empty;

    /// <summary>What the rolled number does when worn. <see cref="AffixEffect.Stat"/> (the
    /// absent-default) is a modifier on <see cref="Stat"/>; a regen member adds the number to that
    /// regeneration rate instead, and <see cref="Stat"/> then only names the resource it restores.</summary>
    [Export] public AffixEffect Effect { get; set; } = AffixEffect.Stat;

    /// <summary>False pins the rolled value to the authored range at every item level. For the few
    /// stats where a larger number is not simply "better gear": flat movement speed is the case.</summary>
    [Export] public bool ScalesWithLevel { get; set; } = true;

    [ExportGroup("Applicable Gear Families")]
    [Export] public bool ForWeapons { get; set; } = true;
    [Export] public bool ForArmor { get; set; } = true;
    [Export] public bool ForAccessories { get; set; } = true;

    /// <summary>True if this affix may roll on the given equippable at the given rarity and item
    /// level. <paramref name="itemLevel"/> 0 is a level-less roll, which only ungated affixes
    /// (<see cref="MinItemLevel"/> 0) pass.</summary>
    public bool AppliesTo(EquippableItemResource item, ItemRarity rarity, int itemLevel = 0)
    {
        if (rarity < MinRarity || itemLevel < MinItemLevel)
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
    /// <param name="rng">The generator to roll on.</param>
    /// <param name="quality">0..1 bias toward <see cref="MaxValue"/>.</param>
    /// <param name="itemLevel">The level the item is generated at; the authored range is the range
    /// at level 1 and grows with it (<see cref="ScaleForLevel"/>). 0 is a level-less roll, unscaled.</param>
    public ItemAffix Roll(RandomNumberGenerator rng, float quality, int itemLevel = 0)
    {
        float value = BlendValue(MinValue, MaxValue, quality, rng.Randf());
        return new ItemAffix(Id, Label, Kind, Stat, ScaleForLevel(value, itemLevel, GrowthPerLevel), ModifierType, Effect);
    }

    /// <summary>True when the rolled value is a fraction shown as a percentage: the authored hint,
    /// or any modifier that is not flat.</summary>
    public bool ReadsAsPercent => IsPercent || ModifierType != ModifierType.Flat;

    /// <summary>How fast this affix's range grows per item level. Flat amounts keep pace with the
    /// gear they sit on; fractions and regeneration rates grow slowly, because a percentage already
    /// scales with the stat it multiplies.</summary>
    public float GrowthPerLevel => !ScalesWithLevel
        ? 0f
        : ReadsAsPercent
            ? PercentGrowthPerLevel
            : Effect != AffixEffect.Stat ? RegenGrowthPerLevel : FlatGrowthPerLevel;

    public const float FlatGrowthPerLevel = 0.05f;
    public const float RegenGrowthPerLevel = 0.02f;
    public const float PercentGrowthPerLevel = 0.01f;

    /// <summary>The highest item level the scaling counts; a higher level scales as this one.</summary>
    public const int MaxScaledLevel = 50;

    /// <summary>
    /// The one place an affix value meets an item level. The authored range is what the affix rolls
    /// at level 1 (and on a level-less roll); each level above adds <paramref name="growthPerLevel"/>
    /// of it, linearly, up to <see cref="MaxScaledLevel"/>. Pure, so the curve is unit-tested.
    /// </summary>
    public static float ScaleForLevel(float value, int itemLevel, float growthPerLevel)
    {
        if (itemLevel <= 1 || growthPerLevel <= 0f)
        {
            return value;
        }

        int levels = System.Math.Min(itemLevel, MaxScaledLevel) - 1;
        return value * (1f + (growthPerLevel * levels));
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
