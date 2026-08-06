using Embervale.Core.Services;
using Embervale.Housing;
using Embervale.World;

namespace Embervale.Economy;

/// <summary>
/// Resolves a fast-travel node into the two facts <see cref="TravelFee"/> needs (Phase 38C). The split
/// is deliberate: the *price* is pure and unit-tested in <see cref="TravelFee"/>, while the lookups that
/// answer "is this somewhere I own" and "is this another realm" need the world's services and cannot be.
///
/// Both the map screen's button state and <c>GameBootstrap.OnFastTravelRequested</c>'s charge call this
/// one function, so the price shown and the price taken are the same number by construction — the Phase
/// 37 rule that a prompt and the action behind it are one decision.
/// </summary>
public static class TravelCosts
{
    /// <summary>The gold this jump costs from <paramref name="currentRegionId"/>.</summary>
    public static int FeeFor(TravelNode node, string currentRegionId) => TravelFee.For(
        ownedHolding: IsOwnedHolding(node.Id),
        crossRegion: !string.IsNullOrEmpty(currentRegionId) && node.RegionId != currentRegionId);

    /// <summary>
    /// Whether the node is the travel point of a holding the player owns. Matched through
    /// <c>PropertyResource.TravelNodeId</c>, the link 37A already authors when a deed is claimed — so
    /// there is no second record tying a property to its waypoint, and none to drift.
    /// </summary>
    private static bool IsOwnedHolding(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || Resolve<HousingService>() is not { } housing)
        {
            return false; // fail closed: an unresolvable service must not hand out free travel
        }

        foreach (PropertyResource property in PropertyDatabase.All)
        {
            if (property.TravelNodeId == nodeId && housing.Owns(property.Id))
            {
                return true;
            }
        }

        return false;
    }

    private static T? Resolve<T>()
        where T : class =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out T service) ? service : null;
}
