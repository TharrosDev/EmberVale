using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the phase/enrage arithmetic behind a boss fight (Phase 36A). The controller, the material
/// flare and the stat modifiers run in-engine, but "which stage is this boss in" and "has the fuse
/// burned down" are pure, and both are the kind of off-by-a-threshold that only shows up as a boss
/// that never escalates — thirty seconds into a fight, on someone else's machine.
/// </summary>
public class BossPhaseTests
{
    /// <summary>The Iron King's table, which is also the fallback: full, two thirds, one third.</summary>
    private static readonly float[] ThreeStage = { 1f, 0.66f, 0.33f };

    [Fact]
    public void AnUndamagedBossIsInTheOpeningPhase()
    {
        Assert.Equal(1, BossPhases.SelectPhase(1f, ThreeStage));
    }

    [Theory]
    [InlineData(0.99f, 1)]
    [InlineData(0.67f, 1)]
    [InlineData(0.66f, 2)]   // entered AT the threshold, not below it
    [InlineData(0.65f, 2)]
    [InlineData(0.34f, 2)]
    [InlineData(0.33f, 3)]
    [InlineData(0.01f, 3)]
    [InlineData(0f, 3)]
    public void EachThresholdIsEnteredAtOrBelowItsFraction(float fraction, int expected)
    {
        Assert.Equal(expected, BossPhases.SelectPhase(fraction, ThreeStage));
    }

    [Fact]
    public void ASingleStageBossIsAlwaysPhaseOne()
    {
        // A legitimate boss: one stage, no escalation, still a BossEntity with a healthbar.
        float[] single = { 1f };

        Assert.Equal(1, BossPhases.SelectPhase(1f, single));
        Assert.Equal(1, BossPhases.SelectPhase(0f, single));
    }

    [Fact]
    public void AnEmptyTableStillYieldsAPhase()
    {
        // A boss is never phase 0 — the caller's fallback depends on this.
        Assert.Equal(1, BossPhases.SelectPhase(0.1f, System.Array.Empty<float>()));
        Assert.Equal(1, BossPhases.SelectPhase(0.1f, null!));
    }

    [Fact]
    public void ABigHitLandsInTheDeepestPhaseCrossed()
    {
        // One blow taking a boss from full to a sliver must not report phase 2 and leave the
        // controller to notice phase 3 on some later hit that might never come.
        Assert.Equal(3, BossPhases.SelectPhase(0.05f, ThreeStage));
    }

    [Fact]
    public void OverhealingCannotPushABossPastItsFinalPhase()
    {
        Assert.Equal(1, BossPhases.SelectPhase(2f, ThreeStage));
    }

    [Fact]
    public void ManyStagesResolveToTheRightOne()
    {
        float[] five = { 1f, 0.8f, 0.6f, 0.4f, 0.2f };

        Assert.Equal(1, BossPhases.SelectPhase(0.85f, five));
        Assert.Equal(3, BossPhases.SelectPhase(0.6f, five));
        Assert.Equal(5, BossPhases.SelectPhase(0.19f, five));
    }

    // --- Enrage -------------------------------------------------------------

    [Fact]
    public void TheFuseFiresOnceTheTimeHasElapsed()
    {
        Assert.True(BossPhases.ShouldEnrage(150d, 150f, alreadyEnraged: false));
        Assert.True(BossPhases.ShouldEnrage(151d, 150f, alreadyEnraged: false));
    }

    [Fact]
    public void TheFuseDoesNotFireEarly()
    {
        Assert.False(BossPhases.ShouldEnrage(149.9d, 150f, alreadyEnraged: false));
    }

    [Fact]
    public void TheFuseFiresOnlyOnce()
    {
        // The controller applies stat modifiers and grants spells here; re-firing would keep
        // re-granting for the rest of the fight.
        Assert.False(BossPhases.ShouldEnrage(999d, 150f, alreadyEnraged: true));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void ANonPositiveDurationMeansNoEnrage(float seconds)
    {
        // The Iron King's setting: a boss that can be fought at the player's own pace.
        Assert.False(BossPhases.ShouldEnrage(100000d, seconds, alreadyEnraged: false));
    }
}
