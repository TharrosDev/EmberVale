using Embervale.Stats;

namespace Embervale.Enemies;

/// <summary>
/// Spawn-time enemy scaling — the one place the realm's "this one is tougher than its archetype"
/// rule lives.
///
/// It was written out twice, in <c>WorldEventDirector</c> (a world event's champion) and in
/// <c>BossController</c> (a boss's adds), differing only in the modifier tag. Three facts have to
/// stay true together at every call site: the bonus is <see cref="ModifierType.PercentMult"/> over
/// whatever the archetype authored rather than a flat number that would swamp a wisp and tickle a
/// golem; a multiplier at or below 1 is a no-op rather than a shrink; and the resources are refilled
/// afterwards, because a health cap raised after the pool was filled otherwise spawns the creature
/// already wounded. That is a balance decision, not a formula — which is why it gets one home and
/// <c>HorizontalDistance</c> deliberately does not.
///
/// <see cref="AshenAffliction"/> is the third variation and is deliberately NOT folded in: it scales
/// health, power and XP together under its own removable tag, so it is a different rule that happens
/// to share one line.
/// </summary>
public static class EnemyScaling
{
    /// <summary>Raises <paramref name="enemy"/>'s health by <paramref name="multiplier"/> and refills
    /// it. <paramref name="source"/> tags the modifier so it stays identifiable and removable as a
    /// set. A multiplier at or below 1, or an enemy with no stats, does nothing.</summary>
    public static void ApplyHealthMultiplier(EnemyEntity enemy, float multiplier, string source)
    {
        if (multiplier <= 1f || enemy.GetComponent<StatsComponent>() is not { } stats)
        {
            return;
        }

        stats.GetStat(StatType.Health).AddModifier(
            new StatModifier(multiplier - 1f, ModifierType.PercentMult, source));
        stats.RefillResources();
    }
}
