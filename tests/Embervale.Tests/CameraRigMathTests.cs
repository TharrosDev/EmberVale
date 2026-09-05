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
    public void StepBlend_MovesTowardTheTargetAndStopsThere()
    {
        float t = 0f;
        for (int i = 0; i < 100; i++)
        {
            float next = CameraRigMath.StepBlend(t, 1f, 0.016f, 0.18f);
            Assert.True(next >= t, "blend must not move away from its target");
            t = next;
        }

        Assert.Equal(1f, t, 5);
    }

    [Fact]
    public void StepBlend_ReversesWhenTheTargetFlipsBack()
    {
        float t = CameraRigMath.StepBlend(1f, 0f, 0.09f, 0.18f);

        Assert.Equal(0.5f, t, 5);
        Assert.Equal(0f, CameraRigMath.StepBlend(t, 0f, 1f, 0.18f), 5);
    }

    [Fact]
    public void StepBlend_SnapsWhenTheDurationIsZero()
    {
        // What SetFirstPerson(immediate: true) relies on: a save resumed in third person opens
        // there instead of swooping out on the first frame.
        Assert.Equal(1f, CameraRigMath.StepBlend(0f, 1f, 0.016f, 0f), 5);
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
    public void SpringDistance_PullsInImmediatelyWhenGeometryCrowdsTheCamera()
    {
        // One frame is all a wall gets: the camera must never be allowed to sit inside it.
        float distance = CameraRigMath.SpringDistance(3.8f, 3.8f, 1.1f, 0.016f, pushOutPerSec: 6f);

        Assert.Equal(1.1f, distance, 5);
    }

    [Fact]
    public void SpringDistance_EasesBackOutRatherThanSnapping()
    {
        float distance = CameraRigMath.SpringDistance(1.1f, 3.8f, 3.8f, 0.1f, pushOutPerSec: 6f);

        Assert.Equal(1.7f, distance, 5); // 1.1 + 6 * 0.1
        Assert.True(distance < 3.8f, "push-out must not snap to full extension in one frame");
    }

    [Fact]
    public void SpringDistance_SettlesAtFullExtensionAndNeverOvershoots()
    {
        float distance = 1.1f;
        for (int i = 0; i < 200; i++)
        {
            distance = CameraRigMath.SpringDistance(distance, 3.8f, 3.8f, 0.016f, 6f);
            Assert.True(distance <= 3.8f, "push-out must never overshoot the desired distance");
        }

        Assert.Equal(3.8f, distance, 5);
    }

    [Fact]
    public void SpringDistance_NeverGoesNegative()
    {
        Assert.Equal(0f, CameraRigMath.SpringDistance(2f, 3.8f, -1f, 0.016f, 6f), 5);
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
}
