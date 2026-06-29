using Embervale.Crafting;
using Embervale.Items;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure salvage maths behind the deconstruction feature. The recovery rate is below 1 and
/// floored so salvaging then re-crafting always nets a material loss (no duplication exploit); XP
/// scales with item worth + rarity and never drops below 1.
/// </summary>
public class DeconstructionTests
{
    [Theory]
    [InlineData(1, 0)]   // a 1-quantity ingredient yields nothing — the anti-duplication guard
    [InlineData(2, 1)]
    [InlineData(3, 1)]   // floor(1.5)
    [InlineData(4, 2)]
    [InlineData(10, 5)]
    public void RecoveredQuantity_FloorsHalf(int ingredientQuantity, int expected)
    {
        Assert.Equal(expected, Deconstruction.RecoveredQuantity(ingredientQuantity));
    }

    [Fact]
    public void Xp_IsAtLeastOne_ForWorthlessCommon()
    {
        Assert.True(Deconstruction.Xp(0, ItemRarity.Common) >= 1);
    }

    [Fact]
    public void Xp_RisesWithRarityTier()
    {
        int common = Deconstruction.Xp(40, ItemRarity.Common);
        int rare = Deconstruction.Xp(40, ItemRarity.Rare);
        int legendary = Deconstruction.Xp(40, ItemRarity.Legendary);

        Assert.True(rare > common);
        Assert.True(legendary > rare);
    }

    [Fact]
    public void Xp_RisesWithItemValue()
    {
        int cheap = Deconstruction.Xp(10, ItemRarity.Common);
        int pricey = Deconstruction.Xp(100, ItemRarity.Common);
        Assert.True(pricey > cheap);
    }

    [Theory]
    [InlineData(ItemRarity.Common, 1)]      // recipe-less salvage always yields at least one scrap
    [InlineData(ItemRarity.Uncommon, 2)]
    [InlineData(ItemRarity.Legendary, 5)]
    public void ScrapYield_ScalesWithRarity(ItemRarity rarity, int expected)
    {
        Assert.Equal(expected, Deconstruction.ScrapYield(rarity));
    }
}
