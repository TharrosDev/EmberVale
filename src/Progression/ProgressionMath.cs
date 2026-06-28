using System;

namespace Embervale.Progression;

/// <summary>
/// Pure XP/level maths behind <see cref="ProgressionComponent"/> and <see cref="ProgressionResource"/>.
/// Kept Godot-free so the level-up boundaries — the classic off-by-one territory — are unit-testable.
/// <see cref="ProgressionResource.XpToReach"/> feeds <see cref="XpToReach"/> its tuning, and
/// <see cref="ProgressionComponent.AddXp"/> delegates its multi-level loop to <see cref="Resolve"/>.
/// </summary>
public static class ProgressionMath
{
    /// <summary>XP needed to advance *from* <paramref name="level"/> to the next, using the curve
    /// <c>round(baseXp × level^exponent)</c>. Returns 0 at or beyond <paramref name="maxLevel"/>.</summary>
    public static int XpToReach(int level, int baseXp, float exponent, int maxLevel)
    {
        if (level >= maxLevel)
        {
            return 0;
        }

        return (int)MathF.Round(baseXp * MathF.Pow(level, exponent));
    }

    /// <summary>
    /// Resolves how a grant of <paramref name="addedXp"/> moves a character from
    /// (<paramref name="level"/>, <paramref name="xp"/>) — accumulating XP and spending it on as many
    /// level-ups as it covers. <paramref name="xpToReach"/> gives the cost to leave a given level;
    /// the loop stops at <paramref name="maxLevel"/> or a non-positive cost, and leftover XP is zeroed
    /// once the cap is reached. A non-positive grant (or already at the cap) is a no-op apart from
    /// pinning XP to 0 at the cap.
    /// </summary>
    public static (int Level, int Xp, int LevelsGained) Resolve(
        int level, int xp, int maxLevel, int addedXp, Func<int, int> xpToReach)
    {
        if (level >= maxLevel)
        {
            return (level, 0, 0);
        }

        if (addedXp <= 0)
        {
            return (level, xp, 0);
        }

        xp += addedXp;
        int levelsGained = 0;

        while (level < maxLevel)
        {
            int need = xpToReach(level);
            if (need <= 0 || xp < need)
            {
                break;
            }

            xp -= need;
            level++;
            levelsGained++;
        }

        if (level >= maxLevel)
        {
            xp = 0;
        }

        return (level, xp, levelsGained);
    }
}
