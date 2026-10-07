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

    [Fact]
    public void WhatTheHandsHold_IsDrawnInFirstPersonOnlyWhileItIsUpForAFight()
    {
        const int Left = 7;
        const int Right = 9;

        // Walking about: the blade the run clips swing across the view is a shadow only.
        Assert.False(CameraRigMath.WeaponUp(false, false, float.PositiveInfinity));
        Assert.True(CameraRigMath.HiddenInFirstPerson(Right, Left, Right, weaponUp: false));
        Assert.True(CameraRigMath.HiddenInFirstPerson(Left, Left, Right, weaponUp: false));

        // An action in progress always shows it, and so does a lock.
        Assert.True(CameraRigMath.WeaponUp(true, false, float.PositiveInfinity));
        Assert.True(CameraRigMath.WeaponUp(false, true, float.PositiveInfinity));
        Assert.False(CameraRigMath.HiddenInFirstPerson(Right, Left, Right, weaponUp: true));

        // It stays up between swings and comes down a while after the last one.
        Assert.True(CameraRigMath.WeaponUp(false, false, CameraRigMath.WeaponLowerSeconds - 0.1f));
        Assert.False(CameraRigMath.WeaponUp(false, false, CameraRigMath.WeaponLowerSeconds + 0.1f));
        Assert.False(CameraRigMath.WeaponUp(false, false, float.NaN));

        // What is not in a hand is hidden either way.
        Assert.True(CameraRigMath.HiddenInFirstPerson(3, Left, Right, weaponUp: true));
    }

    [Fact]
    public void TheThirdPersonSeat_TiltsDownJustEnoughToLiftTheFeetClearOfTheHotbar()
    {
        // The seat of a fight at the default distance: 3.8 m x 0.92 back, 1.62 + 0.4 + 0.05 up.
        const float Distance = 3.8f * 0.92f;
        const float Height = 2.07f;

        // The narrower the view, the further the feet fall off the bottom and the more tilt it takes.
        float narrow = CameraRigMath.FramingTilt(58f, Distance, Height);
        float standard = CameraRigMath.FramingTilt(73f, Distance, Height);
        float wide = CameraRigMath.FramingTilt(100f, Distance, Height);
        Assert.True(narrow > standard && standard > wide);
        Assert.Equal(0f, wide);
        Assert.True(narrow <= CameraRigMath.MaxFramingTilt);

        // Where it is not capped, the tilt puts the feet exactly on the line: 0.7 of the half height.
        float feet = System.MathF.Atan2(Height, Distance) - standard;
        float onScreen = System.MathF.Tan(feet) / System.MathF.Tan(73f * System.MathF.PI / 360f);
        Assert.Equal(CameraRigMath.FramingFeetAtMost, onScreen, 3);

        // A seat far enough back needs none, and nonsense in gives none out.
        Assert.Equal(0f, CameraRigMath.FramingTilt(75f, 8f, 2.02f));
        Assert.Equal(0f, CameraRigMath.FramingTilt(float.NaN, Distance, Height));
        Assert.Equal(0f, CameraRigMath.FramingTilt(75f, Distance, 0f));
        Assert.Equal(CameraRigMath.MaxFramingTilt, CameraRigMath.FramingTilt(55f, 1f, 3f));
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
