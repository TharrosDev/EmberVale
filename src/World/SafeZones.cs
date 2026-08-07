using System.Collections.Generic;
using Godot;

namespace Embervale.World;

/// <summary>
/// The active region's single "safe" bubble — the town hub — where the ambient spawners
/// (<see cref="EncounterDirector"/> and hostile <see cref="WorldEventDirector"/> events) must not
/// drop enemies. Scripted spawns (quests, specific events) go through <c>EnemyFactory</c> /
/// <c>EnemyTemplateRegistry</c> directly and bypass this, so a quest thief/assassin in town still works.
///
/// Populated from the active <see cref="RegionResource"/> at world build and on each region transition.
///
/// <b>A region may now have several</b> (Phase 38K). It held exactly one until the Embermarket, whose
/// stalls sit a district away from the town square: with a single bubble the market was either outside
/// it — goblins spawning among the merchants — or the bubble had to swell wide enough to smother the
/// encounters around the wilds cells too. A second area was the condition the old ponytail note here
/// named for making this a list, and the Embermarket is it.
/// </summary>
public static class SafeZones
{
    private static readonly List<(Vector3 Center, float Radius)> Zones = new();

    /// <summary>Replaces every zone with a single one — the region's own bubble. Callers add the
    /// cell-level areas after it, so a region transition cannot leave the previous realm's districts
    /// protecting ground in this one.</summary>
    public static void Set(Vector3 center, float radius)
    {
        Zones.Clear();
        Add(center, radius);
    }

    /// <summary>Adds another safe area — a settlement cell that is not the region's own hub. A
    /// non-positive radius is ignored rather than stored, which is how <c>RegionCellResource</c>'s
    /// default of <c>0</c> means "this cell is not a safe area" without a second flag to disagree
    /// with it.</summary>
    public static void Add(Vector3 center, float radius)
    {
        if (radius > 0f)
        {
            Zones.Add((center, radius));
        }
    }

    public static void Clear() => Zones.Clear();

    /// <summary>True if <paramref name="worldPos"/> lies inside <em>any</em> safe bubble (XZ
    /// distance).</summary>
    public static bool Contains(Vector3 worldPos)
    {
        foreach ((Vector3 center, float radius) in Zones)
        {
            float dx = worldPos.X - center.X;
            float dz = worldPos.Z - center.Z;
            if ((dx * dx) + (dz * dz) <= radius * radius)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Picks a ring point in [min,max] around <paramref name="center"/> that is outside the
    /// safe zone, retrying up to 8 angles. Returns false if every try landed inside it (the player is
    /// deep in town) — the caller then skips the spawn.</summary>
    public static bool TryRingPointOutside(Vector3 center, float min, float max, out Vector3 point)
    {
        for (int i = 0; i < 8; i++)
        {
            float angle = GD.Randf() * Mathf.Tau;
            float distance = Mathf.Lerp(min, max, GD.Randf());
            point = new Vector3(
                center.X + (Mathf.Cos(angle) * distance),
                0.5f,
                center.Z + (Mathf.Sin(angle) * distance));
            if (!Contains(point))
            {
                return true;
            }
        }

        point = Vector3.Zero;
        return false;
    }
}
