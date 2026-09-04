using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The combat transition table and the two "what now" rules that follow a state ending. The order of
/// the guards is the behaviour, and it was previously expressed only as the order of five early
/// returns inside a method no test could reach.
/// </summary>
public class AiStateRulesTests
{
    /// <summary>A healthy, hostile, visible target well inside home ground: keep fighting.</summary>
    private static EnemyState Fighting(
        bool hasLiveTarget = true,
        bool targetIsHostile = true,
        bool canSeeTarget = true,
        float distanceFromHome = 0f,
        float territoryRadius = 0f,
        bool lowHealth = false,
        double retreatCooldownRemaining = 0d) =>
        CombatTransition.Next(
            hasLiveTarget, targetIsHostile, canSeeTarget,
            distanceFromHome, territoryRadius, lowHealth, retreatCooldownRemaining);

    [Fact]
    public void AHealthyFightContinues()
    {
        Assert.Equal(EnemyState.Combat, Fighting());
    }

    [Fact]
    public void ADeadOrMissingTargetEndsTheFight()
    {
        Assert.Equal(EnemyState.Idle, Fighting(hasLiveTarget: false));
    }

    [Fact]
    public void StandingDownEndsTheFight()
    {
        // Reputation rose to neutral and nothing provoked this actor.
        Assert.Equal(EnemyState.Idle, Fighting(targetIsHostile: false));
    }

    [Fact]
    public void LosingSightSendsItLooking()
    {
        Assert.Equal(EnemyState.Investigate, Fighting(canSeeTarget: false));
    }

    [Fact]
    public void ATerritorialCreatureBreaksOffWhenDrawnOffItsGround()
    {
        Assert.Equal(EnemyState.Returning, Fighting(distanceFromHome: 50f, territoryRadius: 44f));
    }

    [Fact]
    public void NoTerritoryRadiusMeansItChasesForever()
    {
        // Every profile before the dragon. Fine for a wolf, disastrous for a flying world boss.
        Assert.Equal(EnemyState.Combat, Fighting(distanceFromHome: 500f, territoryRadius: 0f));
    }

    [Fact]
    public void TheLeashIsCheckedBeforeTheHealthCheck()
    {
        // ⚠️ THE ORDER IS THE POINT. If health came first, a territorial creature could be walked
        // out of its valley one swing at a time and would retreat rather than go home — and a
        // retreat ends by re-engaging, wherever it happens to be standing.
        Assert.Equal(
            EnemyState.Returning,
            Fighting(distanceFromHome: 50f, territoryRadius: 44f, lowHealth: true));
    }

    [Fact]
    public void AWoundedActorBreaksOff()
    {
        Assert.Equal(EnemyState.Retreat, Fighting(lowHealth: true));
    }

    [Fact]
    public void TheRetreatCooldownStopsTheCombatRetreatPingPong()
    {
        // Nothing heals a wounded enemy, so without the cooldown the re-engage that ends a retreat
        // trips this same check on the very next tick and it flees again forever.
        Assert.Equal(EnemyState.Combat, Fighting(lowHealth: true, retreatCooldownRemaining: 2.0d));
    }

    [Fact]
    public void TheCooldownExpiringAtExactlyZeroAllowsARetreat()
    {
        Assert.Equal(EnemyState.Retreat, Fighting(lowHealth: true, retreatCooldownRemaining: 0d));
    }

    // --- Where a non-combat state ends ---------------------------------------

    [Fact]
    public void AnAmbusherRestsInIdleNeverPatrol()
    {
        // A patrolling ambusher is not an ambush. One rule, four call sites.
        Assert.Equal(EnemyState.Idle, CombatTransition.Resting(isAmbusher: true));
    }

    [Fact]
    public void EveryoneElseRestsByPatrolling()
    {
        Assert.Equal(EnemyState.Patrol, CombatTransition.Resting(isAmbusher: false));
    }

    [Fact]
    public void ACowardNeverRallies()
    {
        // FleeOnSight is a personality, not a wound response. Turning back to fight at the end of a
        // retreat would make it a brute with extra steps.
        Assert.Equal(
            EnemyState.Patrol,
            CombatTransition.AfterRetreat(fleeOnSight: true, isAmbusher: false, hasLiveTarget: true));
    }

    [Fact]
    public void ACowardlyAmbusherGoesBackToLyingInWait()
    {
        Assert.Equal(
            EnemyState.Idle,
            CombatTransition.AfterRetreat(fleeOnSight: true, isAmbusher: true, hasLiveTarget: true));
    }

    [Fact]
    public void EveryoneElseReEngagesAfterARetreat()
    {
        Assert.Equal(
            EnemyState.Combat,
            CombatTransition.AfterRetreat(fleeOnSight: false, isAmbusher: false, hasLiveTarget: true));
    }

    [Fact]
    public void AnActorThatLostItsTargetWhileRetreatingGoesLooking()
    {
        Assert.Equal(
            EnemyState.Investigate,
            CombatTransition.AfterRetreat(fleeOnSight: false, isAmbusher: false, hasLiveTarget: false));
    }
}
