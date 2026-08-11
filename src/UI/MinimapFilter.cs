using System.Collections.Generic;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Which markers the HUD minimap draws (39.5B) — distance first, then priority, then a hard cap.
///
/// Kept engine-free (Godot structs only, no <c>GodotObject</c>) so it is unit testable headlessly,
/// the same way <see cref="MapProjection"/> and <see cref="CompassMath"/> are. The clutter rule is
/// the interesting part of a minimap and the part most likely to be got wrong, so it does not live
/// inside a <c>_Process</c> where nothing can reach it.
/// </summary>
public static class MinimapFilter
{
    /// <summary>Drop order when the cap bites — lower rank survives. Tier is the only importance
    /// signal a location actually carries, so it is the one used; inventing a second would be the
    /// HUD deciding something the world owns (§48).</summary>
    public static int Rank(MapTier tier) => tier switch
    {
        MapTier.Primary => 0,
        MapTier.Secondary => 1,
        _ => 2,
    };

    /// <summary>
    /// The pins within <paramref name="radius"/> of <paramref name="centre"/>, most important first,
    /// capped at <paramref name="max"/>.
    ///
    /// ⚠️ <b>The cap drops the least important, never the last-added.</b> Truncating the unsorted
    /// list is the version of this that looks identical in code review and hides the town the player
    /// is standing in behind three market stalls.
    /// </summary>
    public static void Select(
        IReadOnlyList<MapPin> all, Vector2 centre, float radius, int max, List<MapPin> into)
    {
        into.Clear();
        if (max <= 0)
        {
            return;
        }

        float radiusSq = radius * radius;
        foreach (MapPin pin in all)
        {
            if (pin.WorldXz.DistanceSquaredTo(centre) <= radiusSq)
            {
                into.Add(pin);
            }
        }

        into.Sort((a, b) =>
        {
            int byRank = Rank(a.Tier).CompareTo(Rank(b.Tier));
            return byRank != 0
                ? byRank
                : a.WorldXz.DistanceSquaredTo(centre).CompareTo(b.WorldXz.DistanceSquaredTo(centre));
        });

        if (into.Count > max)
        {
            into.RemoveRange(max, into.Count - max);
        }
    }
}
