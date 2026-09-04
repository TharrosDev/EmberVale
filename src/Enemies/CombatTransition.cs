namespace Embervale.Enemies;

/// <summary>
/// The order in which a fighting actor asks whether it should still be fighting.
///
/// <para>This is the sequence of guards at the top of the combat tick, lifted out as a pure function
/// so the order — which is the whole of the behaviour, and none of which was tested — can be pinned.
/// Each step exists for a reason that cost something to learn:</para>
///
/// <list type="number">
/// <item><b>No live target</b> — the player is gone (a session teardown, a load). Stand down.</item>
/// <item><b>The target is no longer a target</b> — reputation rose to neutral and nothing provoked
/// this actor. Stand down.</item>
/// <item><b>Lost sight</b> — go and look where they were last seen, rather than tracking them
/// through a wall.</item>
/// <item><b>Drawn off its ground</b> — ⚠️ checked BEFORE the health check, so a territorial creature
/// cannot be walked out of its valley one swing at a time.</item>
/// <item><b>Wounded, and allowed to break off</b> — the cooldown is what stops a wounded actor
/// ping-ponging Combat→Retreat forever: nothing heals it, so the re-engage that ends a retreat would
/// otherwise trip this same check on the very next tick.</item>
/// </list>
/// </summary>
public static class CombatTransition
{
    /// <summary>The state a fighting actor should be in, or <see cref="EnemyState.Combat"/> to keep
    /// fighting. Ordered — an earlier answer wins over a later one, and the order is the contract.</summary>
    public static EnemyState Next(
        bool hasLiveTarget,
        bool targetIsHostile,
        bool canSeeTarget,
        float distanceFromHome,
        float territoryRadius,
        bool lowHealth,
        double retreatCooldownRemaining)
    {
        if (!hasLiveTarget || !targetIsHostile)
        {
            return EnemyState.Idle;
        }

        if (!canSeeTarget)
        {
            return EnemyState.Investigate;
        }

        if (TerritoryLeash.ShouldBreakOff(distanceFromHome, territoryRadius, returning: false))
        {
            return EnemyState.Returning;
        }

        if (lowHealth && retreatCooldownRemaining <= 0d)
        {
            return EnemyState.Retreat;
        }

        return EnemyState.Combat;
    }

    /// <summary>
    /// Where an actor goes when it is done with a non-combat state. ⚠️ <b>An ambusher returns to
    /// Idle, never Patrol</b>, from every exit: it lies in wait, and a patrolling ambusher is not an
    /// ambush. This is the single rule behind four separate call sites.
    /// </summary>
    public static EnemyState Resting(bool isAmbusher) => isAmbusher ? EnemyState.Idle : EnemyState.Patrol;

    /// <summary>
    /// What a retreating actor does when its retreat runs out. ⚠️ <b>A coward never rallies</b> —
    /// <see cref="AIProfileResource.FleeOnSight"/> is a personality, not a wound response, and
    /// turning back to fight would make it a brute with extra steps. Everyone else re-engages if the
    /// target is still there, and otherwise goes to look for it.
    /// </summary>
    public static EnemyState AfterRetreat(bool fleeOnSight, bool isAmbusher, bool hasLiveTarget)
    {
        if (fleeOnSight)
        {
            return Resting(isAmbusher);
        }

        return hasLiveTarget ? EnemyState.Combat : EnemyState.Investigate;
    }
}
