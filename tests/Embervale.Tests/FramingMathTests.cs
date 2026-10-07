using System;
using Embervale.Combat;
using Embervale.Player;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>Covers the pure camera-framing rules for lock-on and aim, and the lock-on's camera-facing
/// selection helpers (cycle order, loss grace).</summary>
public class FramingMathTests
{
    private const float DegToRad = MathF.PI / 180f;

    // ----- lock framing -----

    [Fact]
    public void CloseTargetIsFramedPastTheShoulderFromHigherAndFurtherBack()
    {
        LockFraming f = FramingMath.Lock(distance: 2f, heightDelta: 0f, shoulderSign: 1f);

        Assert.Equal(FramingMath.LockLateral, f.Offset.X, 4);
        Assert.Equal(FramingMath.LockRise, f.Offset.Y, 4);
        Assert.Equal(1f + FramingMath.LockPullback, f.DistanceScale, 4);
    }

    [Fact]
    public void FarTargetNeedsNoCloseRangeFraming()
    {
        LockFraming f = FramingMath.Lock(distance: 14f, heightDelta: 0f, shoulderSign: 1f);

        Assert.Equal(0f, f.Offset.X, 4);
        Assert.Equal(0f, f.Offset.Y, 4);
        Assert.Equal(1f, f.DistanceScale, 4);
        Assert.Equal(0f, f.Yaw, 4);
    }

    [Fact]
    public void FramingFadesMonotonicallyWithDistance()
    {
        float previous = float.MaxValue;
        for (float d = 1f; d <= 12f; d += 0.5f)
        {
            float rise = FramingMath.Lock(d, 0f, 1f).Offset.Y;
            Assert.True(rise <= previous + 1e-6f, $"rise grew at {d} m");
            previous = rise;
        }
    }

    [Fact]
    public void LeftShoulderMirrorsTheLateralSlideAndTheYaw()
    {
        LockFraming right = FramingMath.Lock(3f, 0f, 1f);
        LockFraming left = FramingMath.Lock(3f, 0f, -1f);

        Assert.Equal(-right.Offset.X, left.Offset.X, 5);
        Assert.Equal(-right.Yaw, left.Yaw, 5);
        Assert.Equal(right.Offset.Y, left.Offset.Y, 5);
    }

    [Fact]
    public void YawCancelsTheLateralSlideSoTheTargetStaysOnTheCrosshair()
    {
        LockFraming f = FramingMath.Lock(4f, 0f, 1f);

        // Camera moved right by X, target dead ahead at distance d: turning left by atan(X/d) recentres it.
        Assert.True(f.Yaw > 0f);
        Assert.Equal(MathF.Atan2(f.Offset.X, 4f), f.Yaw, 4);
    }

    [Fact]
    public void AnglesStayInsideTheAimSafeBudget()
    {
        foreach (float d in new[] { 0f, 0.5f, 1f, 3f, 8f, 30f })
        {
            foreach (float h in new[] { -50f, -3f, 0f, 3f, 50f })
            {
                LockFraming f = FramingMath.Lock(d, h, 1f);
                Assert.InRange(f.Pitch, -FramingMath.MaxLockPitch, FramingMath.MaxLockPitch);
                Assert.InRange(f.Yaw, -FramingMath.MaxLockYaw, FramingMath.MaxLockYaw);
            }
        }

        Assert.True(FramingMath.MaxLockPitch < 3f * DegToRad);
        Assert.True(FramingMath.MaxLockYaw < 3.1f * DegToRad);
    }

    [Fact]
    public void PitchFollowsTheGroundTheTargetStandsOn()
    {
        Assert.True(FramingMath.Lock(6f, 2f, 1f).Pitch > 0f, "target uphill looks up");
        Assert.True(FramingMath.Lock(6f, -2f, 1f).Pitch < 0f, "target downhill looks down");
        Assert.Equal(0f, FramingMath.Lock(6f, 0f, 1f).Pitch, 5);
    }

    [Fact]
    public void ZeroDistanceDoesNotBlowUp()
    {
        LockFraming f = FramingMath.Lock(0f, 0f, 1f);

        Assert.False(float.IsNaN(f.Yaw));
        Assert.False(float.IsNaN(f.Pitch));
    }

    [Fact]
    public void LockNudgeScalesWithWeightAndIsIdentityAtZero()
    {
        LockFraming f = FramingMath.Lock(2f, 1f, 1f);

        Assert.Equal(CameraNudge.Identity, FramingMath.ToNudge(f, 0f, 1f));

        CameraNudge full = FramingMath.ToNudge(f, 1f, 1f);
        CameraNudge half = FramingMath.ToNudge(f, 0.5f, 1f);
        Assert.Equal(full.Offset.X * 0.5f, half.Offset.X, 5);
        Assert.Equal(full.Euler.X * 0.5f, half.Euler.X, 5);
        Assert.Equal(1f + ((full.DistanceScale - 1f) * 0.5f), half.DistanceScale, 5);
        Assert.Equal(0f, full.FovOffset);
    }

    [Fact]
    public void LockSlideAndYawVanishInFirstPersonButPitchStays()
    {
        LockFraming f = FramingMath.Lock(2f, 2f, 1f);
        CameraNudge fp = FramingMath.ToNudge(f, 1f, thirdBlend: 0f);

        Assert.Equal(Vector3.Zero, fp.Offset);
        Assert.Equal(0f, fp.Euler.Y);
        Assert.Equal(f.Pitch, fp.Euler.X, 5);
    }

    // ----- giants -----

    [Theory]
    [InlineData(0f)]     // unmeasured
    [InlineData(1.8f)]   // a person
    [InlineData(2.6f)]   // the tallest ordinary body
    [InlineData(2.8f)]   // the threshold itself
    public void AnOrdinaryTargetIsFramedExactlyAsBefore(float targetHeight)
    {
        for (float d = 1f; d <= 24f; d += 1.5f)
        {
            Assert.Equal(FramingMath.Lock(d, 0.5f, 1f), FramingMath.Lock(d, 0.5f, 1f, targetHeight));
        }
    }

    [Fact]
    public void AGiantIsSeenFromFurtherBackHigherUpAndLookingUp()
    {
        LockFraming person = FramingMath.Lock(6f, 0f, 1f);
        LockFraming giant = FramingMath.Lock(6f, 0f, 1f, FramingMath.TallestTarget);

        Assert.Equal(person.Offset.Y + FramingMath.TallRise, giant.Offset.Y, 4);
        Assert.Equal(person.DistanceScale + FramingMath.TallPullback, giant.DistanceScale, 4);
        Assert.Equal(FramingMath.TallPitch, giant.Pitch - person.Pitch, 5);

        // It changes how the target is seen, never where it is: the slide and its cancelling yaw
        // are the person's.
        Assert.Equal(person.Offset.X, giant.Offset.X, 5);
        Assert.Equal(person.Yaw, giant.Yaw, 5);
    }

    [Fact]
    public void AGiantUndoesTheLockPullInRatherThanFightingTheCameraSettings()
    {
        // The TargetLock profile pulls the camera in to 0.86 of the player's own distance. Against
        // the tallest target the framing ends a little wider than unlocked, and never by much.
        float scale = FramingMath.Lock(4f, 0f, 1f, 9f).DistanceScale * 0.86f;

        Assert.InRange(scale, 1.0f, 1.15f);
    }

    [Fact]
    public void TheGiantFramingGrowsWithHeightAndStopsAtTheTallest()
    {
        float previous = FramingMath.Lock(6f, 0f, 1f).DistanceScale;
        for (float h = 2.8f; h <= 5.2f; h += 0.2f)
        {
            float scale = FramingMath.Lock(6f, 0f, 1f, h).DistanceScale;
            Assert.True(scale >= previous - 1e-6f, $"pull-back shrank at {h} m");
            previous = scale;
        }

        Assert.Equal(FramingMath.Lock(6f, 0f, 1f, 5.2f), FramingMath.Lock(6f, 0f, 1f, 36f));
        Assert.Equal(0f, FramingMath.Tallness(float.NaN));
    }

    [Fact]
    public void AGiantFarEnoughAwayFitsTheFrameUnaided()
    {
        Assert.Equal(FramingMath.Lock(24f, 0f, 1f), FramingMath.Lock(24f, 0f, 1f, 9f));

        float previous = float.MaxValue;
        for (float d = 1f; d <= 24f; d += 1f)
        {
            float rise = FramingMath.Lock(d, 0f, 1f, 5.2f).Offset.Y;
            Assert.True(rise <= previous + 1e-6f, $"rise grew at {d} m");
            previous = rise;
        }
    }

    [Fact]
    public void TheGiantPitchStaysALeanEvenUphill()
    {
        for (float d = 0.5f; d <= 30f; d += 0.5f)
        {
            float pitch = FramingMath.Lock(d, 40f, 1f, 9f).Pitch;
            Assert.InRange(pitch, 0f, FramingMath.MaxLockPitch + FramingMath.TallPitch + 1e-6f);
        }
    }

    // ----- aim framing -----

    [Fact]
    public void AimLeadsTowardTheChosenShoulder()
    {
        Assert.Equal(FramingMath.AimLead, FramingMath.Aim(0.6f, 1f, 1f).Offset.X, 5);
        Assert.Equal(-FramingMath.AimLead, FramingMath.Aim(-0.6f, 1f, 1f).Offset.X, 5);
    }

    [Fact]
    public void CentredShoulderLeadsFurtherToClearTheBody()
    {
        AimFraming centred = FramingMath.Aim(0f, 1f, 1f);

        Assert.Equal(FramingMath.AimLeadCentred, centred.Offset.X, 5);
        Assert.True(centred.Offset.X > FramingMath.Aim(0.6f, 1f, 1f).Offset.X);
    }

    [Fact]
    public void AimHasNoLeadOrPullInInFirstPersonButStillTightensFov()
    {
        AimFraming fp = FramingMath.Aim(0.6f, thirdBlend: 0f, fovKick: 1f);

        Assert.Equal(Vector3.Zero, fp.Offset);
        Assert.Equal(1f, fp.DistanceScale, 5);
        Assert.Equal(FramingMath.AimFov, fp.FovOffset, 5);
    }

    [Fact]
    public void AimFovIsScaledByComfortAndVanishesUnderReducedMotion()
    {
        Assert.Equal(FramingMath.AimFov * 0.5f, FramingMath.Aim(0.6f, 1f, 0.5f).FovOffset, 5);
        CameraComfort reduced = CameraComfort.From(1f, 1f, 1f, reducedMotion: true);
        Assert.Equal(0f, FramingMath.Aim(0.6f, 1f, reduced.FovKick).FovOffset, 5);
    }

    [Fact]
    public void AimNudgeEasesFromIdentity()
    {
        AimFraming f = FramingMath.Aim(0.6f, 1f, 1f);

        Assert.Equal(CameraNudge.Identity, FramingMath.ToNudge(f, 0f));
        CameraNudge full = FramingMath.ToNudge(f, 1f);
        Assert.Equal(f.FovOffset, full.FovOffset, 5);
        Assert.True(full.DistanceScale < 1f);
        Assert.Equal(Vector3.Zero, full.Euler);
    }

    [Fact]
    public void ShoulderSignTreatsCentreAsRight()
    {
        Assert.Equal(1f, FramingMath.ShoulderSign(0.6f));
        Assert.Equal(-1f, FramingMath.ShoulderSign(-0.6f));
        Assert.Equal(1f, FramingMath.ShoulderSign(0f));
    }

    // ----- easing -----

    [Fact]
    public void WeightArrivesFasterThanItLeaves()
    {
        float inWeight = 0f;
        float outWeight = 1f;
        for (int i = 0; i < 15; i++)
        {
            inWeight = FramingMath.StepWeight(inWeight, true, 0.02f, 0.5f, 0.9f);
            outWeight = FramingMath.StepWeight(outWeight, false, 0.02f, 0.5f, 0.9f);
        }

        Assert.True(inWeight > 1f - outWeight, "0.3 s in should cover more ground than 0.3 s out");
    }

    [Fact]
    public void WeightReachesExactlyZeroAndOneAndNeverOvershoots()
    {
        float w = 0f;
        for (int i = 0; i < 200; i++)
        {
            w = FramingMath.StepWeight(w, true, 0.02f, 0.5f, 0.9f);
            Assert.InRange(w, 0f, 1f);
        }

        Assert.Equal(1f, w);
        for (int i = 0; i < 200; i++)
        {
            w = FramingMath.StepWeight(w, false, 0.02f, 0.5f, 0.9f);
        }

        Assert.Equal(0f, w);
    }

    [Fact]
    public void WeightHoldsWhenNoTimePasses()
    {
        Assert.Equal(0.3f, FramingMath.StepWeight(0.3f, true, 0f, 0.5f, 0.9f));
    }

    [Fact]
    public void WeightStepIsFrameRateIndependent()
    {
        float a = 0f;
        float b = 0f;
        for (int i = 0; i < 25; i++)
        {
            a = FramingMath.StepWeight(a, true, 0.02f, 1f, 1f);
        }

        for (int i = 0; i < 50; i++)
        {
            b = FramingMath.StepWeight(b, true, 0.01f, 1f, 1f);
        }

        Assert.Equal(a, b, 4);
    }

    [Fact]
    public void ApproachIsFrameRateIndependentAndNeverOvershoots()
    {
        float a = 0f;
        float b = 0f;
        for (int i = 0; i < 30; i++)
        {
            a = FramingMath.Approach(a, 10f, 1f / 60f, 0.25f);
        }

        for (int i = 0; i < 60; i++)
        {
            b = FramingMath.Approach(b, 10f, 1f / 120f, 0.25f);
        }

        Assert.Equal(a, b, 3);
        Assert.InRange(a, 0f, 10f);
    }

    [Fact]
    public void Smooth01IsFlatOutsideItsEdgesAndMidwayInside()
    {
        Assert.Equal(0f, FramingMath.Smooth01(3f, 10f, 1f));
        Assert.Equal(1f, FramingMath.Smooth01(3f, 10f, 20f));
        Assert.Equal(0.5f, FramingMath.Smooth01(3f, 10f, 6.5f), 5);
        Assert.Equal(1f, FramingMath.Smooth01(5f, 5f, 6f));
    }

    // ----- bearing, yaw and settle -----

    [Fact]
    public void BearingIsPositiveToTheRightAndNegativeToTheLeft()
    {
        Vector3 forward = Vector3.Forward;

        Assert.True(FramingMath.SignedBearing(forward, Vector3.Right) > 0f);
        Assert.True(FramingMath.SignedBearing(forward, Vector3.Left) < 0f);
        Assert.Equal(0f, FramingMath.SignedBearing(forward, new Vector3(0f, 5f, -3f)), 5);
        Assert.Equal(MathF.PI / 2f, FramingMath.SignedBearing(forward, Vector3.Right), 4);
        Assert.Equal(0f, FramingMath.SignedBearing(forward, Vector3.Zero));
    }

    [Fact]
    public void YawToFacesMinusZTowardTheTarget()
    {
        Assert.Equal(0f, FramingMath.YawTo(Vector3.Forward), 5);
        Assert.Equal(-MathF.PI / 2f, FramingMath.YawTo(Vector3.Right), 4);
        Assert.Equal(MathF.PI / 2f, FramingMath.YawTo(Vector3.Left), 4);
        Assert.Equal(0f, FramingMath.YawTo(Vector3.Zero));
    }

    [Fact]
    public void AngleDeltaTakesTheShortWayRound()
    {
        Assert.Equal(0.2f, FramingMath.AngleDelta(3f, 3.2f), 4);
        Assert.True(FramingMath.AngleDelta(3f, -3f) > 0f, "3 rad to -3 rad is a short step upward across the wrap");
        Assert.InRange(FramingMath.AngleDelta(0f, 7f), -MathF.PI, MathF.PI);
    }

    [Fact]
    public void SlewYawRespectsTheRateCapAndAlwaysArrives()
    {
        float step = FramingMath.SlewYaw(0f, 3f, 0.016f, 0.07f, 9f);
        Assert.InRange(step, 0f, 9f * 0.016f + 1e-5f);

        float yaw = 0f;
        for (int i = 0; i < 120; i++)
        {
            yaw = FramingMath.SlewYaw(yaw, 3f, 0.016f, 0.07f, 9f);
        }

        Assert.Equal(3f, yaw);
    }

    [Fact]
    public void SlewYawSwingsRoundTheShortWay()
    {
        float step = FramingMath.SlewYaw(3f, -3f, 0.016f, 0.07f, 9f);

        Assert.True(step > 3f, "should keep turning up through pi, not sweep back down");
    }

    // ----- aim ray -----

    [Fact]
    public void AimRayStartsAtTheCameraInFirstPersonAndPastItInThird()
    {
        Vector3 cam = new(1f, 2f, 3f);

        Assert.Equal(cam, FramingMath.AimTraceStart(cam, Vector3.Forward, 0f));
        Assert.Equal(new Vector3(1f, 2f, 0f), FramingMath.AimTraceStart(cam, Vector3.Forward, 3f));
        Assert.Equal(cam, FramingMath.AimTraceStart(cam, Vector3.Forward, -2f));
    }

    [Fact]
    public void AimConvergesOnAHitButNotOnASurfaceAtTheNose()
    {
        Vector3 start = Vector3.Zero;
        Vector3 far = start + (Vector3.Forward * 200f);

        Assert.Equal(new Vector3(0f, 0f, -12f),
            FramingMath.AimConvergence(start, Vector3.Forward, new Vector3(0f, 0f, -12f), 1.5f, 200f));
        Assert.Equal(far,
            FramingMath.AimConvergence(start, Vector3.Forward, new Vector3(0f, 0f, -0.5f), 1.5f, 200f));
        Assert.Equal(far, FramingMath.AimConvergence(start, Vector3.Forward, null, 1.5f, 200f));
    }

    // ----- lock-on selection helpers -----

    [Fact]
    public void CyclingWalksLeftToRightAcrossTheScreen()
    {
        // Candidates in arbitrary score order: index 0 is centre, 1 far left, 2 far right, 3 right.
        float[] bearings = { 0f, -0.9f, 0.9f, 0.4f };

        Assert.Equal(3, LockOn.CycleByBearing(bearings, current: 0, dir: 1));  // centre -> right
        Assert.Equal(2, LockOn.CycleByBearing(bearings, current: 3, dir: 1));  // -> far right
        Assert.Equal(1, LockOn.CycleByBearing(bearings, current: 2, dir: 1));  // wraps to far left
        Assert.Equal(1, LockOn.CycleByBearing(bearings, current: 0, dir: -1)); // centre -> left
    }

    [Fact]
    public void CyclingFromNoLockStartsAtTheEndOfTheSweep()
    {
        float[] bearings = { 0f, -0.9f, 0.9f };

        Assert.Equal(1, LockOn.CycleByBearing(bearings, current: -1, dir: 1));
        Assert.Equal(2, LockOn.CycleByBearing(bearings, current: -1, dir: -1));
        Assert.Equal(-1, LockOn.CycleByBearing(Array.Empty<float>(), current: -1, dir: 1));
    }

    [Fact]
    public void CyclingWithOneCandidateKeepsIt()
    {
        Assert.Equal(0, LockOn.CycleByBearing(new[] { 0.3f }, current: 0, dir: 1));
        Assert.Equal(0, LockOn.CycleByBearing(new[] { 0.3f }, current: 0, dir: -1));
    }

    [Fact]
    public void CycleOrderIsIndependentOfTheScoreOrder()
    {
        // The same three enemies (left, centre, right) listed in two different score orders must
        // cycle to the same enemy: from the centre one, "next" is the one on the right either way.
        float[] a = { -0.5f, 0f, 0.5f };
        float[] b = { 0.5f, -0.5f, 0f };

        Assert.Equal(0.5f, a[LockOn.CycleByBearing(a, current: 1, dir: 1)]);
        Assert.Equal(0.5f, b[LockOn.CycleByBearing(b, current: 2, dir: 1)]);
    }

    [Fact]
    public void LossAccumulatesWhileOutOfViewAndResetsTheMomentItReturns()
    {
        float lost = 0f;
        lost = LockOn.StepLoss(lost, inView: false, dt: 0.3f);
        lost = LockOn.StepLoss(lost, inView: false, dt: 0.3f);
        Assert.Equal(0.6f, lost, 5);

        Assert.Equal(0f, LockOn.StepLoss(lost, inView: true, dt: 0.016f));
        Assert.Equal(0.5f, LockOn.StepLoss(0.5f, inView: false, dt: -1f), 5); // a negative dt never rewinds
    }

    [Fact]
    public void LockIsHeldThroughTheGraceAndDroppedAfterIt()
    {
        Assert.False(LockOn.ShouldDrop(0f, LockOn.LossGraceSeconds), "in view is never a drop");
        Assert.False(LockOn.ShouldDrop(LockOn.LossGraceSeconds - 0.01f, LockOn.LossGraceSeconds));
        Assert.True(LockOn.ShouldDrop(LockOn.LossGraceSeconds, LockOn.LossGraceSeconds));
        Assert.True(LockOn.ShouldDrop(0.01f, 0f), "no grace drops the instant it is lost");
    }
}
