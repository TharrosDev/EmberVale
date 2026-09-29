using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>Covers the pure half of lock-on assist: priority, the wider cone, the pass-on after a
/// kill, and flick switching.</summary>
public class CombatLockOnAssistTests
{
    [Fact]
    public void Prioritised_WithoutAssist_IsThePlainScore()
    {
        Assert.Equal(1.2f, LockOn.Prioritised(1.2f, assist: false, threat: true, healthFraction: 0.1f));
    }

    [Fact]
    public void Prioritised_AThreatOutranksAnIdleEnemyAtTheSameScore()
    {
        float idle = LockOn.Prioritised(1f, true, false, 1f);
        float threat = LockOn.Prioritised(1f, true, true, 1f);
        Assert.True(threat < idle);
    }

    [Fact]
    public void Prioritised_AWoundedEnemyEdgesOutAHealthyOne()
    {
        Assert.True(LockOn.Prioritised(1f, true, false, 0.2f) < LockOn.Prioritised(1f, true, false, 0.9f));
    }

    [Fact]
    public void Prioritised_ANonCandidateStaysOne()
    {
        Assert.Equal(-1f, LockOn.Prioritised(-1f, true, true, 0.1f));
    }

    [Fact]
    public void Prioritised_AThreatDoesNotBeatAMuchBetterAimedTarget()
    {
        // Priority is a nudge, not an override: something dead centre still beats a threat at the edge.
        float centred = LockOn.Score(6f, 0.05f, 18f, 1.3f, true);
        float edgeThreat = LockOn.Prioritised(LockOn.Score(6f, 1.1f, 18f, 1.3f, true), true, true, 1f);
        Assert.True(centred < edgeThreat);
    }

    [Fact]
    public void AcquireAngle_AssistWidensTheCone()
    {
        Assert.Equal(1.3f, LockOn.AcquireAngle(1.3f, false));
        Assert.Equal(1.3f + LockOn.AssistAngleBonus, LockOn.AcquireAngle(1.3f, true));
    }

    [Fact]
    public void WiderCone_LocksSomethingThePlainConeRefuses()
    {
        float angle = 1.4f;
        Assert.Equal(-1f, LockOn.Score(8f, angle, 18f, LockOn.AcquireAngle(1.3f, false), true));
        Assert.True(LockOn.Score(8f, angle, 18f, LockOn.AcquireAngle(1.3f, true), true) >= 0f);
    }

    [Theory]
    [InlineData(true, LockBreakReason.TargetDied, true)]
    [InlineData(false, LockBreakReason.TargetDied, false)]
    [InlineData(true, LockBreakReason.Toggled, false)]
    [InlineData(true, LockBreakReason.LostSight, false)]
    [InlineData(true, LockBreakReason.OutOfRange, false)]
    [InlineData(true, LockBreakReason.Invalid, false)]
    public void OnlyAKillWithTheAssistOnPassesTheLockOn(bool assist, LockBreakReason reason, bool expected) =>
        Assert.Equal(expected, LockOn.ShouldAutoAdvance(assist, reason));

    [Fact]
    public void BreakReasons_AreAppendOnly()
    {
        Assert.Equal(0, (int)LockBreakReason.Toggled);
        Assert.Equal(1, (int)LockBreakReason.TargetDied);
        Assert.Equal(2, (int)LockBreakReason.OutOfRange);
        Assert.Equal(3, (int)LockBreakReason.LostSight);
        Assert.Equal(4, (int)LockBreakReason.Invalid);
    }

    [Fact]
    public void Flick_AHardSweepSwitchesOnceInThatDirection()
    {
        var gate = new FlickGate();
        Assert.Equal(0, gate.FeedMouse(50f, 0.016f));
        Assert.Equal(1, gate.FeedMouse(120f, 0.016f));
        Assert.Equal(0, gate.FeedMouse(300f, 0.016f)); // cooling down: one sweep, one step
    }

    [Fact]
    public void Flick_LeftIsNegative()
    {
        var gate = new FlickGate();
        Assert.Equal(-1, gate.FeedMouse(-400f, 0.016f));
    }

    [Fact]
    public void Flick_ASlowDriftNeverSwitches()
    {
        var gate = new FlickGate();
        for (int i = 0; i < 600; i++)
        {
            Assert.Equal(0, gate.FeedMouse(3f, 0.016f));
        }
    }

    [Fact]
    public void Flick_CooldownEnds()
    {
        var gate = new FlickGate();
        Assert.Equal(1, gate.FeedMouse(500f, 0.016f));
        for (int i = 0; i < 40; i++)
        {
            gate.FeedMouse(0f, 0.016f);
        }

        Assert.Equal(1, gate.FeedMouse(500f, 0.016f));
    }

    [Fact]
    public void Stick_SwitchesOnTheEdgeOfAFullPushNotWhileHeld()
    {
        var gate = new FlickGate();
        Assert.Equal(0, gate.FeedStick(0.4f, 0.016f));
        Assert.Equal(1, gate.FeedStick(0.95f, 0.016f));
        for (int i = 0; i < 60; i++)
        {
            Assert.Equal(0, gate.FeedStick(0.95f, 0.016f)); // held
        }

        gate.FeedStick(0f, 0.016f);
        for (int i = 0; i < 40; i++)
        {
            gate.FeedStick(0f, 0.016f);
        }

        Assert.Equal(-1, gate.FeedStick(-0.95f, 0.016f));
    }

    [Fact]
    public void Flick_ResetForgetsTheMotion()
    {
        var gate = new FlickGate();
        gate.FeedMouse(130f, 0.016f);
        gate.Reset();
        Assert.Equal(0, gate.FeedMouse(20f, 0.016f));
    }

    [Fact]
    public void SoftSwitchAfterALock_StillUsesBearingOrder()
    {
        // Flick-right steps to the next target to the right on screen, whatever their score order.
        var bearings = new[] { 0.6f, -0.5f, 0.05f };
        int current = 2;                                    // the centred one
        Assert.Equal(0, LockOn.CycleByBearing(bearings, current, 1));  // right
        Assert.Equal(1, LockOn.CycleByBearing(bearings, current, -1)); // left
    }
}
