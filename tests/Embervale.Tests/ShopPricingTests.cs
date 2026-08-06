using Embervale.Economy;
using Embervale.Factions;
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
        // Swept across every standing (38C): a discount lowers the markup, so the money-printer
        // invariant has to hold at Allied and not merely at the authored price. It does, because
        // BuyPrice clamps its markup to >= 1 — but that is exactly the kind of load-bearing accident
        // that a later refactor removes without noticing, so it is pinned here rather than reasoned about.
        foreach (ReputationTier tier in System.Enum.GetValues<ReputationTier>())
        {
            float adjusted = ShopPricing.MarkupFor(markup, tier);
            Assert.True(
                ShopPricing.SellPrice(value, fraction) <= ShopPricing.BuyPrice(value, adjusted),
                $"value {value} at buy x{markup} ({tier}) / sell x{fraction} prints gold");
        }
    }

    [Fact]
    public void NeutralStandingIsExactlyTheAuthoredPrice()
    {
        // The tier a player who has done nothing sits at must be the price the .tres says, or every
        // authored number in the game is quietly off by a multiplier.
        Assert.Equal(1f, ShopPricing.PriceMultiplierFor(ReputationTier.Neutral));
        Assert.Equal(1.5f, ShopPricing.MarkupFor(1.5f, ReputationTier.Neutral));
    }

    [Fact]
    public void StandingMovesPricesInBothDirections()
    {
        // A one-directional ramp would leave three of the seven tiers inert.
        Assert.True(ShopPricing.PriceMultiplierFor(ReputationTier.Allied) < 1f);
        Assert.True(ShopPricing.PriceMultiplierFor(ReputationTier.Hated) > 1f);
    }

    [Fact]
    public void BetterStandingNeverCostsMore()
    {
        // Monotonic down the ramp, mirroring ReputationTierTests' monotonicity assertion. A single
        // transposed row in the table would otherwise make one tier a worse deal than the one below it,
        // which reads as the discount being broken rather than as a typo.
        var tiers = System.Enum.GetValues<ReputationTier>();
        for (int i = 1; i < tiers.Length; i++)
        {
            Assert.True(
                ShopPricing.PriceMultiplierFor(tiers[i]) <= ShopPricing.PriceMultiplierFor(tiers[i - 1]),
                $"{tiers[i]} charges more than {tiers[i - 1]}");
        }
    }

    [Fact]
    public void ADiscountCannotMakeAnItemFree()
    {
        // The floor survives the multiplier: a 1-value trinket at Allied standing still costs a coin.
        Assert.True(ShopPricing.BuyPrice(1, ShopPricing.MarkupFor(1.5f, ReputationTier.Allied)) >= 1);
        Assert.True(ShopPricing.BuyPrice(0, ShopPricing.MarkupFor(1f, ReputationTier.Allied)) >= 1);
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
