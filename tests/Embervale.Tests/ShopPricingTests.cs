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

    [Fact]
    public void ASpecialistPaysMoreAndChargesLess()
    {
        // The premium is the point of 38F: it is what makes *where* the player sells matter.
        Assert.Equal(40, ShopPricing.SellPrice(100, ShopPricing.SellFractionFor(0.4f, specialty: false)));
        Assert.Equal(62, ShopPricing.SellPrice(100, ShopPricing.SellFractionFor(0.5f, specialty: true)));

        float plain = ShopPricing.MarkupFor(1.5f, ReputationTier.Neutral);
        float own = ShopPricing.MarkupFor(1.5f, ReputationTier.Neutral, specialty: true);
        Assert.True(own < plain);
        Assert.True(ShopPricing.BuyPrice(100, own) < ShopPricing.BuyPrice(100, plain));
    }

    [Fact]
    public void AGenerousFractionStillCannotPayAboveAnItemsValue()
    {
        // 0.9 x the 1.25 premium is 1.125, which SellPrice clamps to 1. That clamp is the whole reason
        // a new multiplier cannot invert the spread, so it is worth pinning directly rather than only
        // through the sweep below.
        Assert.Equal(100, ShopPricing.SellPrice(100, ShopPricing.SellFractionFor(0.9f, specialty: true)));
    }

    /// <summary>
    /// The invariant the whole economy arc rests on: a merchant never pays more for something than they
    /// would charge for it. Every multiplier the arc adds — standing, the specialty premium, and later
    /// regional demand and market saturation — is folded into either the markup or the fraction, and
    /// <see cref="ShopPricing.BuyPrice"/>'s <c>&gt;= 1</c> clamp and <see cref="ShopPricing.SellPrice"/>'s
    /// <c>0..1</c> clamp make <c>sell &lt;= value &lt;= buy</c> true for <em>any</em> of them.
    ///
    /// ⚠️ <b>Every future price multiplier joins this sweep or it does not ship.</b> This is the test
    /// that lets a later sub-phase add one without re-deriving the safety argument, and the reason 38F
    /// did not need a new price class to hold them: the guarantee already lived in the clamps.
    /// </summary>
    [Fact]
    public void NoCombinationOfMultipliersLetsSellingBeatBuying()
    {
        ReputationTier[] tiers =
        {
            ReputationTier.Hated, ReputationTier.Hostile, ReputationTier.Unfriendly,
            ReputationTier.Neutral, ReputationTier.Friendly, ReputationTier.Honored,
            ReputationTier.Allied,
        };
        float[] markups = { 1f, 1.2f, 1.5f, 1.6f, 2f, 3f };
        float[] fractions = { 0.05f, 0.2f, 0.4f, 0.45f, 0.5f, 0.8f, 0.99f };
        int[] values = { 0, 1, 2, 3, 7, 25, 99, 100, 420, 9999 };
        int[] absorbed = { 0, 1, 6, 12, 40 };   // 38H's market saturation, honouring the contract above

        foreach (ReputationTier tier in tiers)
        {
            foreach (float markup in markups)
            {
                foreach (float fraction in fractions)
                {
                    foreach (bool specialty in new[] { false, true })
                    {
                        foreach (int value in values)
                        {
                            int buy = ShopPricing.BuyPrice(
                                value, ShopPricing.MarkupFor(markup, tier, specialty));
                            int unit = ShopPricing.SellPrice(
                                value, ShopPricing.SellFractionFor(fraction, specialty));

                            foreach (int taken in absorbed)
                            {
                                int sell = ShopStock.SaturatedPayout(
                                    unit, taken, quantity: 1, restockDays: 1);

                                Assert.True(
                                    sell <= buy,
                                    $"sell {sell} > buy {buy} at tier {tier}, markup {markup}, " +
                                    $"fraction {fraction}, specialty {specialty}, value {value}, " +
                                    $"absorbed {taken}");
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// The stronger claim, over the band <c>--validate</c> actually permits: not merely that selling
    /// cannot beat buying, but that a round trip always <em>costs</em> something. Equality is not an
    /// exploit, but it is frictionless churn — a player buying and re-selling for nothing at all — and
    /// the margin rule in <c>ValidateShopTrade</c> exists to keep authored data inside this band.
    /// </summary>
    [Fact]
    public void AuthoredSpreadsAlwaysCostSomethingToRoundTrip()
    {
        // ⚠️ These arrays are the set of spreads ACTUALLY AUTHORED in data/shops/. A new shop with a
        // pair outside them is covered only by ValidateShopTrade's margin rule, so the pair goes here
        // in the same commit. 38L widened them from {1.5, 1.6} x {0.4, 0.45} when the Embermarket
        // roster added 1.55, 1.65 and 1.7 markups and a 0.42 fraction.
        ReputationTier[] tiers = { ReputationTier.Neutral, ReputationTier.Honored, ReputationTier.Allied };
        float[] markups = { 1.5f, 1.55f, 1.6f, 1.65f, 1.7f };
        float[] fractions = { 0.4f, 0.42f, 0.45f };

        foreach (ReputationTier tier in tiers)
        {
            foreach (float markup in markups)
            {
                foreach (float fraction in fractions)
                {
                    foreach (bool specialty in new[] { false, true })
                    {
                        for (int value = 1; value <= 500; value++)
                        {
                            int buy = ShopPricing.BuyPrice(
                                value, ShopPricing.MarkupFor(markup, tier, specialty));
                            int sell = ShopPricing.SellPrice(
                                value, ShopPricing.SellFractionFor(fraction, specialty));

                            Assert.True(
                                sell < buy,
                                $"round trip is free at tier {tier}, markup {markup}, " +
                                $"fraction {fraction}, specialty {specialty}, value {value}");
                        }
                    }
                }
            }
        }
    }
}
