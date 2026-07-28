using Embervale.Companions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the party's standing orders (Phase 32B): the quick command's cycle and the engagement
/// envelope each order implies. The envelope is what makes an order mean something — the same
/// <see cref="CompanionDecision"/> runs under every order, only its distances change — so the
/// multipliers are pinned here rather than left to drift in the AI component.
/// </summary>
public class CompanionOrdersTests
{
    [Fact]
    public void CycleVisitsEveryOrderAndReturns()
    {
        CompanionStance stance = CompanionStance.Follow;

        stance = CompanionOrders.Next(stance);
        Assert.Equal(CompanionStance.Hold, stance);

        stance = CompanionOrders.Next(stance);
        Assert.Equal(CompanionStance.Engage, stance);

        stance = CompanionOrders.Next(stance);
        Assert.Equal(CompanionStance.Follow, stance);
    }

    [Fact]
    public void EngageStretchesTheLeash()
    {
        Assert.True(CompanionOrders.Leash(CompanionStance.Engage, 18f) > CompanionOrders.Leash(CompanionStance.Follow, 18f));
    }

    [Fact]
    public void FollowAndHoldShareTheBaseLeash()
    {
        // Holding tightens where a companion *looks* for a fight, not how far it may be dragged —
        // the leash is measured from the hold anchor, which is already the restriction.
        Assert.Equal(18f, CompanionOrders.Leash(CompanionStance.Follow, 18f));
        Assert.Equal(18f, CompanionOrders.Leash(CompanionStance.Hold, 18f));
    }

    [Fact]
    public void EngageWidensAndHoldTightensTheScanRadius()
    {
        float engage = CompanionOrders.EngageRadius(CompanionStance.Engage, 14f);
        float follow = CompanionOrders.EngageRadius(CompanionStance.Follow, 14f);
        float hold = CompanionOrders.EngageRadius(CompanionStance.Hold, 14f);

        Assert.True(engage > follow);
        Assert.True(hold < follow);
    }

    [Fact]
    public void EveryOrderHasItsOwnNameKey()
    {
        Assert.Equal("companion.order.follow", CompanionOrders.NameKey(CompanionStance.Follow));
        Assert.Equal("companion.order.hold", CompanionOrders.NameKey(CompanionStance.Hold));
        Assert.Equal("companion.order.engage", CompanionOrders.NameKey(CompanionStance.Engage));
    }

    [Fact]
    public void EnvelopeScalesWithTheConfiguredBase()
    {
        // A companion tuned with a longer reach keeps that tuning under every order.
        Assert.Equal(
            CompanionOrders.EngageRadius(CompanionStance.Engage, 20f) / 20f,
            CompanionOrders.EngageRadius(CompanionStance.Engage, 10f) / 10f,
            3);
    }
}
