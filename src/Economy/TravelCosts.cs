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
    public static int FeeFor(TravelNode node, string currentRegionId) =>
        QuoteFor(node, currentRegionId).Total;

    /// <summary>
    /// The fee <em>and which of <see cref="TravelFee"/>'s three cases it is</em> (38U). One line, but
    /// the line the map screen could not say before: a free jump reads as a bug unless something names
    /// the holding that made it free, and the 40g one reads as arbitrary unless something names the
    /// realm boundary.
    ///
    /// <see cref="FeeFor"/> is this function's <c>Total</c>, so the button's label, its tooltip and
    /// <c>GameBootstrap</c>'s charge remain one decision — which is the reason this type exists.
    /// </summary>
    public static PriceQuote QuoteFor(TravelNode node, string currentRegionId) => PriceBreakdown.Travel(
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
