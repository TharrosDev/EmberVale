using Embervale.Economy;
using Embervale.Items;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the buy/sell spread (Phase 38A). Prices are the numbers a player argues with, and each of the
/// three rounding rules below is a live exploit or a live item-loss bug if it drifts: a buy price that
/// rounds to zero is an infinite item, a negative sell payout has the player paying to hand things
/// over, and a sell price that can exceed a buy price is a money printer that needs no more than a
/// stack of potions and patience.
///
/// The panel reads exactly these functions for both the button's enabled state and the press itself,
/// which is what keeps a refusal from drifting away from what the button said.
/// </summary>
public class ShopPricingTests
{
    [Fact]
    public void BuyingRoundsUpAndIsNeverFree()
    {
        // 1 gold at a 1.5x markup is 1.5, which must not floor to 1... and a value of 1 against any
        // markup must never reach 0. A free item is an infinite item.
        Assert.Equal(2, ShopPricing.BuyPrice(1, 1.5f));
        Assert.Equal(15, ShopPricing.BuyPrice(10, 1.5f));
        Assert.Equal(1, ShopPricing.BuyPrice(0, 1.5f));
        Assert.Equal(1, ShopPricing.BuyPrice(-50, 1.5f));
    }

    [Fact]
    public void SellingRoundsDownAndNeverGoesNegative()
    {
        Assert.Equal(4, ShopPricing.SellPrice(10, 0.4f));
        Assert.Equal(0, ShopPricing.SellPrice(1, 0.4f)); // 0.4 floors to nothing — the panel refuses it
        Assert.Equal(0, ShopPricing.SellPrice(100, -2f));
        Assert.Equal(0, ShopPricing.SellPrice(-100, 0.4f));
    }

    [Fact]
    public void AMarkupBelowOneIsClampedRatherThanTrusted()
    {
        // --validate rejects one, but a hand-edited .tres must not turn a merchant into a discount.
        Assert.Equal(100, ShopPricing.BuyPrice(100, 0.1f));
        Assert.Equal(100, ShopPricing.BuyPrice(100, -5f));
    }

    [Fact]
    public void AFractionAboveOneIsClampedToTheItemsOwnValue()
    {
        // The clamp is what makes the sell-never-exceeds-buy property hold for *any* authored spread,
        // not just the legal ones.
        Assert.Equal(100, ShopPricing.SellPrice(100, 4f));
    }

    [Theory]
    [InlineData(1, 1.0f, 1.0f)]
    [InlineData(1, 1.5f, 0.4f)]
    [InlineData(7, 1.5f, 0.4f)]
    [InlineData(250, 2.5f, 0.9f)]
    [InlineData(999, 1.0f, 1.0f)]
    [InlineData(40, 0.2f, 3.0f)] // both ends illegal, both ends clamped
    public void SellingBackNeverPaysMoreThanBuyingCost(int value, float markup, float fraction)
    {
        Assert.True(
            ShopPricing.SellPrice(value, fraction) <= ShopPricing.BuyPrice(value, markup),
            $"value {value} at buy x{markup} / sell x{fraction} prints gold");
    }

    [Fact]
    public void ExactlyEnoughGoldIsEnough()
    {
        // Off-by-one here is a player standing at a stall with the exact price in their pocket, being
        // told no — the same boundary PropertyClaimTests pins for a deed.
        Assert.True(ShopPricing.CanAfford(price: 60, goldHeld: 60));
        Assert.False(ShopPricing.CanAfford(price: 60, goldHeld: 59));
        Assert.True(ShopPricing.CanAfford(price: 0, goldHeld: 0));
    }

    [Fact]
    public void QuestItemsAndGoldAreNeverSellable()
    {
        // Selling a quest item silently strands a Collect objective with no way to recover it, and
        // gold-for-gold would leak coins through the spread.
        Assert.False(ShopPricing.Sellable(ItemType.Quest, isCurrency: false));
        Assert.False(ShopPricing.Sellable(ItemType.Misc, isCurrency: true));
    }

    [Theory]
    [InlineData(ItemType.Misc)]
    [InlineData(ItemType.Consumable)]
    [InlineData(ItemType.Weapon)]
    [InlineData(ItemType.Armor)]
    [InlineData(ItemType.Material)]
    public void EverythingElseIsSellable(ItemType type)
    {
        Assert.True(ShopPricing.Sellable(type, isCurrency: false));
    }
}
