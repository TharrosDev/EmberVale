using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure dodge gating + i-frame window (Phase 29E).
/// </summary>
public class DodgeTests
{
    [Fact]
    public void IFrames_OpenOnlyInsideTheWindow()
    {
        // window [0.05, 0.35)
        Assert.False(Dodge.IsInvulnerable(0.0f, 0.05f, 0.35f));  // startup
        Assert.True(Dodge.IsInvulnerable(0.1f, 0.05f, 0.35f));   // mid-roll
        Assert.False(Dodge.IsInvulnerable(0.4f, 0.05f, 0.35f));  // recovery
    }

    [Fact]
    public void CanStart_RequiresGroundStaminaAndNoLock()
    {
        Assert.True(Dodge.CanStart(grounded: true, stamina: 30f, cost: 22f, rolling: false, staggered: false));
        Assert.False(Dodge.CanStart(grounded: false, stamina: 30f, cost: 22f, rolling: false, staggered: false));
        Assert.False(Dodge.CanStart(grounded: true, stamina: 10f, cost: 22f, rolling: false, staggered: false));
        Assert.False(Dodge.CanStart(grounded: true, stamina: 30f, cost: 22f, rolling: true, staggered: false));
        Assert.False(Dodge.CanStart(grounded: true, stamina: 30f, cost: 22f, rolling: false, staggered: true));
    }
}

/// <summary>
/// Covers the directional/curve/recovery/anti-spam dodge rules (movement pass).
/// </summary>
public class DodgeFeelTests
{
    [Fact]
    public void CanStart_RefusedWhileWinded()
    {
        Assert.False(Dodge.CanStart(grounded: true, stamina: 80f, cost: 22f, rolling: false, staggered: false, winded: true));
        Assert.True(Dodge.CanStart(grounded: true, stamina: 80f, cost: 22f, rolling: false, staggered: false, winded: false));
    }

    [Fact]
    public void Resolve_NeutralInputBacksteps_HeldInputRolls()
    {
        Assert.Equal(DodgeKind.Backstep, Dodge.Resolve(0f, 0.2f));
        Assert.Equal(DodgeKind.Backstep, Dodge.Resolve(0.1f * 0.1f, 0.2f)); // inside the deadzone
        Assert.Equal(DodgeKind.Roll, Dodge.Resolve(0.5f * 0.5f, 0.2f));
        Assert.Equal(DodgeKind.Roll, Dodge.Resolve(1f, 0.2f));
    }

    [Fact]
    public void SpeedAt_EasesOutFromPeakToEndFraction()
    {
        Assert.Equal(10f, Dodge.SpeedAt(0f, 0.5f, 10f, 0.2f, 2f), 3);
        float mid = Dodge.SpeedAt(0.25f, 0.5f, 10f, 0.2f, 2f);
        float late = Dodge.SpeedAt(0.45f, 0.5f, 10f, 0.2f, 2f);
        Assert.True(mid < 10f && late < mid && late >= 2f);
        Assert.Equal(0f, Dodge.SpeedAt(0.5f, 0.5f, 10f, 0.2f, 2f)); // outside the motion phase
        Assert.Equal(0f, Dodge.SpeedAt(-0.1f, 0.5f, 10f, 0.2f, 2f));
    }

    [Fact]
    public void Distance_MatchesIntegratedCurve()
    {
        const float duration = 0.45f, peak = 15.5f, end = 0.2f, exp = 1.6f;
        double sum = 0d;
        const int steps = 10000;
        for (int i = 0; i < steps; i++)
        {
            sum += Dodge.SpeedAt((i + 0.5f) * duration / steps, duration, peak, end, exp) * (duration / steps);
        }

        Assert.Equal(Dodge.Distance(duration, peak, end, exp), (float)sum, 2);
        Assert.InRange(Dodge.Distance(duration, peak, end, exp), 3.2f, 3.8f); // shipped roll ≈3.5 m
    }

    [Fact]
    public void Recovery_FinishesAfterMotionPlusTail()
    {
        Assert.False(Dodge.IsFinished(0.5f, 0.45f, 0.15f));
        Assert.True(Dodge.IsFinished(0.6f, 0.45f, 0.15f));
        Assert.True(Dodge.IsFinished(0.45f, 0.45f, -1f)); // negative recovery clamps to none
    }

    [Fact]
    public void AttackCancel_OpensLateInTheMotion()
    {
        Assert.False(Dodge.CanCancelIntoAttack(0.2f, 0.45f, 0.78f));
        Assert.True(Dodge.CanCancelIntoAttack(0.36f, 0.45f, 0.78f));
        Assert.True(Dodge.CanCancelIntoAttack(0.55f, 0.45f, 0.78f)); // recovery tail
    }

    [Fact]
    public void AttackCancel_DefaultsOpenAfterIFramesClose()
    {
        // Roll: i-frames [0.05, 0.35), cancel at 0.78 × 0.45 = 0.351. Backstep: [0.03, 0.17), 0.78 × 0.28 = 0.218.
        Assert.False(Dodge.CanCancelIntoAttack(0.349f, 0.45f, 0.78f));
        Assert.False(Dodge.IsInvulnerable(0.351f, 0.05f, 0.35f));
        Assert.False(Dodge.CanCancelIntoAttack(0.169f, 0.28f, 0.78f));
    }

    [Fact]
    public void Chain_CountsOnlyInsideTheWindow()
    {
        Assert.Equal(1, Dodge.NextChain(0, 0.2, 0.5f));
        Assert.Equal(3, Dodge.NextChain(2, 0.5, 0.5f));
        Assert.Equal(0, Dodge.NextChain(4, 0.8, 0.5f));
        Assert.Equal(0, Dodge.NextChain(0, double.MaxValue, 0.5f));
    }

    [Fact]
    public void ChainedCost_RisesAndCaps()
    {
        Assert.Equal(22f, Dodge.ChainedCost(22f, 0, 0.35f, 2f), 3);
        Assert.Equal(29.7f, Dodge.ChainedCost(22f, 1, 0.35f, 2f), 3);
        Assert.Equal(44f, Dodge.ChainedCost(22f, 5, 0.35f, 2f), 3); // capped at 2×
        Assert.Equal(22f, Dodge.ChainedCost(22f, 3, 0f, 2f), 3);   // no surcharge, no change
    }
}
