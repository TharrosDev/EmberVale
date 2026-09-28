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

    /// <summary>
    /// Whether a boss that yields at <paramref name="withdrawFraction"/> should leave now, at
    /// <paramref name="healthFraction"/> of its health (Phase 47.5). A non-positive fraction is a fight
    /// to the death; a boss already at zero died instead, and the death path owns it.
    /// </summary>
    public static bool ShouldWithdraw(float healthFraction, float withdrawFraction, bool alreadyWithdrawn) =>
        !alreadyWithdrawn && withdrawFraction > 0f && healthFraction > 0f && healthFraction <= withdrawFraction;

    /// <summary>The authored range of <c>BossResource.WithdrawHealthFraction</c>: <c>[0, 1)</c>. At 1 or
    /// above the boss would leave on the first scratch; below 0 is a typo, not a design.</summary>
    public static bool WithdrawFractionValid(float withdrawFraction) =>
        float.IsFinite(withdrawFraction) && withdrawFraction >= 0f && withdrawFraction < 1f;
}
