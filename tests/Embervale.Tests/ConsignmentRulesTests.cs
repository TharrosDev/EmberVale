using Embervale.Economy;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// What a broker pays and when (Phase 38P). The payout is a player-facing number on a path with no
/// merchant purse to catch an authoring mistake, and one of these tests is guarding a realm-wide
/// invariant rather than a rounding rule — see <see cref="ConsignmentNeverInvertsTheSpread"/>.
/// </summary>
public class ConsignmentRulesTests
{
    [Fact]
    public void TheShelfPriceIsTheOrdinarySellPriceAtAKeenerFraction()
    {
        // The whole point of a broker: 0.85 where the best shop in the realm pays 0.62.
        Assert.Equal(85, ConsignmentRules.Gross(100, 0.85f));
        Assert.Equal(62, ShopPricing.SellPrice(100, 0.62f));
    }

    [Fact]
    public void ConsignmentNeverInvertsTheSpread()
    {
        // ⚠️ THE LOAD-BEARING ONE. `sell <= value <= buy` holds at every shop in the realm by
        // construction (38A/38N1), and a broker paying over an item's value would be the first thing
        // in the game to break it — buy from the cheapest merchant, consign, repeat, forever.
        // Gross routes through ShopPricing.SellPrice, whose 0..1 clamp makes that unauthorable.
        Assert.Equal(100, ConsignmentRules.Gross(100, 1f));
        Assert.Equal(100, ConsignmentRules.Gross(100, 4f));
        Assert.Equal(100, ConsignmentRules.Gross(100, float.MaxValue));

        // And the commission only ever subtracts, so net <= gross <= value at every step.
        Assert.True(ConsignmentRules.Net(ConsignmentRules.Gross(100, 9f), 0f) <= 100);
    }

    [Fact]
    public void TheCommissionRoundsAgainstThePlayer()
    {
        // The cut rounds up, so the house is never accidentally working for free. 15% of 85 is 12.75,
        // charged as 13.
        Assert.Equal(72, ConsignmentRules.Net(85, 0.15f));

        // Rounding the cut down instead would pay a 1-gold trinket its whole shelf price at any
        // commission short of a full one.
        Assert.Equal(0, ConsignmentRules.Net(1, 0.15f));
    }

    [Fact]
    public void TheEndsOfTheCommissionRangeBehave()
    {
        Assert.Equal(85, ConsignmentRules.Net(85, 0f));   // no house cut
        Assert.Equal(0, ConsignmentRules.Net(85, 1f));    // the house takes all of it
        Assert.Equal(85, ConsignmentRules.Net(85, -2f));  // clamped, not inverted into a bonus
        Assert.Equal(0, ConsignmentRules.Net(85, 3f));    // clamped, not into a debt
        Assert.Equal(0, ConsignmentRules.Net(-40, 0.1f)); // a negative shelf price pays nothing
    }

    [Fact]
    public void AListingSellsOnTheDayItIsDue()
    {
        // Listed on day 2 with a three-day sale: due on day 5, not before.
        Assert.False(ConsignmentRules.HasSold(2, 4, 3));
        Assert.True(ConsignmentRules.HasSold(2, 5, 3));
        Assert.True(ConsignmentRules.HasSold(2, 40, 3));
    }

    [Fact]
    public void AClockThatWentBackwardsDoesNotStrandAListing()
    {
        // Inherited from ShopStock.IsRestockDue rather than rediscovered: a quickload onto an earlier
        // day must not leave gold owed for the rest of the run. --validate rejects a zero-day house,
        // so the never-sells case below is an authoring fault the player cannot reach.
        Assert.True(ConsignmentRules.HasSold(9, 1, 3));
        Assert.False(ConsignmentRules.HasSold(2, 99, 0));
    }
}
