using Embervale.Items;
using Embervale.Loot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure parts of loot rarity. <see cref="LootRarity.Roll"/> itself is driven by
/// Godot's native <c>RandomNumberGenerator</c> and so is exercised in-engine (GUT), but the
/// affix-count mapping is pure and load-bearing for gear generation, so it is pinned here.
/// </summary>
public class LootRarityTests
{
    [Theory]
    [InlineData(ItemRarity.Common, 0)]
    [InlineData(ItemRarity.Uncommon, 1)]
    [InlineData(ItemRarity.Rare, 2)]
    [InlineData(ItemRarity.Epic, 3)]
    [InlineData(ItemRarity.Legendary, 4)]
    public void AffixCount_ScalesWithRarity(ItemRarity rarity, int expected)
    {
        Assert.Equal(expected, LootRarity.AffixCount(rarity));
    }

    [Fact]
    public void AffixCount_IsMonotonicAcrossTiers()
    {
        int previous = -1;
        foreach (ItemRarity rarity in new[]
                 {
                     ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare,
                     ItemRarity.Epic, ItemRarity.Legendary,
                 })
        {
            int count = LootRarity.AffixCount(rarity);
            Assert.True(count >= previous, $"{rarity} should not carry fewer affixes than the previous tier.");
            previous = count;
        }
    }
}
