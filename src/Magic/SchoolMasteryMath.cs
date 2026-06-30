namespace Embervale.Magic;

/// <summary>
/// Pure maths for the per-school mastery track (Phase 29.5C): how casting points convert to a rank,
/// and how a rank empowers that school's output. Godot-free so the progression curve is unit-testable
/// and tunable in one place. The component <see cref="SchoolMasteryComponent"/> applies it.
///
/// "Hard to master": ranks come slowly (<see cref="PointsPerRank"/> casts each) and the empower is a
/// modest per-rank bonus, not a runaway multiplier — mastery is a long ceiling, not a quick spike.
/// </summary>
public static class SchoolMasteryMath
{
    /// <summary>Casts of a school needed to gain each rank.</summary>
    public const int PointsPerRank = 10;

    /// <summary>The mastery ceiling per school.</summary>
    public const int MaxRank = 5;

    /// <summary>Damage/healing bonus added per mastery rank (rank 5 = +40%).</summary>
    public const float PowerPerRank = 0.08f;

    /// <summary>The rank a school has earned from <paramref name="points"/> casts, capped at
    /// <see cref="MaxRank"/>.</summary>
    public static int RankForPoints(int points)
    {
        if (points <= 0)
        {
            return 0;
        }

        int rank = points / PointsPerRank;
        return rank > MaxRank ? MaxRank : rank;
    }

    /// <summary>The damage/healing multiplier a school's spells get at <paramref name="rank"/>.</summary>
    public static float PowerMultiplier(int rank) =>
        1f + (System.Math.Max(0, rank) * PowerPerRank);
}
