using Godot;

namespace Embervale.World;

/// <summary>
/// The active region's single "safe" bubble — the town hub — where the ambient spawners
/// (<see cref="EncounterDirector"/> and hostile <see cref="WorldEventDirector"/> events) must not
/// drop enemies. Scripted spawns (quests, specific events) go through <c>EnemyFactory</c> /
/// <c>EnemyTemplateRegistry</c> directly and bypass this, so a quest thief/assassin in town still works.
///
/// Populated from the active <see cref="RegionResource"/> at world build and on each region transition.
/// ponytail: one zone per region (RegionResource.SafeZone*); make it a list when a second safe area
/// (e.g. a "safe" POI) actually exists.
/// </summary>
public static class SafeZones
{
    private static Vector3 _center;
    private static float _radius;

    public static void Set(Vector3 center, float radius)
    {
        _center = center;
        _radius = Mathf.Max(0f, radius);
    }

    public static void Clear() => _radius = 0f;

    /// <summary>True if <paramref name="worldPos"/> lies inside the safe bubble (XZ distance).</summary>
    public static bool Contains(Vector3 worldPos)
    {
        if (_radius <= 0f)
        {
            return false;
        }

        float dx = worldPos.X - _center.X;
        float dz = worldPos.Z - _center.Z;
        return (dx * dx) + (dz * dz) <= _radius * _radius;
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
