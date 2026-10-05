using System;

namespace Embervale.Progression;

/// <summary>
/// Units, caps and the folding of summed perk effects into the numbers call sites use. Pure and
/// Godot-free. The caps are the contract that keeps "perks shape, never gate" true: no stack of perks
/// can make an action free, a merchant a gift shop, or a level-up trivial. They are first-pass numbers,
/// tuned after the catalogue exists; the validator and the shop-margin proof read these same constants.
/// </summary>
public static class PerkEffectMath
{
    /// <summary>Stamina costs never fall below this fraction of the base cost.</summary>
    public const float StaminaFactorFloor = 0.5f;

    /// <summary>Spell mana costs never fall below this fraction of the base cost.</summary>
    public const float ManaFactorFloor = 0.6f;

    /// <summary>Most percentage points perks add to a merchant's haggle chance.</summary>
    public const int HaggleBonusMaxPercent = 25;

    /// <summary>Most a perk can take off a shop's buy price (a fraction).</summary>
    public const float BuyDiscountMax = 0.05f;

    /// <summary>Most a perk can add to what a shop pays (a fraction).</summary>
    public const float SellBonusMax = 0.05f;

    /// <summary>Highest XP multiplier perks can reach.</summary>
    public const float XpFactorMax = 1.10f;

    /// <summary>
    /// The bounds of a kind's summed value. Units: <c>*Mult</c> kinds are a signed fraction added to 1
    /// (-0.2 = 20% cheaper); <c>HaggleChanceBonus</c> is percentage points; every other kind is a fraction
    /// added to a base (0.1 = +10%). <see cref="PerkEffectKind.None"/> is pinned to 0.
    /// </summary>
    public static (float Min, float Max) RangeOf(PerkEffectKind kind) => kind switch
    {
        PerkEffectKind.DodgeStaminaMult or PerkEffectKind.BlockStaminaMult or PerkEffectKind.ParryStaminaMult
            or PerkEffectKind.AttackStaminaMult or PerkEffectKind.BowDrawStaminaMult
            or PerkEffectKind.SprintStaminaMult => (StaminaFactorFloor - 1f, 1f),
        PerkEffectKind.ManaCostMult => (ManaFactorFloor - 1f, 1f),
        PerkEffectKind.HaggleChanceBonus => (0f, HaggleBonusMaxPercent),
        PerkEffectKind.BuyDiscount => (0f, BuyDiscountMax),
        PerkEffectKind.SellBonus => (0f, SellBonusMax),
        PerkEffectKind.ServicePriceMult => (-0.05f, 0.25f),
        PerkEffectKind.XpGainMult => (0f, XpFactorMax - 1f),
        PerkEffectKind.SchoolPowerBonus or PerkEffectKind.SalvageYieldBonus or PerkEffectKind.LootQuality
            or PerkEffectKind.CraftXpMult or PerkEffectKind.RangedPowerBonus => (0f, 0.5f),
        PerkEffectKind.MaterialSaveChance or PerkEffectKind.RepGainMult
            or PerkEffectKind.SpellCritBonus => (0f, 0.25f),
        _ => (0f, 0f),
    };

    /// <summary>A summed value held inside its kind's bounds.</summary>
    public static float Clamp(PerkEffectKind kind, float total)
    {
        (float min, float max) = RangeOf(kind);
        return Math.Clamp(total, min, max);
    }

    /// <summary>The multiplier a <c>*Mult</c> kind contributes: <c>1 + the capped total</c>.</summary>
    public static float Factor(PerkEffectKind kind, float total) => 1f + Clamp(kind, total);

    /// <summary>
    /// A merchant's haggle chance (percent) with perks folded in. A merchant who never haggles
    /// (<paramref name="baseChancePercent"/> of 0) stays that way, so a perk shapes how often a deal is
    /// struck and never opens a haggle that was not authored.
    /// </summary>
    public static int HaggleChance(int baseChancePercent, float bonusTotal)
    {
        if (baseChancePercent <= 0)
        {
            return 0;
        }

        float bonus = Clamp(PerkEffectKind.HaggleChanceBonus, bonusTotal);
        return Math.Min(100, baseChancePercent + (int)MathF.Round(bonus));
    }
}
