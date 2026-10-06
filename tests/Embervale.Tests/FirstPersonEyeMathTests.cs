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
    [InlineData(-1.4f)]
    [InlineData(-0.7f)]
    [InlineData(0.6f)]
    [InlineData(1.4f)]
    public void PitchSwingsTheEyeAboutTheNeck_AtAConstantRadius(float pitch)
    {
        Vector3 neck = RestHead + CameraRigMath.NeckOffset;

        Vector3 eye = CameraRigMath.EyeUnpitched(RestHead, pitch, Vector3.Zero);

        Assert.Equal(CameraRigMath.EyeArm.Length(), eye.DistanceTo(neck), 4);
        Assert.Equal(0f, eye.X, 5);
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
        // Rest head on the pivot and no neck drop to speak of: whatever the pitch, the camera's
        // local seat is the arm plus the (turned-back) neck offset, so the eye does not slide about
        // in view as the player looks up and down.
        Vector3 a = CameraRigMath.EyeLocal(Vector3.Zero, -1f, Vector3.Zero);
        Vector3 b = CameraRigMath.EyeLocal(Vector3.Zero, 1f, Vector3.Zero);

        Assert.True(a.DistanceTo(CameraRigMath.EyeArm) <= CameraRigMath.NeckOffset.Length() + 1e-4f);
        Assert.True(b.DistanceTo(CameraRigMath.EyeArm) <= CameraRigMath.NeckOffset.Length() + 1e-4f);
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

    [Theory]
    [InlineData(0f)]
    [InlineData(1.4f)]
    [InlineData(-1.4f)]
    [InlineData(1.45f)]
    [InlineData(-1.45f)]
    public void TheEyeStaysInsideTheHeadCutout_AtEveryPitch(float pitch)
    {
        // The cut-out is centred on the head bone, and at rest that is the rest head. If the eye
        // could swing out of it the camera would be looking at the inside of the player's own face.
        Vector3 centre = CameraRigMath.HeadSphereCentre(RestHead, Vector3.Up, Vector3.Forward);

        Vector3 eye = CameraRigMath.EyeUnpitched(RestHead, pitch, Vector3.Zero);

        Assert.True(
            eye.DistanceTo(centre) < CameraRigMath.HeadSphereRadius - 0.02f,
            $"eye is {eye.DistanceTo(centre):0.000} m from the cut-out centre at pitch {pitch}");
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
