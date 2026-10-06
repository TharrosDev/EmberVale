using System.Collections.Generic;
using Embervale.Crafting;
using Embervale.Items;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure reforge rules. The load-bearing group is the exploit proof: across the whole range of
/// item prices the catalogue can produce (and well past it), at every rarity, workmanship tier and
/// upgrade level, the gold a reforge charges is more than the most its added value could ever be
/// sold for. Materials are not even counted, so the real margin is wider.
/// </summary>
public class ReforgeRulesTests
{
    private static readonly CraftQuality[] Qualities =
    {
        CraftQuality.Standard, CraftQuality.Fine, CraftQuality.Superior, CraftQuality.Masterwork,
    };

    /// <summary>Template values from a copper trinket to far beyond the dearest planned item.</summary>
    private static IEnumerable<int> Prices()
    {
        for (int value = 1; value <= 400; value++)
        {
            yield return value;
        }

        for (int value = 425; value <= 20000; value += 25)
        {
            yield return value;
        }

        yield return 100000;
        yield return 1000000;
    }

    [Fact]
    public void Upgrade_NeverPaysForItself_AcrossThePriceRange()
    {
        foreach (int price in Prices())
        {
            for (int rarity = 0; rarity <= 4; rarity++)
            {
                foreach (CraftQuality quality in Qualities)
                {
                    for (int level = 0; level < ItemUpgrades.MaxLevel; level++)
                    {
                        int before = ReforgeRules.InstanceValue(price, rarity, (ItemRarity)rarity, quality, level);
                        int after = ReforgeRules.InstanceValue(price, rarity, (ItemRarity)rarity, quality, level + 1);
                        int gold = ReforgeRules.UpgradeGold(before, after, level + 1);

                        Assert.True(after >= before);
                        Assert.False(
                            ReforgeRules.Exploitable(gold, before, after),
                            $"upgrade to +{level + 1} of a {(ItemRarity)rarity} {quality} item worth {price}: {gold} gold for {after - before} value");
                    }
                }
            }
        }
    }

    [Fact]
    public void Promote_NeverPaysForItself_AcrossThePriceRange()
    {
        foreach (int price in Prices())
        {
            foreach (ItemRarity rarity in new[] { ItemRarity.Uncommon, ItemRarity.Rare })
            {
                foreach (CraftQuality quality in Qualities)
                {
                    for (int level = 0; level <= ItemUpgrades.MaxLevel; level++)
                    {
                        int affixes = (int)rarity;
                        int before = ReforgeRules.InstanceValue(price, affixes, rarity, quality, level);
                        int after = ReforgeRules.InstanceValue(price, affixes + 1, rarity + 1, quality, level);
                        int gold = ReforgeRules.PromoteGold(before, after);

                        Assert.True(after > before);
                        Assert.False(
                            ReforgeRules.Exploitable(gold, before, after),
                            $"promoting a {rarity} {quality} +{level} item worth {price}: {gold} gold for {after - before} value");
                    }
                }
            }
        }
    }

    [Fact]
    public void FullUpgradeLadder_CostsMoreThanTheValueItAdds()
    {
        foreach (int price in Prices())
        {
            long spent = 0;
            for (int level = 0; level < ItemUpgrades.MaxLevel; level++)
            {
                int before = ReforgeRules.InstanceValue(price, 0, ItemRarity.Common, CraftQuality.Standard, level);
                int after = ReforgeRules.InstanceValue(price, 0, ItemRarity.Common, CraftQuality.Standard, level + 1);
                spent += ReforgeRules.UpgradeGold(before, after, level + 1);
            }

            int plain = ReforgeRules.InstanceValue(price, 0, ItemRarity.Common, CraftQuality.Standard, 0);
            int maxed = ReforgeRules.InstanceValue(price, 0, ItemRarity.Common, CraftQuality.Standard, ItemUpgrades.MaxLevel);
            Assert.True(spent > ReforgeRules.MaxSellFactor * (maxed - plain));
        }
    }

    [Fact]
    public void Exploitable_CountsBreakingEven()
    {
        // 100 value added sells for at most 150 (+2 for rounding): 152 gold is not enough, 153 is.
        Assert.True(ReforgeRules.Exploitable(152, 1000, 1100));
        Assert.False(ReforgeRules.Exploitable(153, 1000, 1100));
        Assert.False(ReforgeRules.Exploitable(3, 1000, 1000));
    }

    [Fact]
    public void RerollGold_RisesWithEachReroll_ThenHolds()
    {
        foreach (int price in new[] { 0, 1, 40, 75, 500, 12345 })
        {
            Assert.Equal(ReforgeRules.RerollBaseGold(price), ReforgeRules.RerollGold(price, 0));
            Assert.True(ReforgeRules.RerollGold(price, 0) >= 15);
            for (int prior = 0; prior < ReforgeRules.MaxRerollSteps; prior++)
            {
                Assert.True(ReforgeRules.RerollGold(price, prior + 1) > ReforgeRules.RerollGold(price, prior));
            }

            Assert.Equal(
                ReforgeRules.RerollGold(price, ReforgeRules.MaxRerollSteps),
                ReforgeRules.RerollGold(price, ReforgeRules.MaxRerollSteps + 50));
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 5)]
    [InlineData(99, 5)]
    public void RerollMaterials_StepUpAndCap(int prior, int expected)
    {
        Assert.Equal(expected, ReforgeRules.RerollMaterials(prior));
    }

    [Fact]
    public void UpgradeGold_RisesWithEveryLevel()
    {
        foreach (int price in new[] { 1, 30, 250, 4000 })
        {
            int previous = 0;
            for (int level = 0; level < ItemUpgrades.MaxLevel; level++)
            {
                int before = ReforgeRules.InstanceValue(price, 2, ItemRarity.Rare, CraftQuality.Fine, level);
                int after = ReforgeRules.InstanceValue(price, 2, ItemRarity.Rare, CraftQuality.Fine, level + 1);
                int gold = ReforgeRules.UpgradeGold(before, after, level + 1);
                Assert.True(gold > previous);
                Assert.Equal(level + 1, ReforgeRules.UpgradeMaterials(level + 1));
                previous = gold;
            }
        }
    }

    [Fact]
    public void Caps_AreHonoured()
    {
        Assert.True(ReforgeRules.CanUpgrade(0));
        Assert.True(ReforgeRules.CanUpgrade(ItemUpgrades.MaxLevel - 1));
        Assert.False(ReforgeRules.CanUpgrade(ItemUpgrades.MaxLevel));
        Assert.False(ReforgeRules.CanUpgrade(-1));

        Assert.False(ReforgeRules.CanPromote(ItemRarity.Common));
        Assert.True(ReforgeRules.CanPromote(ItemRarity.Uncommon));
        Assert.True(ReforgeRules.CanPromote(ItemRarity.Rare));
        Assert.False(ReforgeRules.CanPromote(ItemRarity.Epic));
        Assert.False(ReforgeRules.CanPromote(ItemRarity.Legendary));
        Assert.True(ReforgeRules.PromoteMaterials(ItemRarity.Rare) > ReforgeRules.PromoteMaterials(ItemRarity.Uncommon));
    }

    [Fact]
    public void Fees_SaturateInsteadOfWrapping()
    {
        Assert.True(ReforgeRules.RerollGold(int.MaxValue, 99) > 0);
        Assert.True(ReforgeRules.UpgradeGold(int.MaxValue - 5, int.MaxValue, 5) > 0);
        Assert.True(ReforgeRules.PromoteGold(1, int.MaxValue) > 0);
    }

    [Fact]
    public void UpgradeLevels_AreAFlatShareOfTheBaseBonuses()
    {
        Assert.Equal(1f, ItemUpgrades.StatMultiplier(0));
        Assert.Equal(1f, ItemUpgrades.ValueMultiplier(0));
        Assert.Equal(1f + (ItemUpgrades.StatPerLevel * ItemUpgrades.MaxLevel), ItemUpgrades.StatMultiplier(ItemUpgrades.MaxLevel), 4);
        Assert.Equal(ItemUpgrades.StatMultiplier(ItemUpgrades.MaxLevel), ItemUpgrades.StatMultiplier(ItemUpgrades.MaxLevel + 3));
        for (int level = 1; level <= ItemUpgrades.MaxLevel; level++)
        {
            Assert.True(ItemUpgrades.StatMultiplier(level) > ItemUpgrades.StatMultiplier(level - 1));
            Assert.True(ItemUpgrades.ValueMultiplier(level) > ItemUpgrades.ValueMultiplier(level - 1));
        }
    }

    [Theory]
    [InlineData(3, 0, 3)]     // the item's own tier wins
    [InlineData(9, 0, 6)]     // and is clamped to the last realm
    [InlineData(0, 0, 1)]     // a legacy item with nothing authored reforges with iron
    [InlineData(0, 14, 2)]
    [InlineData(0, 28, 3)]
    [InlineData(0, 41, 5)]
    [InlineData(0, 50, 6)]
    public void MaterialTier_FallsBackToItemLevel(int templateTier, int itemLevel, int expected)
    {
        Assert.Equal(expected, ReforgeRules.MaterialTier(templateTier, itemLevel));
    }

    [Fact]
    public void MaterialFor_NamesOneIngotPerTier()
    {
        var seen = new HashSet<string>();
        for (int tier = 1; tier <= CraftingSkill.MaxTier; tier++)
        {
            string id = ReforgeRules.MaterialFor(tier);
            Assert.StartsWith("item.material.", id);
            Assert.EndsWith("_ingot", id);
            Assert.True(seen.Add(id));
        }

        Assert.Equal(ReforgeRules.MaterialFor(1), ReforgeRules.MaterialFor(0));
    }

    [Theory]
    [InlineData(100, 0, ItemRarity.Common, 100)]
    [InlineData(100, 1, ItemRarity.Uncommon, 165)]   // (100 + 10) x 1.5
    [InlineData(100, 2, ItemRarity.Rare, 240)]       // (100 + 20) x 2
    public void InstanceValue_MatchesTheItemFormula(int templateValue, int affixes, ItemRarity rarity, int expected)
    {
        Assert.Equal(expected, ReforgeRules.InstanceValue(templateValue, affixes, rarity, CraftQuality.Standard, 0));
    }
}
