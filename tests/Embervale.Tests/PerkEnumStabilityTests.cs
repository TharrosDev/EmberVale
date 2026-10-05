using Embervale.Progression;
using Xunit;

namespace Embervale.Tests;

/// <summary>The perk enums are authored into <c>.tres</c> files by ordinal: append only (see
/// <see cref="EnumStabilityTests"/> for the rule).</summary>
public class PerkEnumStabilityTests
{
    [Fact]
    public void PerkBranch_Ordinals()
    {
        PerkBranch[] inOrder =
        {
            PerkBranch.None, PerkBranch.Warrior, PerkBranch.Archer, PerkBranch.Mage,
            PerkBranch.Rogue, PerkBranch.Crafter, PerkBranch.Social, PerkBranch.Ashbound,
        };
        for (int i = 0; i < inOrder.Length; i++)
        {
            Assert.Equal(i, (int)inOrder[i]);
        }
    }

    [Fact]
    public void PerkEffectKind_Ordinals()
    {
        PerkEffectKind[] inOrder =
        {
            PerkEffectKind.None, PerkEffectKind.DodgeStaminaMult, PerkEffectKind.BlockStaminaMult,
            PerkEffectKind.ParryStaminaMult, PerkEffectKind.AttackStaminaMult, PerkEffectKind.BowDrawStaminaMult,
            PerkEffectKind.SprintStaminaMult, PerkEffectKind.ManaCostMult, PerkEffectKind.SchoolPowerBonus,
            PerkEffectKind.HaggleChanceBonus, PerkEffectKind.BuyDiscount, PerkEffectKind.SellBonus,
            PerkEffectKind.ServicePriceMult, PerkEffectKind.MaterialSaveChance, PerkEffectKind.SalvageYieldBonus,
            PerkEffectKind.LootQuality, PerkEffectKind.XpGainMult, PerkEffectKind.RepGainMult,
            PerkEffectKind.CraftXpMult, PerkEffectKind.RangedPowerBonus, PerkEffectKind.SpellCritBonus,
        };
        for (int i = 0; i < inOrder.Length; i++)
        {
            Assert.Equal(i, (int)inOrder[i]);
        }
    }
}
