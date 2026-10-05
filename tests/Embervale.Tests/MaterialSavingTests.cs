using System;
using Embervale.Crafting;
using Embervale.Items;
using Embervale.Progression;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure parts of the economy perks: the derived material-saving roll and its choice of ingredient, the
/// salvage yield caps, and the XP / save-chance folding in <see cref="PerkEffectMath"/>. The components that
/// apply them are Godot nodes, so the live half (a perked craft, the saved serial replacing on load) is in the
/// <c>--lifecycle</c> economy perks probe.
/// </summary>
public class MaterialSavingTests
{
    [Fact]
    public void TheRollIsDerivedNotRandom()
    {
        for (int serial = 0; serial < 200; serial++)
        {
            Assert.Equal(
                MaterialSaving.Saves(serial, "recipe.iron_sword", 20),
                MaterialSaving.Saves(serial, "recipe.iron_sword", 20));
        }
    }

    [Fact]
    public void ZeroNeverSavesAndAHundredAlwaysDoes()
    {
        for (int serial = 0; serial < 200; serial++)
        {
            Assert.False(MaterialSaving.Saves(serial, "recipe.x", 0));
            Assert.False(MaterialSaving.Saves(serial, "recipe.x", -5));
            Assert.True(MaterialSaving.Saves(serial, "recipe.x", 100));
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(25)]
    public void TheHitRateMatchesTheChance(int percent)
    {
        int hits = 0;
        const int serials = 4000;
        for (int serial = 0; serial < serials; serial++)
        {
            if (MaterialSaving.Saves(serial, "recipe.leather_vest", percent))
            {
                hits++;
            }
        }

        double rate = hits / (double)serials * 100.0;
        Assert.InRange(rate, percent - 3.0, percent + 3.0);
    }

    [Fact]
    public void DifferentRecipesRollIndependently()
    {
        int differing = 0;
        for (int serial = 0; serial < 400; serial++)
        {
            if (MaterialSaving.Saves(serial, "recipe.a", 25) != MaterialSaving.Saves(serial, "recipe.b", 25))
            {
                differing++;
            }
        }

        Assert.True(differing > 0, "two recipes shared one roll stream");
    }

    [Theory]
    [InlineData(new[] { 1, 1 }, -1)]
    [InlineData(new[] { 1 }, -1)]
    [InlineData(new int[0], -1)]
    [InlineData(new[] { 3, 6 }, 1)]
    [InlineData(new[] { 6, 1 }, 0)]
    [InlineData(new[] { 4, 4 }, 0)]
    [InlineData(new[] { 1, 2, 5, 5 }, 2)]
    public void TheSavedIngredientIsTheLargestAndNeverASingleUnit(int[] quantities, int expected)
    {
        Assert.Equal(expected, MaterialSaving.SavedIngredient(quantities));
    }

    [Fact]
    public void ACraftAlwaysConsumesSomething()
    {
        // Giving one unit of the largest ingredient back leaves the recipe's cost above zero for every shape.
        foreach (int[] quantities in new[] { new[] { 2 }, new[] { 2, 1 }, new[] { 3, 3 }, new[] { 8, 1, 4 } })
        {
            int index = MaterialSaving.SavedIngredient(quantities);
            Assert.True(index >= 0);
            int remaining = 0;
            for (int i = 0; i < quantities.Length; i++)
            {
                remaining += i == index ? quantities[i] - 1 : quantities[i];
            }

            Assert.True(remaining > 0);
        }
    }

    [Fact]
    public void SalvagePerksStayBelowFullRecovery()
    {
        // 0.5 + the 0.5 cap on the effect would be 1.0, which re-crafts for free; the rate is held at 0.75.
        for (int quantity = 1; quantity <= 40; quantity++)
        {
            Assert.True(Deconstruction.RecoveredQuantity(quantity, 0.5f) < quantity);
            Assert.True(Deconstruction.RecoveredQuantity(quantity, 0.25f) <= (int)(quantity * Deconstruction.MaxRecoveryRate));
            Assert.Equal(Deconstruction.RecoveredQuantity(quantity), Deconstruction.RecoveredQuantity(quantity, 0f));
            Assert.True(Deconstruction.RecoveredQuantity(quantity, 0.25f) >= Deconstruction.RecoveredQuantity(quantity));
        }

        Assert.Equal(3, Deconstruction.RecoveredQuantity(4, 0.25f));
        Assert.Equal(0, Deconstruction.RecoveredQuantity(1, 0.25f));
    }

    [Fact]
    public void ScrapBonusScalesAndNeverGoesBelowBase()
    {
        foreach (ItemRarity rarity in Enum.GetValues<ItemRarity>())
        {
            Assert.Equal(Deconstruction.ScrapYield(rarity), Deconstruction.ScrapYield(rarity, 0f));
            Assert.True(Deconstruction.ScrapYield(rarity, 0.25f) >= Deconstruction.ScrapYield(rarity));
            Assert.Equal(Deconstruction.ScrapYield(rarity), Deconstruction.ScrapYield(rarity, -1f));
        }

        Assert.Equal(1, Deconstruction.ScrapYield(ItemRarity.Common, 0.25f));
        Assert.Equal(6, Deconstruction.ScrapYield(ItemRarity.Legendary, 0.25f));
    }

    [Fact]
    public void XpScalingRoundsAndIsCapped()
    {
        Assert.Equal(10, PerkEffectMath.ScaleXp(10, 0f));
        Assert.Equal(11, PerkEffectMath.ScaleXp(10, 0.06f));
        Assert.Equal(110, PerkEffectMath.ScaleXp(100, 0.10f));
        Assert.Equal(110, PerkEffectMath.ScaleXp(100, 5f));          // capped at +10%
        Assert.Equal(100, PerkEffectMath.ScaleXp(100, -1f));         // never a penalty
        Assert.Equal(1, PerkEffectMath.ScaleXp(1, 0.04f));           // a grant stays at least 1
        Assert.Equal(0, PerkEffectMath.ScaleXp(0, 0.1f));
        Assert.Equal(-3, PerkEffectMath.ScaleXp(-3, 0.1f));          // a non-grant is passed through untouched
    }

    [Fact]
    public void SaveChanceIsAWholePercentAndCapped()
    {
        Assert.Equal(0, PerkEffectMath.SaveChancePercent(0f));
        Assert.Equal(12, PerkEffectMath.SaveChancePercent(0.12f));
        Assert.Equal(20, PerkEffectMath.SaveChancePercent(0.04f * 3f + 0.08f));
        Assert.Equal(25, PerkEffectMath.SaveChancePercent(9f));
        Assert.Equal(0, PerkEffectMath.SaveChancePercent(-1f));
    }

    [Fact]
    public void PriceFactorsFoldAndCap()
    {
        Assert.Equal(1f, PerkEffectMath.BuyFactor(0f));
        Assert.Equal(0.97f, PerkEffectMath.BuyFactor(0.03f), 5);
        Assert.Equal(PerkEffectMath.BestBuyFactor, PerkEffectMath.BuyFactor(1f), 5);
        Assert.Equal(1f, PerkEffectMath.BuyFactor(-1f));             // a perk never raises the asking price
        Assert.Equal(1.05f, PerkEffectMath.SellFactor(0.05f), 5);
        Assert.Equal(PerkEffectMath.BestSellFactor, PerkEffectMath.SellFactor(1f), 5);
        Assert.Equal(1f, PerkEffectMath.SellFactor(-1f));
        Assert.Equal(0.96f, PerkEffectMath.ServiceFactor(-0.04f), 5);
        Assert.Equal(PerkEffectMath.BestServiceFactor, PerkEffectMath.ServiceFactor(-1f), 5);
        Assert.Equal(1.25f, PerkEffectMath.ServiceFactor(5f), 5);
    }

    [Fact]
    public void TheBestConstantsAreTheRangeEdgesTheValidatorProvesAgainst()
    {
        Assert.Equal(PerkEffectMath.BestSellFactor, 1f + PerkEffectMath.RangeOf(PerkEffectKind.SellBonus).Max);
        Assert.Equal(PerkEffectMath.BestBuyFactor, 1f - PerkEffectMath.RangeOf(PerkEffectKind.BuyDiscount).Max);
        Assert.Equal(PerkEffectMath.BestServiceFactor, 1f + PerkEffectMath.RangeOf(PerkEffectKind.ServicePriceMult).Min);
    }

    [Fact]
    public void AHaggleBonusNeverOpensAMerchantWhoDoesNotHaggle()
    {
        Assert.Equal(0, PerkEffectMath.HaggleChance(0, 25f));
        Assert.Equal(40, PerkEffectMath.HaggleChance(30, 10f));
        Assert.Equal(55, PerkEffectMath.HaggleChance(30, 99f));      // capped at +25
        Assert.Equal(100, PerkEffectMath.HaggleChance(90, 25f));
    }
}
