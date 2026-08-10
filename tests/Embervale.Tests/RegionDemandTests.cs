using System.Collections.Generic;
using Embervale.Economy;
using Embervale.Factions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Settlement demand (Phase 38G) — the multiplier that finally makes carrying goods pay, and the only
/// one in the arc that moves an item's <em>value</em> rather than a spread over it. The cells and the
/// shops are Godot resources, so what can be tested here is the arithmetic and the two properties the
/// whole design rests on: it is symmetric, and it does not break the spread at a counter.
/// </summary>
public class RegionDemandTests
{
    private static readonly List<string> Fish = new() { "fish", "food" };
    private static readonly List<string> Ore = new() { "ore" };
    private static readonly List<string> None = new();

    [Fact]
    public void ASurplusIsWorthLessAndADemandMore()
    {
        Assert.True(RegionDemand.ValueAt(100, Fish, surplus: Fish, demand: None) < 100);
        Assert.True(RegionDemand.ValueAt(100, Fish, surplus: None, demand: Fish) > 100);
    }

    [Fact]
    public void AnUntaggedGoodAndAnIndifferentPlaceBothPriceAtPar()
    {
        Assert.Equal(100, RegionDemand.ValueAt(100, None, Fish, Ore));
        Assert.Equal(100, RegionDemand.ValueAt(100, Ore, surplus: Fish, demand: Fish));
    }

    /// <summary>Any one of the item's tags matching is enough — a salted eel is `fish` and `food`, and a
    /// mine short of food must pay for it under either name.</summary>
    [Fact]
    public void AnyMatchingTagCounts()
    {
        Assert.True(RegionDemand.ValueAt(100, Fish, None, new List<string> { "food" }) > 100);
    }

    /// <summary>Authoring nonsense still has to price deterministically: `--validate` refuses a tag in
    /// both lists, and until someone fixes it the surplus wins rather than the answer depending on
    /// list order.</summary>
    [Fact]
    public void ATagInBothListsResolvesAsSurplus()
    {
        Assert.Equal(
            RegionDemand.ValueAt(100, Fish, Fish, None),
            RegionDemand.ValueAt(100, Fish, Fish, Fish));
    }

    /// <summary>A surplus must never round a cheap good to nothing: a value of 0 is a good that is free
    /// to buy and impossible to sell.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ASurplusNeverRoundsAGoodToWorthless(int value)
    {
        Assert.True(RegionDemand.ValueAt(value, Fish, Fish, None) >= 1);
    }

    [Fact]
    public void NothingIsWorthAnythingWhereItHasNoValueToBeginWith()
    {
        Assert.Equal(0, RegionDemand.ValueAt(0, Fish, None, Fish));
        Assert.Equal(0, RegionDemand.ValueAt(-5, Fish, None, Fish));
    }

    /// <summary>
    /// ⚠️ The property the sub-phase rests on, stated directly: the local value goes to <b>both</b>
    /// sides of one counter, so the 38A clamps still hold there. A carry between two places can pay;
    /// a round trip at one place cannot. 38F's carried warning was that demand applied to one side
    /// moves the ratio instead of the price — here there is no one side to apply it to.
    /// </summary>
    [Fact]
    public void ThePriceMovesAtACounterButTheSpreadDoesNot()
    {
        foreach (List<string> place in new[] { None, Fish })
        {
            foreach (bool asDemand in new[] { false, true })
            {
                int local = RegionDemand.ValueAt(
                    100, Fish, asDemand ? None : place, asDemand ? place : None);

                int buy = ShopPricing.BuyPrice(local, ShopPricing.MarkupFor(1.5f, ReputationTier.Allied));
                int sell = ShopPricing.SellPrice(local, ShopPricing.SellFractionFor(0.62f, true));

                Assert.True(buy >= local, $"buy {buy} < local {local}");
                Assert.True(sell <= local, $"sell {sell} > local {local}");
                Assert.True(sell < buy, $"round trip is free at local {local}");
            }
        }
    }

    /// <summary>
    /// The other half, and the reason this is a feature rather than a bug: two counters with opposite
    /// tags can be carried between at a profit. Buy where it is a surplus, sell where it is short —
    /// with the realm's actual worst-case pair of a specialist seller and a specialist buyer.
    /// </summary>
    [Fact]
    public void ASurplusToADemandCarryPays()
    {
        const int Value = 20;

        int atSource = RegionDemand.ValueAt(Value, Fish, Fish, None);
        int atSink = RegionDemand.ValueAt(Value, Fish, None, Fish);

        int buy = ShopPricing.BuyPrice(
            atSource, ShopPricing.MarkupFor(1.18f, ReputationTier.Neutral, specialty: true));
        int sell = ShopPricing.SellPrice(
            atSink, ShopPricing.SellFractionFor(0.62f, specialty: true));

        Assert.True(sell > buy, $"carry pays nothing: buy {buy} at source, sell {sell} at sink");
    }
}
