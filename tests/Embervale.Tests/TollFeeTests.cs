using Embervale.Economy;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the Crossway toll (Phase 38M). The order of the checks <em>is</em> the behaviour here, and two
/// of these cases are live bugs if it drifts: a permit holder whose bribe gets spent anyway has paid
/// twice for one crossing, and a toll that resolves before the purse is consulted is a free road.
///
/// The gate's prompt and <c>GameBootstrap.PayToll</c> both call this one function, so what the player
/// is quoted and what they are charged cannot differ — the same rule <c>TravelFee</c> and
/// <c>ShopPricing</c> hold.
/// </summary>
public class TollFeeTests
{
    [Fact]
    public void AnUntolledRoadIsFreeWhateverTheBearerCarries()
    {
        // Every region authored before 38M is this case, which is why TollGold defaults to 0.
        Assert.Equal(TollOutcome.Free, TollFee.Resolve(hasPermit: false, hasPass: false, fee: 0, goldHeld: 0));
        Assert.Equal(TollOutcome.Free, TollFee.Resolve(hasPermit: true, hasPass: true, fee: -10, goldHeld: 999));
    }

    [Fact]
    public void APermitExemptsAndDoesNotSpendAPassHeldBesideIt()
    {
        // The whole reason permit is tested before pass: a player who bought the permit after bribing
        // would otherwise burn the bribe they had already paid for on their very next crossing.
        Assert.Equal(TollOutcome.PermitHeld, TollFee.Resolve(hasPermit: true, hasPass: false, fee: 40, goldHeld: 0));
        Assert.Equal(TollOutcome.PermitHeld, TollFee.Resolve(hasPermit: true, hasPass: true, fee: 40, goldHeld: 0));
    }

    [Fact]
    public void APassCoversOneCrossingWithNoGold()
    {
        // PassSpent is the caller's instruction to clear the flag; an empty purse must not turn it
        // into a refusal on the way past.
        Assert.Equal(TollOutcome.PassSpent, TollFee.Resolve(hasPermit: false, hasPass: true, fee: 40, goldHeld: 0));
    }

    [Fact]
    public void AnUnpaperedTravellerPaysOrIsTurnedBack()
    {
        Assert.Equal(TollOutcome.Charged, TollFee.Resolve(hasPermit: false, hasPass: false, fee: 40, goldHeld: 40));
        Assert.Equal(TollOutcome.Charged, TollFee.Resolve(hasPermit: false, hasPass: false, fee: 40, goldHeld: 41));

        // Exactly one coin short is the boundary the wardens are strict about.
        Assert.Equal(TollOutcome.CannotAfford, TollFee.Resolve(hasPermit: false, hasPass: false, fee: 40, goldHeld: 39));
        Assert.Equal(TollOutcome.CannotAfford, TollFee.Resolve(hasPermit: false, hasPass: false, fee: 40, goldHeld: 0));
    }
}
