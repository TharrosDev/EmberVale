using Embervale.Economy;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins what a fast-travel jump costs (Phase 38C). Fast travel was free from Phase 25G until now, so
/// this is a price being introduced onto something players have had for nothing — the free case is the
/// one that has to be exactly right, because a fee charged where the design says there should be none
/// is indistinguishable from the ownership check being broken.
///
/// The map screen's button label and the bootstrap's charge both call <see cref="TravelFee.For"/>, so
/// what these assertions really pin is that the price shown and the price taken are one number.
/// </summary>
public class TravelFeeTests
{
    [Fact]
    public void TravellingToAHoldingYouOwnIsFree()
    {
        // The whole reason the sink does not read as a toll booth: your house is the anchor you can
        // always afford to reach. Free in both directions — crossing a realm to get home costs nothing.
        Assert.Equal(0, TravelFee.For(ownedHolding: true, crossRegion: false, mounted: false));
        Assert.Equal(0, TravelFee.For(ownedHolding: true, crossRegion: true, mounted: false));
    }

    [Fact]
    public void ALocalJumpCostsTheLocalFee()
    {
        Assert.Equal(
            TravelFee.LocalFee, TravelFee.For(ownedHolding: false, crossRegion: false, mounted: false));
    }

    [Fact]
    public void CrossingARealmCostsMore()
    {
        // A flat fee would make the short jump feel arbitrary; the gap is what makes the price read as
        // distance rather than as a tax on using the map.
        Assert.Equal(
            TravelFee.CrossRegionFee, TravelFee.For(ownedHolding: false, crossRegion: true, mounted: false));
        Assert.True(TravelFee.CrossRegionFee > TravelFee.LocalFee);
    }

    [Fact]
    public void NoFeeIsEverNegative()
    {
        // A negative fee would pay the player to travel, which is a faucet hiding inside a sink.
        Assert.True(TravelFee.LocalFee >= 0);
        Assert.True(TravelFee.CrossRegionFee >= 0);
        foreach (bool owned in new[] { false, true })
        {
            foreach (bool cross in new[] { false, true })
            {
                foreach (bool mounted in new[] { false, true })
                {
                    Assert.True(TravelFee.For(owned, cross, mounted) >= 0);
                }
            }
        }
    }

    /// <summary>
    /// 39B. The 15 gold buys a seat on somebody's cart, so a player with their own horse is paying
    /// for a thing they already own. Same shape as the two discounts that were already here.
    /// </summary>
    [Fact]
    public void AMountMakesALocalJumpFree()
    {
        Assert.Equal(0, TravelFee.For(ownedHolding: false, crossRegion: false, mounted: true));
    }

    /// <summary>
    /// ⚠️ The line that keeps the cross-region fee from becoming a tax on the mountless. A horse
    /// shortens a walk across the Ember Crown; it does not carry the player through the Crossway for
    /// nothing, and 38M's toll is the reason a realm boundary is not just a longer road.
    /// </summary>
    [Fact]
    public void AMountDoesNotPayForCrossingARealm()
    {
        Assert.Equal(
            TravelFee.CrossRegionFee, TravelFee.For(ownedHolding: false, crossRegion: true, mounted: true));
    }

    /// <summary>
    /// ⚠️ Both zero cases can be true at once — riding home. The fee agrees either way, but
    /// <c>PriceBreakdown.Travel</c> has to pick ONE reason to print, and if the two functions
    /// disagreed about which wins the map screen would explain a number it did not charge (38U).
    /// Owned land wins in both, because it is the older and the more specific fact.
    /// </summary>
    [Fact]
    public void RidingToYourOwnHoldingIsStillFreeAndStillReadsAsOwnership()
    {
        Assert.Equal(0, TravelFee.For(ownedHolding: true, crossRegion: false, mounted: true));

        PriceQuote quote = PriceBreakdown.Travel(ownedHolding: true, crossRegion: false, mounted: true);
        Assert.Equal(0, quote.Total);
        Assert.Equal(PriceBreakdown.KeyTravelOwned, Assert.Single(quote.Lines).Key);
    }

    [Fact]
    public void TheBreakdownNamesTheMountWhenTheMountIsTheReason()
    {
        PriceQuote quote = PriceBreakdown.Travel(ownedHolding: false, crossRegion: false, mounted: true);

        Assert.Equal(0, quote.Total);
        Assert.Equal(PriceBreakdown.KeyTravelMounted, Assert.Single(quote.Lines).Key);
    }
}
