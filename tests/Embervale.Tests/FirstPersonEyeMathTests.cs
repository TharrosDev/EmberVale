using Embervale.Player;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the first-person eye: anchored to where the head RESTS, swung about the neck by the look's
/// pitch, taking only a share of the head's animated travel, and held near the pivot. The engine
/// half (reading the bone, the wall sweep, the shader uniform) is covered by
/// <c>tools/camera_probe.gd</c>; what decides whether the view bobs, lurches or shows the player's
/// own skull is this arithmetic.
/// </summary>
public class FirstPersonEyeMathTests
{
    /// <summary>A plausible head rest: a few centimetres under the pivot, on the body's axis.</summary>
    private static readonly Vector3 RestHead = new(0f, -0.06f, 0f);

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(expected.DistanceTo(actual) <= tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void LevelLook_SitsOnTheRestHeadPlusNeckAndArm()
    {
        Vector3 eye = CameraRigMath.EyeLocal(RestHead, 0f, Vector3.Zero);

        AssertNear(RestHead + CameraRigMath.NeckOffset + CameraRigMath.EyeArm, eye);
    }

    [Fact]
    public void TheEyeIsInFrontOfAndAboveTheNeck()
    {
        // -Z is forward. An eye behind the neck would look out through the back of the skull.
        Assert.True(CameraRigMath.EyeArm.Z < 0f);
        Assert.True(CameraRigMath.EyeArm.Y > 0f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.6f)]
    [InlineData(1.4f)]
    public void LevelAndUp_PitchSwingsTheEyeAboutTheNeck_AtAConstantRadius(float pitch)
    {
        Vector3 neck = RestHead + CameraRigMath.NeckOffset;

        Vector3 eye = CameraRigMath.EyeUnpitched(RestHead, pitch, Vector3.Zero);

        Assert.Equal(CameraRigMath.EyeArm.Length(), eye.DistanceTo(neck), 4);
        Assert.Equal(0f, eye.X, 5);
    }

    [Theory]
    [InlineData(-1.4f)]
    [InlineData(-0.7f)]
    [InlineData(-0.2f)]
    public void LookingDown_TheEyeIsTheSwungArmPlusTheReach(float pitch)
    {
        Vector3 swung = RestHead + CameraRigMath.NeckOffset + CameraRigMath.EyeArm.Rotated(Vector3.Right, pitch);

        Vector3 eye = CameraRigMath.EyeUnpitched(RestHead, pitch, Vector3.Zero);

        AssertNear(swung + CameraRigMath.LookDownReach(pitch), eye);
        Assert.Equal(0f, eye.X, 5);
    }

    [Fact]
    public void TheReach_IsNothingAtOrAboveLevel_AndFullStraightDown()
    {
        Assert.Equal(Vector3.Zero, CameraRigMath.LookDownReach(0f));
        Assert.Equal(Vector3.Zero, CameraRigMath.LookDownReach(0.8f));
        Assert.Equal(Vector3.Zero, CameraRigMath.LookDownReach(float.NaN));

        Vector3 full = CameraRigMath.LookDownReach(-CameraRigMath.LookDownFullPitch);
        AssertNear(new Vector3(0f, CameraRigMath.LookDownRise, -CameraRigMath.LookDownForward), full);
        AssertNear(full, CameraRigMath.LookDownReach(-1.57f));
    }

    [Fact]
    public void TheReach_OnlyEverGrowsAsTheLookGoesDown()
    {
        // No step and no reversal: the eye slides out as the look drops and back as it rises.
        float last = 0f;
        for (float pitch = 0f; pitch >= -1.5f; pitch -= 0.02f)
        {
            float forward = -CameraRigMath.LookDownReach(pitch).Z;
            Assert.True(forward >= last - 1e-6f, $"the reach shrank at pitch {pitch}");
            Assert.True(forward - last < 0.012f, $"the reach jumped {forward - last:0.000} m at pitch {pitch}");
            last = forward;
        }
    }

    /// <summary>The eye's offset from the head bone at rest: what <see cref="CameraRigMath.TorsoClearance"/>
    /// measures against the body.</summary>
    private static Vector3 EyeFromHead(float pitch) =>
        CameraRigMath.EyeUnpitched(Vector3.Zero, pitch, Vector3.Zero);

    [Fact]
    public void TheEyeKeepsClearOfTheTorso_FromFullyDownToFullyUp()
    {
        // The defect this pins: looking straight down the eye sat three centimetres off the collar
        // with the chest under it, and the first-person frame was the player's own torso. From
        // -85 to +85 degrees the eye has to keep more than a near plane's depth from the body.
        Assert.True(CameraRigMath.SafeEyeClearance > 0.08f, "the margin has to exceed the near plane");
        for (int degrees = -85; degrees <= 85; degrees++)
        {
            float clearance = CameraRigMath.TorsoClearance(EyeFromHead(Mathf.DegToRad(degrees)));
            Assert.True(
                clearance >= CameraRigMath.SafeEyeClearance,
                $"at {degrees} degrees the eye is {clearance:0.000} m from the torso");
        }
    }

    [Fact]
    public void LookingWellDown_TheEyeIsOutInFrontOfTheChest()
    {
        // Once the body is in view (the lower edge of a 75 degree view passes straight down at a
        // look of about -52) the eye is well ahead of the chest's surface, not merely off it.
        for (int degrees = -85; degrees <= -45; degrees++)
        {
            Vector3 eye = EyeFromHead(Mathf.DegToRad(degrees));
            Assert.True(-eye.Z >= CameraRigMath.ChestFront + 0.08f, $"at {degrees} degrees the eye is {-eye.Z:0.000} m forward");
            Assert.True(CameraRigMath.TorsoClearance(eye) >= 0.17f, $"at {degrees} degrees");
        }
    }

    [Fact]
    public void LookingStraightDown_TheEyeStaysNearHeadHeight()
    {
        // It leans out; it does not sink into the shoulders.
        Vector3 down = EyeFromHead(Mathf.DegToRad(-85f));

        Assert.InRange(down.Y, CameraRigMath.CollarTop, 0.05f);
    }

    [Fact]
    public void TorsoClearance_IsNegativeInsideTheBody_AndTheDistanceOutsideIt()
    {
        // Inside the chest.
        Assert.True(CameraRigMath.TorsoClearance(new Vector3(0f, -0.3f, -0.05f)) < 0f);
        // Straight above the collar: the height above it.
        Assert.Equal(0.1f, CameraRigMath.TorsoClearance(new Vector3(0f, CameraRigMath.CollarTop + 0.1f, 0f)), 4);
        // Straight ahead of the chest: the distance ahead of it.
        Assert.Equal(0.2f, CameraRigMath.TorsoClearance(new Vector3(0f, -0.4f, -(CameraRigMath.ChestFront + 0.2f))), 4);
        // Off the collar's corner: the straight line to it.
        float corner = CameraRigMath.TorsoClearance(
            new Vector3(0f, CameraRigMath.CollarTop + 0.03f, -(CameraRigMath.CollarFront + 0.04f)));
        Assert.Equal(0.05f, corner, 4);
    }

    [Fact]
    public void LookingDown_CarriesTheEyeForwardAndDown()
    {
        Vector3 level = CameraRigMath.EyeUnpitched(RestHead, 0f, Vector3.Zero);
        Vector3 down = CameraRigMath.EyeUnpitched(RestHead, -1.4f, Vector3.Zero);

        Assert.True(down.Y < level.Y, "a head tilted down lowers the eye");
        Assert.True(down.Z < level.Z, "and carries it forward, out over the chest");
    }

    [Fact]
    public void LookingUp_CarriesTheEyeBack()
    {
        Vector3 level = CameraRigMath.EyeUnpitched(RestHead, 0f, Vector3.Zero);
        Vector3 up = CameraRigMath.EyeUnpitched(RestHead, 1.4f, Vector3.Zero);

        Assert.True(up.Z > level.Z);
    }

    [Theory]
    [InlineData(-1.2f)]
    [InlineData(0.9f)]
    public void EyeLocal_IsTheUnpitchedEyeSeenFromThePitchedPivot(float pitch)
    {
        // The camera is the pivot's child and the pivot carries the pitch: turning the local seat
        // by the pitch must land back on the eye.
        var delta = new Vector3(0.01f, -0.02f, 0.015f);

        Vector3 local = CameraRigMath.EyeLocal(RestHead, pitch, delta);

        AssertNear(
            CameraRigMath.EyeUnpitched(RestHead, pitch, delta),
            local.Rotated(Vector3.Right, pitch));
    }

    [Fact]
    public void TheArmEndsUpFixedInThePitchedFrame()
    {
        // Rest head on the pivot and no neck drop to speak of: looking up, the camera's local seat
        // is the arm plus the (turned-back) neck offset; looking down it is that plus the reach.
        Vector3 down = CameraRigMath.EyeLocal(Vector3.Zero, -1f, Vector3.Zero);
        Vector3 up = CameraRigMath.EyeLocal(Vector3.Zero, 1f, Vector3.Zero);
        Vector3 reach = CameraRigMath.LookDownReach(-1f).Rotated(Vector3.Right, 1f);

        Assert.True((down - reach).DistanceTo(CameraRigMath.EyeArm) <= CameraRigMath.NeckOffset.Length() + 1e-4f);
        Assert.True(up.DistanceTo(CameraRigMath.EyeArm) <= CameraRigMath.NeckOffset.Length() + 1e-4f);
    }

    [Fact]
    public void OnFoot_OnlyAShareOfTheHeadsTravelIsFollowed()
    {
        Vector2 follow = CameraRigMath.EyeFollow(reducedMotion: false, mountedBlend: 0f);

        Vector3 followed = CameraRigMath.FollowDelta(new Vector3(0.2f, -0.1f, -0.3f), follow);

        Assert.Equal(0.2f * CameraRigMath.EyeFollowHorizontal, followed.X, 5);
        Assert.Equal(-0.1f * CameraRigMath.EyeFollowVertical, followed.Y, 5);
        Assert.Equal(-0.3f * CameraRigMath.EyeFollowHorizontal, followed.Z, 5);
    }

    [Fact]
    public void TheSprintLean_BarelyMovesTheEye()
    {
        // The report this fixes: at a sprint the head leans well forward and bobs. A quarter of a
        // metre of lean has to stay under three centimetres of camera.
        Vector2 follow = CameraRigMath.EyeFollow(false, 0f);
        Vector3 level = CameraRigMath.EyeLocal(RestHead, 0f, Vector3.Zero);

        Vector3 sprinting = CameraRigMath.EyeLocal(
            RestHead, 0f, CameraRigMath.FollowDelta(new Vector3(0f, -0.04f, -0.25f), follow));

        Assert.True(level.DistanceTo(sprinting) < 0.03f);
    }

    [Fact]
    public void ReducedMotion_FollowsNothingOnFoot()
    {
        Vector2 follow = CameraRigMath.EyeFollow(reducedMotion: true, mountedBlend: 0f);

        Assert.Equal(Vector2.Zero, follow);
        Assert.Equal(Vector3.Zero, CameraRigMath.FollowDelta(new Vector3(0.3f, 0.3f, 0.3f), follow));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InTheSaddle_TheEyeGoesWithTheSeatedHead(bool reducedMotion)
    {
        // The seated pose is where the head is, not a motion: the eye takes all of it either way.
        Vector2 follow = CameraRigMath.EyeFollow(reducedMotion, mountedBlend: 1f);

        Assert.Equal(1f, follow.X, 5);
        Assert.Equal(1f, follow.Y, 5);
    }

    [Fact]
    public void MountingEasesTheFollowUp()
    {
        Vector2 half = CameraRigMath.EyeFollow(false, 0.5f);

        Assert.InRange(half.X, CameraRigMath.EyeFollowVertical, 1f);
        Assert.InRange(half.Y, CameraRigMath.EyeFollowHorizontal, 1f);
        Assert.Equal(CameraRigMath.EyeFollow(false, 1f), CameraRigMath.EyeFollow(false, 7f));
        Assert.Equal(CameraRigMath.EyeFollow(false, 0f), CameraRigMath.EyeFollow(false, -3f));
    }

    [Fact]
    public void ANonFiniteDelta_IsDropped()
    {
        Vector3 followed = CameraRigMath.FollowDelta(
            new Vector3(float.NaN, float.PositiveInfinity, 0.1f), new Vector2(1f, 1f));

        Assert.Equal(new Vector3(0f, 0f, 0.1f), followed);
    }

    [Fact]
    public void AThrownHead_CannotThrowTheCamera()
    {
        // A knockdown puts the head on the floor. Even followed in full the eye stays within reach
        // of the pivot.
        Vector3 eye = CameraRigMath.EyeLocal(RestHead, 0.2f, new Vector3(0.4f, -1.5f, 0.6f));

        Assert.Equal(CameraRigMath.MaxEyeOffset, eye.Length(), 4);
    }

    [Fact]
    public void AnOrdinaryEye_IsNotClamped()
    {
        Vector3 eye = CameraRigMath.EyeLocal(RestHead, -1.45f, Vector3.Zero);

        Assert.True(eye.Length() < CameraRigMath.MaxEyeOffset);
    }

    [Fact]
    public void TheReachedEye_IsNotClampedOnThePlayersOwnBody()
    {
        // chr_player_base: the head bone rests 0.15 m under the 1.62 m pivot. The clamp pulls the
        // eye back toward the pivot, which looking down is back toward the chest, so the whole of
        // the reach has to fit inside it with room for the head's followed travel.
        var playerRestHead = new Vector3(0f, 1.472f - 1.62f, 0f);
        for (int degrees = -85; degrees <= 85; degrees += 5)
        {
            Vector3 eye = CameraRigMath.EyeUnpitched(playerRestHead, Mathf.DegToRad(degrees), Vector3.Zero);
            Assert.True(
                eye.Length() < CameraRigMath.MaxEyeOffset - 0.04f,
                $"at {degrees} degrees the eye is {eye.Length():0.000} m from the pivot");
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.7f)]
    [InlineData(1.4f)]
    [InlineData(1.45f)]
    public void LevelAndUp_TheEyeIsInsideTheHeadCutout(float pitch)
    {
        // Level and looking up the eye is in the head, so the head's own sphere has to hold it.
        Vector3 centre = CameraRigMath.HeadSphereCentre(RestHead, Vector3.Up, Vector3.Forward);

        Vector3 eye = CameraRigMath.EyeUnpitched(RestHead, pitch, Vector3.Zero);

        Assert.True(
            eye.DistanceTo(centre) < CameraRigMath.HeadSphereRadius - 0.02f,
            $"eye is {eye.DistanceTo(centre):0.000} m from the cut-out centre at pitch {pitch}");
    }

    [Fact]
    public void TheWholeHeadIsInsideItsCutout()
    {
        // The face is never drawn because the head is inside the sphere, not because the eye is.
        // Crown, face, back of the skull and chin of chr_player_base, from its head bone.
        Vector3 centre = CameraRigMath.HeadSphereCentre(Vector3.Zero, Vector3.Up, Vector3.Forward);
        Vector3[] head =
        {
            new(0f, 0.18f, 0f), new(0f, 0.05f, -0.10f), new(0f, 0.05f, 0.11f), new(0f, -0.05f, -0.04f),
            new(0.08f, 0.06f, 0f), new(-0.08f, 0.06f, 0f),
        };

        foreach (Vector3 point in head)
        {
            Assert.True(point.DistanceTo(centre) < CameraRigMath.HeadSphereRadius - 0.03f, $"{point} is not cut out");
        }
    }

    [Theory]
    [InlineData(60f, 16f / 9f)]
    [InlineData(75f, 16f / 9f)]
    [InlineData(90f, 16f / 10f)]
    [InlineData(100f, 4f / 3f)]
    public void TheEyeCutout_HoldsTheNearPlanesCorners(float fov, float aspect)
    {
        const float near = 0.08f;
        float tan = Mathf.Tan(Mathf.DegToRad(fov) * 0.5f);
        var corner = new Vector3(near * tan * aspect, near * tan, -near);

        float radius = CameraRigMath.EyeSphereRadius(near, fov, aspect);

        Assert.True(radius > corner.Length(), $"radius {radius:0.000} against a corner at {corner.Length():0.000}");
        Assert.InRange(radius, CameraRigMath.MinEyeSphereRadius, CameraRigMath.MaxEyeSphereRadius);
    }

    [Fact]
    public void TheEyeCutout_IsHeldBetweenItsLimits_WhateverItIsGiven()
    {
        Assert.Equal(CameraRigMath.MinEyeSphereRadius, CameraRigMath.EyeSphereRadius(0.001f, 60f, 1.78f), 5);
        Assert.Equal(CameraRigMath.MaxEyeSphereRadius, CameraRigMath.EyeSphereRadius(0.08f, 115f, 3.6f), 5);
        Assert.InRange(
            CameraRigMath.EyeSphereRadius(float.NaN, float.NaN, float.NaN),
            CameraRigMath.MinEyeSphereRadius, CameraRigMath.MaxEyeSphereRadius);
        Assert.Equal(
            CameraRigMath.EyeSphereRadius(0.08f, 75f, 16f / 9f), CameraRigMath.EyeSphereRadius(0.08f, 75f, 0f), 5);
    }

    [Fact]
    public void TheEyeCutout_NarrowsAsTheLookGoesDown_AndNeverAboveLevel()
    {
        float level = CameraRigMath.EyeSphereRadius(0.08f, 110f, 16f / 9f);
        Assert.Equal(CameraRigMath.MaxEyeSphereRadius, level, 5);
        Assert.Equal(level, CameraRigMath.EyeSphereRadius(0.08f, 110f, 16f / 9f, Mathf.DegToRad(85f)), 5);
        Assert.Equal(level, CameraRigMath.EyeSphereRadius(0.08f, 110f, 16f / 9f, float.NaN), 5);

        float previous = level;
        for (int degrees = 0; degrees >= -90; degrees -= 5)
        {
            float radius = CameraRigMath.EyeSphereRadius(0.08f, 110f, 16f / 9f, Mathf.DegToRad(degrees));
            Assert.True(radius <= previous + 1e-6f, $"the cut-out widened at {degrees} degrees");
            Assert.InRange(radius, CameraRigMath.MinEyeSphereRadius, CameraRigMath.MaxEyeSphereRadius);
            previous = radius;
        }

        // Straight down it is at its narrowest whatever the view: a chest standing 0.10 m behind
        // the view's axis is then only inside it less than 0.07 m below the eye, which is further
        // off the axis than the widest field of view (110 degrees) sees.
        float down = CameraRigMath.EyeSphereRadius(0.08f, 110f, 16f / 9f, -CameraRigMath.LookDownFullPitch);
        Assert.Equal(CameraRigMath.MinEyeSphereRadius, down, 5);
        float below = Mathf.Sqrt((down * down) - (0.10f * 0.10f));
        Assert.True(Mathf.RadToDeg(Mathf.Atan2(0.10f, below)) > 55f);
    }

    [Fact]
    public void TheEyeIsAlwaysInsideTheCutout_AndTheBodyBelowIsNot()
    {
        // At every pitch the eye is cut clear (it is the centre of its own sphere), and looking down
        // the chest, the belt and the shoulder joints are all still drawn.
        float eyeRadius = CameraRigMath.EyeSphereRadius(0.08f, 75f, 16f / 9f);
        Vector3 centre = CameraRigMath.HeadSphereCentre(Vector3.Zero, Vector3.Up, Vector3.Forward);
        Vector3[] body =
        {
            new(0f, -0.19f, -0.14f),   // the chest, where it stands proudest
            new(0f, -0.55f, -0.16f),   // the belt
            new(0.17f, -0.16f, 0.02f), // the shoulder joints
            new(-0.17f, -0.16f, 0.02f),
        };

        for (int degrees = -85; degrees <= 85; degrees += 5)
        {
            Vector3 eye = EyeFromHead(Mathf.DegToRad(degrees));
            Assert.True(CameraRigMath.InCutout(eye, centre, CameraRigMath.HeadSphereRadius, eye, eyeRadius));
            if (degrees > -45)
            {
                continue;
            }

            foreach (Vector3 point in body)
            {
                Assert.False(
                    CameraRigMath.InCutout(point, centre, CameraRigMath.HeadSphereRadius, eye, eyeRadius),
                    $"at {degrees} degrees {point} is cut out of the body");
            }
        }
    }

    [Fact]
    public void InCutout_IsEitherSphere_AndNeitherWhenBothAreOff()
    {
        var head = new Vector3(0f, 1.6f, 0f);
        var eye = new Vector3(0f, 1.5f, -0.4f);

        Assert.True(CameraRigMath.InCutout(head + (Vector3.Up * 0.1f), head, 0.22f, eye, 0.17f));
        Assert.True(CameraRigMath.InCutout(eye + (Vector3.Right * 0.1f), head, 0.22f, eye, 0.17f));
        Assert.False(CameraRigMath.InCutout(new Vector3(0f, 1.0f, 0f), head, 0.22f, eye, 0.17f));
        Assert.False(CameraRigMath.InCutout(head, head, 0f, eye, 0f));
    }

    [Fact]
    public void TheCutoutLeavesTheShoulders()
    {
        // Looking down has to find a body. A shoulder joint sits about 17 cm out and 10 cm below the
        // head bone; the sphere must stop short of it.
        Vector3 centre = CameraRigMath.HeadSphereCentre(Vector3.Zero, Vector3.Up, Vector3.Forward);

        Assert.True(new Vector3(0.17f, -0.10f, 0f).DistanceTo(centre) > CameraRigMath.HeadSphereRadius);
        Assert.True(new Vector3(-0.17f, -0.10f, 0f).DistanceTo(centre) > CameraRigMath.HeadSphereRadius);
    }

    [Fact]
    public void TheCutoutCentre_IsUpAndForwardOfTheHeadBone()
    {
        var head = new Vector3(3f, 1.6f, -2f);

        Vector3 centre = CameraRigMath.HeadSphereCentre(head, Vector3.Up, Vector3.Right);

        AssertNear(
            head + new Vector3(CameraRigMath.HeadSphereForward, CameraRigMath.HeadSphereRise, 0f), centre);
    }

    [Fact]
    public void TheHeadHidesWhenTheCameraIsInIt_AndShowsWhenItLeaves()
    {
        Assert.True(CameraRigMath.HeadHidden(false, 0.1f));
        Assert.False(CameraRigMath.HeadHidden(false, 3.8f));
        Assert.False(CameraRigMath.HeadHidden(true, 3.8f));
    }

    [Fact]
    public void TheHeadDoesNotFlickerBetweenTheTwoDistances()
    {
        float between = (CameraRigMath.HeadHideWithin + CameraRigMath.HeadShowBeyond) * 0.5f;

        Assert.True(CameraRigMath.HeadHidden(true, between), "hidden stays hidden");
        Assert.False(CameraRigMath.HeadHidden(false, between), "shown stays shown");
        Assert.True(CameraRigMath.HeadShowBeyond > CameraRigMath.HeadHideWithin);
    }

    [Fact]
    public void TheHeadIsHiddenForTheWholeFirstPersonEye()
    {
        // Wherever the clamp lets the eye go relative to a head at rest, the head must be cut out:
        // the hide distance has to cover the eye's reach from the cut-out's centre.
        Vector3 centre = CameraRigMath.HeadSphereCentre(RestHead, Vector3.Up, Vector3.Forward);
        for (float pitch = -1.45f; pitch <= 1.45f; pitch += 0.05f)
        {
            Vector3 eye = CameraRigMath.EyeUnpitched(RestHead, pitch, Vector3.Zero);
            Assert.True(CameraRigMath.HeadHidden(false, eye.DistanceTo(centre)));
        }
    }

    [Fact]
    public void FullyInFirstPerson_TheHeadStaysHiddenPastTheOrdinaryShowDistance()
    {
        // Looking down, the eye leans out ahead of a head a clip may have thrown back: further than
        // the show distance. In first person that must not bring the head back.
        float leanedOut = CameraRigMath.HeadShowBeyond + 0.2f;

        Assert.True(CameraRigMath.HeadHidden(false, leanedOut, thirdBlend: 0f));
        Assert.True(CameraRigMath.HeadHidden(true, leanedOut, thirdBlend: 0f));
    }

    [Fact]
    public void FullyInFirstPerson_AHeadThrownClearIsDrawn()
    {
        // A knockdown puts the head on the floor, a metre and more from the eye. It is drawn there:
        // the player must not look down at their own headless body.
        Assert.False(CameraRigMath.HeadHidden(true, 1.2f, thirdBlend: 0f));
        Assert.True(CameraRigMath.FirstPersonHeadShowBeyond > CameraRigMath.HeadShowBeyond);
    }

    [Fact]
    public void TheWholeReachOfTheEye_KeepsTheHeadHidden_EvenWithTheHeadThrownBack()
    {
        // The furthest the eye gets from the head's cut-out in ordinary play: fully down, with the
        // animated head a quarter of a metre behind and 15 cm above where it rests.
        var thrown = new Vector3(0f, 0.15f, 0.25f);
        Vector3 centre = CameraRigMath.HeadSphereCentre(thrown, Vector3.Up, Vector3.Forward);
        for (int degrees = -85; degrees <= 85; degrees += 5)
        {
            Vector3 eye = CameraRigMath.EyeUnpitched(Vector3.Zero, Mathf.DegToRad(degrees), Vector3.Zero);
            Assert.True(
                CameraRigMath.HeadHidden(false, eye.DistanceTo(centre), thirdBlend: 0f),
                $"at {degrees} degrees the eye is {eye.DistanceTo(centre):0.000} m from the head's centre");
        }
    }

    [Fact]
    public void OnTheWayOutToThirdPerson_TheHeadGoesByDistance()
    {
        Assert.False(CameraRigMath.HeadHidden(true, 0.9f, thirdBlend: 0.3f));
        Assert.True(CameraRigMath.HeadHidden(true, 0.2f, thirdBlend: 0.3f));
        Assert.False(CameraRigMath.HeadHidden(false, 3.8f, thirdBlend: 1f));
        Assert.True(CameraRigMath.HeadHidden(false, 0.1f, thirdBlend: 1f));
    }

    [Fact]
    public void TheThirdPersonSeat_NeverHidesTheHead()
    {
        // The nearest a usable seat sits is MinSeatDistance behind the pivot.
        Assert.True(CameraRigMath.MinSeatDistance - CameraRigMath.MaxEyeOffset > CameraRigMath.HeadShowBeyond);
    }

    [Fact]
    public void AimWidensTheShoulderOnlySlightly()
    {
        CameraProfile aim = CameraProfile.For(CameraContext.Aim);

        Assert.Equal(CameraProfile.AimShoulderScale, aim.ShoulderScale, 5);
        Assert.InRange(aim.ShoulderScale, 1f, 1.2f);
    }
}
