using Godot;

namespace Embervale.Magic;

/// <summary>
/// Pure steering maths for a homing projectile (Ball Lightning, Phase 29.5G). Kept Godot-light (only
/// the <see cref="Vector3"/> value type) so the turn-toward-target behaviour is unit-testable apart from
/// the physics query that finds the target in <see cref="SpellProjectile"/>.
/// </summary>
public static class SpellHoming
{
    /// <summary>Steers <paramref name="direction"/> toward <paramref name="toTarget"/> by at most
    /// <paramref name="turnPerSecond"/>·<paramref name="dt"/> of the way (a normalized lerp). Returns a
    /// unit vector. A negligible target offset leaves the direction unchanged.</summary>
    public static Vector3 Steer(Vector3 direction, Vector3 toTarget, float turnPerSecond, float dt)
    {
        Vector3 dir = direction.LengthSquared() < 1e-6f ? Vector3.Forward : direction.Normalized();
        if (toTarget.LengthSquared() < 1e-6f)
        {
            return dir;
        }

        Vector3 desired = toTarget.Normalized();
        float t = Mathf.Clamp(turnPerSecond * dt, 0f, 1f);
        Vector3 steered = dir.Lerp(desired, t);
        return steered.LengthSquared() < 1e-6f ? desired : steered.Normalized();
    }

    /// <summary>
    /// Which candidate a homing bolt hunts: the nearest one carrying the brand (Ball Lightning prefers a
    /// Stormbranded foe), otherwise the nearest of all. Returns its index, or -1 for none.
    /// </summary>
    public static int Pick(System.Collections.Generic.IReadOnlyList<(float DistanceSquared, bool Branded)> candidates)
    {
        int best = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (best < 0 ||
                IsPreferred(candidates[i].DistanceSquared, candidates[i].Branded,
                    candidates[best].DistanceSquared, candidates[best].Branded))
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>Whether a candidate replaces the current target. Shared by the streaming physics
    /// query and the pure list picker, so a homing frame needs no candidate-list allocations.</summary>
    public static bool IsPreferred(float distanceSquared, bool branded, float currentDistanceSquared, bool currentBranded) =>
        (branded && !currentBranded) || (branded == currentBranded && distanceSquared < currentDistanceSquared);
}
