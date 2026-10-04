using System;
using System.Collections.Generic;

namespace Embervale.UI;

/// <summary>Where the compass chevron points for a tracked objective.</summary>
public enum CompassMode
{
    /// <summary>At the objective's own position in this region.</summary>
    Direct,

    /// <summary>At this region's door toward the objective's region.</summary>
    Portal,
}

/// <summary>Whether the chevron is drawn solid (a live objective) or hollow and dim (an optional one).</summary>
public enum CompassMarkState
{
    Active,
    Optional,
}

/// <summary>Whose colour the chevron takes: the main thread's, or an errand's.</summary>
public enum CompassMarkKind
{
    Main,
    Side,
}

/// <summary>
/// The compass's pure decisions for a tracked objective: direct pointer or portal pointer, solid or
/// hollow, main or side colour, and the route across the region graph that picks which door to point at.
/// </summary>
public static class CompassRoutingRules
{
    /// <summary>
    /// Direct versus portal. An objective whose own location lives in another region always routes through a
    /// portal, even when a same-named enemy happens to be loaded here (the live-target scan would otherwise
    /// point at the wrong realm's creature). An objective with no location falls back to the quest's home
    /// region, and only when no live target was found in this one.
    /// </summary>
    public static CompassMode ModeFor(
        string objectiveRegionId, string questRegionId, string currentRegionId, bool hasLiveTarget)
    {
        if (currentRegionId.Length == 0)
        {
            return CompassMode.Direct;
        }

        if (objectiveRegionId.Length > 0)
        {
            return objectiveRegionId == currentRegionId ? CompassMode.Direct : CompassMode.Portal;
        }

        return !hasLiveTarget && questRegionId.Length > 0 && questRegionId != currentRegionId
            ? CompassMode.Portal
            : CompassMode.Direct;
    }

    /// <summary>The region the portal pointer is aimed through: the objective's region when its location
    /// names one, else the quest's.</summary>
    public static string DestinationRegion(string objectiveRegionId, string questRegionId) =>
        objectiveRegionId.Length > 0 ? objectiveRegionId : questRegionId;

    public static CompassMarkKind KindOf(bool isMainQuest) => isMainQuest ? CompassMarkKind.Main : CompassMarkKind.Side;

    public static CompassMarkState StateOf(bool optional) => optional ? CompassMarkState.Optional : CompassMarkState.Active;

    /// <summary>
    /// The first region to step into when travelling from <paramref name="from"/> to <paramref name="to"/>
    /// through <paramref name="neighbours"/> (shortest route). Regions for which <paramref name="isOpen"/> is
    /// false (a story door still sealed) are not entered. Null when already there, or when no open route
    /// exists, in which case the compass falls back to its direct behaviour.
    /// </summary>
    public static string? NextHop(
        string from,
        string to,
        IReadOnlyDictionary<string, IReadOnlyList<string>> neighbours,
        Func<string, bool>? isOpen = null)
    {
        if (from.Length == 0 || to.Length == 0 || from == to || !neighbours.ContainsKey(from))
        {
            return null;
        }

        var firstHop = new Dictionary<string, string>();
        var queue = new Queue<string>();
        queue.Enqueue(from);
        var seen = new HashSet<string> { from };

        while (queue.Count > 0)
        {
            string at = queue.Dequeue();
            if (!neighbours.TryGetValue(at, out IReadOnlyList<string>? next))
            {
                continue;
            }

            foreach (string region in next)
            {
                if (!seen.Add(region) || (isOpen != null && !isOpen(region)))
                {
                    continue;
                }

                firstHop[region] = at == from ? region : firstHop[at];
                if (region == to)
                {
                    return firstHop[region];
                }

                queue.Enqueue(region);
            }
        }

        return null;
    }
}
