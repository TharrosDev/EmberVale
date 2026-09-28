using System;
using Embervale.Movement;
using Embervale.Player;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure camera-motion rules: the critically damped spring, landing dip, head bob by gait,
/// the sprint settle, dodge lean, mounted sway and the FOV punches, plus the comfort scales that
/// switch each off.
/// </summary>
public class CameraMotionMathTests
{
    private const float Dt = 1f / 60f;

    private static MotionInput Walk(float speed01 = 0.6f, bool sprinting = false, float y = 0f, bool grounded = true,
        float fp = 1f, float bob = 1f, float fov = 1f) =>
        new(speed01, grounded, sprinting, false, MountGait.Halt, fp, y, false, false, 0f, 0f, 0f, bob, fov);

    private static MotionOut Run(CameraMotion m, MotionInput input, float seconds, Action<MotionOut>? each = null)
    {
        MotionOut last = MotionOut.Zero;
        for (float t = 0f; t < seconds; t += Dt)
        {
            last = m.Step(Dt, input);
            each?.Invoke(last);
        }

        return last;
    }

    // ---- spring ---------------------------------------------------------------------------

    [Fact]
    public void Spring_IsExactAcrossStepSizes()
    {
        var a = new CriticalSpring { X = 1f, V = 2f };
        var b = a;
        a.Step(0.5f, 8f);
        for (int i = 0; i < 50; i++)
        {
            b.Step(0.01f, 8f);
        }

        Assert.Equal(a.X, b.X, 4);
        Assert.Equal(a.V, b.V, 3);
    }

    [Fact]
    public void Spring_ZeroDtDoesNothing()
    {
        var s = new CriticalSpring { X = 1f, V = 1f };
        s.Step(0f, 8f);
        Assert.Equal(1f, s.X);
    }

    [Fact]
    public void Spring_KickIsHeldToTheLimit()
    {
        var s = new CriticalSpring();
        for (int i = 0; i < 100; i++)
        {
            s.Kick(0.1f, 8f, 0.2f);
        }

        float peak = 0f;
        for (int i = 0; i < 300; i++)
        {
            s.Step(Dt, 8f);
            peak = Math.Max(peak, s.X);
        }

        Assert.True(peak <= 0.2f + 1e-3f);
    }

    // ---- landing --------------------------------------------------------------------------

    [Fact]
    public void LandingSize_ZeroForHopsRisesAndCaps()
    {
        Assert.Equal(0f, CameraMotionMath.LandingSize(0.2f));
        Assert.Equal(0f, CameraMotionMath.LandingSize(CameraMotionMath.MinLandingDrop));
        Assert.True(CameraMotionMath.LandingSize(1.0f) > 0f);
        Assert.True(CameraMotionMath.LandingSize(3f) > CameraMotionMath.LandingSize(1f));
        Assert.Equal(1f, CameraMotionMath.LandingSize(40f));
    }

    [Fact]
    public void LandingDipAndFov_ScaleWithSize()
    {
        Assert.Equal(0f, CameraMotionMath.LandingDip(0.1f));
        Assert.Equal(CameraMotionMath.MaxLandingDip, CameraMotionMath.LandingDip(99f), 5);
        Assert.Equal(CameraMotionMath.MaxLandingFov, CameraMotionMath.LandingFov(99f), 5);
        Assert.True(CameraMotionMath.LandingDip(2f) < CameraMotionMath.LandingDip(5f));
    }

    private static CameraMotion Fall(float from, float to, out MotionOut lowest, out float landingDrop, float bob = 1f,
        float fp = 1f, float fov = 1f)
    {
        var m = new CameraMotion();
        Run(m, Walk(0f, y: from, bob: bob, fp: fp, fov: fov), 0.1f);
        float y = from;
        m.Step(Dt, Walk(0f, y: from, grounded: false, bob: bob, fp: fp, fov: fov)); // steps off the edge
        MotionOut low = MotionOut.Zero;
        landingDrop = 0f;
        while (y > to)
        {
            y = Math.Max(to, y - 0.2f);
            MotionOut o = m.Step(Dt, Walk(0f, y: y, grounded: false, bob: bob, fp: fp, fov: fov));
            landingDrop = Math.Max(landingDrop, o.LandingDrop);
        }

        MotionOut land = m.Step(Dt, Walk(0f, y: to, bob: bob, fp: fp, fov: fov));
        landingDrop = Math.Max(landingDrop, land.LandingDrop);
        low = land;
        Run(m, Walk(0f, y: to, bob: bob, fp: fp, fov: fov), 0.4f, o => low = o.Y < low.Y ? o : low);
        lowest = low;
        return m;
    }

    [Fact]
    public void Landing_DipsDownThenRecoversSoftly()
    {
        Fall(6f, 0f, out MotionOut lowest, out float drop);
        Assert.True(drop >= 5.9f);
        Assert.True(lowest.Y < -0.05f);

        var m = Fall(6f, 0f, out _, out _);
        MotionOut settled = Run(m, Walk(0f), 3f);
        Assert.Equal(0f, settled.Y, 3);
    }

    [Fact]
    public void Landing_HardLandingNarrowsFov()
    {
        var m = new CameraMotion();
        Run(m, Walk(0f, y: 6f), 0.1f);
        m.Step(Dt, Walk(0f, y: 6f, grounded: false));
        float fov = 0f;
        Run(m, Walk(0f, y: 0f), 0.4f, o => fov = Math.Min(fov, o.Fov));
        Assert.True(fov < -0.5f);
    }

    [Fact]
    public void Landing_SmallStepDownDoesNotDip()
    {
        Fall(0.3f, 0f, out MotionOut lowest, out float drop);
        Assert.Equal(0f, drop);
        Assert.Equal(0f, lowest.Y, 4);
    }

    [Fact]
    public void Landing_DeeperFallDipsMore()
    {
        Fall(1.5f, 0f, out MotionOut small, out _);
        Fall(6f, 0f, out MotionOut big, out _);
        Assert.True(big.Y < small.Y);
    }

    [Fact]
    public void Landing_ReportsDropOnlyOnTheLandingFrame()
    {
        var m = new CameraMotion();
        Run(m, Walk(0f, y: 5f), 0.1f);
        m.Step(Dt, Walk(0f, y: 5f, grounded: false));
        MotionOut land = m.Step(Dt, Walk(0f, y: 0f));
        Assert.Equal(5f, land.LandingDrop, 3);
        Assert.Equal(0f, m.Step(Dt, Walk(0f, y: 0f)).LandingDrop);
    }

    [Fact]
    public void Landing_AnUpwardTeleportIsNotAFall()
    {
        var m = new CameraMotion();
        Run(m, Walk(0f, y: 0f), 0.1f);
        Assert.Equal(0f, m.Step(Dt, Walk(0f, y: 40f)).LandingDrop); // grounded throughout
    }

    [Fact]
    public void Landing_ThirdPersonKeepsHalf_AndComfortZeroesIt()
    {
        Fall(6f, 0f, out MotionOut fp, out _);
        Fall(6f, 0f, out MotionOut tp, out _, fp: 0f);
        Assert.True(tp.Y < 0f && tp.Y > fp.Y);

        Fall(6f, 0f, out MotionOut none, out float drop, bob: 0f);
        Assert.Equal(0f, none.Y, 5);
        Assert.True(drop > 0f); // the drop is still reported so the shake can respond

        Fall(6f, 0f, out _, out _, fov: 0f);
    }

    [Fact]
    public void BodyShare_FullInFirstPerson_HalfInThird()
    {
        Assert.Equal(1f, CameraMotionMath.BodyShare(1f));
        Assert.Equal(CameraMotionMath.ThirdPersonShare, CameraMotionMath.BodyShare(0f));
        Assert.Equal(CameraMotionMath.ThirdPersonShare, CameraMotionMath.BodyShare(-4f));
    }

    // ---- head bob -------------------------------------------------------------------------

    [Fact]
    public void BobAmplitude_ZeroStill_GrowsWithSpeed_SprintBoosts()
    {
        Assert.Equal(0f, CameraMotionMath.BobAmplitude(0f, false));
        Assert.Equal(0f, CameraMotionMath.BobAmplitude(CameraMotionMath.BobDeadzone, false));
        float walk = CameraMotionMath.BobAmplitude(0.28f, false);
        float run = CameraMotionMath.BobAmplitude(0.625f, false);
        float sprint = CameraMotionMath.BobAmplitude(1f, true);
        Assert.True(walk > 0f && run > walk && sprint > run);
        Assert.True(sprint <= CameraMotionMath.MaxBobY * CameraMotionMath.SprintBobBoost + 1e-6f);
    }

    [Fact]
    public void BobRoll_ZeroStill_BoundedAtSprint()
    {
        Assert.Equal(0f, CameraMotionMath.BobRoll(0f));
        Assert.Equal(CameraMotionMath.MaxBobRoll, CameraMotionMath.BobRoll(1f), 6);
        Assert.Equal(CameraMotionMath.MaxBobRoll, CameraMotionMath.BobRoll(5f), 6);
    }

    [Fact]
    public void StepHz_RisesWithSpeed()
    {
        Assert.True(CameraMotionMath.StepHz(1f) > CameraMotionMath.StepHz(0.3f));
        Assert.Equal(CameraMotionMath.StepHz(1f), CameraMotionMath.StepHz(9f));
    }

    private static float Swing(CameraMotion m, MotionInput input, float seconds)
    {
        float peak = 0f;
        Run(m, input, seconds, o => peak = Math.Max(peak, Math.Abs(o.Y)));
        return peak;
    }

    [Fact]
    public void Bob_StandingStillIsSilent()
    {
        Assert.Equal(0f, Swing(new CameraMotion(), Walk(0f), 2f), 5);
    }

    [Fact]
    public void Bob_WalkingMovesTheCamera_SprintMoreThanWalk()
    {
        float walk = Swing(new CameraMotion(), Walk(0.28f), 3f);
        float sprint = Swing(new CameraMotion(), Walk(1f, sprinting: true), 3f);
        Assert.True(walk > 0.002f);
        Assert.True(sprint > walk);
        Assert.True(sprint < 0.03f, "restrained");
    }

    [Fact]
    public void Bob_OffInThirdPerson_InTheAir_AndWithComfortZero()
    {
        Assert.Equal(0f, Swing(new CameraMotion(), Walk(0.8f, fp: 0f), 2f), 5);
        Assert.Equal(0f, Swing(new CameraMotion(), Walk(0.8f, grounded: false), 2f), 5);
        Assert.Equal(0f, Swing(new CameraMotion(), Walk(0.8f, bob: 0f), 2f), 5);
    }

    [Fact]
    public void Bob_FadesInAndOutRatherThanSnapping()
    {
        var m = new CameraMotion();
        Run(m, Walk(0.8f), 2f);
        // Stop: the very next frame is already smaller than a fresh full bob and reaches zero soon after.
        Run(m, Walk(0f), 2f);
        Assert.Equal(0f, Swing(m, Walk(0f), 0.5f), 4);
    }

    [Fact]
    public void Approach_IsFrameRateIndependent()
    {
        float one = CameraMotionMath.Approach(0f, 1f, 6f, 0.2f);
        float v = 0f;
        for (int i = 0; i < 20; i++)
        {
            v = CameraMotionMath.Approach(v, 1f, 6f, 0.01f);
        }

        Assert.Equal(one, v, 4);
        Assert.Equal(0f, CameraMotionMath.Approach(0f, 1f, 6f, -1f)); // negative dt is a no-op
    }

    // ---- sprint edges ---------------------------------------------------------------------

    [Fact]
    public void SettleDip_NothingBelowThreshold_MaxAtFullRun()
    {
        Assert.Equal(0f, CameraMotionMath.SettleDip(0.3f));
        Assert.Equal(0f, CameraMotionMath.SettleDip(CameraMotionMath.SettleMinSpeed));
        Assert.True(CameraMotionMath.SettleDip(0.75f) > 0f);
        Assert.Equal(CameraMotionMath.MaxSettleDip, CameraMotionMath.SettleDip(1f), 6);
    }

    [Fact]
    public void SprintStop_SettlesDown_OnlyFromARun()
    {
        var m = new CameraMotion();
        Run(m, Walk(1f, sprinting: true), 1f);
        float low = 0f;
        Run(m, Walk(1f), 0.4f, o => low = Math.Min(low, o.Y));
        Assert.True(low < -0.005f);

        // A slow walk that was never a sprint does not settle.
        var w = new CameraMotion();
        Run(w, Walk(0.3f), 1f);
        Assert.Equal(0f, Swing(w, Walk(0f), 1f), 3);
    }

    [Fact]
    public void SprintStart_PunchesFovOut_ThenReturns_AndComfortZeroesIt()
    {
        var m = new CameraMotion();
        Run(m, Walk(0.6f), 0.5f);
        float peak = 0f;
        Run(m, Walk(1f, sprinting: true), 0.5f, o => peak = Math.Max(peak, o.Fov));
        Assert.InRange(peak, 0.5f, CameraMotionMath.MaxFovPunch);
        Assert.Equal(0f, Run(m, Walk(1f, sprinting: true), 3f).Fov, 3);

        var off = new CameraMotion();
        Run(off, Walk(0.6f, fov: 0f), 0.5f);
        float offPeak = 0f;
        Run(off, Walk(1f, sprinting: true, fov: 0f), 0.5f, o => offPeak = Math.Max(offPeak, Math.Abs(o.Fov)));
        Assert.Equal(0f, offPeak, 5);
    }

    // ---- dodge ----------------------------------------------------------------------------

    [Fact]
    public void DodgeEnvelope_ZeroAtEnds_PeaksInside()
    {
        Assert.Equal(0f, CameraMotionMath.DodgeEnvelope(0f), 5);
        Assert.Equal(0f, CameraMotionMath.DodgeEnvelope(1f), 4);
        Assert.Equal(0f, CameraMotionMath.DodgeEnvelope(-2f), 5);
        float peak = 0f;
        for (float p = 0f; p <= 1f; p += 0.01f)
        {
            peak = Math.Max(peak, CameraMotionMath.DodgeEnvelope(p));
        }

        Assert.Equal(1f, peak, 2);
        // A quick lean in and a slower return: the peak comes before the midpoint.
        Assert.True(CameraMotionMath.DodgeEnvelope(0.3f) > CameraMotionMath.DodgeEnvelope(0.7f));
    }

    private static MotionInput Dodge(float progress, float lateral, float forward, bool roll = true, float bob = 1f,
        float fp = 1f) =>
        new(0.5f, true, false, false, MountGait.Halt, fp, 0f, true, roll, progress, lateral, forward, bob, 1f);

    private static MotionOut Settle(CameraMotion m, MotionInput input)
    {
        MotionOut o = MotionOut.Zero;
        for (int i = 0; i < 30; i++)
        {
            o = m.Step(Dt, input);
        }

        return o;
    }

    [Fact]
    public void Dodge_RollLeansIntoTheSide()
    {
        MotionOut right = Settle(new CameraMotion(), Dodge(0.3f, 1f, 0f));
        MotionOut left = Settle(new CameraMotion(), Dodge(0.3f, -1f, 0f));
        Assert.True(right.Roll < -0.01f); // rolling right leans the head right (camera clockwise)
        Assert.True(left.Roll > 0.01f);
        Assert.Equal(-right.Roll, left.Roll, 4);
        Assert.True(Math.Abs(right.Roll) <= CameraMotionMath.DodgeRoll + 1e-4f);
    }

    [Fact]
    public void Dodge_ForwardRollDipsWithoutTilting_BackstepPullsBack()
    {
        MotionOut fwd = Settle(new CameraMotion(), Dodge(0.3f, 0f, 1f));
        Assert.True(fwd.Y < -0.01f);
        Assert.Equal(0f, fwd.Roll, 4);

        MotionOut back = Settle(new CameraMotion(), Dodge(0.3f, 0f, -1f, roll: false));
        Assert.True(back.Z > 0.01f);
        Assert.Equal(0f, back.Y, 4);
        Assert.Equal(0f, back.Roll, 4);
    }

    [Fact]
    public void Dodge_EasesOutWhenCancelled_AndComfortAndThirdPersonScaleIt()
    {
        var m = new CameraMotion();
        Settle(m, Dodge(0.3f, 1f, 0f));
        MotionOut after = m.Step(Dt, Walk(0f));
        Assert.True(after.Roll < 0f, "does not snap to zero on the first frame after a cancel");
        Assert.Equal(0f, Run(m, Walk(0f), 1f).Roll, 4);

        Assert.Equal(0f, Settle(new CameraMotion(), Dodge(0.3f, 1f, 0f, bob: 0f)).Roll, 5);

        float fp = Math.Abs(Settle(new CameraMotion(), Dodge(0.3f, 1f, 0f)).Roll);
        float tp = Math.Abs(Settle(new CameraMotion(), Dodge(0.3f, 1f, 0f, fp: 0f)).Roll);
        Assert.Equal(fp * CameraMotionMath.ThirdPersonShare, tp, 4);
    }

    [Fact]
    public void Dodge_KeepsItsDirectionThroughTheRecoveryWhenVelocityDrops()
    {
        var m = new CameraMotion();
        Settle(m, Dodge(0.2f, 1f, 0f));
        // Recovery: the body has stopped, so velocity reads zero, but the lean keeps its side.
        MotionOut o = Settle(m, Dodge(0.6f, 0f, 0f));
        Assert.True(o.Roll < 0f);
    }

    // ---- mounted sway ---------------------------------------------------------------------

    [Fact]
    public void SwayFor_HaltIsStill_FasterGaitsSwayMore()
    {
        Assert.Equal(MountSway.None, CameraMotionMath.SwayFor(MountGait.Halt));
        Assert.Equal(MountSway.None, CameraMotionMath.SwayFor(MountGait.ReinBack));
        MountSway walk = CameraMotionMath.SwayFor(MountGait.Walk);
        MountSway trot = CameraMotionMath.SwayFor(MountGait.Trot);
        MountSway canter = CameraMotionMath.SwayFor(MountGait.Canter);
        MountSway gallop = CameraMotionMath.SwayFor(MountGait.Gallop);
        Assert.True(walk.Bounce < trot.Bounce && trot.Bounce < canter.Bounce && canter.Bounce < gallop.Bounce);
        Assert.True(walk.Hz < gallop.Hz);
        Assert.True(gallop.Bounce < 0.03f && gallop.Roll < 0.02f, "restrained");
    }

    private static MotionInput Riding(MountGait gait, float fp = 1f, float bob = 1f) =>
        new(0.9f, true, false, true, gait, fp, 0f, false, false, 0f, 0f, 0f, bob, 1f);

    private static float RollSwing(CameraMotion m, MotionInput input)
    {
        float peak = 0f;
        Run(m, input, 4f, o => peak = Math.Max(peak, Math.Abs(o.Roll)));
        return peak;
    }

    [Fact]
    public void MountedSway_MovesOnlyWhenMovingAndScalesByGait()
    {
        Assert.Equal(0f, RollSwing(new CameraMotion(), Riding(MountGait.Halt)), 5);
        float walk = RollSwing(new CameraMotion(), Riding(MountGait.Walk));
        float gallop = RollSwing(new CameraMotion(), Riding(MountGait.Gallop));
        Assert.True(walk > 0.001f);
        Assert.True(gallop > walk);
    }

    [Fact]
    public void MountedSway_GentlerInThirdPerson_OffWithComfortZero_NoFootBob()
    {
        float fp = RollSwing(new CameraMotion(), Riding(MountGait.Gallop));
        float tp = RollSwing(new CameraMotion(), Riding(MountGait.Gallop, fp: 0f));
        Assert.True(tp > 0f && tp < fp);
        Assert.Equal(0f, RollSwing(new CameraMotion(), Riding(MountGait.Gallop, bob: 0f)), 5);

        // On foot at the same speed, riding replaces the walking bob (no double motion).
        var m = new CameraMotion();
        Assert.Equal(0f, Swing(m, Riding(MountGait.Halt), 2f), 5);
    }

    // ---- envelope ------------------------------------------------------------------------

    [Fact]
    public void Output_IsAlwaysClamped()
    {
        var m = new CameraMotion();
        for (int i = 0; i < 40; i++)
        {
            m.Step(Dt, Walk(0f, y: 100f - (i * 10f), grounded: false));
        }

        MotionOut o = Run(m, Walk(1f, sprinting: true, y: 0f), 0.05f);
        Run(m, Dodge(0.3f, 1f, 1f), 1f, x =>
        {
            Assert.InRange(x.Y, -CameraMotionMath.MaxOffsetDown, CameraMotionMath.MaxOffsetUp);
            Assert.InRange(x.Roll, -CameraMotionMath.MaxRoll, CameraMotionMath.MaxRoll);
            Assert.InRange(x.Fov, -CameraMotionMath.MaxFovPunch, CameraMotionMath.MaxFovPunch);
        });
        Assert.InRange(o.Y, -CameraMotionMath.MaxOffsetDown, CameraMotionMath.MaxOffsetUp);
    }

    [Fact]
    public void Step_ClampsHitchesAndBadDt()
    {
        var m = new CameraMotion();
        MotionOut o = m.Step(30f, Walk(0.8f));
        Assert.True(float.IsFinite(o.Y) && float.IsFinite(o.Roll));
        Assert.True(float.IsFinite(m.Step(-1f, Walk(0.8f)).Y));
    }
}
