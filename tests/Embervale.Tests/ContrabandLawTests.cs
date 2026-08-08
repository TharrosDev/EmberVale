using Embervale.Economy;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The fine the Crossway impound charges to give confiscated goods back (Phase 38O). It is a
/// player-facing number on a path where getting it wrong either deletes the player's property or pays
/// them for being caught, so both ends of the range are pinned rather than read.
/// </summary>
public class ContrabandLawTests
{
    [Fact]
    public void TheFineIsPerUnit()
    {
        // Flat-rate would make one enormous seizure cheaper per unit than a small one, which rewards
        // carrying more — the opposite of what a search is for.
        Assert.Equal(12, ContrabandLaw.Fine(12, 1));
        Assert.Equal(120, ContrabandLaw.Fine(12, 10));
    }

    [Fact]
    public void AnEmptyImpoundCostsNothing()
    {
        // Not a free redemption: ServiceComponent refuses a Redeem with nothing held before the price
        // is ever read. This is only the arithmetic agreeing with that.
        Assert.Equal(0, ContrabandLaw.Fine(12, 0));
        Assert.Equal(0, ContrabandLaw.Fine(12, -3));
    }

    [Fact]
    public void AnythingHeldCostsAtLeastOneCoin()
    {
        // The ShopPricing.BuyPrice rule, for the same reason: a fine that rounds to nothing makes the
        // search a mild inconvenience instead of a cost, and a zero-priced service is one the player
        // walks through without noticing it happened.
        Assert.Equal(1, ContrabandLaw.Fine(0, 5));
        Assert.Equal(1, ContrabandLaw.Fine(-40, 5));
    }

    [Fact]
    public void AHugeImpoundSaturatesInsteadOfWrapping()
    {
        // ⚠️ The one path here that could pay the player to be caught: a 32-bit wrap yields a negative
        // fine, and ShopPricing.ServicePrice floors a non-positive price to free.
        Assert.Equal(int.MaxValue, ContrabandLaw.Fine(int.MaxValue, 2));
        Assert.True(ContrabandLaw.Fine(int.MaxValue, int.MaxValue) > 0);
    }
}
