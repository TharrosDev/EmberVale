namespace Embervale.Combat;

/// <summary>
/// Pure lock-on selection maths (Phase 29H). Godot-free so the cycling/range logic is unit-testable;
/// <see cref="LockOnComponent"/> drives the target queries and applies these.
/// </summary>
public static class LockOn
{
    /// <summary>The next target index when cycling by <paramref name="dir"/> (+1 / -1), wrapping. A
    /// <paramref name="current"/> of -1 (no lock) starts the cycle at the first/last entry.</summary>
    public static int CycleIndex(int current, int count, int dir)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current < 0)
        {
            return dir >= 0 ? 0 : count - 1;
        }

        return ((current + dir) % count + count) % count;
    }

    /// <summary>Whether a candidate at <paramref name="distanceSq"/> is still within
    /// <paramref name="rangeSq"/> (both squared, to skip a sqrt).</summary>
    public static bool InRange(float distanceSq, float rangeSq) => distanceSq <= rangeSq;
}
