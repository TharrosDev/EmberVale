namespace Embervale.Magic;

/// <summary>
/// Pure maths for the per-school mastery track: how casting points convert to a rank, and what each
/// rank gives. Godot-free so the curve is unit-testable and tunable in one place;
/// <see cref="SchoolMasteryComponent"/> applies it.
///
/// "Hard to master": ranks come slowly and every rank is a small, readable step rather than a spike.
/// <list type="bullet">
/// <item>Every rank: +<see cref="PowerPerRank"/> damage and healing for the school.</item>
/// <item>Rank 2 and rank 4: the school's cooldowns shorten by <see cref="CooldownTrimPerStep"/> each.</item>
/// <item>Rank 3, the attunement: <see cref="AttunementResist"/> resistance to the school's own damage.
/// Small, flavoured (a fire mage is harder to burn) and data-light: one flat stat modifier.</item>
/// </list>
/// </summary>
public static class SchoolMasteryMath
{
    /// <summary>The mastery ceiling per school.</summary>
    public const int MaxRank = 5;

    /// <summary>Cumulative casts of a school needed to hold rank 1..<see cref="MaxRank"/>. Each step
    /// asks a little more than the last: the first rank is a few fights, the last an adventure.</summary>
    private static readonly int[] Thresholds = { 10, 25, 45, 70, 100 };

    /// <summary>Damage/healing bonus added per mastery rank (rank 5 = +40%).</summary>
    public const float PowerPerRank = 0.08f;

    /// <summary>The ranks at which the school's cooldowns shorten (a step each).</summary>
    public const int FirstCooldownRank = 2;
    public const int SecondCooldownRank = 4;

    /// <summary>Cooldown share removed at each cooldown rank (rank 2 = -5%, rank 4 = -10%).</summary>
    public const float CooldownTrimPerStep = 0.05f;

    /// <summary>The rank at which the school attunes the caster against its own damage.</summary>
    public const int AttunementRank = 3;

    /// <summary>Flat resistance to the school's own damage type once attuned (the same 100/(100+x)
    /// curve armour uses: 15 is about 13% less damage from that school).</summary>
    public const float AttunementResist = 15f;

    /// <summary>A channelled spell publishes a cast event every tick; mastery banks it at most this
    /// often per spell, so holding a beam for four seconds earns four points, not twenty.</summary>
    public const double ChannelPointSeconds = 1.0;

    /// <summary>The rank a school has earned from <paramref name="points"/> casts, capped at
    /// <see cref="MaxRank"/>.</summary>
    public static int RankForPoints(int points)
    {
        int rank = 0;
        while (rank < MaxRank && points >= Thresholds[rank])
        {
            rank++;
        }

        return rank;
    }

    /// <summary>Cumulative points at which <paramref name="rank"/> is reached (0 for rank 0 and below;
    /// the last threshold for anything above the cap).</summary>
    public static int PointsForRank(int rank)
    {
        if (rank <= 0)
        {
            return 0;
        }

        return Thresholds[System.Math.Min(rank, MaxRank) - 1];
    }

    /// <summary>Progress through the current rank: casts banked since it began and casts the next rank
    /// asks in total. A capped school reports (0, 0).</summary>
    public static (int Into, int Needed) ProgressToNext(int points)
    {
        int rank = RankForPoints(points);
        if (rank >= MaxRank)
        {
            return (0, 0);
        }

        int from = PointsForRank(rank);
        return (System.Math.Max(0, points - from), Thresholds[rank] - from);
    }

    /// <summary>The damage/healing multiplier a school's spells get at <paramref name="rank"/>.</summary>
    public static float PowerMultiplier(int rank) =>
        1f + (System.Math.Max(0, System.Math.Min(rank, MaxRank)) * PowerPerRank);

    /// <summary>The multiplier on the school's spell cooldowns at <paramref name="rank"/> (1 = none).</summary>
    public static float CooldownMultiplier(int rank)
    {
        float trim = 0f;
        if (rank >= FirstCooldownRank)
        {
            trim += CooldownTrimPerStep;
        }

        if (rank >= SecondCooldownRank)
        {
            trim += CooldownTrimPerStep;
        }

        return 1f - trim;
    }

    /// <summary>Flat own-school resistance the attunement grants at <paramref name="rank"/>.</summary>
    public static float AttunementResistFor(int rank) => rank >= AttunementRank ? AttunementResist : 0f;

    /// <summary>Whether a cast event banks a mastery point. Instant and charged casts always do; a
    /// channel banks one per <see cref="ChannelPointSeconds"/>. <paramref name="secondsSinceLast"/> is
    /// how long ago this spell last banked one (use a large number for the first).</summary>
    public static bool BanksPoint(bool channelled, double secondsSinceLast) =>
        !channelled || secondsSinceLast >= ChannelPointSeconds;

    /// <summary>The new rank if going from <paramref name="before"/> to <paramref name="after"/> points
    /// crossed a rank threshold, else 0.</summary>
    public static int RankGained(int before, int after)
    {
        int old = RankForPoints(before);
        int now = RankForPoints(after);
        return now > old ? now : 0;
    }
}
