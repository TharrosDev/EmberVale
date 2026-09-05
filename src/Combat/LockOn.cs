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

    /// <summary>
    /// How good a lock-on candidate is. <b>Lower is better</b>, and a negative score means "not a
    /// candidate at all".
    ///
    /// <para>⚠️ <b>This used to be distance, and nothing else.</b> The nearest valid enemy won, so
    /// standing between two of them locked whichever was a hand's width closer, and an enemy behind
    /// the player beat one they were looking straight at. Angle is weighted heavily for exactly that
    /// reason: what the player is aiming at is a far better guess at what they mean than what they
    /// happen to be standing near.</para>
    ///
    /// <param name="distance">Metres to the candidate.</param>
    /// <param name="angleFromView">Radians between the camera's forward and the candidate.</param>
    /// <param name="maxDistance">Beyond this, not a candidate.</param>
    /// <param name="maxAngle">Beyond this off-screen, not a candidate — which is what stops the
    /// player locking something behind them.</param>
    /// <param name="hasLineOfSight">False for a candidate behind a wall.</param>
    /// </summary>
    public static float Score(
        float distance, float angleFromView, float maxDistance, float maxAngle, bool hasLineOfSight)
    {
        if (!hasLineOfSight || distance > maxDistance || angleFromView > maxAngle ||
            distance < 0f || angleFromView < 0f)
        {
            return -1f;
        }

        // Normalised so the weights mean something regardless of the ranges: at the edge of either
        // limit the term is 1. Angle is worth three times distance.
        float byAngle = maxAngle > 0f ? angleFromView / maxAngle : 0f;
        float byDistance = maxDistance > 0f ? distance / maxDistance : 0f;
        return (byAngle * 3f) + byDistance;
    }

    /// <summary>
    /// Whether a better-scoring candidate is worth switching to while a lock is already held.
    ///
    /// ⚠️ <b>Hysteresis, and it is the difference between a lock and a flicker.</b> Two enemies
    /// scoring within a hair of each other will trade places every frame the player's aim drifts,
    /// and the camera snaps back and forth between them. A challenger has to be meaningfully better,
    /// not merely better.
    /// </summary>
    public static bool ShouldSwitch(float currentScore, float challengerScore, float margin)
    {
        if (challengerScore < 0f)
        {
            return false;
        }

        return currentScore < 0f || challengerScore < currentScore - margin;
    }
}
