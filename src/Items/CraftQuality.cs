namespace Embervale.Items;

/// <summary>
/// The workmanship tier frozen onto an <see cref="ItemInstance"/> when it is crafted. Loot is always
/// <see cref="Standard"/>; only the crafting bench produces the higher tiers. Ordinal 0 is the
/// absent-default, so an instance saved before the field existed loads as Standard.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum CraftQuality
{
    Standard,
    Fine,
    Superior,
    Masterwork,
}

/// <summary>
/// The one place a <see cref="CraftQuality"/> turns into a number; every reader
/// (<see cref="ItemInstance.Value"/>, <see cref="ItemInstance.StatBonuses"/>) picks the numbers up
/// from these two methods. Stats move less than value on purpose: a Masterwork piece is a clear
/// find without out-scaling the next realm's ordinary gear. Pure, so it is unit-testable.
/// </summary>
public static class CraftQualities
{
    /// <summary>Multiplier on an instance's merchant value.</summary>
    public static float ValueMultiplier(CraftQuality quality) => quality switch
    {
        CraftQuality.Fine => 1.08f,
        CraftQuality.Superior => 1.18f,
        CraftQuality.Masterwork => 1.35f,
        _ => 1f,
    };

    /// <summary>Multiplier on the template's flat stat bonuses (rolled affixes are not scaled).</summary>
    public static float StatMultiplier(CraftQuality quality) => quality switch
    {
        CraftQuality.Fine => 1.05f,
        CraftQuality.Superior => 1.10f,
        CraftQuality.Masterwork => 1.18f,
        _ => 1f,
    };

    /// <summary>Clamps a saved or authored ordinal into the defined range.</summary>
    public static CraftQuality FromOrdinal(int ordinal)
    {
        return (CraftQuality)System.Math.Clamp(ordinal, (int)CraftQuality.Standard, (int)CraftQuality.Masterwork);
    }
}

/// <summary>
/// The matching hook for <see cref="ItemInstance.UpgradeLevel"/>: the cap and what a level is worth.
/// A level is a flat share of the item's base bonuses and a smaller share of its value, so the
/// forge's fee (<c>Crafting.ReforgeRules</c>) always costs more than the value it adds.
/// </summary>
public static class ItemUpgrades
{
    /// <summary>The highest upgrade level an instance can hold.</summary>
    public const int MaxLevel = 5;

    /// <summary>Share of the base stat bonuses one level adds.</summary>
    public const float StatPerLevel = 0.06f;

    /// <summary>Share of the merchant value one level adds.</summary>
    public const float ValuePerLevel = 0.05f;

    /// <summary>Multiplier on an instance's merchant value.</summary>
    public static float ValueMultiplier(int upgradeLevel) => 1f + (ValuePerLevel * Clamp(upgradeLevel));

    /// <summary>Multiplier on the template's flat stat bonuses (rolled affixes are not scaled).</summary>
    public static float StatMultiplier(int upgradeLevel) => 1f + (StatPerLevel * Clamp(upgradeLevel));

    /// <summary>Clamps a level into <c>0..MaxLevel</c>.</summary>
    public static int Clamp(int upgradeLevel) => System.Math.Clamp(upgradeLevel, 0, MaxLevel);
}
