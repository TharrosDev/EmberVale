using System;
using Embervale.Items;

namespace Embervale.Crafting;

/// <summary>
/// The pure rules of reforging at a forge: what a reroll, an upgrade level and a rarity promotion
/// cost, where each stops, and the proof none of them is a money printer. Godot-free; the
/// <see cref="CraftingComponent"/> applies them and <c>CraftingPanel</c> quotes them.
///
/// ⚠️ <b>Every fee is built from the value it adds.</b> A reroll changes no value. An upgrade and a
/// promotion both raise <see cref="ItemInstance.Value"/>, and the gold charged is a floor plus
/// <see cref="GainMultiple"/> times that rise, while the keenest buyer in the realm pays at most
/// <see cref="MaxSellFactor"/> times an item's value. So paying the forge and selling the piece
/// always loses gold, at any price, before the materials are even counted.
/// <see cref="Exploitable"/> is that inequality and the unit tests run it over the whole price range.
/// </summary>
public static class ReforgeRules
{
    /// <summary>The most any merchant pays as a multiple of an item's value: a sell fraction is
    /// clamped to 1 (<c>ShopPricing.SellPrice</c>) and local demand lifts the value itself by at most
    /// <c>RegionDemand.DemandFactor</c>.</summary>
    public const float MaxSellFactor = 1.5f;

    /// <summary>Gold charged per gold of value an upgrade or promotion adds. Twice
    /// <see cref="MaxSellFactor"/>, so the margin survives rounding and a future keener buyer.</summary>
    public const int GainMultiple = 3;

    /// <summary>Rerolls past this many stop raising the price.</summary>
    public const int MaxRerollSteps = 8;

    /// <summary>The most tier materials a single reroll asks for.</summary>
    public const int MaxRerollMaterials = 5;

    // --- Reroll one affix -------------------------------------------------------------------

    /// <summary>Gold to reroll one affix on an item worth <paramref name="itemValue"/> that has
    /// already been rerolled <paramref name="priorRerolls"/> times: a fifth of its value (at least
    /// 15), rising by half of that again with each earlier reroll.</summary>
    public static int RerollGold(int itemValue, int priorRerolls)
    {
        long baseFee = RerollBaseGold(itemValue);
        int steps = Math.Clamp(priorRerolls, 0, MaxRerollSteps);
        return (int)Math.Min(int.MaxValue, baseFee * (2 + steps) / 2);
    }

    /// <summary>The first reroll's fee: what <see cref="RerollGold"/> charges before any earlier
    /// reroll raises it (shown as its own line on the bill).</summary>
    public static int RerollBaseGold(int itemValue) =>
        (int)Math.Min(int.MaxValue, Math.Max(15L, (Math.Max(0, itemValue) + 4L) / 5L));

    /// <summary>Tier materials for that reroll: one, and one more for every two earlier rerolls.</summary>
    public static int RerollMaterials(int priorRerolls) =>
        Math.Min(MaxRerollMaterials, 1 + (Math.Max(0, priorRerolls) / 2));

    // --- Upgrade ----------------------------------------------------------------------------

    /// <summary>Whether an item at <paramref name="upgradeLevel"/> can take another level.</summary>
    public static bool CanUpgrade(int upgradeLevel) => upgradeLevel >= 0 && upgradeLevel < ItemUpgrades.MaxLevel;

    /// <summary>Gold to raise an item to <paramref name="targetLevel"/>, given its value before and
    /// after the level.</summary>
    public static int UpgradeGold(int valueBefore, int valueAfter, int targetLevel)
    {
        long gain = Math.Max(0L, (long)valueAfter - valueBefore);
        long fee = UpgradeBaseGold(valueBefore, targetLevel) + (GainMultiple * gain);
        return (int)Math.Min(int.MaxValue, fee);
    }

    /// <summary>The smith's work for that level, before the value it adds is charged for: 20 gold
    /// a level reached plus a tenth of the item's value.</summary>
    public static int UpgradeBaseGold(int valueBefore, int targetLevel) =>
        (int)Math.Min(
            int.MaxValue,
            (20L * Math.Clamp(targetLevel, 1, ItemUpgrades.MaxLevel)) + ((Math.Max(0, valueBefore) + 9L) / 10L));

    /// <summary>Tier materials for that level: as many as the level being reached.</summary>
    public static int UpgradeMaterials(int targetLevel) => Math.Clamp(targetLevel, 1, ItemUpgrades.MaxLevel);

    // --- Promote rarity ---------------------------------------------------------------------

    /// <summary>Whether an item of this rarity can be promoted one step. Only Uncommon and Rare
    /// can: a Common piece has no affixes to build on, and the forge never makes a Legendary.</summary>
    public static bool CanPromote(ItemRarity rarity) => rarity is ItemRarity.Uncommon or ItemRarity.Rare;

    /// <summary>Gold to promote, given the item's value before and after.</summary>
    public static int PromoteGold(int valueBefore, int valueAfter)
    {
        long gain = Math.Max(0L, (long)valueAfter - valueBefore);
        long fee = PromoteBaseGold(valueBefore) + (GainMultiple * gain);
        return (int)Math.Min(int.MaxValue, fee);
    }

    /// <summary>The smith's work for a promotion, before the value it adds: 150 gold plus half
    /// the item's value.</summary>
    public static int PromoteBaseGold(int valueBefore) =>
        (int)Math.Min(int.MaxValue, 150L + ((Math.Max(0, valueBefore) + 1L) / 2L));

    /// <summary>Tier materials to promote from <paramref name="rarity"/>: 5 from Uncommon, 7 from Rare.</summary>
    public static int PromoteMaterials(ItemRarity rarity) => 3 + (2 * Math.Clamp((int)rarity, 0, 4));

    // --- Shared -----------------------------------------------------------------------------

    /// <summary>
    /// Whether paying <paramref name="goldCost"/> to lift an item from <paramref name="valueBefore"/>
    /// to <paramref name="valueAfter"/> could be recovered by selling it to the keenest buyer.
    /// <c>true</c> is a rules fault, never a runtime state. Breaking even counts as exploitable,
    /// for the reason <c>CommissionRules.Exploitable</c> gives.
    /// </summary>
    public static bool Exploitable(int goldCost, int valueBefore, int valueAfter)
    {
        // The most a sale can rise by: both prices are floored from a value rounded after the demand
        // factor, so the difference can exceed the exact product by at most one coin each way.
        double best = (MaxSellFactor * Math.Max(0L, (long)valueAfter - valueBefore)) + 2d;
        return best >= goldCost;
    }

    /// <summary>
    /// An instance's merchant value from plain numbers: the same arithmetic as
    /// <see cref="ItemInstance.Value"/>, restated because a test cannot construct an item.
    /// <c>--validate</c> compares the two over every equippable so they cannot drift.
    /// </summary>
    public static int InstanceValue(int templateValue, int affixCount, ItemRarity rarity, CraftQuality quality, int upgradeLevel)
    {
        float rarityMult = 1f + ((int)rarity * 0.5f);
        float workmanship = CraftQualities.ValueMultiplier(quality) * ItemUpgrades.ValueMultiplier(upgradeLevel);
        return (int)MathF.Round((templateValue + (affixCount * 10)) * rarityMult * workmanship);
    }

    /// <summary>The realm tier whose metal a reforge consumes: the item's own tier, or for a legacy
    /// item with none, the tier its item level falls in (1 when it has neither).</summary>
    public static int MaterialTier(int templateTier, int itemLevel)
    {
        if (templateTier > 0)
        {
            return Math.Min(templateTier, CraftingSkill.MaxTier);
        }

        return itemLevel switch
        {
            < 10 => 1,
            < 20 => 2,
            < 30 => 3,
            < 40 => 4,
            < 46 => 5,
            _ => 6,
        };
    }

    /// <summary>The ingot a reforge of <paramref name="tier"/> gear consumes.</summary>
    public static string MaterialFor(int tier) => Math.Clamp(tier, 1, CraftingSkill.MaxTier) switch
    {
        1 => "item.material.iron_ingot",
        2 => "item.material.steel_ingot",
        3 => "item.material.blacksteel_ingot",
        4 => "item.material.sunsteel_ingot",
        5 => "item.material.moonsilver_ingot",
        _ => "item.material.starmetal_ingot",
    };
}
