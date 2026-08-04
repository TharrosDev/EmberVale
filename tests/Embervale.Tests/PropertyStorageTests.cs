using Embervale.Housing;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins <see cref="PropertyStorage.Resolve"/> — the gate a holding's stash opens on (Phase 37B), and
/// the sibling of <see cref="PropertyClaimTests"/>. Small surface, but the ordering is a decision
/// rather than an accident, so it is pinned here rather than left to be re-read.
/// </summary>
public class PropertyStorageTests
{
    [Fact]
    public void AClaimedHoldingOpens()
    {
        Assert.Equal(
            StorageOutcome.Open,
            PropertyStorage.Resolve(propertyKnown: true, owned: true));
    }

    [Fact]
    public void AnUnclaimedHoldingIsRefusedAsNotYours()
    {
        // A real property the player has simply not bought yet — the refusal a player can act on.
        Assert.Equal(
            StorageOutcome.NotOwned,
            PropertyStorage.Resolve(propertyKnown: true, owned: false));
    }

    [Fact]
    public void AnUnknownPropertyIsReportedBeforeOwnership()
    {
        // Deliberate ordering: an id that resolves to nothing is an authoring fault, not a gate. If
        // it reported "not yours" instead, a typo would look exactly like a property to go and buy,
        // and would send the player after something that does not exist.
        Assert.Equal(
            StorageOutcome.UnknownProperty,
            PropertyStorage.Resolve(propertyKnown: false, owned: false));
    }

    [Fact]
    public void AnUnknownPropertyStaysUnknownEvenIfOwnershipSaysOtherwise()
    {
        // Nothing can own a property the database has never heard of, but the gate must not depend
        // on that being true — an unresolvable id is refused on its own terms.
        Assert.Equal(
            StorageOutcome.UnknownProperty,
            PropertyStorage.Resolve(propertyKnown: false, owned: true));
    }
}
