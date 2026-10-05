namespace Embervale.Progression;

/// <summary>
/// The non-stat things a perk can change. A perk's stat bonus rides a <c>StatModifier</c> as before;
/// every kind here is a number read at a call site through <see cref="PerkQuery"/> and bounded by
/// <see cref="PerkEffectMath"/>. Each kind's unit and cap are documented on
/// <see cref="PerkEffectMath.RangeOf"/>.
/// </summary>
// APPEND ONLY: ordinals are authored into perk .tres files — never reorder/insert/remove
// (EnumStabilityTests).
public enum PerkEffectKind
{
    /// <summary>Not a non-stat effect: a <see cref="PerkEffectResource"/> with this kind is a stat modifier.</summary>
    None,
    DodgeStaminaMult,
    BlockStaminaMult,
    ParryStaminaMult,
    AttackStaminaMult,
    BowDrawStaminaMult,
    SprintStaminaMult,
    ManaCostMult,
    SchoolPowerBonus,
    HaggleChanceBonus,
    BuyDiscount,
    SellBonus,
    ServicePriceMult,
    MaterialSaveChance,
    SalvageYieldBonus,
    LootQuality,
    XpGainMult,
    RepGainMult,
    CraftXpMult,
    RangedPowerBonus,
    SpellCritBonus,
}
