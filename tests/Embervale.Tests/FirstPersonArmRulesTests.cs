using Embervale.Player;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins where the casting hand is put in first person: on one line of sight, low and to the hand's
/// own side, inside the frame at every field of view; reached by one swing at the shoulder that is
/// eased, bounded, and nothing at all when the arm is down.
/// </summary>
public class FirstPersonArmRulesTests
{
    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(expected.DistanceTo(actual) <= tolerance, $"expected {expected}, got {actual}");

    [Theory]
    [InlineData(60f, 16f / 9f)]
    [InlineData(75f, 16f / 9f)]
    [InlineData(110f, 16f / 9f)]
    [InlineData(90f, 4f / 3f)]
    [InlineData(75f, 21f / 9f)]
    public void TheLineOfSight_IsInsideTheFrame_LowAndToTheHandsSide(float fov, float aspect)
    {
        float tan = Mathf.Tan(Mathf.DegToRad(fov) * 0.5f);

        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 line = FirstPersonArmRules.ViewDirection(fov, aspect, side);

            Assert.Equal(1f, line.Length(), 4);
            Assert.True(line.Z < 0f, "ahead of the camera");

            // As fractions of the half-frame: inside it, below centre, on the asked side.
            float x = line.X / -line.Z / (tan * aspect);
            float y = line.Y / -line.Z / tan;
            Assert.Equal(side * FirstPersonArmRules.FrameSide, x, 4);
            Assert.Equal(-FirstPersonArmRules.FrameBelow, y, 4);
            Assert.InRange(Mathf.Abs(x), 0.2f, 0.8f);
            Assert.InRange(y, -0.8f, -0.25f);
        }
    }

    [Fact]
    public void ANonsenseView_StillGivesALineAhead()
    {
        Vector3 line = FirstPersonArmRules.ViewDirection(float.NaN, -3f, 0f);

        Assert.True(line.IsFinite());
        Assert.True(line.Z < 0f);
    }

    [Fact]
    public void TheTarget_IsOnTheLine_AndAnArmsLengthFromTheShoulder()
    {
        var eye = new Vector3(0f, 1.6f, 0f);
        var shoulder = new Vector3(-0.17f, 1.45f, 0.02f);
        Vector3 line = FirstPersonArmRules.ViewDirection(75f, 16f / 9f, -1f);
        const float reach = 0.42f;

        Vector3 target = FirstPersonArmRules.HandTarget(eye, line, shoulder, reach);

        Assert.Equal(reach, target.DistanceTo(shoulder), 3);
        float along = (target - eye).Dot(line);
        AssertNear(eye + (line * along), target);
        Assert.True(along >= FirstPersonArmRules.MinAhead);
    }

    [Fact]
    public void AnArmTooShortToReachTheLine_PointsAtItsNearestPoint()
    {
        var eye = new Vector3(0f, 1.6f, 0f);
        var shoulder = new Vector3(-0.2f, 0.6f, 0.1f);
        Vector3 line = FirstPersonArmRules.ViewDirection(75f, 16f / 9f, -1f);

        Vector3 target = FirstPersonArmRules.HandTarget(eye, line, shoulder, 0.3f);

        Assert.True(target.IsFinite());
        float along = (target - eye).Dot(line);
        Assert.True(along >= FirstPersonArmRules.MinAhead - 1e-4f);
        AssertNear(eye + (line * along), target);
    }

    [Fact]
    public void TheTarget_IsNeverNearerTheEyeThanTheMinimum()
    {
        // A shoulder right behind the eye and a short arm: the sphere is passed almost at once.
        var eye = new Vector3(0f, 1.6f, 0f);
        Vector3 line = FirstPersonArmRules.ViewDirection(75f, 16f / 9f, 1f);

        Vector3 target = FirstPersonArmRules.HandTarget(eye, line, eye + new Vector3(0f, 0f, 0.05f), 0.1f);

        Assert.Equal(FirstPersonArmRules.MinAhead, target.DistanceTo(eye), 4);
    }

    [Fact]
    public void AFullSwing_PutsTheHandOnTheTarget()
    {
        var shoulder = new Vector3(-0.17f, 1.45f, 0f);
        var hand = new Vector3(-0.3f, 1.05f, 0.05f);
        Vector3 target = shoulder + (new Vector3(-0.1f, -0.1f, -0.9f).Normalized() * hand.DistanceTo(shoulder));

        Quaternion swing = FirstPersonArmRules.Swing(shoulder, hand, target, 1f);

        AssertNear(target, shoulder + (swing * (hand - shoulder)), 1e-3f);
    }

    [Fact]
    public void TheSwing_IsNothingWhenTheArmIsDown_AndEasesUp()
    {
        var shoulder = new Vector3(-0.17f, 1.45f, 0f);
        var hand = new Vector3(-0.3f, 1.05f, 0.05f);
        var target = new Vector3(-0.25f, 1.4f, -0.4f);

        Assert.Equal(Quaternion.Identity, FirstPersonArmRules.Swing(shoulder, hand, target, 0f));

        float full = FirstPersonArmRules.Swing(shoulder, hand, target, 1f).GetAngle();
        float half = FirstPersonArmRules.Swing(shoulder, hand, target, 0.5f).GetAngle();
        float early = FirstPersonArmRules.Swing(shoulder, hand, target, 0.1f).GetAngle();
        Assert.Equal(full * 0.5f, half, 3);
        Assert.True(early < full * 0.05f, "it starts gently");
    }

    [Fact]
    public void TheSwing_IsBounded()
    {
        // An arm pointing straight back asked to point straight ahead: it goes partway, not all.
        var shoulder = new Vector3(0f, 1.45f, 0f);
        var hand = shoulder + new Vector3(0.05f, -0.02f, 0.4f);
        var target = shoulder + new Vector3(0f, 0f, -0.4f);

        Quaternion swing = FirstPersonArmRules.Swing(shoulder, hand, target, 1f);

        Assert.Equal(FirstPersonArmRules.MaxSwing, swing.GetAngle(), 3);
    }

    [Fact]
    public void ADegenerateArm_IsLeftAlone()
    {
        var shoulder = new Vector3(0f, 1.45f, 0f);

        Assert.Equal(Quaternion.Identity, FirstPersonArmRules.Swing(shoulder, shoulder, shoulder + Vector3.Forward, 1f));
        Assert.Equal(Quaternion.Identity, FirstPersonArmRules.Swing(shoulder, shoulder + Vector3.Down, shoulder, 1f));
        Assert.Equal(
            Quaternion.Identity,
            FirstPersonArmRules.Swing(shoulder, shoulder + Vector3.Down, shoulder + (Vector3.Down * 2f), 1f));
        Assert.Equal(
            Quaternion.Identity,
            FirstPersonArmRules.Swing(shoulder, new Vector3(float.NaN, 0f, 0f), shoulder + Vector3.Forward, 1f));
    }

    [Fact]
    public void TheRaise_ComesUpQuicklyAndGoesDownSlower_AndStopsAtItsEnds()
    {
        float up = FirstPersonArmRules.StepWeight(0f, 1f, FirstPersonArmRules.RaiseSeconds * 0.5f);
        float down = FirstPersonArmRules.StepWeight(1f, 0f, FirstPersonArmRules.RaiseSeconds * 0.5f);

        Assert.Equal(0.5f, up, 4);
        Assert.True(1f - down < up, "it lowers more slowly than it rises");
        Assert.Equal(1f, FirstPersonArmRules.StepWeight(0.9f, 1f, 5f), 5);
        Assert.Equal(0f, FirstPersonArmRules.StepWeight(0.1f, 0f, 5f), 5);
        Assert.Equal(0.4f, FirstPersonArmRules.StepWeight(0.4f, 1f, 0f), 5);
        Assert.Equal(0.4f, FirstPersonArmRules.StepWeight(0.4f, 1f, -1f), 5);
    }

    [Fact]
    public void ThePartialRaiseOfASwap_IsATargetLikeAnyOther()
    {
        // Half way out to third person the arm is asked for half: it settles there, from either side.
        Assert.Equal(0.5f, FirstPersonArmRules.StepWeight(0f, 0.5f, 5f), 5);
        Assert.Equal(0.5f, FirstPersonArmRules.StepWeight(1f, 0.5f, 5f), 5);
        Assert.Equal(0f, FirstPersonArmRules.StepWeight(0.3f, float.NaN, 5f), 5);
    }
}
