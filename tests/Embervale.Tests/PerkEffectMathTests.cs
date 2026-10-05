using System;
using Embervale.Progression;
using Xunit;

namespace Embervale.Tests;

/// <summary>Pins the units and caps of <see cref="PerkEffectMath"/> and the lookup rules of
/// <see cref="PerkEffectTotals"/>: the contract that keeps stacked perks from making anything free.</summary>
public class PerkEffectMathTests
{
    [Fact]
    public void EveryKind_HasASaneRange()
    {
        foreach (PerkEffectKind kind in Enum.GetValues<PerkEffectKind>())
        {
            (float min, float max) = PerkEffectMath.RangeOf(kind);
            Assert.True(min <= 0f && max >= 0f, $"{kind} range must contain 0");
            if (kind != PerkEffectKind.None)
            {
                Assert.True(max > min, $"{kind} must allow some effect");
            }
        }
    }

    [Fact]
    public void None_IsPinnedToZero()
    {
        Assert.Equal(0f, PerkEffectMath.Clamp(PerkEffectKind.None, 5f));
        Assert.Equal(1f, PerkEffectMath.Factor(PerkEffectKind.None, -5f));
    }

    [Theory]
    [InlineData(PerkEffectKind.DodgeStaminaMult)]
    [InlineData(PerkEffectKind.BlockStaminaMult)]
    [InlineData(PerkEffectKind.ParryStaminaMult)]
    [InlineData(PerkEffectKind.AttackStaminaMult)]
    [InlineData(PerkEffectKind.BowDrawStaminaMult)]
    [InlineData(PerkEffectKind.SprintStaminaMult)]
    public void StaminaFactor_NeverFallsBelowFloor(PerkEffectKind kind)
    {
        Assert.Equal(1f, PerkEffectMath.Factor(kind, 0f));
        Assert.Equal(0.8f, PerkEffectMath.Factor(kind, -0.2f), 5);
        Assert.Equal(PerkEffectMath.StaminaFactorFloor, PerkEffectMath.Factor(kind, -3f), 5);
    }

    [Fact]
    public void ManaFactor_NeverFallsBelowFloor()
    {
        Assert.Equal(PerkEffectMath.ManaFactorFloor, PerkEffectMath.Factor(PerkEffectKind.ManaCostMult, -1f), 5);
        Assert.Equal(0.9f, PerkEffectMath.Factor(PerkEffectKind.ManaCostMult, -0.1f), 5);
    }

    [Fact]
    public void XpFactor_IsCappedAndNeverAPenalty()
    {
        Assert.Equal(PerkEffectMath.XpFactorMax, PerkEffectMath.Factor(PerkEffectKind.XpGainMult, 2f), 5);
        Assert.Equal(1f, PerkEffectMath.Factor(PerkEffectKind.XpGainMult, -1f), 5);
        Assert.Equal(1.05f, PerkEffectMath.Factor(PerkEffectKind.XpGainMult, 0.05f), 5);
    }

    [Fact]
    public void ShopDiscountAndSellBonus_AreCapped()
    {
        Assert.Equal(PerkEffectMath.BuyDiscountMax, PerkEffectMath.Clamp(PerkEffectKind.BuyDiscount, 0.5f));
        Assert.Equal(PerkEffectMath.SellBonusMax, PerkEffectMath.Clamp(PerkEffectKind.SellBonus, 0.5f));
        Assert.Equal(0f, PerkEffectMath.Clamp(PerkEffectKind.BuyDiscount, -0.5f));
    }

    [Fact]
    public void HaggleChance_AddsCappedPointsAndNeverOpensAClosedMerchant()
    {
        Assert.Equal(0, PerkEffectMath.HaggleChance(0, 25f));
        Assert.Equal(0, PerkEffectMath.HaggleChance(-5, 25f));
        Assert.Equal(40, PerkEffectMath.HaggleChance(30, 10f));
        Assert.Equal(30 + PerkEffectMath.HaggleBonusMaxPercent, PerkEffectMath.HaggleChance(30, 90f));
        Assert.Equal(30, PerkEffectMath.HaggleChance(30, -10f));
        Assert.Equal(100, PerkEffectMath.HaggleChance(90, 25f));
    }

    [Fact]
    public void Totals_QualifiedReadIncludesGlobalSum()
    {
        var totals = new PerkEffectTotals();
        Assert.True(totals.IsEmpty);

        totals.Add(new PerkEffectEntry(PerkEffectKind.SchoolPowerBonus, string.Empty, 0.05f), 2);
        totals.Add(new PerkEffectEntry(PerkEffectKind.SchoolPowerBonus, "fire", 0.1f), 1);

        Assert.Equal(0.1f, totals.Get(PerkEffectKind.SchoolPowerBonus), 5);
        Assert.Equal(0.2f, totals.Get(PerkEffectKind.SchoolPowerBonus, "fire"), 5);
        Assert.Equal(0.1f, totals.Get(PerkEffectKind.SchoolPowerBonus, "frost"), 5);
        Assert.Equal(0f, totals.Get(PerkEffectKind.SellBonus));
    }

    [Fact]
    public void Totals_AccumulateAndClear()
    {
        var totals = new PerkEffectTotals();
        totals.Add(PerkEffectKind.LootQuality, null, 0.1f);
        totals.Add(PerkEffectKind.LootQuality, string.Empty, 0.15f);
        Assert.Equal(0.25f, totals.Get(PerkEffectKind.LootQuality), 5);

        totals.Clear();
        Assert.True(totals.IsEmpty);
        Assert.Equal(0f, totals.Get(PerkEffectKind.LootQuality));
    }

    [Fact]
    public void DodgeQualifiers_StackForABackstepAndStayCappedAtTheFloor()
    {
        var totals = new PerkEffectTotals();
        totals.Add(new PerkEffectEntry(PerkEffectKind.DodgeStaminaMult, string.Empty, -0.06f), 3);
        totals.Add(new PerkEffectEntry(PerkEffectKind.DodgeStaminaMult, "backstep", -0.10f), 2);

        float roll = PerkEffectMath.Factor(PerkEffectKind.DodgeStaminaMult, totals.Get(PerkEffectKind.DodgeStaminaMult, "roll"));
        float backstep = PerkEffectMath.Factor(PerkEffectKind.DodgeStaminaMult, totals.Get(PerkEffectKind.DodgeStaminaMult, "backstep"));
        Assert.Equal(0.82f, roll, 5);
        Assert.Equal(0.62f, backstep, 5);

        totals.Add(new PerkEffectEntry(PerkEffectKind.DodgeStaminaMult, "backstep", -0.5f), 1);
        Assert.Equal(PerkEffectMath.StaminaFactorFloor,
            PerkEffectMath.Factor(PerkEffectKind.DodgeStaminaMult, totals.Get(PerkEffectKind.DodgeStaminaMult, "backstep")), 5);
    }

    [Fact]
    public void ScaleReputation_RaisesGainsOnly_AndNeverShrinksOne()
    {
        Assert.Equal(25, PerkEffectMath.ScaleReputation(20, 0.24f));
        Assert.Equal(20, PerkEffectMath.ScaleReputation(20, 0f));
        Assert.Equal(1, PerkEffectMath.ScaleReputation(1, 0.24f));
        Assert.Equal(-10, PerkEffectMath.ScaleReputation(-10, 0.24f));
        Assert.Equal(0, PerkEffectMath.ScaleReputation(0, 0.24f));
        // The cap holds: 4.0 is read as the 0.25 maximum.
        Assert.Equal(25, PerkEffectMath.ScaleReputation(20, 4f));
    }

    [Fact]
    public void ScaleCraftXp_RaisesAGrant_UpToItsCap()
    {
        Assert.Equal(14, PerkEffectMath.ScaleCraftXp(10, 0.4f));
        Assert.Equal(10, PerkEffectMath.ScaleCraftXp(10, 0f));
        Assert.Equal(15, PerkEffectMath.ScaleCraftXp(10, 9f));
        Assert.Equal(0, PerkEffectMath.ScaleCraftXp(0, 0.4f));
    }
}
