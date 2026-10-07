using Embervale.Magic.Vfx;
using Embervale.Player;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>The camera polish pass: the one-sided look-up limit in third person, what thins the
/// player's own body out near the camera, what is hidden in first person, and the casting arm's
/// line of sight.</summary>
public class CameraPolishRulesTests
{
    [Fact]
    public void LookingUp_IsHeldShorterThanLookingDown_InThirdPersonOnly()
    {
        float limit = CameraRigMath.PitchLimit(CameraProfile.Neutral.PitchLimit, 1f);

        Assert.Equal(CameraProfile.Neutral.PitchLimit, CameraRigMath.LookUpLimit(CameraProfile.Neutral.PitchLimit, 0f), 5);
        Assert.Equal(CameraRigMath.ThirdPersonLookUpLimit, CameraRigMath.LookUpLimit(limit, 1f), 5);
        Assert.True(CameraRigMath.ThirdPersonLookUpLimit < CameraRigMath.ThirdPersonPitchLimit);

        // A context tighter than the look-up limit stays tighter, and the swap eases between the two.
        Assert.Equal(0.8f, CameraRigMath.LookUpLimit(0.8f, 1f), 5);
        Assert.InRange(CameraRigMath.LookUpLimit(1.45f, 0.5f), CameraRigMath.ThirdPersonLookUpLimit, 1.45f);
        Assert.Equal(CameraRigMath.LookUpLimit(1.45f, 1f), CameraRigMath.LookUpLimit(1.45f, 7f), 5);
    }

    [Fact]
    public void TheBody_ThinsItsLimbsInFirstPerson_AndWholeNearTheCameraInThird()
    {
        Vector4 first = CameraRigMath.ViewFade(headHidden: true, thirdBlend: 0f);
        Assert.Equal(CameraRigMath.LimbFadeRadius, first.X, 5);
        Assert.Equal(CameraRigMath.LimbCoreRadius, first.Y, 5);
        Assert.Equal(0f, first.Z, 5);

        Vector4 third = CameraRigMath.ViewFade(headHidden: false, thirdBlend: 1f);
        Assert.Equal(0f, third.X, 5);
        Assert.Equal(CameraRigMath.NearBodyFade, third.Z, 5);

        // A third-person camera a wall has pushed into the head counts as first person here.
        Assert.Equal(first, CameraRigMath.ViewFade(headHidden: true, thirdBlend: 1f));

        // Travelling through the head on a swap, with the head drawn again: nothing.
        Assert.Equal(Vector4.Zero, CameraRigMath.ViewFade(headHidden: false, thirdBlend: 0.2f));
    }

    [Fact]
    public void TheLimbFade_LeavesTheChestAlone_AndStartsOutsideTheEyesOwnCutOut()
    {
        // The core is wider than the chest and the collar stand out from the head's line...
        Assert.True(CameraRigMath.LimbCoreRadius > CameraRigMath.ChestFront);
        Assert.True(CameraRigMath.LimbCoreRadius > CameraRigMath.CollarFront);

        // ...the fade is gone before the eye's own sphere would have cut the limb hard...
        Assert.True(CameraRigMath.LimbFadeRadius * CameraRigMath.LimbFadeGone > CameraRigMath.MaxEyeSphereRadius);

        // ...and a hand held out to cast, at least MinAhead from the eye, is past where the fade
        // is complete, so the casting hand is never opened to nothing.
        Assert.True(FirstPersonArmRules.MinAhead > CameraRigMath.LimbFadeRadius * CameraRigMath.LimbFadeGone);

        // The third-person fade does nothing at the nearest the seat is ever placed on purpose.
        Assert.True(CameraRigMath.NearBodyFade < CameraRigMath.MinSeatDistance);
    }

    [Fact]
    public void InFirstPerson_OnlyWhatTheHandsHoldIsDrawn()
    {
        const int Left = 11;
        const int Right = 12;
        Assert.False(CameraRigMath.HiddenInFirstPerson(Left, Left, Right));
        Assert.False(CameraRigMath.HiddenInFirstPerson(Right, Left, Right));
        Assert.True(CameraRigMath.HiddenInFirstPerson(3, Left, Right));  // head, hips, back
        Assert.True(CameraRigMath.HiddenInFirstPerson(-1, Left, Right)); // a mount on no bone

        // A rig with no hands found hides everything it carries.
        Assert.True(CameraRigMath.HiddenInFirstPerson(3, -1, -1));
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void TheCastingArm_PointsTheWristAtWhereTheSpellIsDrawn(float side)
    {
        Vector3 point = VfxViewRules.HandOffset;
        Vector3 line = FirstPersonArmRules.LineToward(point, side);

        Assert.Equal(1f, line.Length(), 4);
        Assert.True(line.Z < 0f, "ahead of the camera");
        Assert.True(line.Y < 0f, "below the crosshair");
        Assert.Equal(side, System.MathF.Sign(line.X));

        // On the hand's own side it is exactly the line of sight to the point, so on screen the
        // hand and the effect are in the same place whatever the field of view.
        var mirrored = new Vector3(side * System.MathF.Abs(point.X), point.Y, point.Z);
        Assert.True(line.Cross(mirrored.Normalized()).Length() < 1e-4f);
    }

    [Fact]
    public void ACastingPointThatIsNotAhead_FallsBackToTheDefaultLine()
    {
        Vector3 fallback = FirstPersonArmRules.ViewDirection(75f, 16f / 9f, -1f);
        Assert.Equal(fallback, FirstPersonArmRules.LineToward(new Vector3(0.5f, -0.4f, 0.2f), -1f));
        Assert.Equal(fallback, FirstPersonArmRules.LineToward(new Vector3(float.NaN, 0f, -1f), -1f));
    }
}
