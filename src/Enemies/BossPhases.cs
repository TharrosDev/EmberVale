using System.Collections.Generic;

namespace Embervale.Enemies;

/// <summary>
/// The pure phase/enrage arithmetic behind <see cref="BossController"/> (Phase 36A), kept Godot-free
/// so a fight's structure is unit-testable apart from the components it drives — the same idiom as
/// <see cref="GuardCycle"/>, <see cref="TerritoryLeash"/> and <see cref="PackFlank"/>.
/// </summary>
public static class BossPhases
{
    /// <summary>
    /// The 1-based phase a boss belongs in at <paramref name="healthFraction"/> of its max health,
    /// given <paramref name="thresholds"/> ordered high to low (the first entry is the opening
    /// phase, normally <c>1.0</c>). A phase is entered at or below its threshold.
    ///
    /// Returns the <em>deepest</em> phase reached, so a hit big enough to cross two thresholds at
    /// once lands in the right one rather than stepping through and briefly buffing twice. An empty
    /// table means "no phases authored" and yields phase 1, which is what the caller's fallback
    /// wants — a boss is never phase 0.
    /// </summary>
    public static int SelectPhase(float healthFraction, IReadOnlyList<float> thresholds)
    {
        if (thresholds == null || thresholds.Count == 0)
        {
            return 1;
        }

        int phase = 1;
        for (int i = 1; i < thresholds.Count; i++)
        {
            if (healthFraction <= thresholds[i])
            {
                phase = i + 1;
            }
        }

        return phase;
    }

    /// <summary>
    /// Whether the enrage fuse should fire now: a positive <paramref name="enrageSeconds"/> has
    /// elapsed and it has not fired already. A non-positive duration is "no enrage", which is every
    /// boss that would rather be out-waited than rush the player.
    /// </summary>
    public static bool ShouldEnrage(double elapsed, float enrageSeconds, bool alreadyEnraged) =>
        !alreadyEnraged && enrageSeconds > 0f && elapsed >= enrageSeconds;
}
