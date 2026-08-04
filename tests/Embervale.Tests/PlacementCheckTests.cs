using Embervale.Housing;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins <see cref="PlacementCheck.Resolve"/> (Phase 37C) — the gate the placement ghost tints itself
/// from and the commit obeys. The sibling of <see cref="PropertyClaimTests"/> and
/// <see cref="PropertyStorageTests"/>; as in those, the <em>ordering</em> is the behaviour.
/// </summary>
public class PlacementCheckTests
{
    private const float Radius = 7f;

    [Fact]
    public void AClearSpotInsideAnOwnedHoldingIsPlaceable()
    {
        Assert.Equal(
            PlacementOutcome.Ok,
            PlacementCheck.Resolve(owned: true, hasGround: true, distanceFromCenter: 3f, Radius, blocked: false));
    }

    [Fact]
    public void TheEdgeOfTheRadiusIsInside()
    {
        // Inclusive: a prop placed exactly on the boundary is in the yard, not out of it. Pinned
        // because the alternative reading only ever shows up as a spot that refuses for no visible
        // reason.
        Assert.Equal(
            PlacementOutcome.Ok,
            PlacementCheck.Resolve(owned: true, hasGround: true, distanceFromCenter: Radius, Radius, blocked: false));

        Assert.Equal(
            PlacementOutcome.OutsideProperty,
            PlacementCheck.Resolve(
                owned: true, hasGround: true, distanceFromCenter: Radius + 0.01f, Radius, blocked: false));
    }

    [Fact]
    public void OwnershipIsCheckedBeforeAnyGeometry()
    {
        // Standing in someone else's town square on perfectly clear ground: the answer is "not
        // yours", not "outside" and not "blocked".
        Assert.Equal(
            PlacementOutcome.NotOwned,
            PlacementCheck.Resolve(owned: false, hasGround: false, distanceFromCenter: 999f, Radius, blocked: true));
    }

    [Fact]
    public void MissingGroundIsReportedBeforeTheHolding()
    {
        // Aiming at the sky inside your own yard. Distance is meaningless when the ray hit nothing,
        // so it must not be the thing reported.
        Assert.Equal(
            PlacementOutcome.NoGround,
            PlacementCheck.Resolve(owned: true, hasGround: false, distanceFromCenter: 0f, Radius, blocked: false));
    }

    [Fact]
    public void TheHoldingIsReportedBeforeALocalObstruction()
    {
        // Deliberate: "blocked" while a player stands across town tells them to shuffle sideways,
        // when what they need to hear is that they are nowhere near their own house.
        Assert.Equal(
            PlacementOutcome.OutsideProperty,
            PlacementCheck.Resolve(owned: true, hasGround: true, distanceFromCenter: 50f, Radius, blocked: true));
    }

    [Fact]
    public void BlockedIsReportedLast()
    {
        Assert.Equal(
            PlacementOutcome.Blocked,
            PlacementCheck.Resolve(owned: true, hasGround: true, distanceFromCenter: 1f, Radius, blocked: true));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void AHoldingWithNoPlacementAreaRefusesEverywhere(float radius)
    {
        // A property that authors no area is not a property you may build anywhere in. The
        // permissive reading of missing data is always the one that ships a bug.
        Assert.Equal(
            PlacementOutcome.OutsideProperty,
            PlacementCheck.Resolve(owned: true, hasGround: true, distanceFromCenter: 0f, radius, blocked: false));
    }
}
