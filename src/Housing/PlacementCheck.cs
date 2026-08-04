namespace Embervale.Housing;

/// <summary>Why a prop can or cannot be placed at the spot the player is aiming at.</summary>
public enum PlacementOutcome
{
    /// <summary>The holding is not the player's.</summary>
    NotOwned,

    /// <summary>The aim ray hit nothing — there is no ground under the cursor.</summary>
    NoGround,

    /// <summary>Real ground, but outside the holding's placement area.</summary>
    OutsideProperty,

    /// <summary>Inside the holding, but something is already standing there.</summary>
    Blocked,

    /// <summary>Placeable now.</summary>
    Ok,
}

/// <summary>
/// Whether a prop may be placed here, and if not, which reason to say out loud (Phase 37C). Third in
/// the <see cref="PropertyClaim"/> / <see cref="PropertyStorage"/> line, and pure for the same
/// reason: the ghost's tint and the commit both read this one function, so the colour the player is
/// shown and what actually happens cannot drift apart.
///
/// The order is deliberate and pinned by tests. <b>Ownership before geometry</b>, and <b>the holding
/// before the local obstruction</b>: telling someone a spot is "blocked" while they are standing in
/// the town square sends them to shuffle two metres to the left, when what they need to hear is that
/// they are nowhere near their own house.
/// </summary>
public static class PlacementCheck
{
    /// <summary>
    /// Resolves a placement attempt. <paramref name="distanceFromCenter"/> is the <b>horizontal</b>
    /// (XZ) distance from the holding's placement centre — measuring it in three dimensions would
    /// refuse a perfectly good spot for being uphill.
    /// </summary>
    public static PlacementOutcome Resolve(
        bool owned, bool hasGround, float distanceFromCenter, float radius, bool blocked)
    {
        if (!owned)
        {
            return PlacementOutcome.NotOwned;
        }

        if (!hasGround)
        {
            return PlacementOutcome.NoGround;
        }

        // A non-positive radius is a holding that authors no placement area at all. It refuses
        // everywhere rather than succeeding everywhere — the same call PropertyClaim makes about a
        // non-positive price, and for the same reason: the permissive reading of missing data is
        // always the one that ships a bug.
        if (radius <= 0f || distanceFromCenter > radius)
        {
            return PlacementOutcome.OutsideProperty;
        }

        return blocked ? PlacementOutcome.Blocked : PlacementOutcome.Ok;
    }
}
