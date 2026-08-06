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
        Assert.Equal(0, TravelFee.For(ownedHolding: true, crossRegion: false));
        Assert.Equal(0, TravelFee.For(ownedHolding: true, crossRegion: true));
    }

    [Fact]
    public void ALocalJumpCostsTheLocalFee()
    {
        Assert.Equal(TravelFee.LocalFee, TravelFee.For(ownedHolding: false, crossRegion: false));
    }

    [Fact]
    public void CrossingARealmCostsMore()
    {
        // A flat fee would make the short jump feel arbitrary; the gap is what makes the price read as
        // distance rather than as a tax on using the map.
        Assert.Equal(TravelFee.CrossRegionFee, TravelFee.For(ownedHolding: false, crossRegion: true));
        Assert.True(TravelFee.CrossRegionFee > TravelFee.LocalFee);
    }

    [Fact]
    public void NoFeeIsEverNegative()
    {
        // A negative fee would pay the player to travel, which is a faucet hiding inside a sink.
        Assert.True(TravelFee.LocalFee >= 0);
        Assert.True(TravelFee.CrossRegionFee >= 0);
        Assert.True(TravelFee.For(false, false) >= 0);
        Assert.True(TravelFee.For(false, true) >= 0);
        Assert.True(TravelFee.For(true, false) >= 0);
        Assert.True(TravelFee.For(true, true) >= 0);
    }
}
