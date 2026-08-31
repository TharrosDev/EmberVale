namespace Embervale.Magic;

/// <summary>
/// The pure half of a projectile's flight: how far a bolt may move between two collision tests
/// before it can pass through something.
///
/// ⚠️ <b>IT EXISTS BECAUSE A BOLT USED TO TELEPORT ONCE A FRAME.</b>
/// <see cref="SpellProjectile"/> advanced its position by <c>speed × delta</c> and then asked what it
/// overlapped at the arrival point. A 40 m/s bolt covers 0.67 m per frame at 60 Hz and over two
/// metres in one 30 Hz hitch, so anything thinner than that gap sitting between the two positions —
/// an actor's hurtbox, a fence, a wall — was never tested at all: the shot went through it and
/// detonated on whatever was behind. Godot-free and here rather than inline so the rule ("no step
/// longer than the bolt is wide") is pinned by the ordinary unit suite instead of by a comment.
/// </summary>
public static class SpellSweep
{
    /// <summary>
    /// How many equal sub-steps one frame's travel must be split into so no single step exceeds
    /// <paramref name="radius"/>.
    /// </summary>
    /// <param name="distance">Distance the bolt travels this frame (<c>speed × delta</c>).</param>
    /// <param name="radius">The bolt's collision radius — the largest gap that is still safe.</param>
    /// <param name="maxSteps">Ceiling, so an absurd speed or a one-second hitch cannot spin the
    /// caller. At the cap the bolt is moving faster than a sweep can honestly resolve.</param>
    /// <returns>At least 1, at most <paramref name="maxSteps"/>.</returns>
    public static int SubStepCount(float distance, float radius, int maxSteps)
    {
        if (maxSteps < 1)
        {
            return 1;
        }

        if (!(distance > 0f) || !(radius > 0f))
        {
            // Not moving, or a degenerate radius: one test where it stands is the honest answer.
            return 1;
        }

        int steps = (int)System.Math.Ceiling(distance / radius);
        if (steps < 1)
        {
            return 1;
        }

        return steps > maxSteps ? maxSteps : steps;
    }

    /// <summary>The length of each sub-step for a given frame — <c>distance / SubStepCount</c>.
    /// Exposed so a test can assert the property that matters (no step longer than the radius)
    /// rather than the arithmetic that produces it.</summary>
    public static float SubStepLength(float distance, float radius, int maxSteps) =>
        distance / SubStepCount(distance, radius, maxSteps);
}
