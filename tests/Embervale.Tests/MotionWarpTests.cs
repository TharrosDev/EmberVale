using Embervale.Combat.Actions;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Motion warping — closing the last of the gap so a committed attack lands where it is aimed.
///
/// Every test here is really the same question asked from a different side: <b>can the warp be felt
/// as the game cheating?</b> A warp that travels any distance is a teleport, one that turns any
/// amount is a homing missile, and one that keeps running while the blade is live slides the actor
/// through the hit. The bounds are the design, so the bounds are what is pinned.
/// </summary>
public class MotionWarpTests
{
    [Fact]
    public void TheWarpIsOverBeforeTheBlowLands()
    {
        // Spread across the STARTUP only. Past ActiveFrom the actor is committed and settled, so the
        // hit is decided from a fixed position rather than mid-slide.
        Assert.True(MotionWarp.Fraction(0.1f, 0.34f, 0.016d, 1d) > 0f);
        Assert.Equal(0f, MotionWarp.Fraction(0.34f, 0.34f, 0.016d, 1d));
        Assert.Equal(0f, MotionWarp.Fraction(0.9f, 0.34f, 0.016d, 1d));
    }

    [Fact]
    public void AFrameLongerThanTheWindowLeftClosesTheRest()
    {
        // Framed as a share of the time remaining, so a dropped frame catches up instead of losing
        // ground and leaving the actor short.
        Assert.Equal(1f, MotionWarp.Fraction(0.3f, 0.34f, 5d, 1d));
    }

    [Fact]
    public void AZeroLengthActionCannotDivideByZero()
    {
        Assert.Equal(0f, MotionWarp.Fraction(0.1f, 0.34f, 0.016d, 0d));
        Assert.Equal(0f, MotionWarp.Fraction(0.1f, 0f, 0.016d, 1d));
    }

    [Fact]
    public void ItClosesTowardTheTargetButStopsShortOfIt()
    {
        // Stopping at `reach` is what keeps an attacker at weapon range instead of standing inside
        // the thing it is hitting.
        Vector3 step = MotionWarp.Step(Vector3.Zero, new Vector3(0f, 0f, 5f), 1.4f, 10f, 1f);
        Assert.Equal(3.6f, step.Length(), 3);
        Assert.Equal(0f, step.X, 3);
        Assert.Equal(3.6f, step.Z, 3);
    }

    [Fact]
    public void ItNeverTravelsFurtherThanTheActionAllows()
    {
        // The difference between a lunge and a teleport is this number.
        Vector3 step = MotionWarp.Step(Vector3.Zero, new Vector3(0f, 0f, 40f), 1.4f, 1.5f, 1f);
        Assert.Equal(1.5f, step.Length(), 3);
    }

    [Fact]
    public void ATargetAlreadyInReachIsNotShoved()
    {
        // ⚠️ A warp must never push an actor backwards or jostle one already in position, or a crowd
        // of attackers spends the fight shuffling into each other.
        Assert.Equal(Vector3.Zero, MotionWarp.Step(Vector3.Zero, new Vector3(0f, 0f, 1.0f), 1.4f, 2f, 1f));
        Assert.Equal(Vector3.Zero, MotionWarp.Step(Vector3.Zero, Vector3.Zero, 1.4f, 2f, 1f));
    }

    [Fact]
    public void HeightIsIgnored()
    {
        // The gap that matters is the one on the ground. A target on a ledge must not drag the
        // attacker upward, and gravity stays the locomotion component's business.
        Vector3 step = MotionWarp.Step(Vector3.Zero, new Vector3(0f, 9f, 5f), 1.4f, 10f, 1f);
        Assert.Equal(0f, step.Y, 4);
    }

    [Fact]
    public void APartialFractionMovesAProportionOfTheWay()
    {
        Vector3 step = MotionWarp.Step(Vector3.Zero, new Vector3(0f, 0f, 5f), 1.4f, 10f, 0.25f);
        Assert.Equal(0.9f, step.Length(), 3);
    }

    [Fact]
    public void TheYawTurnsTowardTheTarget()
    {
        // Godot faces -Z, so a target at +X is a +90 degree yaw.
        float yaw = MotionWarp.YawStep(0f, Vector3.Zero, new Vector3(5f, 0f, 0f), 180f, 1f);
        Assert.Equal(Mathf.Pi / 2f, yaw, 3);
    }

    [Fact]
    public void TheTurnIsCappedByWhatTheActionHasLeftToSpend()
    {
        // ⚠️ The cap is per ACTION, not per frame. That is the difference between correcting onto a
        // target that stepped aside and tracking a circling one through the whole animation.
        float yaw = MotionWarp.YawStep(0f, Vector3.Zero, new Vector3(5f, 0f, 0f), 10f, 1f);
        Assert.Equal(Mathf.DegToRad(10f), yaw, 4);

        Assert.Equal(0f, MotionWarp.YawStep(0f, Vector3.Zero, new Vector3(5f, 0f, 0f), 0f, 1f));
    }

    [Fact]
    public void TheTurnTakesTheShortWayRound()
    {
        // Without wrapping, a target just behind the actor turns it the long way round — a full
        // spin in place, which reads as the character panicking.
        float yaw = MotionWarp.YawStep(Mathf.Pi - 0.1f, Vector3.Zero, new Vector3(0f, 0f, -5f), 180f, 1f);
        Assert.True(Mathf.Abs(yaw) < Mathf.Pi / 2f, $"expected a short turn, got {yaw} rad");
    }

    [Fact]
    public void ATargetUnderfootDoesNotSpinTheActor()
    {
        Assert.Equal(0f, MotionWarp.YawStep(0.4f, Vector3.Zero, Vector3.Zero, 180f, 1f));
    }
}
