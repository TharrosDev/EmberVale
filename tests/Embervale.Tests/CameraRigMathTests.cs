using System;
using Embervale.Player;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the pure maths behind the hybrid first/third-person camera rig. The physics casts and the
/// node writes run in-engine, but the mode blend and the wall-collision spring decide whether the
/// camera ends up inside a wall or lagging behind a swap, so they are pinned here.
/// </summary>
public class CameraRigMathTests
{
    private const float Back = 3.8f;
    private const float Rise = 0.4f;
    private const float Shoulder = 0.6f;

    [Fact]
    public void RestOffset_FirstPerson_SitsOnThePivot()
    {
        Assert.Equal(Vector3.Zero, CameraRigMath.RestOffset(true, Back, Rise, Shoulder));
    }

    [Fact]
    public void RestOffset_ThirdPerson_IsBehindUpAndOverTheShoulder()
    {
        Vector3 offset = CameraRigMath.RestOffset(false, Back, Rise, Shoulder);

        Assert.Equal(Shoulder, offset.X, 5);
        Assert.Equal(Rise, offset.Y, 5);
        Assert.Equal(Back, offset.Z, 5); // +Z is behind: Godot cameras look down -Z
    }

    [Theory]
    [InlineData(Embervale.Settings.Settings.ShoulderRight, Shoulder)]
    [InlineData(Embervale.Settings.Settings.ShoulderLeft, -Shoulder)]
    [InlineData(Embervale.Settings.Settings.ShoulderCentre, 0f)]
    [InlineData(47, Shoulder)] // a hand-edited settings file must not break the camera
    public void ShoulderOffset_MapsEachSideToItsLateralOffset(int side, float expected)
    {
        Assert.Equal(expected, CameraRigMath.ShoulderOffset(side, Shoulder), 5);
    }

    [Fact]
    public void Ease_PinsTheEndpointsAndIsSymmetricAtTheMiddle()
    {
        Assert.Equal(0f, CameraRigMath.Ease(0f), 5);
        Assert.Equal(1f, CameraRigMath.Ease(1f), 5);
        Assert.Equal(0.5f, CameraRigMath.Ease(0.5f), 5);
        Assert.Equal(0f, CameraRigMath.Ease(-3f), 5);
        Assert.Equal(1f, CameraRigMath.Ease(9f), 5);
    }

    [Fact]
    public void Blend_InterpolatesBetweenTheTwoSeats()
    {
        Vector3 third = CameraRigMath.RestOffset(false, Back, Rise, Shoulder);

        Assert.Equal(Vector3.Zero, CameraRigMath.Blend(Vector3.Zero, third, 0f));
        Assert.Equal(third, CameraRigMath.Blend(Vector3.Zero, third, 1f));
        Assert.Equal(third.Z * 0.5f, CameraRigMath.Blend(Vector3.Zero, third, 0.5f).Z, 5);
    }

    [Fact]
    public void AimDirection_IsThePivotForwardWhenTheCameraSitsOnThePivot()
    {
        // The first-person invariant: with the camera on the pivot, the crosshair converges on a
        // point straight ahead, so re-aiming the aim node is a no-op and spells behave exactly as
        // they did before the rig existed.
        var pivot = new Vector3(10f, 1.62f, -4f);
        Vector3 forward = Vector3.Forward;
        Vector3 focus = pivot + (forward * 200f);

        Vector3 aim = CameraRigMath.AimDirection(pivot, focus);

        Assert.Equal(forward.X, aim.X, 5);
        Assert.Equal(forward.Y, aim.Y, 5);
        Assert.Equal(forward.Z, aim.Z, 5);
    }

    [Fact]
    public void AimDirection_ConvergesOnTheCrosshairPointFromAnOffsetEye()
    {
        // Third person: the aim node is at the head, the crosshair point was found from a camera
        // 3.8 m back and 0.6 m to the right. The direction must run head → that point, not
        // head → camera-forward.
        var head = new Vector3(0f, 1.62f, 0f);
        var focus = new Vector3(0.6f, 1.6f, -20f);

        Vector3 aim = CameraRigMath.AimDirection(head, focus);

        Assert.Equal(1f, aim.Length(), 4);
        Assert.True(aim.X > 0f, "aim must lean toward the shoulder-offset crosshair point");
        Assert.True(aim.Z < 0f, "aim must still run forward");
    }

    [Fact]
    public void AimDirection_FallsBackWhenTheFocusPointIsTheOrigin()
    {
        Assert.Equal(Vector3.Forward, CameraRigMath.AimDirection(Vector3.Zero, Vector3.Zero));
    }

    [Fact]
    public void DampConvergesAtTheSameRateWhateverTheFrameRate()
    {
        // ⚠️ THE POINT OF Damp, and the bug it prevents: a raw Lerp(a, b, 0.1f) converges twice as
        // fast at 120 fps as at 60, so a camera tuned on one machine is wrong on another. Stepping
        // the same half-second in 30 slices and in 120 must land in the same place.
        float coarse = 1f;
        for (int i = 0; i < 30; i++)
        {
            coarse -= coarse * CameraRigMath.Damp(0.5f / 30f, 0.06f);
        }

        float fine = 1f;
        for (int i = 0; i < 120; i++)
        {
            fine -= fine * CameraRigMath.Damp(0.5f / 120f, 0.06f);
        }

        Assert.Equal(coarse, fine, 4);
    }

    [Fact]
    public void DampDecaysMostOfTheErrorInOneTimeConstant()
    {
        // One time constant should remove ~63% of the remaining distance; that is what makes the
        // "seconds" argument mean something a designer can reason about.
        Assert.Equal(0.632f, CameraRigMath.Damp(0.06f, 0.06f), 3);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void DampWithNoSmoothingSnaps(float seconds) =>
        Assert.Equal(1f, CameraRigMath.Damp(0.016f, seconds), 4);

    [Fact]
    public void DampIsAlwaysAUsableFraction()
    {
        // A factor outside 0..1 would overshoot or reverse the lerp it feeds.
        foreach (float delta in new[] { 0f, 0.001f, 0.016f, 1f, 100f })
        {
            float t = CameraRigMath.Damp(delta, 0.06f);
            Assert.InRange(t, 0f, 1f);
        }
    }

    [Fact]
    public void SpringBlend_SettlesExactlyOnTheTargetAndStopsMoving()
    {
        float value = 0f;
        float velocity = 0f;
        for (int i = 0; i < 200; i++)
        {
            CameraRigMath.SpringBlend(ref value, ref velocity, 1f, 0.016f, 0.4f);
            Assert.InRange(value, 0f, 1f);
        }

        Assert.Equal(1f, value);
        Assert.Equal(0f, velocity);
    }

    [Fact]
    public void SpringBlend_ReachesMostOfTheWayInTheSettleTime()
    {
        float value = 0f;
        float velocity = 0f;
        for (int i = 0; i < 25; i++)
        {
            CameraRigMath.SpringBlend(ref value, ref velocity, 1f, 0.016f, 0.4f);
        }

        Assert.InRange(value, 0.93f, 0.97f); // 25 frames = 0.4 s: 95% by construction
    }

    [Fact]
    public void SpringBlend_ATogglePressedMidSwapTurnsAroundWithoutALurch()
    {
        // ⚠️ The point of using a spring. A linear ramp reverses in one frame at full speed, which is
        // the lurch that makes a swap feel like a cut. Carrying velocity, the reversal has to slow the
        // camera first: no frame may move further than the fastest frame of the outward swing, and
        // the first frame after the flip must already be braking.
        float value = 0f;
        float velocity = 0f;
        float fastest = 0f;
        for (int i = 0; i < 8; i++)
        {
            float before = value;
            CameraRigMath.SpringBlend(ref value, ref velocity, 1f, 0.016f, 0.4f);
            fastest = Math.Max(fastest, value - before);
        }

        float lastOutward = velocity;
        float peak = value;
        for (int i = 0; i < 200; i++)
        {
            float before = value;
            CameraRigMath.SpringBlend(ref value, ref velocity, 0f, 0.016f, 0.4f);
            Assert.InRange(value, 0f, 1f);
            Assert.True(Math.Abs(value - before) <= fastest + 0.0001f, "a reversal must never out-run the swap");
            if (i == 0)
            {
                Assert.True(velocity < lastOutward, "the first frame after the flip must already be braking");
            }

            peak = Math.Max(peak, value);
        }

        Assert.Equal(0f, value);
        Assert.True(peak < 1f, "turning around must not carry the camera all the way to the far view");
    }

    [Fact]
    public void SpringBlend_IsFrameRateIndependent()
    {
        float coarse = 0f, coarseV = 0f;
        for (int i = 0; i < 15; i++)
        {
            CameraRigMath.SpringBlend(ref coarse, ref coarseV, 1f, 0.2f / 15f, 0.4f);
        }

        float fine = 0f, fineV = 0f;
        for (int i = 0; i < 120; i++)
        {
            CameraRigMath.SpringBlend(ref fine, ref fineV, 1f, 0.2f / 120f, 0.4f);
        }

        Assert.Equal(coarse, fine, 3);
    }

    [Fact]
    public void SpringBlend_SnapsWhenTheDurationIsZeroAndIgnoresAZeroFrame()
    {
        // What SetFirstPerson(immediate: true) relies on: a save resumed in third person opens there
        // instead of swooping out on the first frame.
        float value = 0f;
        float velocity = 0.5f;
        CameraRigMath.SpringBlend(ref value, ref velocity, 1f, 0.016f, 0f);
        Assert.Equal(1f, value);
        Assert.Equal(0f, velocity);

        value = 0.4f;
        velocity = 0.2f;
        CameraRigMath.SpringBlend(ref value, ref velocity, 1f, 0f, 0.4f);
        Assert.Equal(0.4f, value);
        Assert.Equal(0.2f, velocity);
    }

    [Fact]
    public void ComposeSeat_ClampsDistanceRiseAndShoulderWhateverIsStackedOnIt()
    {
        Vector3 wild = CameraRigMath.ComposeSeat(6f, 1.5f, 1.6f, 9f, -9f);
        Assert.Equal(CameraRigMath.MaxDistance, wild.Z, 5);
        Assert.Equal(CameraRigMath.MaxRise, wild.Y, 5);
        Assert.Equal(-CameraRigMath.MaxShoulder, wild.X, 5);

        Vector3 tiny = CameraRigMath.ComposeSeat(2f, 0.5f, 0.5f, -3f, 0f);
        Assert.Equal(CameraRigMath.MinDistance, tiny.Z, 5);
        Assert.Equal(0f, tiny.Y, 5);
    }

    [Fact]
    public void ComposeSeat_LeavesAnOrdinaryPlayerSettingAlone()
    {
        Vector3 seat = CameraRigMath.ComposeSeat(Back, 1f, 1f, Rise, Shoulder);
        Assert.Equal(CameraRigMath.RestOffset(false, Back, Rise, Shoulder), seat);
    }

    [Theory]
    [InlineData(75f, 6f, 81f)]
    [InlineData(75f, -12f, 63f)]
    [InlineData(110f, 40f, 115f)] // clamped at the top
    [InlineData(60f, -40f, 55f)] // and at the bottom
    public void ComposeFov_StaysInsideTheRange(float baseFov, float offset, float expected) =>
        Assert.Equal(expected, CameraRigMath.ComposeFov(baseFov, offset), 4);

    [Fact]
    public void FovKick_ScalesOnlyTheWideningAndReducedMotionRemovesIt()
    {
        Assert.Equal(3.6f, CameraRigMath.ScaleFovKick(6f, 0.6f), 4);
        Assert.Equal(6f, CameraRigMath.ScaleFovKick(6f, 1f), 4);
        Assert.Equal(0f, CameraRigMath.ScaleFovKick(6f, 0f), 4);
        Assert.Equal(-12f, CameraRigMath.ScaleFovKick(-12f, 0f), 4); // an aim narrows regardless
    }

    [Fact]
    public void CombineLayers_WithNoLayersChangesNothing()
    {
        Assert.Equal(CameraNudge.Identity, CameraRigMath.CombineLayers(ReadOnlySpan<CameraNudge>.Empty));
    }

    [Fact]
    public void CombineLayers_SumsOffsetsAnglesAndFovAndMultipliesDistance()
    {
        var a = new CameraNudge(new Vector3(0.1f, 0f, 0f), new Vector3(0f, 0f, 0.1f), 2f, 1.1f);
        var b = new CameraNudge(new Vector3(0.05f, 0.2f, 0f), new Vector3(0.02f, 0f, 0.05f), -5f, 0.9f);

        CameraNudge sum = CameraRigMath.CombineLayers(new[] { a, b });

        Assert.Equal(0.15f, sum.Offset.X, 5);
        Assert.Equal(0.2f, sum.Offset.Y, 5);
        Assert.Equal(0.15f, sum.Euler.Z, 5);
        Assert.Equal(0.02f, sum.Euler.X, 5);
        Assert.Equal(-3f, sum.FovOffset, 5);
        Assert.Equal(0.99f, sum.DistanceScale, 5);
    }

    [Fact]
    public void CombineLayers_ClampsTheTotalNotJustEachLayer()
    {
        // Three well-behaved layers can still add up to a bad camera; the clamp is on the sum.
        var each = new CameraNudge(new Vector3(0.6f, 0f, 0f), new Vector3(0.2f, 0f, 0.4f), 12f, 1.3f);

        CameraNudge sum = CameraRigMath.CombineLayers(new[] { each, each, each });

        Assert.Equal(CameraRigMath.MaxNudgeOffset, sum.Offset.X, 5);
        Assert.Equal(CameraRigMath.MaxNudgeAim, sum.Euler.X, 5);
        Assert.Equal(CameraRigMath.MaxNudgeRoll, sum.Euler.Z, 5);
        Assert.Equal(CameraRigMath.MaxNudgeFov, sum.FovOffset, 5);
        Assert.Equal(CameraRigMath.MaxNudgeDistanceScale, sum.DistanceScale, 5);
    }

    [Fact]
    public void CombineLayers_ADamagedLayerCannotWriteANonFiniteValueToTheCamera()
    {
        var broken = new CameraNudge(
            new Vector3(float.NaN, 0f, 0f), new Vector3(0f, float.PositiveInfinity, 0f), float.NaN, float.NaN);

        CameraNudge sum = CameraRigMath.CombineLayers(new[] { broken });

        Assert.Equal(0f, sum.Offset.X);
        Assert.Equal(0f, sum.Euler.Y);
        Assert.Equal(0f, sum.FovOffset);
        Assert.Equal(1f, sum.DistanceScale);
    }

    [Theory]
    [InlineData(0f, 8f, 0f)]
    [InlineData(4f, 8f, 0.5f)]
    [InlineData(20f, 8f, 1f)]
    [InlineData(5f, 0f, 0f)] // nothing to measure against
    public void Speed01_IsAFractionOfSprintSpeed(float speed, float sprint, float expected) =>
        Assert.Equal(expected, CameraRigMath.Speed01(speed, sprint), 5);

    [Fact]
    public void ASeatWithNoRoomIsNotWorthSwingingOutTo()
    {
        Assert.False(CameraRigMath.SeatUsable(0.4f), "a closet: the camera would sit against the head");
        Assert.True(CameraRigMath.SeatUsable(CameraRigMath.MinSeatDistance));
        Assert.True(CameraRigMath.SeatUsable(3.8f));
    }

    [Fact]
    public void ShoulderSwap_SwingsToTheFreeShoulderWhenTheChosenOneIsAgainstAWall()
    {
        Assert.True(CameraRigMath.ShoulderSwapped(false, homeClear: 0.2f, awayClear: 1f, secondsSinceSwap: 5f));
    }

    [Fact]
    public void ShoulderSwap_DoesNotSwapWhenTheOtherShoulderIsNoBetter()
    {
        // Two walls a body-width apart: swapping would just trade one squeeze for another.
        Assert.False(CameraRigMath.ShoulderSwapped(false, 0.2f, 0.3f, 5f));
        Assert.False(CameraRigMath.ShoulderSwapped(false, 0.2f, 0.2f, 5f));
    }

    [Fact]
    public void ShoulderSwap_DoesNotSwapForAShoulderThatIsMostlyClear()
    {
        Assert.False(CameraRigMath.ShoulderSwapped(false, 0.7f, 1f, 5f));
    }

    [Fact]
    public void ShoulderSwap_HasHysteresisSoItDoesNotFlicker()
    {
        // ⚠️ The gap between the two thresholds is the whole feature. Between "blocked" and "clear",
        // whichever shoulder the camera is on is the one it stays on, in BOTH directions.
        Assert.False(CameraRigMath.ShoulderSwapped(false, 0.7f, 1f, 5f));
        Assert.True(CameraRigMath.ShoulderSwapped(true, 0.7f, 1f, 5f));
        Assert.True(CameraRigMath.ShoulderSwapped(true, 0.9f, 1f, 5f));
        Assert.False(CameraRigMath.ShoulderSwapped(true, 0.97f, 1f, 5f), "returns once the home shoulder is nearly free");
    }

    [Fact]
    public void ShoulderSwap_ReturnsHomeWhenTheSwappedShoulderIsNowTheWorseOne()
    {
        Assert.False(CameraRigMath.ShoulderSwapped(true, homeClear: 0.8f, awayClear: 0.3f, secondsSinceSwap: 5f));
    }

    [Fact]
    public void ShoulderSwap_HoldsForAMinimumTimeAfterASwap()
    {
        Assert.False(CameraRigMath.ShoulderSwapped(false, 0.1f, 1f, CameraRigMath.SwapHoldSeconds - 0.1f));
        Assert.True(CameraRigMath.ShoulderSwapped(true, 0.99f, 1f, CameraRigMath.SwapHoldSeconds - 0.1f));
    }

    [Fact]
    public void ProbeMotion_StartsAtTheSeatAndFansOutSymmetricallyAroundIt()
    {
        var seat = new Vector3(0.6f, 0.4f, 3.8f);

        Assert.Equal(seat, CameraRigMath.ProbeMotion(seat, 0));
        Assert.Equal(CameraRigMath.ProbeSpreadX, CameraRigMath.ProbeMotion(seat, 1).X - seat.X, 5);
        Assert.Equal(-CameraRigMath.ProbeSpreadX, CameraRigMath.ProbeMotion(seat, 2).X - seat.X, 5);
        Assert.Equal(CameraRigMath.ProbeSpreadY, CameraRigMath.ProbeMotion(seat, 3).Y - seat.Y, 5);
        Assert.Equal(-CameraRigMath.ProbeSpreadY, CameraRigMath.ProbeMotion(seat, 4).Y - seat.Y, 5);
        for (int i = 0; i < CameraRigMath.ProbeCount; i++)
        {
            Assert.Equal(seat.Z, CameraRigMath.ProbeMotion(seat, i).Z, 5); // only the far end fans out
        }
    }

    [Fact]
    public void SpringStep_PullsInImmediatelyWhenGeometryCrowdsTheCamera()
    {
        // One frame is all a wall gets: the camera must never be allowed to sit inside it.
        float clear = 0.5f;
        float fraction = CameraRigMath.SpringStep(1f, 1f, 0.29f, 0.016f, 1.6f, ref clear, 0.25f);

        Assert.Equal(0.29f, fraction, 5);
        Assert.Equal(0f, clear);
    }

    [Fact]
    public void SpringStep_WaitsBeforePushingBackOut()
    {
        float clear = 0f;
        float fraction = 0.3f;
        for (int i = 0; i < 10; i++) // 0.16 s: still inside the hold
        {
            fraction = CameraRigMath.SpringStep(fraction, 1f, 1f, 0.016f, 1.6f, ref clear, 0.25f);
        }

        Assert.Equal(0.3f, fraction, 5);
    }

    [Fact]
    public void SpringStep_EasesBackOutRatherThanSnappingAndNeverOvershoots()
    {
        float clear = 0f;
        float fraction = 0.3f;
        float previous = fraction;
        float firstStep = 0f;
        for (int i = 0; i < 400; i++)
        {
            fraction = CameraRigMath.SpringStep(fraction, 1f, 1f, 0.016f, 1.6f, ref clear, 0.25f);
            Assert.True(fraction <= 1f, "push-out must never overshoot full extension");
            Assert.True(fraction >= previous, "push-out must never move back in on its own");
            if (firstStep == 0f && fraction > previous)
            {
                firstStep = fraction - previous;
                Assert.True(firstStep < 1.6f * 0.016f, "the push-out must ramp up, not start at full speed");
            }

            previous = fraction;
        }

        Assert.Equal(1f, fraction, 5);
    }

    [Fact]
    public void SpringStep_ACrowdingWallRestartsTheHold()
    {
        // A fence post: clear for a moment, then blocked again. The camera must not creep out between.
        float clear = 0f;
        float fraction = 0.3f;
        for (int i = 0; i < 12; i++)
        {
            fraction = CameraRigMath.SpringStep(fraction, 1f, 1f, 0.016f, 1.6f, ref clear, 0.25f);
        }

        fraction = CameraRigMath.SpringStep(fraction, 1f, 0.3f, 0.016f, 1.6f, ref clear, 0.25f);
        Assert.Equal(0f, clear);
        for (int i = 0; i < 10; i++)
        {
            fraction = CameraRigMath.SpringStep(fraction, 1f, 1f, 0.016f, 1.6f, ref clear, 0.25f);
        }

        Assert.Equal(0.3f, fraction, 5);
    }

    [Fact]
    public void SpringStep_NeverGoesNegative()
    {
        float clear = 0f;
        Assert.Equal(0f, CameraRigMath.SpringStep(0.6f, 1f, -1f, 0.016f, 1.6f, ref clear, 0.25f), 5);
    }

    [Fact]
    public void PitchLimit_IsTheContextsInFirstPersonAndCappedInThird()
    {
        Assert.Equal(1.45f, CameraRigMath.PitchLimit(1.45f, 0f), 5);
        Assert.Equal(CameraRigMath.ThirdPersonPitchLimit, CameraRigMath.PitchLimit(1.45f, 1f), 5);
        Assert.Equal(1.1f, CameraRigMath.PitchLimit(1.1f, 1f), 5); // a tighter context stays tighter
        Assert.InRange(CameraRigMath.PitchLimit(1.45f, 0.5f), CameraRigMath.ThirdPersonPitchLimit, 1.45f);
    }

    [Fact]
    public void EasePitchInto_LeavesAPitchInsideTheLimitAloneAndEasesOneOutsideIt()
    {
        Assert.Equal(0.9f, CameraRigMath.EasePitchInto(0.9f, 1.1f, 0.016f));

        float pitch = 1.4f;
        for (int i = 0; i < 300; i++)
        {
            float next = CameraRigMath.EasePitchInto(pitch, 1.1f, 0.016f);
            Assert.InRange(next, 1.1f, pitch);
            pitch = next;
        }

        Assert.Equal(1.1f, pitch, 5);
        Assert.Equal(-1.1f, CameraRigMath.EasePitchInto(-1.1004f, 1.1f, 1f), 4);
    }

    [Fact]
    public void LookScale_SlowsANarrowedViewAndNeverSpeedsUpAWidenedOne()
    {
        Assert.Equal(1f, CameraRigMath.LookScale(75f, 75f), 5);
        Assert.Equal(1f, CameraRigMath.LookScale(81f, 75f), 5);
        Assert.InRange(CameraRigMath.LookScale(63f, 75f), 0.75f, 0.85f);
        Assert.Equal(CameraRigMath.MinLookScale, CameraRigMath.LookScale(5f, 110f), 5);
        Assert.Equal(1f, CameraRigMath.LookScale(0f, 75f), 5);
    }

    [Fact]
    public void AsymmetricDamp_LandsFastAndBleedsOffSlowly()
    {
        float up = CameraRigMath.AsymmetricDamp(0f, 1f, 0.05f, 0.05f, 0.3f);
        float down = 1f - CameraRigMath.AsymmetricDamp(1f, 0f, 0.05f, 0.05f, 0.3f);

        Assert.True(up > down * 2f, $"rose {up}, fell {down}");
    }

    [Fact]
    public void TicksWhilePaused_OnlyForAnOpenDialogueInAPausedTreeThatIsStillPlaying()
    {
        Assert.True(CameraRigMath.TicksWhilePaused(dialogueOpen: true, treePaused: true, playing: true));

        // Not in a dialogue: the router ticks the rig itself, so this must not double it.
        Assert.False(CameraRigMath.TicksWhilePaused(dialogueOpen: false, treePaused: true, playing: true));
        // Tree running: same, the router owns the tick.
        Assert.False(CameraRigMath.TicksWhilePaused(dialogueOpen: true, treePaused: false, playing: true));
        // Pause menu, loading or game over during a dialogue: the camera holds still.
        Assert.False(CameraRigMath.TicksWhilePaused(dialogueOpen: true, treePaused: true, playing: false));
    }
}
