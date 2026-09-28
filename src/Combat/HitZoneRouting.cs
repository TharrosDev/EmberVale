namespace Embervale.Combat;

/// <summary>
/// Which hurtbox an arrow that overlaps several actually strikes. Pure, so the rule is testable.
///
/// <para>The nearest <i>body</i> wins (an arrow stops at the first thing it reaches, so a second enemy
/// standing behind the first is not the one hit), and within that body the highest-multiplier zone
/// wins (a dragon's head is a sphere sitting inside the cloud of its neck and torso capsules, and an
/// arrow that touches both must land on the head, or the weak point is only reachable by luck).</para>
/// </summary>
public static class HitZoneRouting
{
    /// <summary>One overlapping hurtbox: which body owns it, what its zone multiplies damage by, and
    /// how far its centre is from the arrow, squared.</summary>
    public readonly record struct Candidate(int Owner, float Multiplier, float DistanceSquared);

    /// <summary>The index of the hurtbox struck, or -1 with nothing to strike.</summary>
    public static int Pick(System.ReadOnlySpan<Candidate> candidates)
    {
        if (candidates.Length == 0)
        {
            return -1;
        }

        int nearest = 0;
        for (int i = 1; i < candidates.Length; i++)
        {
            if (candidates[i].DistanceSquared < candidates[nearest].DistanceSquared)
            {
                nearest = i;
            }
        }

        int owner = candidates[nearest].Owner;
        int best = -1;
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i].Owner != owner)
            {
                continue;
            }

            if (best < 0 ||
                candidates[i].Multiplier > candidates[best].Multiplier ||
                (candidates[i].Multiplier == candidates[best].Multiplier &&
                 candidates[i].DistanceSquared < candidates[best].DistanceSquared))
            {
                best = i;
            }
        }

        return best;
    }
}
