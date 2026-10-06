namespace Embervale.Items;

/// <summary>
/// Where an affix's name fragment sits relative to the base item name and how it
/// is grouped during generation. A rolled item draws at most one prefix and one
/// suffix into its display name (e.g. <c>Vicious</c> Steel Sword <c>of the Bear</c>).
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum AffixKind
{
    Prefix,
    Suffix,
}

/// <summary>
/// What an affix's rolled number does once the item is worn. Most affixes are
/// <see cref="Stat"/>: a modifier on a <see cref="Embervale.Stats.StatType"/>. Regeneration is a
/// rate on the stats component rather than a stat, so those affixes carry one of the regen members
/// and their number is added to that rate while the item is equipped.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum AffixEffect
{
    Stat,
    HealthRegen,
    StaminaRegen,
    ManaRegen,
}
